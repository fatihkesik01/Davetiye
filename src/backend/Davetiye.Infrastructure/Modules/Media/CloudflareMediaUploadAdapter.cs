using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>
/// Issues short-lived Worker image capabilities and asks the Worker Durable Object to provision
/// an idempotent, byte-bounded Stream TUS session. Cloudflare master credentials remain in Worker
/// secrets; this adapter sends only a server-to-server broker authorization key.
/// </summary>
public sealed class CloudflareMediaUploadAdapter(HttpClient httpClient, IOptions<CloudflareMediaOptions> options)
    : IMediaUploadGateway, IMediaImageNormalizationPipeline
{
    private const int MaximumBrokerResponseBytes = 8 * 1024;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly CloudflareMediaOptions _options = options.Value;

    public Task<MediaUploadCapability> CreateNormalizedImageIngressAsync(
        MediaUploadRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, MediaKind.Image);
        var expiresAtSeconds = request.ExpiresAt.ToUnixTimeSeconds();
        var claims = new ImageUploadCapabilityClaims(1, "davetiye-image-ingress",
            request.AssetId.ToString("N"), request.MaximumBytes, expiresAtSeconds);
        var claimsBytes = JsonSerializer.SerializeToUtf8Bytes(claims, WebJson);
        var encodedClaims = Base64Url(claimsBytes);
        var signature = HMACSHA256.HashData(DecodeBase64Url(_options.ImageCapabilitySigningKeyBase64Url),
            Encoding.ASCII.GetBytes(encodedClaims));
        var token = $"{encodedClaims}.{Base64Url(signature)}";
        var ingress = new Uri(new Uri(_options.WorkerBaseUrl.TrimEnd('/') + "/"),
            $"v1/images/{request.AssetId:N}");
        return Task.FromResult(new MediaUploadCapability(ingress,
            DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Media-Capability"] = token }));
    }

    public async Task<MediaUploadCapability> CreateVideoCapabilityAsync(
        MediaUploadRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, MediaKind.Video);
        if (request.MaximumDurationSeconds <= 0)
        {
            throw new InvalidOperationException("A positive effective video-duration entitlement is required.");
        }

        var brokerUri = new Uri(new Uri(_options.WorkerBaseUrl.TrimEnd('/') + "/"), "v1/stream/upload-capabilities");
        using var message = new HttpRequestMessage(HttpMethod.Post, brokerUri);
        message.Headers.TryAddWithoutValidation("X-Broker-Authorization", $"Bearer {_options.WorkerBrokerAuthorizationKey}");
        message.Content = JsonContent.Create(new StreamProvisionRequest(
            request.AssetId.ToString("N"), request.MaximumBytes, request.MaximumBytes,
            request.ExpiresAt.ToUniversalTime(), request.MaximumDurationSeconds), options: WebJson);

        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.Gone)
        {
            throw new HttpRequestException("The Stream upload request conflicts with the existing asset capability.",
                null, response.StatusCode);
        }
        response.EnsureSuccessStatusCode();

        StreamProvisionResponse? result;
        try
        {
            result = await ReadBoundedJsonAsync<StreamProvisionResponse>(response, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("The media ingress broker returned an unreadable response.", exception);
        }
        if (result is null || !Uri.TryCreate(result.UploadUrl, UriKind.Absolute, out var uploadUri) ||
            uploadUri.Scheme != Uri.UriSchemeHttps || uploadUri.Host != "upload.videodelivery.net" ||
            uploadUri.Port != 443 || !string.IsNullOrEmpty(uploadUri.UserInfo) || !string.IsNullOrEmpty(uploadUri.Fragment) ||
            !DateTimeOffset.TryParse(result.ExpiresAt, out var expiresAt))
        {
            throw new HttpRequestException("The media ingress broker returned an invalid TUS capability.");
        }

        return new MediaUploadCapability(uploadUri, expiresAt.ToUniversalTime());
    }

    public async Task<MediaProviderVideoInspection?> InspectVideoAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var result = await GetInspectionAsync<VideoInspectionResponse>(
            $"v1/provider/inspections/videos/{assetId:N}", cancellationToken);
        if (result is null || !Guid.TryParseExact(result.AssetId, "N", out var inspectedAsset) || inspectedAsset != assetId ||
            string.IsNullOrWhiteSpace(result.Uid) || result.Uid.Length > 512 || result.Uid.Any(char.IsControl))
        {
            return null;
        }

        MediaVerificationEvidence? evidence = null;
        if (result.State == "ready" && result.ReadyToStream && result.ByteLength is > 0 &&
            result.DurationSeconds is > 0 && result.ContentType is not null &&
            result.ContentType is "video/mp4" or "video/webm" or "video/quicktime" or "video/x-m4v")
        {
            evidence = new MediaVerificationEvidence(result.Uid, result.ContentType,
                result.ByteLength.Value, result.DurationSeconds.Value);
        }

        return new MediaProviderVideoInspection(result.Uid, result.State ?? string.Empty, result.ReadyToStream,
            result.ByteLength, result.DurationSeconds, result.ContentType, evidence);
    }

    public async Task<NormalizedImageVerificationEvidence?> VerifyStoredImageAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var result = await GetInspectionAsync<ImageInspectionResponse>(
            $"v1/provider/inspections/images/{assetId:N}", cancellationToken);
        if (result is null || !Guid.TryParseExact(result.AssetId, "N", out var inspectedAsset) || inspectedAsset != assetId ||
            !result.Exists || !result.Normalized || result.ContentType != "image/webp" || result.ByteLength <= 0)
        {
            return null;
        }

        return new NormalizedImageVerificationEvidence($"creator/{assetId:N}.webp", result.ContentType, result.ByteLength);
    }

    public Task DeleteAsync(string providerObjectReference, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("Media deletion is not part of P4-M3."));

    private static void ValidateRequest(MediaUploadRequest request, MediaKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AssetId == Guid.Empty || request.Kind != expectedKind ||
            !Enum.IsDefined(request.QuotaScope) || request.MaximumBytes <= 0 ||
            request.ExpiresAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The media request is invalid for this provider ingress.", nameof(request));
        }
    }

    private async Task<T?> GetInspectionAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        var uri = new Uri(new Uri(_options.WorkerBaseUrl.TrimEnd('/') + "/"), path);
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.TryAddWithoutValidation("X-Broker-Authorization", $"Bearer {_options.WorkerBrokerAuthorizationKey}");
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        try
        {
            return await ReadBoundedJsonAsync<T>(response, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("The media inspection service returned an unreadable response.", exception);
        }
    }

    private static async Task<T?> ReadBoundedJsonAsync<T>(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = response.Content;
        if (content.Headers.ContentLength is > MaximumBrokerResponseBytes)
            throw new HttpRequestException("The media broker response exceeded the allowed size.");

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(content.Headers.ContentLength is > 0 and <= MaximumBrokerResponseBytes
            ? (int)content.Headers.ContentLength.Value : 1024);
        var chunk = new byte[1024];
        while (true)
        {
            var read = await input.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumBrokerResponseBytes)
                throw new HttpRequestException("The media broker response exceeded the allowed size.");
            await output.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return JsonSerializer.Deserialize<T>(output.ToArray(), WebJson);
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    private sealed record ImageUploadCapabilityClaims(int V, string Aud, string AssetId, long MaxBytes, long Exp);
    private sealed record StreamProvisionRequest(string AssetId, long DeclaredByteLength, long MaximumBytes,
        DateTimeOffset ExpiresAt, long MaxDurationSeconds);
    private sealed record StreamProvisionResponse(string UploadUrl, string ExpiresAt);
    private sealed record ImageInspectionResponse(string AssetId, bool Exists, bool Normalized, string ContentType, long ByteLength);
    private sealed record VideoInspectionResponse(string AssetId, string Uid, string State, bool ReadyToStream,
        long? ByteLength, int? DurationSeconds, string? ContentType);
}
