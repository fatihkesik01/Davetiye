using Davetiye.Application.Modules.Payments.Contracts;

namespace Davetiye.Infrastructure.Modules.Payments;

/// <summary>
/// Development-only deterministic provider adapter. It creates a stable local checkout locator but
/// does not report payment success; only a future verified provider event can change payment state.
/// </summary>
public sealed class FakePaymentGateway(
    string baseUrl,
    Func<string, ProviderPaymentVerificationResult>? verification = null) : IPaymentGateway, IPaymentResultVerifier
{
    private readonly Uri _baseUri = Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp &&
            (parsed.Host == "localhost" || parsed.Host == "127.0.0.1")))
            ? parsed
            : throw new ArgumentException("Fake checkout base URL must be HTTPS or loopback HTTP.", nameof(baseUrl));

    public Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checkoutUrl = new Uri(_baseUri, Uri.EscapeDataString(request.Reference)).ToString();
        return Task.FromResult(new ProviderCheckoutResult(checkoutUrl, $"fake-{request.Reference}"));
    }

    public bool IsTrustedCheckoutUrl(string checkoutUrl)
    {
        if (!Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var candidate)) return false;
        return string.Equals(candidate.Scheme, _baseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.IdnHost, _baseUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            candidate.Port == _baseUri.Port &&
            string.IsNullOrEmpty(candidate.UserInfo) &&
            candidate.AbsolutePath.StartsWith(_baseUri.AbsolutePath, StringComparison.Ordinal);
    }

    public Task<ProviderPaymentVerificationResult> RetrievePaymentAsync(
        string paymentId,
        string checkoutToken,
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = verification?.Invoke(paymentId) ??
            new ProviderPaymentVerificationResult(ProviderPaymentVerificationOutcome.Unavailable);
        return Task.FromResult(result);
    }
}
