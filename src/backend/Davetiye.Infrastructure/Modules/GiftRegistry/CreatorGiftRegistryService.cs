using System.Data;
using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Domain.Modules.GiftRegistry;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.GiftRegistry;

/// <summary>Account-scoped Creator item management. Invitation row locking serializes ordinal mutations.</summary>
public sealed class CreatorGiftRegistryService(DavetiyeDbContext dbContext, IClock clock,
    IGiftCreatorInvitationAccessReader invitationAccess) : ICreatorGiftRegistryService
{
    public async Task<CreatorGiftItemsResult> ListAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken)
    {
        if (!await invitationAccess.IsOwnedAsync(accountId, invitationId, cancellationToken)) return new(CreatorGiftRegistryOutcome.NotFound);
        return new(CreatorGiftRegistryOutcome.Succeeded, await LoadItemsAsync(invitationId, cancellationToken));
    }

    public async Task<CreatorGiftItemResult> CreateAsync(Guid accountId, Guid invitationId, CreateGiftItemRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.Name, request.RequestedQuantity);
        if (validation is not null) return new(CreatorGiftRegistryOutcome.Invalid, Errors: validation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (!await invitationAccess.LockOwnedAsync(accountId, invitationId, cancellationToken)) return new(CreatorGiftRegistryOutcome.NotFound);
        var ordinal = await dbContext.GiftItems.CountAsync(item => item.InvitationId == invitationId, cancellationToken);
        var now = clock.UtcNow.ToUniversalTime();
        var item = GiftItem.Create(Guid.NewGuid(), invitationId, request.Name!, request.RequestedQuantity, ordinal, now);
        dbContext.GiftItems.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(CreatorGiftRegistryOutcome.Succeeded, ToContract(item, 0));
    }

    public async Task<CreatorGiftItemResult> UpdateAsync(Guid accountId, Guid invitationId, Guid itemId,
        UpdateGiftItemRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request.Name, request.RequestedQuantity);
        if (validation is not null) return new(CreatorGiftRegistryOutcome.Invalid, Errors: validation);
        if (request.ExpectedRevision < 0) return Invalid("expectedRevision", "Expected revision must not be negative.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await invitationAccess.LockOwnedAsync(accountId, invitationId, cancellationToken)) return new(CreatorGiftRegistryOutcome.NotFound);
        var item = await dbContext.GiftItems.SingleOrDefaultAsync(value => value.Id == itemId && value.InvitationId == invitationId, cancellationToken);
        if (item is null) return new(CreatorGiftRegistryOutcome.NotFound);
        if (item.Revision != request.ExpectedRevision)
            return new(CreatorGiftRegistryOutcome.Conflict, CurrentRevision: item.Revision);
        var reserved = await dbContext.GiftReservations.Where(value => value.GiftItemId == itemId && value.InvitationId == invitationId)
            .SumAsync(value => (int?)value.Quantity, cancellationToken) ?? 0;
        if (request.RequestedQuantity < reserved)
            return Invalid("requestedQuantity", "Requested quantity cannot be less than the quantity already reserved.");
        var now = clock.UtcNow.ToUniversalTime();
        item.Update(request.Name!, request.RequestedQuantity, item.Ordinal, now < item.UpdatedAt ? item.UpdatedAt : now);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new(CreatorGiftRegistryOutcome.Conflict);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(CreatorGiftRegistryOutcome.Succeeded, ToContract(item, reserved));
    }

    public async Task<CreatorGiftItemResult> DeleteAsync(Guid accountId, Guid invitationId, Guid itemId, long expectedRevision,
        CancellationToken cancellationToken)
    {
        if (expectedRevision < 0) return Invalid("expectedRevision", "Expected revision must not be negative.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await invitationAccess.LockOwnedAsync(accountId, invitationId, cancellationToken)) return new(CreatorGiftRegistryOutcome.NotFound);
        var item = await dbContext.GiftItems.SingleOrDefaultAsync(value => value.Id == itemId && value.InvitationId == invitationId, cancellationToken);
        if (item is null) return new(CreatorGiftRegistryOutcome.NotFound);
        if (item.Revision != expectedRevision) return new(CreatorGiftRegistryOutcome.Conflict, CurrentRevision: item.Revision);
        if (await dbContext.GiftReservations.AnyAsync(value => value.GiftItemId == itemId && value.InvitationId == invitationId, cancellationToken))
            return new(CreatorGiftRegistryOutcome.Conflict);
        var affected = await dbContext.GiftItems.Where(value => value.InvitationId == invitationId && value.Ordinal > item.Ordinal)
            .OrderBy(value => value.Ordinal).ToListAsync(cancellationToken);
        var now = clock.UtcNow.ToUniversalTime();
        foreach (var value in affected)
            value.Update(value.Name, value.RequestedQuantity, checked(value.Ordinal + affected.Count + 1), now < value.UpdatedAt ? value.UpdatedAt : now);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.GiftItems.Remove(item);
        for (var i = 0; i < affected.Count; i++)
        {
            affected[i].Update(affected[i].Name, affected[i].RequestedQuantity, item.Ordinal + i,
                now < affected[i].UpdatedAt ? affected[i].UpdatedAt : now);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(CreatorGiftRegistryOutcome.Succeeded);
    }

    public async Task<CreatorGiftItemsResult> ReorderAsync(Guid accountId, Guid invitationId, ReorderGiftItemsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null) return InvalidItems("A complete item order is required.");
        if (request.Items.Any(item => item is null || item.Id == Guid.Empty || item.Revision < 0) ||
            request.Items.Select(item => item.Id).Distinct().Count() != request.Items.Count)
            return InvalidItems("Item identifiers must be unique and revisions non-negative.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await invitationAccess.LockOwnedAsync(accountId, invitationId, cancellationToken)) return new(CreatorGiftRegistryOutcome.NotFound);
        var items = await dbContext.GiftItems.Where(value => value.InvitationId == invitationId).OrderBy(value => value.Ordinal).ToListAsync(cancellationToken);
        if (items.Count != request.Items.Count || !items.Select(value => value.Id).ToHashSet().SetEquals(request.Items.Select(value => value.Id)))
            return InvalidItems("Order must contain every gift item exactly once.");
        var revisions = request.Items.ToDictionary(value => value.Id, value => value.Revision);
        if (items.Any(value => revisions[value.Id] != value.Revision)) return new(CreatorGiftRegistryOutcome.Conflict);
        if (items.Select((value, index) => value.Ordinal == index).All(value => value))
        {
            var desired = request.Items.Select(value => value.Id).ToArray();
            if (items.Select(value => value.Id).SequenceEqual(desired))
                return new(CreatorGiftRegistryOutcome.Succeeded, await LoadItemsAsync(invitationId, cancellationToken));
        }
        var now = clock.UtcNow.ToUniversalTime();
        // Move all rows to a disjoint positive range first, avoiding the unique ordinal index during swaps.
        foreach (var item in items) item.Update(item.Name, item.RequestedQuantity, checked(item.Ordinal + items.Count), now < item.UpdatedAt ? item.UpdatedAt : now);
        await dbContext.SaveChangesAsync(cancellationToken);
        var byId = items.ToDictionary(value => value.Id);
        for (var ordinal = 0; ordinal < request.Items.Count; ordinal++)
        {
            var item = byId[request.Items[ordinal].Id];
            item.Update(item.Name, item.RequestedQuantity, ordinal, now < item.UpdatedAt ? item.UpdatedAt : now);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(CreatorGiftRegistryOutcome.Succeeded, await LoadItemsAsync(invitationId, cancellationToken));
    }

    public async Task<CreatorGiftReservationsResult> ListReservationsAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken)
    {
        if (!await invitationAccess.IsOwnedAsync(accountId, invitationId, cancellationToken)) return new(CreatorGiftRegistryOutcome.NotFound);
        var rows = await dbContext.GiftReservations.AsNoTracking().Where(value => value.InvitationId == invitationId)
            .OrderBy(value => value.CreatedAt).Select(value => new CreatorGiftReservation(value.Id, value.GiftItemId,
                value.Quantity, value.GuestFullName, value.Email, value.Phone, value.CreatedAt)).ToListAsync(cancellationToken);
        return new(CreatorGiftRegistryOutcome.Succeeded, rows);
    }

    public async Task<CreatorGiftRegistryOutcome> RemoveReservationAsync(Guid accountId, Guid invitationId, Guid reservationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await invitationAccess.LockOwnedAsync(accountId, invitationId, cancellationToken)) return CreatorGiftRegistryOutcome.NotFound;
        var row = await dbContext.GiftReservations.SingleOrDefaultAsync(value => value.Id == reservationId && value.InvitationId == invitationId, cancellationToken);
        if (row is null) return CreatorGiftRegistryOutcome.NotFound;
        dbContext.GiftReservations.Remove(row);
        var session = await dbContext.GuestGiftSessions.SingleOrDefaultAsync(value => value.Id == row.GuestGiftSessionId, cancellationToken);
        if (session is not null && !await dbContext.GiftReservations.AnyAsync(value => value.GuestGiftSessionId == session.Id && value.Id != reservationId, cancellationToken))
            session.Revoke(clock.UtcNow.ToUniversalTime());
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CreatorGiftRegistryOutcome.Succeeded;
    }

    private async Task<IReadOnlyList<CreatorGiftItem>> LoadItemsAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var items = await dbContext.GiftItems.AsNoTracking().Where(value => value.InvitationId == invitationId).OrderBy(value => value.Ordinal).ToListAsync(cancellationToken);
        var reservations = await dbContext.GiftReservations.AsNoTracking().Where(value => value.InvitationId == invitationId)
            .GroupBy(value => value.GiftItemId).Select(group => new { Id = group.Key, Quantity = group.Sum(value => value.Quantity) })
            .ToDictionaryAsync(value => value.Id, value => value.Quantity, cancellationToken);
        return items.Select(value => ToContract(value, reservations.GetValueOrDefault(value.Id))).ToArray();
    }

    private static CreatorGiftItem ToContract(GiftItem item, int reserved) =>
        new(item.Id, item.Name, item.RequestedQuantity, reserved, item.RequestedQuantity - reserved, item.Ordinal, item.Revision);
    private static IReadOnlyDictionary<string, string[]>? Validate(string? name, int quantity)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) errors["name"] = ["Name must contain between 1 and 200 characters."];
        if (quantity <= 0) errors["requestedQuantity"] = ["Requested quantity must be positive."];
        return errors.Count == 0 ? null : errors;
    }
    private static CreatorGiftItemResult Invalid(string key, string message) =>
        new(CreatorGiftRegistryOutcome.Invalid, Errors: new Dictionary<string, string[]> { [key] = [message] });
    private static CreatorGiftItemsResult InvalidItems(string message) =>
        new(CreatorGiftRegistryOutcome.Invalid, Errors: new Dictionary<string, string[]> { ["items"] = [message] });
}
