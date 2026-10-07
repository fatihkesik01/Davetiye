using Davetiye.Application.Modules.Payments.Contracts;

namespace Davetiye.Infrastructure.Modules.Payments;

/// <summary>Phase 11 provider acceptance is a release gate; non-development checkout fails closed.</summary>
public sealed class UnavailablePaymentGateway : IPaymentGateway, IPaymentResultVerifier
{
    public Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("A verified payment provider is not configured for this environment.");

    public bool IsTrustedCheckoutUrl(string checkoutUrl) => false;

    public Task<ProviderPaymentVerificationResult> RetrievePaymentAsync(
        string paymentId,
        string checkoutToken,
        string conversationId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderPaymentVerificationResult(ProviderPaymentVerificationOutcome.Unavailable));
}
