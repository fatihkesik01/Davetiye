namespace Davetiye.Application.Modules.Payments.Contracts;

public sealed record StartPaymentCheckoutRequest(Guid InvitationId, string PlanKey, string IdempotencyKey);

public sealed record PaymentCheckoutResponse(Guid AttemptId, string Reference, string Status, string PlanKey,
    decimal Amount, string Currency, string BillingPeriod, string? CheckoutUrl);

public sealed record IndividualPurchasePlan(string Key, string DisplayName, decimal Amount, string Currency,
    string BillingPeriod);

public sealed record IndividualPurchasePlanCatalog(bool IsEligible, IReadOnlyList<IndividualPurchasePlan> Plans);

public enum PaymentCheckoutOutcome { Created, Replayed, NotFound, Conflict, Invalid, NotEligible, ProviderUnavailable }

public sealed record PaymentCheckoutResult(PaymentCheckoutOutcome Outcome, PaymentCheckoutResponse? Checkout = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);

public interface IPaymentCheckoutService
{
    Task<IndividualPurchasePlanCatalog> ListPlansAsync(Guid accountId, CancellationToken cancellationToken);
    Task<PaymentCheckoutResult> StartAsync(Guid accountId, StartPaymentCheckoutRequest request, CancellationToken cancellationToken);
}

/// <summary>Payments-owned read ports keep cross-module checks narrow and read-only.</summary>
public interface IPaymentAccountEligibilityReader
{
    Task<bool> IsIndividualCreatorAsync(Guid accountId, CancellationToken cancellationToken);
}

public interface IPaymentInvitationEligibilityReader
{
    Task<bool> IsOwnedAndAvailableAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
}

public sealed record PurchasablePlanSnapshot(Guid PlanId, string Key, string DisplayName, decimal Amount, string Currency,
    PurchasableBillingKind BillingKind);

public enum PurchasableBillingKind { OneTime, Monthly }

public interface IPaymentPlanCatalogReader
{
    Task<IReadOnlyList<IndividualPurchasePlan>> ListIndividualPlansAsync(CancellationToken cancellationToken);
    Task<PurchasablePlanSnapshot?> FindIndividualPlanAsync(string planKey, CancellationToken cancellationToken);
    Task<bool> HasActivePaidGrantAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
}

/// <summary>Provider-neutral creation request; all commercial fields come from the server catalog.</summary>
public sealed record ProviderCheckoutRequest(string Reference, string PlanKey, decimal Amount, string Currency,
    string SuccessUrl, string CancelUrl, string IdempotencyKey);

public sealed record ProviderCheckoutResult(string CheckoutUrl, string? ProviderCheckoutId);

public interface IPaymentGateway
{
    Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken);
    bool IsTrustedCheckoutUrl(string checkoutUrl);
}

/// <summary>Provider-neutral authoritative payment-result lookup used before entitlement changes.</summary>
public interface IPaymentResultVerifier
{
    Task<ProviderPaymentVerificationResult> RetrievePaymentAsync(
        string paymentId,
        string checkoutToken,
        string conversationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Authoritative server-to-server payment lookup. Provider adapters return Verified only after
/// authenticating the API response; callers must still reconcile every field with their immutable
/// local purchase snapshot before changing business state.
/// </summary>
public sealed record ProviderPaymentVerificationResult(
    ProviderPaymentVerificationOutcome Outcome,
    string? PaymentId = null,
    string? Currency = null,
    string? BasketId = null,
    string? ConversationId = null,
    decimal? Price = null,
    decimal? PaidPrice = null,
    string? ProviderResponseStatus = null,
    string? PaymentStatus = null,
    int? FraudStatus = null,
    string? CheckoutToken = null);

public enum ProviderPaymentVerificationOutcome
{
    Verified,
    Unavailable,
    InvalidResponse
}
