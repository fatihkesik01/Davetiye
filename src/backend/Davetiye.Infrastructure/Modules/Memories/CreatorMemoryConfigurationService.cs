using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Domain.Modules.Memories;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Memories;

/// <summary>Creator-owned Memories configuration. Ownership is enforced at query level (foreign or missing invitation = NotFound).</summary>
public sealed class CreatorMemoryConfigurationService(DavetiyeDbContext dbContext, IClock clock,
    IMemoriesCreatorInvitationAccessReader invitationAccessReader, IOptions<MemoryInputLimits> limitsOptions)
    : ICreatorMemoryConfigurationService
{
    private readonly MemoryInputLimits limits = limitsOptions.Value;

    public async Task<CreatorMemoryConfigurationResult> GetAsync(Guid accountId, Guid invitationId,
        CancellationToken cancellationToken)
    {
        var access = await invitationAccessReader.GetOwnedEffectiveStateAsync(accountId, invitationId, cancellationToken);
        if (access is null) return new(CreatorMemoryConfigurationOutcome.NotFound);
        var configuration = await dbContext.MemoryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.InvitationId == invitationId, cancellationToken);
        return Success(invitationId, configuration, access.EffectiveState);
    }

    public async Task<CreatorMemoryConfigurationResult> UpdateAsync(Guid accountId, Guid invitationId,
        UpdateMemoryConfigurationRequest request, CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision < 0) return Invalid("expectedRevision", "Expected revision must not be negative.");
        if (request.Visibility is null || !Enum.GetNames<MemoryVisibility>().Contains(request.Visibility, StringComparer.Ordinal))
            return Invalid("visibility", "Visibility must be CreatorOnly or Public.");
        var visibility = Enum.Parse<MemoryVisibility>(request.Visibility);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var access = await invitationAccessReader.LockOwnedAndGetEffectiveStateAsync(accountId, invitationId, cancellationToken);
        if (access is null) return new(CreatorMemoryConfigurationOutcome.NotFound);

        var now = clock.UtcNow.ToUniversalTime();
        var configuration = await dbContext.MemoryConfigurations
            .SingleOrDefaultAsync(item => item.InvitationId == invitationId, cancellationToken);
        if (configuration is null)
        {
            configuration = MemoryConfiguration.Create(Guid.NewGuid(), invitationId, now);
            dbContext.MemoryConfigurations.Add(configuration);
        }
        if (configuration.Revision != request.ExpectedRevision)
            return new(CreatorMemoryConfigurationOutcome.Conflict, CurrentRevision: configuration.Revision);

        try
        {
            var changedAt = now < configuration.UpdatedAt ? configuration.UpdatedAt : now;
            configuration.SetEnabled(request.IsEnabled, changedAt);
            configuration.SetVisibility(visibility, changedAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.MemoryConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.InvitationId == invitationId, cancellationToken);
            return new(CreatorMemoryConfigurationOutcome.Conflict, CurrentRevision: current?.Revision ?? 0);
        }

        var saved = await dbContext.MemoryConfigurations.AsNoTracking()
            .SingleAsync(item => item.InvitationId == invitationId, cancellationToken);
        return Success(invitationId, saved, access.EffectiveState);
    }

    private CreatorMemoryConfigurationResult Success(Guid invitationId, MemoryConfiguration? configuration, string effectiveState) =>
        new(CreatorMemoryConfigurationOutcome.Succeeded, new CreatorMemoryConfiguration(invitationId,
            configuration?.IsEnabled ?? false, (configuration?.Visibility ?? MemoryVisibility.CreatorOnly).ToString(),
            configuration?.Revision ?? 0, effectiveState,
            new CreatorMemoryInputLimits(limits.MaxDisplayNameCharacters, limits.MaxTextCharacters,
                limits.MaxEmojiCharacters, limits.MaxMemoriesPerInvitation)));

    private static CreatorMemoryConfigurationResult Invalid(string key, string message) =>
        new(CreatorMemoryConfigurationOutcome.Invalid, Errors: new Dictionary<string, string[]> { [key] = [message] });
}
