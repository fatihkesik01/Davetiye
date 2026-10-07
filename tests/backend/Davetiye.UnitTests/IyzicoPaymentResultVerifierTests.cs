using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Application.Modules.Payments.Contracts;
using Davetiye.Infrastructure.Modules.Payments;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class IyzicoPaymentResultVerifierTests
{
    private const string ApiKey = "test-api-key";
    private const string SecretKey = "test-secret-key";
    private const string PaymentId = "123456789";
    private const string Reference = "dv0123456789abcdef0123456789abcdef";
    private const string CheckoutToken = "cf311111-2222-4333-8444-555555555555";

    [Fact]
    public async Task Retrieve_uses_checkout_form_token_endpoint_and_validates_documented_response_signature()
    {
        HttpMethod? capturedMethod = null;
        string? capturedUri = null;
        string? capturedContentType = null;
        string? capturedRandomKey = null;
        string? capturedAuthorization = null;
        string? capturedBody = null;
        var responseBody = CreateResponse(PaymentId, Reference, Reference, CheckoutToken, "699.00", "699.000", "SUCCESS", 1);
        var verifier = CreateVerifier(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
            return response;
        }, onRequest: request =>
        {
            capturedMethod = request.Method;
            capturedUri = request.RequestUri!.ToString();
            capturedContentType = request.Content!.Headers.ContentType!.MediaType;
            capturedRandomKey = request.Headers.GetValues("x-iyzi-rnd").Single();
            capturedAuthorization = request.Headers.GetValues("Authorization").Single();
            capturedBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        });

        var result = await verifier.RetrievePaymentAsync(PaymentId, CheckoutToken, Reference, CancellationToken.None);

        Assert.Equal(ProviderPaymentVerificationOutcome.Verified, result.Outcome);
        Assert.Equal(PaymentId, result.PaymentId);
        Assert.Equal(Reference, result.BasketId);
        Assert.Equal(Reference, result.ConversationId);
        Assert.Equal(699m, result.Price);
        Assert.Equal(699m, result.PaidPrice);
        Assert.Equal("SUCCESS", result.PaymentStatus);
        Assert.Equal(1, result.FraudStatus);
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("https://api.iyzipay.com/payment/iyzipos/checkoutform/auth/ecom/detail", capturedUri);
        Assert.Equal("application/json", capturedContentType);
        var randomKey = Assert.IsType<string>(capturedRandomKey);
        var body = Assert.IsType<string>(capturedBody);
        var expected = IyzicoPaymentResultVerifier.CreateAuthorization(ApiKey, SecretKey, randomKey,
            "/payment/iyzipos/checkoutform/auth/ecom/detail", Encoding.UTF8.GetBytes(body));
        Assert.Equal(expected, capturedAuthorization);
        Assert.Contains("\"token\":\"cf311111-2222-4333-8444-555555555555\"", body, StringComparison.Ordinal);
        Assert.Contains("\"conversationId\":\"dv0123456789abcdef0123456789abcdef\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"paymentId\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Response_signature_mismatch_fails_closed()
    {
        var body = CreateResponse(PaymentId, Reference, Reference, CheckoutToken, "699", "699", "SUCCESS", 1, signatureOverride: new string('0', 64));
        var verifier = CreateVerifier(_ => JsonResponse(body));

        var result = await verifier.RetrievePaymentAsync(PaymentId, CheckoutToken, Reference, CancellationToken.None);

        Assert.Equal(ProviderPaymentVerificationOutcome.InvalidResponse, result.Outcome);
    }

    [Theory]
    [InlineData("paymentStatus", "FAILURE")]
    [InlineData("token", "cf399999-2222-4333-8444-555555555555")]
    public async Task Tampered_checkout_form_signed_field_fails_closed(string field, string tamperedValue)
    {
        var body = CreateResponse(PaymentId, Reference, Reference, CheckoutToken, "699", "699", "SUCCESS", 1)
            .Replace($"\"{field}\":\"{(field == "paymentStatus" ? "SUCCESS" : CheckoutToken)}\"",
                $"\"{field}\":\"{tamperedValue}\"", StringComparison.Ordinal);
        var verifier = CreateVerifier(_ => JsonResponse(body));

        // Use the changed token as the expected stored token in that case. The only remaining
        // rejection reason is the stale response HMAC, proving token is part of its preimage.
        var expectedCheckoutToken = field == "token" ? tamperedValue : CheckoutToken;
        var result = await verifier.RetrievePaymentAsync(PaymentId, expectedCheckoutToken, Reference, CancellationToken.None);

        Assert.Equal(ProviderPaymentVerificationOutcome.InvalidResponse, result.Outcome);
    }

    [Fact]
    public async Task Duplicate_identity_field_fails_closed()
    {
        var body = CreateResponse(PaymentId, Reference, Reference, CheckoutToken, "699", "699", "SUCCESS", 1)
            .Replace("\"paymentId\":\"123456789\"", "\"paymentId\":\"123456789\",\"paymentId\":\"123456789\"", StringComparison.Ordinal);
        var verifier = CreateVerifier(_ => JsonResponse(body));

        var result = await verifier.RetrievePaymentAsync(PaymentId, CheckoutToken, Reference, CancellationToken.None);

        Assert.Equal(ProviderPaymentVerificationOutcome.InvalidResponse, result.Outcome);
    }

    [Fact]
    public async Task Oversized_provider_response_body_is_rejected_before_json_processing()
    {
        var oversized = new string(' ', 65 * 1024);
        var verifier = CreateVerifier(_ => JsonResponse(oversized));

        var result = await verifier.RetrievePaymentAsync(PaymentId, CheckoutToken, Reference, CancellationToken.None);

        Assert.Equal(ProviderPaymentVerificationOutcome.InvalidResponse, result.Outcome);
    }

    [Fact]
    public async Task Blank_credentials_and_untrusted_loopback_base_url_do_not_send_request()
    {
        var requests = 0;
        using var client = new HttpClient(new RecordingHandler(_ =>
        {
            requests++;
            return JsonResponse("{}");
        }));
        var options = Options.Create(new IyzicoPaymentApiOptions
        {
            ApiBaseUrl = "http://127.0.0.1:8080",
            ApiKey = ApiKey,
            SecretKey = SecretKey
        });
        var verifier = new IyzicoPaymentResultVerifier(client, options);

        var result = await verifier.RetrievePaymentAsync(PaymentId, CheckoutToken, Reference, CancellationToken.None);

        Assert.False(options.Value.IsConfigured);
        Assert.Equal(ProviderPaymentVerificationOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Provider_status_fields_are_taken_from_authenticated_api_response_but_not_claimed_as_hmac_signed()
    {
        // Checkout Form Retrieve signs paymentStatus, paymentId, currency, basketId, conversationId,
        // paidPrice, price, and token. Top-level `status` and `fraudStatus` are not part of that HMAC;
        // they are read only from the credential-authenticated HTTPS response and gated by the processor.
        var body = CreateResponse(PaymentId, Reference, Reference, CheckoutToken, "699", "699", "FAILURE", -1,
            responseStatus: "success");
        var verifier = CreateVerifier(_ => JsonResponse(body));

        var result = await verifier.RetrievePaymentAsync(PaymentId, CheckoutToken, Reference, CancellationToken.None);

        Assert.Equal(ProviderPaymentVerificationOutcome.Verified, result.Outcome);
        Assert.Equal("success", result.ProviderResponseStatus);
        Assert.Equal("FAILURE", result.PaymentStatus);
        Assert.Equal(-1, result.FraudStatus);
    }

    [Fact]
    public void Production_handler_disables_redirects()
    {
        using var handler = IyzicoPaymentResultVerifier.CreatePrimaryHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    private static IyzicoPaymentResultVerifier CreateVerifier(
        Func<HttpRequestMessage, HttpResponseMessage> send,
        Action<HttpRequestMessage>? onRequest = null)
    {
        var client = new HttpClient(new RecordingHandler(request =>
        {
            onRequest?.Invoke(request);
            return send(request);
        }));
        return new IyzicoPaymentResultVerifier(client, Options.Create(new IyzicoPaymentApiOptions
        {
            ApiKey = ApiKey,
            SecretKey = SecretKey,
            ApiBaseUrl = IyzicoPaymentApiOptions.DefaultApiBaseUrl
        }));
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string CreateResponse(
        string paymentId,
        string basketId,
        string conversationId,
        string token,
        string price,
        string paidPrice,
        string paymentStatus,
        int fraudStatus,
        string responseStatus = "success",
        string? signatureOverride = null)
    {
        var normalizedPrice = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)
            .ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
        var normalizedPaidPrice = decimal.Parse(paidPrice, System.Globalization.CultureInfo.InvariantCulture)
            .ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
        var signedFields = string.Join(':', paymentStatus, paymentId, "TRY", basketId, conversationId,
            normalizedPaidPrice, normalizedPrice, token);
        var signature = signatureOverride ?? Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SecretKey), Encoding.UTF8.GetBytes(signedFields))).ToLowerInvariant();
        return $$"""{"status":"{{responseStatus}}","paymentStatus":"{{paymentStatus}}","paymentId":"{{paymentId}}","currency":"TRY","basketId":"{{basketId}}","conversationId":"{{conversationId}}","paidPrice":{{paidPrice}},"price":{{price}},"token":"{{token}}","fraudStatus":{{fraudStatus}},"signature":"{{signature}}"}""";
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
