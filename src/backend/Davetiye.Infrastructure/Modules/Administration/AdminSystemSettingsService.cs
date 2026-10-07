using System.Globalization;
using Davetiye.Application.Modules.Administration.Contracts;
using Davetiye.Domain.Modules.Administration;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Administration;

public sealed class AdminSystemSettingsService(
    DavetiyeDbContext db,
    IAdminAuditWriter audit,
    IClock clock) : IAdminSystemSettingsService
{
    private const int MinRetentionDays = 0;
    private const int MaxRetentionDays = 365;
    private const string DeletedInvitationRetentionDaysKey = "deletedInvitationRetentionDays";
    private const string AbandonedMemoryRetentionDaysKey = "abandonedMemoryRetentionDays";

    private static readonly SettingDescriptor[] Descriptors =
    [
        new(DeletedInvitationRetentionDaysKey, "Deleted invitation retention",
            "Days an invitation remains in Trash before permanent deletion.",
            ReadRetentionDays),
        new(AbandonedMemoryRetentionDaysKey, "Abandoned memory retention",
            "Days to retain abandoned Guest memory drafts before cleanup.",
            ReadRetentionDays),
    ];

    public async Task<AdminSystemSettingsResponse> ListAsync(CancellationToken cancellationToken)
    {
        var keys = Descriptors.Select(item => item.Key).ToArray();
        var settings = await db.SystemSettings.AsNoTracking()
            .Where(setting => keys.Contains(setting.Key))
            .ToDictionaryAsync(setting => setting.Key, cancellationToken);

        var items = Descriptors.OrderBy(descriptor => descriptor.Key, StringComparer.Ordinal).Select(descriptor => ToItem(
            descriptor,
            settings.TryGetValue(descriptor.Key, out var setting)
                ? setting
                : throw new InvalidOperationException($"Required system setting '{descriptor.Key}' is missing.")))
            .ToArray();
        return new AdminSystemSettingsResponse(items);
    }

    public async Task<AdminSystemSettingUpdateResult> UpdateAsync(
        Guid actorId,
        string key,
        AdminSystemSettingUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty || request.ExpectedRevision < 0 ||
            request.Value is < MinRetentionDays or > MaxRetentionDays)
            return new(AdminSystemSettingUpdateOutcome.InvalidRequest);

        var descriptor = Descriptors.SingleOrDefault(item => item.Key == key);
        if (descriptor is null)
            return new(AdminSystemSettingUpdateOutcome.NotFound);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var setting = await db.SystemSettings.AsNoTracking().SingleOrDefaultAsync(
            item => item.Key == descriptor.Key, cancellationToken);
        if (setting is null)
            return new(AdminSystemSettingUpdateOutcome.NotFound);
        if (setting.Revision != request.ExpectedRevision)
            return new(AdminSystemSettingUpdateOutcome.Conflict);
        if (descriptor.ReadDays(setting.ValueType.ToString(), setting.Value) is null)
            throw new InvalidOperationException($"System setting '{descriptor.Key}' is invalid.");

        var newValue = request.Value.ToString(CultureInfo.InvariantCulture);
        if (setting.Value != newValue)
        {
            var rows = await db.SystemSettings
                .Where(item => item.Id == setting.Id && item.Revision == request.ExpectedRevision)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(item => item.Value, newValue)
                    .SetProperty(item => item.Revision, item => item.Revision + 1), cancellationToken);
            if (rows != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(AdminSystemSettingUpdateOutcome.Conflict);
            }

            audit.Add(actorId, clock.UtcNow, "GlobalSettingUpdated", setting.Id);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // A no-op still checks the caller's revision atomically, so it cannot report success
            // from a stale read if another transaction changes the setting concurrently.
            var rows = await db.SystemSettings
                .Where(item => item.Id == setting.Id && item.Revision == request.ExpectedRevision)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Value, setting.Value), cancellationToken);
            if (rows != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(AdminSystemSettingUpdateOutcome.Conflict);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new(AdminSystemSettingUpdateOutcome.Succeeded, ToItem(
            descriptor, setting, setting.Value == newValue ? setting.Revision : setting.Revision + 1, newValue));
    }

    private static AdminSystemSettingItem ToItem(
        SettingDescriptor descriptor,
        SystemSetting setting,
        long? revision = null,
        string? rawValue = null)
    {
        var value = descriptor.ReadDays(setting.ValueType.ToString(), rawValue ?? setting.Value)
            ?? throw new InvalidOperationException($"System setting '{descriptor.Key}' is invalid.");
        return new(descriptor.Key, descriptor.DisplayName, descriptor.Description,
            value, MinRetentionDays, MaxRetentionDays, revision ?? setting.Revision);
    }

    private sealed record SettingDescriptor(
        string Key,
        string DisplayName,
        string Description,
        Func<string, string, int?> ReadDays);

    private static int? ReadRetentionDays(string valueType, string value) =>
        string.Equals(valueType, "Integer", StringComparison.Ordinal) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) &&
        days is >= MinRetentionDays and <= MaxRetentionDays
            ? days
            : null;
}
