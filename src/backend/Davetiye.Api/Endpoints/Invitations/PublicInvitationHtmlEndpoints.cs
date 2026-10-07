using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Davetiye.Api.Infrastructure.Security;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Api.Infrastructure.Hosting;

namespace Davetiye.Api.Endpoints.Invitations;

public static partial class PublicInvitationHtmlEndpoints
{
    public static IServiceCollection AddPublicInvitationHtml(this IServiceCollection services)
    {
        services.AddHttpClient("public-web-shell", client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        return services;
    }

    public static IEndpointRouteBuilder MapPublicInvitationHtmlEndpoints(this IEndpointRouteBuilder endpoints, string policy)
    {
        endpoints.MapGet("/davetiye/{slug}", GetAsync).AllowAnonymous().RequireRateLimiting(policy)
            .WithMetadata(new PublicInvitationEndpointMetadata()).AddEndpointFilter<RequireHttpsInProductionFilter>();
        endpoints.MapGet("/api/v1/public/og-image.png", () => Results.Bytes(PublicInvitationOgImage.Bytes, "image/png"))
            .AllowAnonymous().RequireRateLimiting(policy).WithMetadata(new PublicInvitationEndpointMetadata());
        endpoints.MapGet("/api/v1/public/invitations/{publicCode}/og-image.png", ImageAsync)
            .AllowAnonymous().RequireRateLimiting(policy).WithMetadata(new PublicInvitationEndpointMetadata())
            .AddEndpointFilter<RequireHttpsInProductionFilter>();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(string slug, IPublicInvitationService invitations,
        IHttpClientFactory clients, PublicInvitationPresentationSettings options, CancellationToken token)
    {
        var code = slug.Length >= 64 ? slug[^64..] : slug;
        if (slug.Length > 64 && slug[^65] != '-' || !Davetiye.Domain.Modules.Invitations.PublicInvitationCode.IsValid(code))
            return Html(GenericHtml(), 404);
        string shell;
        try
        {
            using var response = await clients.CreateClient("public-web-shell")
                .GetAsync(options.AppShellUrl, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode != HttpStatusCode.OK) return Html(GenericHtml(), 503);
            if (response.Content.Headers.ContentLength > 2_097_152) return Html(GenericHtml(), 503);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, token)) > 0)
            {
                if (buffer.Length + count > 2_097_152) return Html(GenericHtml(), 503);
                buffer.Write(chunk, 0, count);
            }
            shell = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            if (!shell.Contains("</head>", StringComparison.OrdinalIgnoreCase)) return Html(GenericHtml(), 503);
        }
        catch (HttpRequestException) { return Html(GenericHtml(), 503); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Html(GenericHtml(), 503); }

        // Fetching the trusted shell can take time. The public gate reads a fresh database
        // snapshot and final UTC only after that await, immediately before HTML projection.
        var result = await invitations.GetAsync(code, token);
        if (result.Outcome == PublicInvitationOutcome.NotFound) return Html(GenericHtml(), 404);
        var title = "Davetiye";
        var description = "Bu davetiye şu anda görüntülenemiyor.";
        if (result.Outcome == PublicInvitationOutcome.Active)
        {
            var content = result.Invitation!.Content;
            title = Limit(content.Headline, 160) ?? title;
            var date = content.StartsAt?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
            description = string.Join(" · ", new[] { date, Limit(content.Venue?.Name, 80) }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (description.Length == 0) description = "Davetlisiniz.";
        }
        var canonical = options.BaseUrl.TrimEnd('/') + "/davetiye/" + code;
        var image = options.BaseUrl.TrimEnd('/') + "/api/v1/public/invitations/" + code + "/og-image.png";
        var metadata = $"<title>{Encode(title)}</title><meta name=\"description\" content=\"{Encode(description)}\"><meta name=\"robots\" content=\"noindex,nofollow\"><link rel=\"canonical\" href=\"{Encode(canonical)}\"><meta property=\"og:type\" content=\"website\"><meta property=\"og:title\" content=\"{Encode(title)}\"><meta property=\"og:description\" content=\"{Encode(description)}\"><meta property=\"og:url\" content=\"{Encode(canonical)}\"><meta property=\"og:image\" content=\"{Encode(image)}\">";
        metadata = MarkMetadata(metadata);
        shell = ExistingTitleAndMetadata().Replace(shell, "");
        var head = shell.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return Html(shell.Insert(head, metadata), 200);
    }

    private static async Task<IResult> ImageAsync(string publicCode, IPublicInvitationService invitations, CancellationToken token)
    {
        var result = await invitations.GetAsync(publicCode, token);
        if (result.Outcome == PublicInvitationOutcome.NotFound) return Results.NotFound();
        if (result.Outcome != PublicInvitationOutcome.Active) return Results.Bytes(PublicInvitationOgImage.Bytes, "image/png");
        var content = result.Invitation!.Content;
        var details = string.Join(" - ", new[] { content.StartsAt?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture), Limit(content.Venue?.Name, 80) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return Results.Bytes(PublicInvitationOgImage.Create(Limit(content.Headline, 160), details), "image/png");
    }

    private static string? Limit(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];
    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
    private static IResult Html(string value, int status) => Results.Content(value, "text/html", System.Text.Encoding.UTF8, status);
    private static string GenericHtml() => "<!doctype html><html lang=\"tr\"><head>" +
        MarkMetadata("<title>Davetiye</title><meta name=\"robots\" content=\"noindex,nofollow\">") +
        "</head><body>Bu davetiye şu anda görüntülenemiyor.</body></html>";
    private static string MarkMetadata(string metadata) => metadata
        .Replace("<title>", "<title data-public-invitation-meta=\"true\">", StringComparison.Ordinal)
        .Replace("<meta ", "<meta data-public-invitation-meta=\"true\" ", StringComparison.Ordinal)
        .Replace("<link ", "<link data-public-invitation-meta=\"true\" ", StringComparison.Ordinal);
    [GeneratedRegex("<title\\b[^>]*>.*?</title>|<meta\\b[^>]*(?:name\\s*=\\s*[\"'](?:description|robots)[\"']|property\\s*=\\s*[\"']og:[^\"']+[\"'])[^>]*>|<link\\b[^>]*rel\\s*=\\s*[\"']canonical[\"'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ExistingTitleAndMetadata();
}
