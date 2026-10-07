using System.Diagnostics.Metrics;
using Davetiye.Infrastructure.Modules.Media;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class MediaLifecycleMetricsTests
{
    [Fact]
    public void Operational_measurements_expose_aggregate_states_and_distinguish_absence_from_failure_without_identifiers()
    {
        using var metrics = new MediaLifecycleMetrics();
        var observed = new List<(string Name, double Value, string Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == MediaLifecycleMetrics.MeterName) meterListener.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            observed.Add((instrument.Name, measurement, Format(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            observed.Add((instrument.Name, measurement, Format(tags))));
        listener.Start();

        metrics.SetAssetSnapshot("ready", 2, 4096);
        metrics.SetAssetSnapshot("rejected", 1, 512_000);
        metrics.SetAssetSnapshot("pending_deletion", 3, 2048);
        metrics.SetDeletionBacklog(7, 120, 2);
        metrics.RecordDeletionResult("already_absent");
        metrics.RecordDeletionResult("retryable_failure");
        listener.RecordObservableInstruments();

        Assert.Contains(observed, item => item.Name == "davetiye.media.assets.storage_upper_bound_bytes" &&
            item.Value == 4096 && item.Tags.Contains("state=ready", StringComparison.Ordinal));
        Assert.Contains(observed, item => item.Name == "davetiye.media.assets.storage_upper_bound_bytes" &&
            item.Value == 512_000 && item.Tags.Contains("state=rejected", StringComparison.Ordinal));
        Assert.Contains(observed, item => item.Name == "davetiye.media.deletion_backlog.count" && item.Value == 7);
        Assert.Contains(observed, item => item.Name == "davetiye.media.deletion_backlog.oldest_age_seconds" && item.Value == 120);
        Assert.Contains(observed, item => item.Name == "davetiye.media.deletion_backlog.terminal_count" && item.Value == 2);
        Assert.Contains(observed, item => item.Name == "davetiye.media.deletion_attempts" &&
            item.Tags.Contains("outcome=already_absent", StringComparison.Ordinal));
        Assert.Contains(observed, item => item.Name == "davetiye.media.deletion_attempts" &&
            item.Tags.Contains("outcome=retryable_failure", StringComparison.Ordinal));
        Assert.DoesNotContain(observed, item => item.Tags.Contains("asset", StringComparison.OrdinalIgnoreCase) ||
            item.Tags.Contains("account", StringComparison.OrdinalIgnoreCase));
    }

    private static string Format(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        string.Join(';', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
}
