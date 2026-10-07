using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Infrastructure.Modules.Media;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class CloudflarePrivateMediaDeliveryGatewayTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    private static readonly string EncodedKey = Base64Url(Key);
    private static readonly Guid Asset = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private static readonly DateTimeOffset Expiry = new(2026, 10, 4, 13, 0, 0, TimeSpan.Zero);
    private const int ResponseByteLimit = 8 * 1024;

    [Fact]
    public async Task Image_delivery_url_is_asset_bound_expiring_and_signed_without_provider_origin()
    {
        var gateway = CreateGateway(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var result = await gateway.CreateImageCapabilityAsync(Asset, Expiry, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("https://media.example.test/v1/delivery/images/12345678123412341234123456789abc", result.Value.Url.GetLeftPart(UriPartial.Path));
        Assert.Equal(Expiry, result.Value.ExpiresAt);
        var token = Uri.UnescapeDataString(result.Value.Url.Query["?capability=".Length..]);
        var parts = token.Split('.');
        Assert.Equal(2, parts.Length);
        Assert.Equal(HMACSHA256.HashData(Key, Encoding.ASCII.GetBytes(parts[0])), Decode(parts[1]));
        using var claims = JsonDocument.Parse(Decode(parts[0]));
        Assert.Equal("davetiye-image-delivery", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(Asset.ToString("N"), claims.RootElement.GetProperty("assetId").GetString());
        Assert.Equal(Expiry.ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
    }

    [Fact]
    public async Task Video_session_sets_expiry_and_builds_only_the_configured_embed_url()
    {
        string? target = null;
        string? authorization = null;
        string? body = null;
        var handler = new Handler(request =>
        {
            target = request.RequestUri!.ToString();
            authorization = request.Headers.GetValues("X-Broker-Authorization").Single();
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { token = "signed.jwt.token", expiresAt = Expiry.ToUnixTimeSeconds() }),
            };
        });
        var gateway = CreateGateway(handler);
        var result = await gateway.CreateVideoSessionAsync(Asset, Expiry, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("https://customer-test.cloudflarestream.com/signed.jwt.token/iframe", result.Value.Url.ToString());
        Assert.Equal(Expiry, result.Value.ExpiresAt);
        Assert.Equal("https://media.example.test/v1/stream/playback-sessions", target);
        Assert.Equal("Bearer broker-secret-material-at-least-32-bytes", authorization);
        using var request = JsonDocument.Parse(body!);
        Assert.Equal(Asset.ToString("N"), request.RootElement.GetProperty("assetId").GetString());
        Assert.Equal(Expiry.ToUnixTimeSeconds(), request.RootElement.GetProperty("exp").GetInt64());
    }

    [Fact]
    public async Task Video_session_rejects_a_declared_oversized_response_before_reading_it()
    {
        var content = new BoundedTestContent(new byte[ResponseByteLimit + 1], ResponseByteLimit + 1);
        var gateway = CreateGateway(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        await Assert.ThrowsAsync<HttpRequestException>(() => gateway.CreateVideoSessionAsync(Asset, Expiry, CancellationToken.None));
        Assert.False(content.Opened);
    }

    [Fact]
    public async Task Video_session_rejects_an_oversized_unknown_length_response_while_streaming()
    {
        var content = new BoundedTestContent(new byte[ResponseByteLimit + 1], null);
        var gateway = CreateGateway(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        await Assert.ThrowsAsync<HttpRequestException>(() => gateway.CreateVideoSessionAsync(Asset, Expiry, CancellationToken.None));
        Assert.True(content.Opened);
    }

    [Theory]
    [InlineData(59)]
    [InlineData(3601)]
    public void Playback_ttl_configuration_has_a_bounded_policy(int seconds)
    {
        var options = new CloudflareMediaOptions
        {
            Enabled = true,
            WorkerBaseUrl = "https://media.example.test",
            ImageCapabilitySigningKeyBase64Url = EncodedKey,
            WorkerBrokerAuthorizationKey = "broker-secret-material-at-least-32-bytes",
            StreamWebhookSecret = "webhook-secret-material-128-bits",
            StreamCustomerHostname = "customer-test.cloudflarestream.com",
            MaximumVideoPlaybackSessionSeconds = seconds,
        };
        Assert.False(new CloudflareMediaOptionsValidator().Validate(null, options).Succeeded);
    }

    private static CloudflarePrivateMediaDeliveryGateway CreateGateway(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(CreateOptions()));

    private static CloudflareMediaOptions CreateOptions() => new()
    {
        Enabled = true,
        WorkerBaseUrl = "https://media.example.test",
        ImageCapabilitySigningKeyBase64Url = EncodedKey,
        WorkerBrokerAuthorizationKey = "broker-secret-material-at-least-32-bytes",
        StreamWebhookSecret = "webhook-secret-material-128-bits",
        StreamCustomerHostname = "customer-test.cloudflarestream.com",
        MaximumVideoPlaybackSessionSeconds = 1800,
    };

    private static string Base64Url(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class BoundedTestContent(byte[] body, long? contentLength) : HttpContent
    {
        public bool Opened { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();
        protected override bool TryComputeLength(out long length)
        {
            length = contentLength ?? 0;
            return contentLength.HasValue;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            Opened = true;
            return Task.FromResult<Stream>(new MemoryStream(body, writable: false));
        }
    }
}
