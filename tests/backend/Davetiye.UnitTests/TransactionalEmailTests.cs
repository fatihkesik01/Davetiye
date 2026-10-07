using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Infrastructure.Modules.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class TransactionalEmailTests
{
    [Fact]
    public void Protected_auth_payload_hides_link_and_expires_at_the_requested_deadline()
    {
        var now = DateTimeOffset.UtcNow;
        var protector = new EmailOutboxPayloadProtector(new EphemeralDataProtectionProvider());
        const string secretLink = "https://app.example/auth/reset-password?token=secret-token";
        var payload = protector.Protect("person@example.test", EmailNotificationKinds.PasswordReset,
            new Dictionary<string, string> { ["resetLink"] = secretLink }, now.AddMinutes(20));

        Assert.DoesNotContain("person@example.test", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(secretLink, payload, StringComparison.Ordinal);
        var (message, expired) = protector.Unprotect(payload, now.AddMinutes(10));
        Assert.False(expired);
        Assert.Equal(secretLink, message.Data["resetLink"]);

        var (expiredMessage, isExpired) = protector.Unprotect(payload, now.AddMinutes(21));
        Assert.True(isExpired);
        Assert.Null(expiredMessage);
    }

    [Fact]
    public void Template_renderer_escapes_dynamic_values_and_rejects_unknown_kinds()
    {
        var renderer = new EmailTemplateRenderer();
        var message = renderer.Render("creator@example.test", EmailNotificationKinds.InvitationPublished,
            new Dictionary<string, string> { ["invitationTitle"] = "<script>alert(1)</script>" });

        Assert.Contains("&lt;script&gt;", message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", message.Html, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => renderer.Render("x@example.test", "unrecognized", new Dictionary<string, string>()));
    }

    [Fact]
    public void Organization_access_expiry_reminder_states_paid_through_end_date_separately_from_publication_expiry()
    {
        var renderer = new EmailTemplateRenderer();
        var organizationReminder = renderer.Render("creator@example.test", EmailNotificationKinds.SubscriptionAccessExpiryReminder,
            new Dictionary<string, string> { ["accessEndDate"] = "12 Ekim 2026" });
        var individualReminder = renderer.Render("creator@example.test", EmailNotificationKinds.PublicationExpiryReminder,
            new Dictionary<string, string> { ["invitationTitle"] = "Düğün" });

        Assert.Contains("7 gün", organizationReminder.Subject, StringComparison.Ordinal);
        Assert.Contains("paid-through erişimi", organizationReminder.Text, StringComparison.Ordinal);
        Assert.Contains("12 Ekim 2026", organizationReminder.Text, StringComparison.Ordinal);
        Assert.Contains("yayın süresi", individualReminder.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("paid-through", individualReminder.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Retry_window_expires_before_provider_idempotency_retention_even_if_configuration_is_too_large()
    {
        var createdAt = DateTimeOffset.Parse("2026-10-06T00:00:00Z");

        Assert.False(EmailOutboxDeliveryPolicy.IsOverdue(createdAt, createdAt.AddHours(22), 24));
        Assert.True(EmailOutboxDeliveryPolicy.IsOverdue(createdAt, createdAt.AddHours(23), 24));
    }

    [Fact]
    public async Task Resend_transport_uses_stable_outbox_id_as_idempotency_key()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var transport = new ResendEmailTransport(client, Options.Create(new ResendOptions
        {
            ApiKey = "runtime-test-key",
            FromAddress = "noreply@example.test"
        }));

        await transport.SendAsync(new EmailDeliveryMessage("user@example.test", "Subject", "<p>body</p>", "body"), id, CancellationToken.None);

        Assert.Equal("https://api.resend.com/emails", handler.Url);
        Assert.Equal(id.ToString("N"), handler.IdempotencyKey);
        Assert.Equal("Bearer runtime-test-key", handler.Authorization);
        Assert.Contains("user@example.test", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task Resend_primary_handler_does_not_follow_307_or_308_or_replay_the_request(HttpStatusCode redirectStatus)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var receivedRequests = 0;
        var server = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient connection;
                try
                {
                    connection = await listener.AcceptTcpClientAsync(cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                using (connection)
                {
                    Interlocked.Increment(ref receivedRequests);
                    var stream = connection.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    string? line;
                    do { line = await reader.ReadLineAsync(cancellation.Token); } while (!string.IsNullOrEmpty(line));
                    var status = receivedRequests == 1 ? (int)redirectStatus : (int)HttpStatusCode.OK;
                    var location = receivedRequests == 1 ? $"Location: http://127.0.0.1:{port}/second\r\n" : string.Empty;
                    var response = $"HTTP/1.1 {status} Test\r\n{location}Content-Length: 0\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellation.Token);
                }
            }
        }, cancellation.Token);

        using var client = new HttpClient(ResendEmailTransport.CreatePrimaryHandler())
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/emails")
        {
            Content = JsonContent.Create(new { to = "creator@example.test", subject = "test" })
        };
        using var response = await client.SendAsync(request);
        Assert.Equal(redirectStatus, response.StatusCode);
        await Task.Delay(150, cancellation.Token);
        cancellation.Cancel();
        listener.Stop();
        await server;

        Assert.Equal(1, receivedRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", true)]
    [InlineData(HttpStatusCode.Conflict, "{\"name\":\"concurrent_idempotent_requests\"}", true)]
    [InlineData(HttpStatusCode.Conflict, "{\"name\":\"invalid_idempotent_request\"}", false)]
    [InlineData(HttpStatusCode.BadRequest, "", false)]
    public async Task Resend_transport_classifies_provider_errors(HttpStatusCode status, string body, bool transient)
    {
        var transport = new ResendEmailTransport(new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        })), Options.Create(new ResendOptions { ApiKey = "key", FromAddress = "noreply@example.test" }));

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() => transport.SendAsync(
            new EmailDeliveryMessage("user@example.test", "Subject", "html", "text"), Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(transient, exception.IsTransient);
        Assert.DoesNotContain("user@example.test", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_provider_credentials_fail_as_retryable_without_sending()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Unexpected send."));
        var transport = new ResendEmailTransport(new HttpClient(handler), Options.Create(new ResendOptions { FromAddress = "noreply@example.test" }));
        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() => transport.SendAsync(
            new EmailDeliveryMessage("user@example.test", "Subject", "html", "text"), Guid.NewGuid(), CancellationToken.None));
        Assert.True(exception.IsTransient);
        Assert.Null(handler.Url);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? Authorization { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
