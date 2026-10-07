using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Davetiye.Infrastructure.Modules.Media;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class CloudflareMediaUploadAdapterTests
{
    private static readonly byte[] CapabilityKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly string CapabilityKeyBase64Url = Base64Url(CapabilityKey);
    private static readonly DateTimeOffset ExpiresAt = new(2026, 10, 4, 12, 15, 0, TimeSpan.Zero);
    private static readonly Guid AssetId = Guid.Parse("12345678-1234-1234-1234-123456789abc");

    [Fact]
    public async Task Image_capability_is_asset_bound_expiring_and_sent_outside_the_url()
    {
        var assetId = Guid.NewGuid();
        var adapter = CreateAdapter(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var capability = await adapter.CreateNormalizedImageIngressAsync(
            new MediaUploadRequest(assetId, MediaKind.Image, MediaQuotaScope.Creator, 5 * 1024 * 1024, ExpiresAt),
            CancellationToken.None);

        Assert.Equal($"https://media.example.test/v1/images/{assetId:N}", capability.IngressUri.ToString());
        Assert.Equal(ExpiresAt.ToUnixTimeSeconds(), capability.ExpiresAt.ToUnixTimeSeconds());
        Assert.False(capability.IngressUri.Query.Contains("capability", StringComparison.OrdinalIgnoreCase));
        var token = Assert.Single(capability.IngressHeaders!).Value;
        var parts = token.Split('.');
        Assert.Equal(2, parts.Length);
        var expectedSignature = HMACSHA256.HashData(CapabilityKey, Encoding.ASCII.GetBytes(parts[0]));
        Assert.Equal(expectedSignature, DecodeBase64Url(parts[1]));
        using var claims = JsonDocument.Parse(DecodeBase64Url(parts[0]));
        Assert.Equal(assetId.ToString("N"), claims.RootElement.GetProperty("assetId").GetString());
        Assert.Equal(5 * 1024 * 1024, claims.RootElement.GetProperty("maxBytes").GetInt64());
        Assert.Equal(ExpiresAt.ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
    }

    [Fact]
    public async Task Guest_scope_images_use_the_same_normalizing_ingress_bound_to_one_asset()
    {
        var assetId = Guid.NewGuid();
        var adapter = CreateAdapter(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var capability = await adapter.CreateNormalizedImageIngressAsync(
            new MediaUploadRequest(assetId, MediaKind.Image, MediaQuotaScope.Guest, 10 * 1024 * 1024, ExpiresAt),
            CancellationToken.None);

        Assert.Equal($"https://media.example.test/v1/images/{assetId:N}", capability.IngressUri.ToString());
        Assert.Single(capability.IngressHeaders!);
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.CreateNormalizedImageIngressAsync(
            new MediaUploadRequest(assetId, MediaKind.Image, (MediaQuotaScope)99, 1024, ExpiresAt), CancellationToken.None));
    }

    [Fact]
    public async Task Video_capability_provisioning_uses_effective_limits_and_broker_auth()
    {
        string? capturedUri = null;
        string? capturedAuthorization = null;
        string? capturedBody = null;
        var handler = new CapturingHandler(request =>
        {
            capturedUri = request.RequestUri!.ToString();
            capturedAuthorization = request.Headers.GetValues("X-Broker-Authorization").Single();
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    uploadUrl = "https://upload.videodelivery.net/provider-bearer-id",
                    expiresAt = ExpiresAt.ToString("O"),
                }),
            };
        });
        var adapter = CreateAdapter(handler);
        var assetId = Guid.NewGuid();

        var capability = await adapter.CreateVideoCapabilityAsync(
            new MediaUploadRequest(assetId, MediaKind.Video, MediaQuotaScope.Creator, 250_000_000, ExpiresAt, 600),
            CancellationToken.None);

        Assert.Equal("https://upload.videodelivery.net/provider-bearer-id", capability.IngressUri.ToString());
        Assert.Null(capability.IngressHeaders);
        Assert.Equal("https://media.example.test/v1/stream/upload-capabilities", capturedUri);
        Assert.Equal("Bearer test-broker-secret-material-at-least-32-bytes", capturedAuthorization);
        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal(assetId.ToString("N"), body.RootElement.GetProperty("assetId").GetString());
        Assert.Equal(250_000_000, body.RootElement.GetProperty("declaredByteLength").GetInt64());
        Assert.Equal(250_000_000, body.RootElement.GetProperty("maximumBytes").GetInt64());
        Assert.Equal(600, body.RootElement.GetProperty("maxDurationSeconds").GetInt64());
    }

    [Fact]
    public async Task Video_capability_rejects_provider_locations_on_nonstandard_ports()
    {
        var adapter = CreateAdapter(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                uploadUrl = "https://upload.videodelivery.net:8443/provider-bearer-id",
                expiresAt = ExpiresAt.ToString("O"),
            }),
        }));

        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.CreateVideoCapabilityAsync(
            new MediaUploadRequest(Guid.NewGuid(), MediaKind.Video, MediaQuotaScope.Creator, 250_000_000, ExpiresAt, 600),
            CancellationToken.None));
    }

    [Fact]
    public async Task Video_capability_rejects_oversized_declared_broker_response_before_reading_body()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}")),
        };
        response.Content.Headers.ContentLength = 8 * 1024 + 1;
        var adapter = CreateAdapter(new CapturingHandler(_ => response));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => adapter.CreateVideoCapabilityAsync(
            new MediaUploadRequest(Guid.NewGuid(), MediaKind.Video, MediaQuotaScope.Creator, 250_000_000, ExpiresAt, 600),
            CancellationToken.None));

        Assert.Contains("exceeded the allowed size", exception.Message);
    }

    [Fact]
    public async Task Image_inspection_rejects_chunked_broker_response_over_limit()
    {
        var oversizedJson = Encoding.UTF8.GetBytes(
            $"{{\"assetId\":\"{AssetId:N}\",\"exists\":true,\"normalized\":true,\"contentType\":\"image/webp\",\"byteLength\":2048}}" +
            new string(' ', 8 * 1024));
        var adapter = CreateAdapter(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(oversizedJson),
        }));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            adapter.VerifyStoredImageAsync(AssetId, CancellationToken.None));

        Assert.Contains("exceeded the allowed size", exception.Message);
    }

    [Fact]
    public async Task Image_verification_uses_authenticated_private_worker_inspection_and_requires_normalized_webp()
    {
        string? uri = null;
        string? authorization = null;
        var adapter = CreateAdapter(new CapturingHandler(request =>
        {
            uri = request.RequestUri!.ToString();
            authorization = request.Headers.GetValues("X-Broker-Authorization").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    assetId = AssetId.ToString("N"), exists = true, normalized = true,
                    contentType = "image/webp", byteLength = 2048,
                }),
            };
        }));

        var evidence = await adapter.VerifyStoredImageAsync(AssetId, CancellationToken.None);

        Assert.Equal($"https://media.example.test/v1/provider/inspections/images/{AssetId:N}", uri);
        Assert.Equal("Bearer test-broker-secret-material-at-least-32-bytes", authorization);
        Assert.Equal($"creator/{AssetId:N}.webp", evidence!.ProviderObjectReference);
        Assert.Equal("image/webp", evidence.DetectedContentType);
        Assert.Equal(2048, evidence.ByteLength);
    }

    [Fact]
    public async Task Video_inspection_returns_provider_uid_and_only_trusted_metadata()
    {
        var adapter = CreateAdapter(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                assetId = AssetId.ToString("N"), uid = "provider-stream-uid", state = "ready",
                readyToStream = true, byteLength = 9000, durationSeconds = 120, contentType = "video/mp4",
            }),
        }));

        var inspection = await adapter.InspectVideoAsync(AssetId, CancellationToken.None);

        Assert.Equal("provider-stream-uid", inspection!.ProviderUid);
        Assert.Equal(9000, inspection.ByteLength);
        Assert.Equal(120, inspection.DurationSeconds);
        Assert.Equal("video/mp4", inspection.ContentType);
        Assert.Equal("video/mp4", inspection.Evidence!.DetectedContentType);
    }

    private static CloudflareMediaUploadAdapter CreateAdapter(HttpMessageHandler handler) => new(
        new HttpClient(handler, disposeHandler: false),
        Options.Create(new CloudflareMediaOptions
        {
            Enabled = true,
            WorkerBaseUrl = "https://media.example.test/",
            ImageCapabilitySigningKeyBase64Url = CapabilityKeyBase64Url,
            WorkerBrokerAuthorizationKey = "test-broker-secret-material-at-least-32-bytes",
        }));

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> createResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(createResponse(request));
    }

    private sealed class UnknownLengthContent(byte[] payload) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(payload, 0, payload.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
