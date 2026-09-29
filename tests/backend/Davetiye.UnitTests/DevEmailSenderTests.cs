using Davetiye.Application.Modules.Notifications.Contracts;
using Davetiye.Infrastructure.Modules.Notifications;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class DevEmailSenderTests
{
    private const string RawToken = "super-secret-single-use-token-value";

    [Fact]
    public async Task SendAsync_never_logs_the_raw_token_or_recipient_embedded_in_a_link_notification()
    {
        var logger = new RecordingLogger<DevEmailSender>();
        var sender = new DevEmailSender(logger);
        var confirmationLink =
            $"https://davetiye.example.test/auth/confirm-email?userId=11111111-1111-1111-1111-111111111111&token={RawToken}";

        await sender.SendAsync(
            "creator@example.test",
            EmailNotificationKinds.EmailConfirmation,
            new Dictionary<string, string> { ["confirmationLink"] = confirmationLink },
            CancellationToken.None);

        var allMessages = string.Join('\n', logger.Messages);
        Assert.DoesNotContain(RawToken, allMessages, StringComparison.Ordinal);
        Assert.DoesNotContain("creator@example.test", allMessages, StringComparison.Ordinal);
        Assert.Contains("confirmationLink", allMessages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_never_logs_non_token_field_values()
    {
        var logger = new RecordingLogger<DevEmailSender>();
        var sender = new DevEmailSender(logger);

        await sender.SendAsync(
            "creator@example.test",
            EmailNotificationKinds.EmailConfirmation,
            new Dictionary<string, string> { ["displayName"] = "Ada Lovelace" },
            CancellationToken.None);

        var allMessages = string.Join('\n', logger.Messages);
        Assert.DoesNotContain("Ada Lovelace", allMessages, StringComparison.Ordinal);
        Assert.Contains("displayName", allMessages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_logs_only_code_owned_field_names_for_reset_notifications()
    {
        var logger = new RecordingLogger<DevEmailSender>();
        var sender = new DevEmailSender(logger);
        var resetLink =
            $"https://davetiye.example.test/auth/reset-password?userId=11111111-1111-1111-1111-111111111111&token={RawToken}&source=email";

        await sender.SendAsync(
            "creator@example.test",
            EmailNotificationKinds.PasswordReset,
            new Dictionary<string, string> { ["resetLink"] = resetLink },
            CancellationToken.None);

        var allMessages = string.Join('\n', logger.Messages);
        Assert.DoesNotContain(RawToken, allMessages, StringComparison.Ordinal);
        Assert.DoesNotContain("userId=11111111-1111-1111-1111-111111111111", allMessages, StringComparison.Ordinal);
        Assert.DoesNotContain("source=email", allMessages, StringComparison.Ordinal);
        Assert.Contains("resetLink", allMessages, StringComparison.Ordinal);
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
