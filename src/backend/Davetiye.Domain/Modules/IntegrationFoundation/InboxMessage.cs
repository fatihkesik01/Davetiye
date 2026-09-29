namespace Davetiye.Domain.Modules.IntegrationFoundation;

/// <summary>
/// A single inbound provider event awaiting processing, per docs/PHASE_0_BASELINE.md §2
/// ("Integration Foundation, Application'da generic inbox/outbox contract'larını, Infrastructure'da
/// persistence/claim/worker primitive'lerini barındırır; provider-specific handler veya business
/// state transition sahiplenmez").
///
/// This entity is deliberately provider-neutral: <see cref="ProviderName"/> is a plain string
/// discriminator, not an enum of known providers (Payments/Media add their own typed handler on top
/// of this module later), and <see cref="Payload"/> is stored as an opaque string (raw JSON) rather
/// than a typed provider-specific shape. This module never decodes the payload or decides what an
/// event means; it only guarantees at-most-once acceptance and retry-safe claim/processing state.
///
/// Idempotency/replay-protection (ADR-0006, applied generically here rather than for payments
/// specifically) is a real DB-level unique constraint on (<see cref="ProviderName"/>,
/// <see cref="ProviderEventId"/>), configured in
/// Davetiye.Infrastructure.Modules.IntegrationFoundation.InboxMessageConfiguration — not just an
/// application-level check, which would race under concurrent requests.
/// </summary>
public sealed class InboxMessage
{
    // Parameterless constructor is for EF Core materialization only; application code must go
    // through Create to keep the entity's invariants enforced.
    private InboxMessage()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Generic provider/source discriminator supplied by the caller. This module does not
    /// know or care which concrete providers exist.</summary>
    public string ProviderName { get; private set; } = string.Empty;

    /// <summary>The provider's own idempotency key for this event.</summary>
    public string ProviderEventId { get; private set; } = string.Empty;

    /// <summary>Raw provider payload (e.g. JSON), stored opaque. Not decoded by this module.</summary>
    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Number of processing attempts that have failed so far.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Earliest time a worker may next attempt to process this message.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>Worker lease expiry. Null when the message is not currently claimed by any worker.</summary>
    public DateTimeOffset? ClaimedUntil { get; private set; }

    /// <summary>Set once retries are exhausted; a terminally failed message is never claimed again.</summary>
    public bool FailedPermanently { get; private set; }

    public long Revision { get; private set; }

    public static InboxMessage Create(
        Guid id,
        string providerName,
        string providerEventId,
        string payload,
        DateTimeOffset receivedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Inbox message id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Inbox message must name a provider.", nameof(providerName));
        }

        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            throw new ArgumentException(
                "Inbox message must carry the provider's event id.", nameof(providerEventId));
        }

        ArgumentNullException.ThrowIfNull(payload);

        return new InboxMessage
        {
            Id = id,
            ProviderName = providerName.Trim(),
            ProviderEventId = providerEventId.Trim(),
            Payload = payload,
            ReceivedAt = receivedAt,
            NextAttemptAt = receivedAt,
            AttemptCount = 0,
            FailedPermanently = false
        };
    }

    /// <summary>Called by a worker after its handler successfully processed this message.</summary>
    public void MarkProcessed(DateTimeOffset processedAt)
    {
        if (FailedPermanently)
        {
            throw new InvalidOperationException(
                "A permanently failed inbox message cannot be marked processed.");
        }

        if (ProcessedAt is not null)
        {
            throw new InvalidOperationException("Inbox message is already processed.");
        }

        ProcessedAt = processedAt;
        ClaimedUntil = null;
    }

    /// <summary>
    /// Called by a worker after its handler failed. Pass <paramref name="nextAttemptAt"/> as
    /// <see langword="null"/> to mark the message permanently failed (retries exhausted); the
    /// backoff/max-attempt policy itself is the caller's (worker primitive's) decision, not this
    /// entity's.
    /// </summary>
    public void RecordFailedAttempt(DateTimeOffset failedAt, DateTimeOffset? nextAttemptAt)
    {
        if (ProcessedAt is not null)
        {
            throw new InvalidOperationException(
                "A processed inbox message cannot record a failed attempt.");
        }

        if (FailedPermanently)
        {
            throw new InvalidOperationException("Inbox message has already failed permanently.");
        }

        AttemptCount++;
        ClaimedUntil = null;

        if (nextAttemptAt is null)
        {
            FailedPermanently = true;
            return;
        }

        if (nextAttemptAt <= failedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextAttemptAt),
                nextAttemptAt,
                "The next attempt must be scheduled after the failed attempt.");
        }

        NextAttemptAt = nextAttemptAt.Value;
    }
}
