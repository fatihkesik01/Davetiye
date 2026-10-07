using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Infrastructure.Modules.Notifications;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class DevEmailTransportTests
{
    private const string RawToken = "super-secret-single-use-token-value";

    [Fact]
    public async Task SendAsync_never_logs_recipient_or_message_content()
    {
        var logger = new RecordingLogger<DevEmailTransport>();
        var sender = new DevEmailTransport(logger);
        var confirmationLink =
            $"https://davetiye.example.test/auth/confirm-email?userId=11111111-1111-1111-1111-111111111111&token={RawToken}";

        await sender.SendAsync(new EmailDeliveryMessage("creator@example.test", "verify", confirmationLink, confirmationLink), Guid.NewGuid(), CancellationToken.None);

        var allMessages = string.Join('\n', logger.Messages);
        Assert.DoesNotContain(RawToken, allMessages, StringComparison.Ordinal);
        Assert.DoesNotContain("creator@example.test", allMessages, StringComparison.Ordinal);
        Assert.DoesNotContain("verify", allMessages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_never_logs_non_token_field_values()
    {
        var logger = new RecordingLogger<DevEmailTransport>();
        var sender = new DevEmailTransport(logger);

        await sender.SendAsync(new EmailDeliveryMessage("creator@example.test", "Ada Lovelace", "Ada Lovelace", "Ada Lovelace"), Guid.NewGuid(), CancellationToken.None);

        var allMessages = string.Join('\n', logger.Messages);
        Assert.DoesNotContain("Ada Lovelace", allMessages, StringComparison.Ordinal);
        Assert.Contains("MessageId", allMessages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_logs_only_code_owned_field_names_for_reset_notifications()
    {
        var logger = new RecordingLogger<DevEmailTransport>();
        var sender = new DevEmailTransport(logger);
        var resetLink =
            $"https://davetiye.example.test/auth/reset-password?userId=11111111-1111-1111-1111-111111111111&token={RawToken}&source=email";

        await sender.SendAsync(new EmailDeliveryMessage("creator@example.test", "reset", resetLink, resetLink), Guid.NewGuid(), CancellationToken.None);

        var allMessages = string.Join('\n', logger.Messages);
        Assert.DoesNotContain(RawToken, allMessages, StringComparison.Ordinal);
        Assert.DoesNotContain("userId=11111111-1111-1111-1111-111111111111", allMessages, StringComparison.Ordinal);
        Assert.DoesNotContain("source=email", allMessages, StringComparison.Ordinal);
        Assert.Contains("MessageId", allMessages, StringComparison.Ordinal);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
