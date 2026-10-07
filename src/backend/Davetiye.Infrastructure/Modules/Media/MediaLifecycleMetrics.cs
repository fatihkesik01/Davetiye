using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using Davetiye.Application.Modules.Media.Contracts;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class MediaLifecycleMetrics : IMediaLifecycleMetrics, IDisposable
{
    public const string MeterName = "Davetiye.MediaLifecycle";
    private readonly Meter meter = new(MeterName, "1.0.0");
    private readonly Counter<long> expiredIntents;
    private readonly Counter<long> reconciliationResults;
    private readonly Counter<long> deletionAttempts;
    private readonly ConcurrentDictionary<string, (long Count, long Bytes)> assetSnapshot = new(StringComparer.Ordinal);
    private long pendingDeletionCount;
    private long terminalCount;
    private double pendingDeletionOldestAgeSeconds;

    public MediaLifecycleMetrics()
    {
        expiredIntents = meter.CreateCounter<long>("davetiye.media.expired_intents.closed");
        reconciliationResults = meter.CreateCounter<long>("davetiye.media.reconciliation.results");
        deletionAttempts = meter.CreateCounter<long>("davetiye.media.deletion_attempts");
        meter.CreateObservableGauge<long>("davetiye.media.assets.count", ObserveCounts);
        meter.CreateObservableGauge<long>("davetiye.media.assets.storage_upper_bound_bytes", ObserveBytes, unit: "By");
        meter.CreateObservableGauge<long>("davetiye.media.deletion_backlog.count", () => Interlocked.Read(ref pendingDeletionCount));
        meter.CreateObservableGauge<long>("davetiye.media.deletion_backlog.terminal_count", () => Interlocked.Read(ref terminalCount));
        meter.CreateObservableGauge<double>("davetiye.media.deletion_backlog.oldest_age_seconds", () => Volatile.Read(ref pendingDeletionOldestAgeSeconds), unit: "s");
    }

    public void RecordExpiredIntents(int count)
    {
        if (count > 0) expiredIntents.Add(count);
    }

    public void RecordDeletionResult(string outcome)
    {
        if (outcome is not ("deleted" or "already_absent" or "retryable_failure" or "terminal_failure"))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        deletionAttempts.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordReconciliation(string outcome)
    {
        if (outcome is not ("ready_present" or "ready_missing" or "ready_invalid" or
            "retained_present" or "retained_absent" or "retained_invalid" or "inspection_failed"))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        reconciliationResults.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void SetAssetSnapshot(string state, long count, long storageUpperBoundBytes)
    {
        if (state is not ("ready" or "rejected" or "pending_deletion")) throw new ArgumentOutOfRangeException(nameof(state));
        if (count < 0 || storageUpperBoundBytes < 0) throw new ArgumentOutOfRangeException(nameof(count));
        assetSnapshot[state] = (count, storageUpperBoundBytes);
    }

    public void SetDeletionBacklog(long pendingCount, double oldestAgeSeconds, long failedPermanentlyCount)
    {
        if (pendingCount < 0 || failedPermanentlyCount < 0 || !double.IsFinite(oldestAgeSeconds) || oldestAgeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(pendingCount));
        Interlocked.Exchange(ref pendingDeletionCount, pendingCount);
        Interlocked.Exchange(ref terminalCount, failedPermanentlyCount);
        Volatile.Write(ref pendingDeletionOldestAgeSeconds, oldestAgeSeconds);
    }

    private IEnumerable<Measurement<long>> ObserveCounts() => assetSnapshot.Select(item =>
        new Measurement<long>(item.Value.Count, new KeyValuePair<string, object?>("state", item.Key)));

    private IEnumerable<Measurement<long>> ObserveBytes() => assetSnapshot.Select(item =>
        new Measurement<long>(item.Value.Bytes, new KeyValuePair<string, object?>("state", item.Key)));

    public void Dispose() => meter.Dispose();
}
