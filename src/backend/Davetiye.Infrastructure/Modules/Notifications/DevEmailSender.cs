using Davetiye.Application.Modules.Notifications.Contracts;
using Microsoft.Extensions.Logging;

namespace Davetiye.Infrastructure.Modules.Notifications;

/// <summary>
/// The development/fake <see cref="IEmailSender"/> adapter (docs/ARCHITECTURE.md §5:
/// "Development may use a fake/local sender"). It never contacts a real provider; it only logs the
/// notification, deliberately at Information level and without ever writing the raw reset/
/// confirmation token (docs/THREAT_MODEL.md §10 lists "reset token" among things that must not be
/// logged). A field such as <c>confirmationLink</c>/<c>resetLink</c> embeds the raw single-use token
/// as a URL query parameter, so logging the link verbatim would still be logging the token, just
/// wrapped in a URL - <see cref="RedactTokenQueryParameter"/> strips exactly that query parameter's
/// value before anything is logged, regardless of which field it appears in.
/// A later milestone can add a real Resend-backed adapter behind the same port without this type
/// or the port changing; only <see cref="Davetiye.Infrastructure.DependencyInjection"/> registers
/// this type, and only in Development (see its comment for why Production has no valid choice yet).
/// </summary>
public sealed class DevEmailSender(ILogger<DevEmailSender> logger) : IEmailSender
{
    public Task SendAsync(
        string toEmail,
        string kind,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(data);

        logger.LogInformation(
            "[DevEmailSender] Queued notification. Kind={Kind} FieldNames={Fields}",
            kind,
            string.Join(", ", data.Keys.OrderBy(key => key, StringComparer.Ordinal)));

        return Task.CompletedTask;
    }
}
