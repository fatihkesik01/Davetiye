using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Davetiye.Application.Modules.Media.Contracts;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

public sealed class CloudflarePrivateMediaDeliveryGateway(HttpClient client, IOptions<CloudflareMediaOptions> options)
    : IPrivateMediaDeliveryGateway
{
    private const int MaximumPlaybackResponseBytes = 8 * 1024;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly CloudflareMediaOptions settings = options.Value;

    public Task<(Uri Url, DateTimeOffset ExpiresAt)?> CreateImageCapabilityAsync(Guid assetId,
        DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (!settings.Enabled || assetId == Guid.Empty || expiresAt.Offset != TimeSpan.Zero) return Task.FromResult<(Uri, DateTimeOffset)?>(null);
        var claims = new ImageDeliveryClaims(1, "davetiye-image-delivery", assetId.ToString("N"), expiresAt.ToUnixTimeSeconds());
        var encoded = Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims, WebJson));
        var key = DecodeBase64Url(settings.ImageCapabilitySigningKeyBase64Url);
        var signature = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(encoded));
        var token = encoded + "." + Base64Url(signature);
        var url = new Uri(new Uri(settings.WorkerBaseUrl.TrimEnd('/') + "/"), $"v1/delivery/images/{assetId:N}?capability={Uri.EscapeDataString(token)}");
        return Task.FromResult<(Uri Url, DateTimeOffset ExpiresAt)?>( (url, expiresAt) );
    }

    public async Task<(Uri Url, DateTimeOffset ExpiresAt)?> CreateVideoSessionAsync(Guid assetId,
        DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (!settings.Enabled || assetId == Guid.Empty || expiresAt.Offset != TimeSpan.Zero) return null;
        var uri = new Uri(new Uri(settings.WorkerBaseUrl.TrimEnd('/') + "/"), "v1/stream/playback-sessions");
        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        message.Headers.TryAddWithoutValidation("X-Broker-Authorization", $"Bearer {settings.WorkerBrokerAuthorizationKey}");
        message.Content = JsonContent.Create(new PlaybackSessionRequest(assetId.ToString("N"), expiresAt.ToUnixTimeSeconds()), options: WebJson);
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.Gone) return null;
        response.EnsureSuccessStatusCode();
        PlaybackSessionResponse? result;
        try
        {
            var body = await ReadBoundedResponseAsync(response.Content, cancellationToken);
            result = JsonSerializer.Deserialize<PlaybackSessionResponse>(body, WebJson);
        }
        catch (JsonException exception) { throw new HttpRequestException("The media broker returned an unreadable playback session.", exception); }
        if (result is null || string.IsNullOrWhiteSpace(result.Token) || result.Token.Length > 4096 ||
            result.Token.Any(char.IsControl) || result.ExpiresAt != expiresAt.ToUnixTimeSeconds()) return null;
        var url = new UriBuilder(Uri.UriSchemeHttps, settings.StreamCustomerHostname)
        {
            Path = $"/{Uri.EscapeDataString(result.Token)}/iframe"
        }.Uri;
        return (url, DateTimeOffset.FromUnixTimeSeconds(result.ExpiresAt));
    }

    private static async Task<byte[]> ReadBoundedResponseAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumPlaybackResponseBytes)
            throw new HttpRequestException("The media broker playback session response exceeded the allowed size.");

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(Math.Min(content.Headers.ContentLength is > 0 and <= MaximumPlaybackResponseBytes
            ? (int)content.Headers.ContentLength.Value : 1024, MaximumPlaybackResponseBytes));
        var chunk = new byte[2048];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumPlaybackResponseBytes)
                throw new HttpRequestException("The media broker playback session response exceeded the allowed size.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string Base64Url(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }
    private sealed record ImageDeliveryClaims(int V, string Aud, string AssetId, long Exp);
    private sealed record PlaybackSessionRequest(string AssetId, long Exp);
    private sealed record PlaybackSessionResponse(string Token, long ExpiresAt);
}
