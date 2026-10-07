using System.Net;
using System.Text.Json;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Domain.Modules.Media;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>Broker-authenticated operations address assets only by the application-generated asset id.</summary>
public sealed class CloudflareMediaAssetLifecycleGateway(HttpClient client, IOptions<CloudflareMediaOptions> options)
    : IMediaProviderAssetMaintenance
{
    private const int MaximumResponseBytes = 8 * 1024;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly CloudflareMediaOptions settings = options.Value;

    public async Task<MediaProviderDeletionResult> DeleteAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var uri = BuildUri($"v1/provider/assets/{FormatAssetId(assetId)}/delete");
        using var request = CreateBrokerRequest(HttpMethod.Post, uri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return MediaProviderDeletionResult.AlreadyAbsent;
        response.EnsureSuccessStatusCode();
        return string.Equals(response.Headers.TryGetValues("X-Media-Deletion-Result", out var values) ? values.FirstOrDefault() : null,
            "already-absent", StringComparison.OrdinalIgnoreCase)
            ? MediaProviderDeletionResult.AlreadyAbsent
            : MediaProviderDeletionResult.Deleted;
    }

    public async Task<MediaProviderAssetPresence> InspectAsync(Guid assetId, MediaKind kind,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var uri = BuildUri($"v1/provider/assets/{FormatAssetId(assetId)}/inspection?kind={kind.ToString().ToLowerInvariant()}");
        using var request = CreateBrokerRequest(HttpMethod.Get, uri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return MediaProviderAssetPresence.Absent;
        response.EnsureSuccessStatusCode();
        var result = await ReadBoundedJsonAsync<AssetInspectionResponse>(response, cancellationToken);
        if (result is null || !Guid.TryParseExact(result.AssetId, "N", out var returnedId) || returnedId != assetId)
            return MediaProviderAssetPresence.Invalid;
        return !result.Exists ? MediaProviderAssetPresence.Absent :
            result.Valid ? MediaProviderAssetPresence.Present : MediaProviderAssetPresence.Invalid;
    }

    private HttpRequestMessage CreateBrokerRequest(HttpMethod method, Uri uri)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.WorkerBrokerAuthorizationKey))
            throw new HttpRequestException("The private media provider lifecycle gateway is not configured.");
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("X-Broker-Authorization", $"Bearer {settings.WorkerBrokerAuthorizationKey}");
        return request;
    }

    private Uri BuildUri(string path)
    {
        if (!Uri.TryCreate(settings.WorkerBaseUrl, UriKind.Absolute, out var origin) ||
            origin.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(origin.UserInfo))
            throw new HttpRequestException("The private media provider lifecycle gateway is not configured.");
        return new Uri(new Uri(settings.WorkerBaseUrl.TrimEnd('/') + "/"), path);
    }

    private static string FormatAssetId(Guid assetId) => assetId == Guid.Empty
        ? throw new ArgumentException("Media asset id is required.", nameof(assetId))
        : assetId.ToString("N");

    private static async Task<T?> ReadBoundedJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            throw new HttpRequestException("The media provider lifecycle response exceeded its size bound.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var chunk = new byte[2048];
        while (true)
        {
            var read = await input.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumResponseBytes)
                throw new HttpRequestException("The media provider lifecycle response exceeded its size bound.");
            output.Write(chunk, 0, read);
        }
        try { return JsonSerializer.Deserialize<T>(output.ToArray(), WebJson); }
        catch (JsonException exception) { throw new HttpRequestException("The media provider lifecycle response was invalid.", exception); }
    }

    private sealed record AssetInspectionResponse(string AssetId, bool Exists, bool Valid);
}
