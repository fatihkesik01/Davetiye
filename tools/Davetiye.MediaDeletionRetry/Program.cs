using System.Text.Json;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

MediaDeletionRetryArguments command;
try
{
    command = MediaDeletionRetryArguments.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(MediaDeletionRetryArguments.Usage);
    Environment.ExitCode = 2;
    return;
}

if (command.ShowHelp)
{
    Console.WriteLine(MediaDeletionRetryArguments.Usage);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Deliberately do not start the host: that would start unrelated email, payment,
// invitation, and media workers while an operator is inspecting/retrying one item.
using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWorkStore>();

if (command.List)
{
    var terminal = await outbox.GetTerminalAsync(MediaOutboxMessageTypes.PermanentAssetDeletion,
        command.Limit, command.AfterMessageId, CancellationToken.None);
    foreach (var work in terminal)
    {
        var payload = JsonSerializer.Deserialize<PermanentMediaDeletionPayload>(work.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var assetId = payload?.AssetId ?? "unknown";
        Console.WriteLine($"message={work.MessageId:D} asset={assetId} attempts={work.AttemptCount} created={work.CreatedAt:O}");
    }

    if (terminal.Count == 0) Console.WriteLine("No terminal media deletion work was found in this page.");
    return;
}

TerminalOutboxWork? target = null;
Guid? afterMessageId = null;
while (true)
{
    var page = await outbox.GetTerminalAsync(MediaOutboxMessageTypes.PermanentAssetDeletion,
        1000, afterMessageId, CancellationToken.None);
    target = page.SingleOrDefault(work => work.MessageId == command.MessageId);
    if (target is not null || page.Count < 1000) break;
    afterMessageId = page[^1].MessageId;
}

if (target is null)
{
    Console.Error.WriteLine("The specified message is not a terminal media deletion item.");
    Environment.ExitCode = 1;
    return;
}

if (!IsSupportedPayload(target.Payload))
{
    Console.Error.WriteLine("The terminal item has an invalid/unsupported media deletion payload; refusing to retry it.");
    Environment.ExitCode = 1;
    return;
}

var requeued = await outbox.RetryTerminalAsync(MediaOutboxMessageTypes.PermanentAssetDeletion,
    target.MessageId, DateTimeOffset.UtcNow, CancellationToken.None);
if (!requeued)
{
    Console.Error.WriteLine("The terminal item changed before retry; inspect the queue again.");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"Requeued terminal media deletion message {target.MessageId:D}. Its stored capability-expiry guard remains in force.");

static bool IsSupportedPayload(string json)
{
    try
    {
        var payload = JsonSerializer.Deserialize<PermanentMediaDeletionPayload>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (payload is not { Version: 1 or 2 } || !Guid.TryParseExact(payload.AssetId, "N", out var assetId) || assetId == Guid.Empty)
            return false;
        return payload.Version == 1 ||
            Guid.TryParseExact(payload.ProviderAssetId, "N", out var providerAssetId) && providerAssetId == assetId &&
            payload.Kind is not null && Enum.IsDefined(payload.Kind.Value) && payload.DeleteNotBefore is not null;
    }
    catch (JsonException)
    {
        return false;
    }
}

internal sealed record MediaDeletionRetryArguments(bool ShowHelp, bool List, Guid? MessageId, Guid? AfterMessageId,
    int Limit, bool Confirm)
{
    public const string Usage = "Usage: Davetiye.MediaDeletionRetry --list [--after <message-guid>] [--limit 1..1000] | --retry <message-guid> --confirm | --help";

    public static MediaDeletionRetryArguments Parse(string[] args)
    {
        var help = false;
        var list = false;
        Guid? messageId = null;
        Guid? after = null;
        var limit = 100;
        var confirm = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--help":
                    help = true;
                    break;
                case "--list":
                    list = true;
                    break;
                case "--retry":
                    messageId = ParseGuid(RequireValue(args, ref index, "--retry"), "--retry");
                    break;
                case "--after":
                    after = ParseGuid(RequireValue(args, ref index, "--after"), "--after");
                    break;
                case "--limit":
                    var rawLimit = RequireValue(args, ref index, "--limit");
                    if (!int.TryParse(rawLimit, out limit) || limit is < 1 or > 1000)
                        throw new ArgumentException("--limit must be between 1 and 1000.");
                    break;
                case "--confirm":
                    confirm = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (help)
        {
            if (args.Length != 1) throw new ArgumentException("--help cannot be combined with other arguments.");
            return new(true, false, null, null, limit, false);
        }

        if (list == (messageId is not null))
            throw new ArgumentException("Choose exactly one of --list or --retry <message-guid>.");
        if (list && confirm) throw new ArgumentException("--confirm applies only to --retry.");
        if (messageId is not null && !confirm) throw new ArgumentException("--retry requires explicit --confirm.");
        if (!list && after is not null) throw new ArgumentException("--after applies only to --list.");
        if (!list && limit != 100) throw new ArgumentException("--limit applies only to --list.");

        return new(false, list, messageId, after, limit, confirm);
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"{option} requires a value.");
        return args[++index];
    }

    private static Guid ParseGuid(string value, string option) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new ArgumentException($"{option} requires a non-empty GUID.");
}
