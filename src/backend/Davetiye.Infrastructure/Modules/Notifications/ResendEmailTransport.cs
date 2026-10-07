using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Davetiye.Application.Modules.Notifications.Contracts;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Notifications;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";
    public string? ApiKey { get; init; }
    public string? FromAddress { get; init; }
    public string FromName { get; init; } = "Davetiye";
}

public sealed class EmailDeliveryException(string message, bool isTransient) : Exception(message)
{
    public bool IsTransient { get; } = isTransient;
}

/// <summary>Resend HTTP adapter. It sends only rendered messages and never writes message data to logs.</summary>
public sealed class ResendEmailTransport(HttpClient httpClient, IOptions<ResendOptions> options) : IEmailTransport
{
    private static readonly Uri Endpoint = new("https://api.resend.com/emails");

    public static SocketsHttpHandler CreatePrimaryHandler() => new()
    {
        AllowAutoRedirect = false
    };

    public async Task SendAsync(EmailDeliveryMessage message, Guid idempotencyId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new EmailDeliveryException("Email provider credentials are not configured.", isTransient: true);
        if (string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new EmailDeliveryException("Email sender address is not configured.", isTransient: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyId.ToString("N"));
        request.Content = JsonContent.Create(new
        {
            from = $"{settings.FromName} <{settings.FromAddress}>",
            to = new[] { message.ToEmail },
            subject = message.Subject,
            html = message.Html,
            text = message.Text
        });

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmailDeliveryException("Email provider request timed out.", isTransient: true);
        }
        catch (HttpRequestException)
        {
            throw new EmailDeliveryException("Email provider could not be reached.", isTransient: true);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return;

            var transient = await IsTransientFailureAsync(response, cancellationToken);
            throw new EmailDeliveryException("Email provider rejected the request.", transient);
        }
    }

    private static async Task<bool> IsTransientFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || status >= 500)
            return true;
        if (response.StatusCode != HttpStatusCode.Conflict)
            return false;

        // Resend documents concurrent_idempotent_requests (retry later) separately from
        // invalid_idempotent_request (same key with a different body; terminal for this message).
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var name = document.RootElement.TryGetProperty("name", out var field) ? field.GetString() : null;
            return string.Equals(name, "concurrent_idempotent_requests", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
