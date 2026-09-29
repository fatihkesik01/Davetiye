namespace Davetiye.Domain.Modules.IntegrationFoundation;

/// <summary>
/// A reliable side-effect awaiting dispatch (e.g. a future email send), per
/// docs/PHASE_0_BASELINE.md §2. Deliberately provider- and business-neutral: <see cref="MessageType"/>
/// is a plain string discriminator chosen by the owning business module (not modeled here), and
/// <see cref="Payload"/> is an opaque string (e.g. JSON). This module does not send email, call any
/// provider, or decide what a message means — it only provides a durable queue with retry-safe
/// claim/processing state. A later milestone's real dispatcher (e.g. Notifications) supplies its own
/// typed payload shape and handler on top of this.
/// </summary>
public sealed class OutboxMessage
{
    // Parameterless constructor is for EF Core materialization only; application code must go
    // through Create to keep the entity's invariants enforced.
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Generic side-effect discriminator, e.g. "email.rsvp-confirmation". Owned/interpreted
    /// entirely by the producing business module, not by this module.</summary>
    public string MessageType { get; private set; } = string.Empty;

    /// <summary>Opaque payload (e.g. JSON) describing the side-effect to dispatch.</summary>
    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Number of dispatch attempts that have failed so far.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Earliest time a worker may next attempt to dispatch this message.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>Worker lease expiry. Null when the message is not currently claimed by any worker.</summary>
    public DateTimeOffset? ClaimedUntil { get; private set; }

    /// <summary>Set once retries are exhausted; a terminally failed message is never claimed again.</summary>
    public bool FailedPermanently { get; private set; }

    public long Revision { get; private set; }

    public static OutboxMessage Create(
        Guid id,
        string messageType,
        string payload,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Outbox message id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("Outbox message must declare a message type.", nameof(messageType));
        }

        ArgumentNullException.ThrowIfNull(payload);

        return new OutboxMessage
        {
            Id = id,
            MessageType = messageType.Trim(),
            Payload = payload,
            CreatedAt = createdAt,
            NextAttemptAt = createdAt,
            AttemptCount = 0,
            FailedPermanently = false
        };
    }

    /// <summary>Called by a worker after its handler successfully dispatched this message.</summary>
    public void MarkProcessed(DateTimeOffset processedAt)
    {
        if (FailedPermanently)
        {
            throw new InvalidOperationException(
                "A permanently failed outbox message cannot be marked processed.");
        }

        if (ProcessedAt is not null)
        {
            throw new InvalidOperationException("Outbox message is already processed.");
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
                "A processed outbox message cannot record a failed attempt.");
        }

        if (FailedPermanently)
        {
            throw new InvalidOperationException("Outbox message has already failed permanently.");
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
