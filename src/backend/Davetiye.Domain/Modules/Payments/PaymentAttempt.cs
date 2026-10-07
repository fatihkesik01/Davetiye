namespace Davetiye.Domain.Modules.Payments;

/// <summary>Immutable server-priced snapshot of a single invitation purchase.</summary>
public sealed class PaymentAttempt
{
    private PaymentAttempt() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid InvitationId { get; private set; }
    public Guid PlanId { get; private set; }
    public PaymentBillingKind BillingKindAtAttempt { get; private set; }
    public string PlanKey { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public string Status { get; private set; } = PaymentAttemptStatus.Pending;
    public string? CheckoutUrl { get; private set; }
    public string? ProviderCheckoutId { get; private set; }
    public string? ProviderPaymentId { get; private set; }
    public Guid? GrantedPlanGrantId { get; private set; }
    public PaymentAttemptSettlementDisposition? SettlementDisposition { get; private set; }
    public DateTimeOffset? ReversedAtUtc { get; private set; }
    public string? ReversalKind { get; private set; }
    public DateTimeOffset? ChargebackResolvedAtUtc { get; private set; }
    public string? ChargebackResolution { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static PaymentAttempt Create(Guid id, Guid accountId, Guid invitationId, Guid planId,
        string planKey, decimal amount, string currency, string idempotencyKey, string reference,
        DateTimeOffset now, PaymentBillingKind billingKindAtAttempt = PaymentBillingKind.OneTime)
    {
        if (id == Guid.Empty || accountId == Guid.Empty || invitationId == Guid.Empty || planId == Guid.Empty)
            throw new ArgumentException("Payment attempt identifiers must not be empty.");
        if (string.IsNullOrWhiteSpace(planKey) || planKey.Length > 64) throw new ArgumentException("Invalid plan key.", nameof(planKey));
        if (billingKindAtAttempt != PaymentBillingKind.OneTime)
            throw new ArgumentOutOfRangeException(nameof(billingKindAtAttempt), "Individual payment attempts must snapshot a OneTime plan.");
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        if (currency != "TRY") throw new ArgumentException("Only TRY purchases are supported.", nameof(currency));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new ArgumentException("Invalid idempotency key.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 64) throw new ArgumentException("Invalid reference.", nameof(reference));
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(now));

        return new PaymentAttempt
        {
            Id = id, AccountId = accountId, InvitationId = invitationId, PlanId = planId, BillingKindAtAttempt = billingKindAtAttempt,
            PlanKey = planKey, Amount = amount, Currency = currency, IdempotencyKey = idempotencyKey,
            Reference = reference, CreatedAt = now, UpdatedAt = now
        };
    }

    public void SetCheckout(string checkoutUrl, string? providerCheckoutId, DateTimeOffset now)
    {
        if (Status != PaymentAttemptStatus.Pending)
            throw new InvalidOperationException("Checkout cannot be set for a terminal attempt.");
        if (!Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp &&
                (uri.Host == "localhost" || uri.Host == "127.0.0.1"))))
            throw new ArgumentException("Checkout URL must use HTTPS or localhost.", nameof(checkoutUrl));
        CheckoutUrl = checkoutUrl;
        ProviderCheckoutId = providerCheckoutId;
        Status = PaymentAttemptStatus.Pending;
        UpdatedAt = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        if (Status != PaymentAttemptStatus.Pending) return;
        Status = PaymentAttemptStatus.Unknown;
        UpdatedAt = now;
    }

    /// <summary>Moves an in-flight attempt to success after authoritative provider verification.</summary>
    public bool MarkSucceeded(DateTimeOffset now, string providerPaymentId, Guid grantId)
    {
        EnsureUtc(now);
        if (string.IsNullOrWhiteSpace(providerPaymentId) || providerPaymentId.Length > 32 ||
            !providerPaymentId.All(char.IsAsciiDigit) || grantId == Guid.Empty)
            throw new ArgumentException("Verified payment and grant identities are required.");
        if (Status is PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.Reversed)
            return false;
        if (Status is PaymentAttemptStatus.Failed or PaymentAttemptStatus.Canceled)
            return false;
        if (Status is not PaymentAttemptStatus.Pending and not PaymentAttemptStatus.Unknown)
            throw new InvalidOperationException("Unsupported payment attempt state.");

        Status = PaymentAttemptStatus.Succeeded;
        ProviderPaymentId = providerPaymentId;
        GrantedPlanGrantId = grantId;
        SettlementDisposition = PaymentAttemptSettlementDisposition.Granted;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Records provider-confirmed settlement after account deletion without opening an entitlement.</summary>
    public bool MarkSucceededWithoutEntitlement(DateTimeOffset now, string providerPaymentId)
    {
        EnsureUtc(now);
        if (string.IsNullOrWhiteSpace(providerPaymentId) || providerPaymentId.Length > 32 ||
            !providerPaymentId.All(char.IsAsciiDigit))
            throw new ArgumentException("Verified provider payment identity is required.", nameof(providerPaymentId));
        if (Status is PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.Reversed)
            return false;
        if (Status is PaymentAttemptStatus.Failed or PaymentAttemptStatus.Canceled)
            return false;
        if (Status is not PaymentAttemptStatus.Pending and not PaymentAttemptStatus.Unknown)
            throw new InvalidOperationException("Unsupported payment attempt state.");

        Status = PaymentAttemptStatus.Succeeded;
        ProviderPaymentId = providerPaymentId;
        GrantedPlanGrantId = null;
        SettlementDisposition = PaymentAttemptSettlementDisposition.NoEntitlement;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Moves an in-flight attempt to failure after authoritative provider verification.</summary>
    public bool MarkFailed(DateTimeOffset now)
    {
        EnsureUtc(now);
        if (Status == PaymentAttemptStatus.Failed)
            return false;
        if (Status is PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.Reversed or PaymentAttemptStatus.Canceled)
            return false;
        if (Status is not PaymentAttemptStatus.Pending and not PaymentAttemptStatus.Unknown)
            throw new InvalidOperationException("Unsupported payment attempt state.");

        Status = PaymentAttemptStatus.Failed;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Applies a provider-confirmed full refund or final lost chargeback to this payment.</summary>
    public bool MarkReversed(DateTimeOffset now, string reversalKind)
    {
        EnsureUtc(now);
        if (reversalKind is not PaymentAttemptReversalKind.FullRefund and not PaymentAttemptReversalKind.FinalLostChargeback)
            throw new ArgumentException("Only a confirmed full refund or final lost chargeback reverses a payment.", nameof(reversalKind));
        if (Status == PaymentAttemptStatus.Reversed)
            return false;
        if (Status != PaymentAttemptStatus.Succeeded || ProviderPaymentId is null || SettlementDisposition is null)
            return false;
        if (now < UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(now), "A payment reversal cannot predate the successful payment transition.");

        Status = PaymentAttemptStatus.Reversed;
        ReversalKind = reversalKind;
        ReversedAtUtc = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Persists a final chargeback resolution; a later opposite provider outcome cannot replace it.</summary>
    public bool MarkChargebackResolved(DateTimeOffset appliedAtUtc, string resolution)
    {
        EnsureUtc(appliedAtUtc);
        if (resolution is not PaymentAttemptChargebackResolution.FinalWon and not PaymentAttemptChargebackResolution.FinalLost)
            throw new ArgumentException("A final won or lost chargeback resolution is required.", nameof(resolution));
        if (Status != PaymentAttemptStatus.Succeeded || SettlementDisposition is null || ChargebackResolution is not null)
            return false;
        if (appliedAtUtc < UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(appliedAtUtc), "Chargeback resolution cannot predate payment settlement.");

        ChargebackResolution = resolution;
        ChargebackResolvedAtUtc = appliedAtUtc;
        UpdatedAt = appliedAtUtc;
        return true;
    }

    private static void EnsureUtc(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", nameof(now));
    }
}

public enum PaymentAttemptSettlementDisposition
{
    Granted,
    NoEntitlement,
}

public static class PaymentAttemptStatus
{
    public const string Pending = "Pending";
    public const string Unknown = "Unknown";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
    public const string Succeeded = "Succeeded";
    public const string Reversed = "Reversed";
}

public static class PaymentAttemptReversalKind
{
    public const string FullRefund = "FullRefund";
    public const string FinalLostChargeback = "FinalLostChargeback";
}

public static class PaymentAttemptChargebackResolution
{
    public const string FinalWon = "FinalWon";
    public const string FinalLost = "FinalLost";
}
