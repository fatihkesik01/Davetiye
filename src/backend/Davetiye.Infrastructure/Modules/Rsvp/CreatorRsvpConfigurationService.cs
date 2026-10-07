using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Rsvp;

/// <summary>Creator-owned RSVP configuration. Every write rechecks ownership and lifecycle under the invitation row lock.</summary>
public sealed class CreatorRsvpConfigurationService(DavetiyeDbContext dbContext, IClock clock,
    IRsvpCreatorInvitationAccessReader invitationAccessReader, IOptions<RsvpInputLimits> inputLimitsOptions)
    : ICreatorRsvpConfigurationService
{
    private readonly RsvpInputLimits inputLimits = inputLimitsOptions.Value;
    public async Task<CreatorRsvpConfigurationResult> GetAsync(Guid accountId, Guid invitationId,
        CancellationToken cancellationToken)
    {
        var access = await invitationAccessReader.GetOwnedEffectiveStateAsync(accountId, invitationId, cancellationToken);
        if (access is null) return new(CreatorRsvpConfigurationOutcome.NotFound);
        var configuration = await LoadConfigurationAsync(invitationId, tracking: false, cancellationToken);
        return Success(ToContract(invitationId, configuration, access.EffectiveState, inputLimits));
    }

    public Task<CreatorRsvpConfigurationResult> SetEnabledAsync(Guid accountId, Guid invitationId,
        SetRsvpEnabledRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(accountId, invitationId, request.ExpectedRevision, (configuration, now) =>
        {
            if (request.IsEnabled && !configuration.Questions.Any())
            {
                AddQuestionEntity(configuration, "Adınız", RsvpQuestionType.ShortText, true, 0, null, now);
                AddQuestionEntity(configuration, "Katılacak mısınız?", RsvpQuestionType.YesNo, true, 1, null, now);
                AddQuestionEntity(configuration, "Kaç kişi katılacaksınız?", RsvpQuestionType.Number, true, 2,
                    RsvpQuestionSemanticRole.ParticipantCount, now);
                AddQuestionEntity(configuration, "Notunuz / mesajınız", RsvpQuestionType.LongText, false, 3, null, now);
            }
            configuration.SetEnabled(request.IsEnabled, now);
            return Task.FromResult<CreatorRsvpConfigurationResult?>(null);
        }, cancellationToken);

    public Task<CreatorRsvpConfigurationResult> AddQuestionAsync(Guid accountId, Guid invitationId,
        SaveRsvpQuestionRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(accountId, invitationId, request.ExpectedRevision, (configuration, now) =>
        {
            var activeCount = configuration.Questions.Count(item => item.IsActive);
            if (!inputLimits.IsActiveQuestionCountWithinLimit(activeCount + 1))
                return Task.FromResult<CreatorRsvpConfigurationResult?>(Invalid("questions",
                    $"An invitation may have at most {inputLimits.MaxActiveQuestionsPerInvitation} active RSVP questions."));
            var validation = ValidateQuestion(request, inputLimits, out var type, out var role, requireChoiceOptions: true);
            if (validation is not null) return Task.FromResult<CreatorRsvpConfigurationResult?>(validation);
            var question = configuration.AddQuestion(Guid.NewGuid(), request.Prompt!, type, request.IsRequired,
                configuration.Questions.Where(item => item.IsActive).Select(item => item.SortOrder).DefaultIfEmpty(-1).Max() + 1,
                role, now);
            dbContext.Entry(question).State = EntityState.Added;
            if (request.Options is not null)
            {
                question.ReplaceOptions(ToOptions(request.Options), now);
                MarkNewOptionsAdded(question, new HashSet<Guid>());
            }
            return Task.FromResult<CreatorRsvpConfigurationResult?>(null);
        }, cancellationToken);

    public Task<CreatorRsvpConfigurationResult> UpdateQuestionAsync(Guid accountId, Guid invitationId, Guid questionId,
        SaveRsvpQuestionRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(accountId, invitationId, request.ExpectedRevision, async (configuration, now) =>
        {
            var question = configuration.Questions.SingleOrDefault(item => item.Id == questionId && item.IsActive);
            if (question is null) return new(CreatorRsvpConfigurationOutcome.NotFound);
            var validation = ValidateQuestion(request, inputLimits, out var type, out var role, requireChoiceOptions: false);
            if (validation is not null) return validation;
            if (type is RsvpQuestionType.SingleChoice or RsvpQuestionType.MultipleChoice &&
                question.Type is not (RsvpQuestionType.SingleChoice or RsvpQuestionType.MultipleChoice) && request.Options is null)
                return Invalid("options", "Options are required when changing to a choice question.");
            if (request.Options is not null)
            {
                var activeOptionIds = question.Options.Where(item => item.IsActive).Select(item => item.Id).ToHashSet();
                if (request.Options.Any(option => option.Id is Guid optionId && !activeOptionIds.Contains(optionId)))
                    return Invalid("options", "Option identifier does not belong to this question.");
            }
            configuration.UpdateQuestion(questionId, request.Prompt!, type, request.IsRequired, role, now);
            if (request.Options is not null)
            {
                var existingOptionIds = question.Options.Select(item => item.Id).ToHashSet();
                question.PrepareOptionReplacement();
                await dbContext.SaveChangesAsync(cancellationToken);
                // Existing IDs are edited in place; omitted IDs are archived, preserving historical answer FKs.
                question.ReplaceOptions(ToOptions(request.Options), now);
                MarkNewOptionsAdded(question, existingOptionIds);
            }
            return null;
        }, cancellationToken);

    public Task<CreatorRsvpConfigurationResult> ArchiveQuestionAsync(Guid accountId, Guid invitationId, Guid questionId,
        RsvpRevisionRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(accountId, invitationId, request.ExpectedRevision, (configuration, now) =>
        {
            if (!configuration.Questions.Any(item => item.Id == questionId && item.IsActive))
                return Task.FromResult<CreatorRsvpConfigurationResult?>(new(CreatorRsvpConfigurationOutcome.NotFound));
            configuration.ArchiveQuestion(questionId, now);
            return Task.FromResult<CreatorRsvpConfigurationResult?>(null);
        }, cancellationToken);

    public Task<CreatorRsvpConfigurationResult> ReorderQuestionsAsync(Guid accountId, Guid invitationId,
        ReorderRsvpQuestionsRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(accountId, invitationId, request.ExpectedRevision, async (configuration, now) =>
        {
            if (request.QuestionIds is null) return Invalid("questionIds", "Question order is required.");
            try
            {
                // The active-order index is immediate: move rows to a disjoint positive range, save,
                // then store contiguous final positions in the same transaction.
                configuration.PrepareQuestionReorder();
                await dbContext.SaveChangesAsync(cancellationToken);
                configuration.ReorderQuestions(request.QuestionIds, now);
                return null;
            }
            catch (InvalidOperationException exception)
            {
                return Invalid("questionIds", exception.Message);
            }
        }, cancellationToken);

    private async Task<CreatorRsvpConfigurationResult> ExecuteWriteAsync(Guid accountId, Guid invitationId,
        long expectedRevision, Func<RsvpConfiguration, DateTimeOffset, Task<CreatorRsvpConfigurationResult?>> mutate,
        CancellationToken cancellationToken)
    {
        if (expectedRevision < 0) return Invalid("expectedRevision", "Expected revision must not be negative.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var access = await invitationAccessReader.LockOwnedAndGetEffectiveStateAsync(accountId, invitationId, cancellationToken);
        if (access is null)
            return new(CreatorRsvpConfigurationOutcome.NotFound);
        if (access.EffectiveState is not ("Draft" or "Paused"))
            return new(CreatorRsvpConfigurationOutcome.LifecycleConflict, EffectiveState: access.EffectiveState);

        var configuration = await dbContext.RsvpConfigurations.Include(item => item.Questions).ThenInclude(item => item.Options)
            .SingleOrDefaultAsync(item => item.InvitationId == invitationId, cancellationToken);
        if (configuration is null)
        {
            configuration = RsvpConfiguration.Create(Guid.NewGuid(), invitationId, clock.UtcNow.ToUniversalTime());
            dbContext.RsvpConfigurations.Add(configuration);
        }
        if (configuration.Revision != expectedRevision)
            return new(CreatorRsvpConfigurationOutcome.Conflict, CurrentRevision: configuration.Revision);

        var now = clock.UtcNow.ToUniversalTime();
        CreatorRsvpConfigurationResult? failure;
        try
        {
            failure = await mutate(configuration, now);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Invalid("configuration", exception.Message);
        }
        if (failure is not null) return failure;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var currentAccess = await invitationAccessReader.GetOwnedEffectiveStateAsync(accountId, invitationId, cancellationToken);
            var current = currentAccess is null ? null : await LoadConfigurationAsync(invitationId, tracking: false, cancellationToken);
            return current is null
                ? new(CreatorRsvpConfigurationOutcome.NotFound)
                : new(CreatorRsvpConfigurationOutcome.Conflict, CurrentRevision: current.Revision);
        }

        var savedAccess = await invitationAccessReader.GetOwnedEffectiveStateAsync(accountId, invitationId, cancellationToken);
        if (savedAccess is null) return new(CreatorRsvpConfigurationOutcome.NotFound);
        var saved = await LoadConfigurationAsync(invitationId, tracking: false, cancellationToken);
        return Success(ToContract(invitationId, saved, savedAccess.EffectiveState, inputLimits));
    }

    private async Task<RsvpConfiguration?> LoadConfigurationAsync(Guid invitationId, bool tracking,
        CancellationToken cancellationToken)
    {
        var configQuery = tracking ? dbContext.RsvpConfigurations.AsQueryable() : dbContext.RsvpConfigurations.AsNoTracking();
        return await configQuery.Include(item => item.Questions).ThenInclude(item => item.Options)
            .SingleOrDefaultAsync(item => item.InvitationId == invitationId, cancellationToken);
    }

    private static CreatorRsvpConfiguration ToContract(Guid invitationId, RsvpConfiguration? configuration,
        string effectiveState, RsvpInputLimits limits) => new(invitationId, configuration?.IsEnabled ?? false,
        configuration?.Revision ?? 0, effectiveState, configuration?.Questions.Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder).Select(question => new CreatorRsvpQuestion(question.Id, question.Prompt,
                question.Type.ToString(), question.IsRequired, question.SortOrder, question.SemanticRole?.ToString(),
                question.Options.Where(option => option.IsActive).OrderBy(option => option.SortOrder)
                    .Select(option => new CreatorRsvpQuestionOption(option.Id, option.Label, option.SortOrder)).ToArray())).ToArray() ?? [],
        new CreatorRsvpInputLimits(limits.MaxActiveQuestionsPerInvitation, limits.MaxQuestionPromptCharacters,
            limits.MaxOptionLabelCharacters, limits.MaxDefinedOptionsPerChoiceQuestion, limits.MaxShortTextAnswerCharacters,
            limits.MaxLongTextAnswerCharacters, limits.MinimumParticipantCount, limits.MaximumParticipantCount,
            limits.MaxMultipleChoiceSelections));

    private static CreatorRsvpConfigurationResult? ValidateQuestion(SaveRsvpQuestionRequest request, RsvpInputLimits inputLimits,
        out RsvpQuestionType type, out RsvpQuestionSemanticRole? role, bool requireChoiceOptions)
    {
        type = default;
        role = null;
        if (string.IsNullOrWhiteSpace(request.Prompt)) return Invalid("prompt", "Question prompt is required.");
        if (request.Type is null || !Enum.GetNames<RsvpQuestionType>().Contains(request.Type, StringComparer.Ordinal))
            return Invalid("type", "Question type is not supported.");
        type = Enum.Parse<RsvpQuestionType>(request.Type, ignoreCase: false);
        if (!inputLimits.IsPromptWithinLimit(request.Prompt))
            return Invalid("prompt", $"Question prompts may contain at most {inputLimits.MaxQuestionPromptCharacters} characters.");
        if (request.SemanticRole is not null)
        {
            if (!Enum.GetNames<RsvpQuestionSemanticRole>().Contains(request.SemanticRole, StringComparer.Ordinal))
                return Invalid("semanticRole", "Question semantic role is not supported.");
            role = Enum.Parse<RsvpQuestionSemanticRole>(request.SemanticRole, ignoreCase: false);
        }
        if (role == RsvpQuestionSemanticRole.ParticipantCount && type != RsvpQuestionType.Number)
            return Invalid("semanticRole", "Participant count can only be assigned to a Number question.");
        if (type is RsvpQuestionType.SingleChoice or RsvpQuestionType.MultipleChoice && requireChoiceOptions &&
            (request.Options is null || request.Options.Count == 0))
            return Invalid("options", "Choice questions require at least one option.");
        if (type is RsvpQuestionType.SingleChoice or RsvpQuestionType.MultipleChoice && request.Options is { Count: 0 })
            return Invalid("options", "Choice questions require at least one option.");
        if (request.Options is not null && !inputLimits.IsDefinedOptionCountWithinLimit(request.Options.Count))
            return Invalid("options", $"Choice questions may define at most {inputLimits.MaxDefinedOptionsPerChoiceQuestion} options.");
        if (request.Options?.Any(option => option is null) == true)
            return Invalid("options", "Options cannot contain null entries.");
        if (request.Options is not null && request.Options.Any(option => string.IsNullOrWhiteSpace(option.Label)))
            return Invalid("options", "Option labels are required.");
        if (request.Options is not null && request.Options.Any(option => !inputLimits.IsOptionLabelWithinLimit(option.Label)))
            return Invalid("options", $"Option labels may contain at most {inputLimits.MaxOptionLabelCharacters} characters.");
        if (request.Options is not null && request.Options.Select(option => option.Id).Where(id => id is not null).Distinct().Count() !=
            request.Options.Count(option => option.Id is not null))
            return Invalid("options", "Option identifiers must be unique.");
        if (request.Options is not null && (request.Options.Select(option => option.SortOrder).Distinct().Count() != request.Options.Count ||
            request.Options.Any(option => option.SortOrder < 0) ||
            !request.Options.Select(option => option.SortOrder).OrderBy(order => order).SequenceEqual(Enumerable.Range(0, request.Options.Count))))
            return Invalid("options", "Option order must be contiguous starting at zero.");
        if (type is not (RsvpQuestionType.SingleChoice or RsvpQuestionType.MultipleChoice) && request.Options is not null)
            return Invalid("options", "Only choice questions can have options.");
        return null;
    }

    private static IReadOnlyList<(Guid? Id, string Label, int SortOrder)> ToOptions(IReadOnlyList<RsvpQuestionOptionInput> options) =>
        options.Select(option => (option.Id, option.Label!, option.SortOrder)).ToArray();

    private RsvpQuestion AddQuestionEntity(RsvpConfiguration configuration, string prompt, RsvpQuestionType type,
        bool isRequired, int sortOrder, RsvpQuestionSemanticRole? role, DateTimeOffset now)
    {
        var question = configuration.AddQuestion(Guid.NewGuid(), prompt, type, isRequired, sortOrder, role, now);
        dbContext.Entry(question).State = EntityState.Added;
        return question;
    }

    private void MarkNewOptionsAdded(RsvpQuestion question, IReadOnlySet<Guid> existingOptionIds)
    {
        foreach (var option in question.Options.Where(option => !existingOptionIds.Contains(option.Id)))
            dbContext.Entry(option).State = EntityState.Added;
    }

    private static CreatorRsvpConfigurationResult Success(CreatorRsvpConfiguration config) => new(CreatorRsvpConfigurationOutcome.Succeeded, config);
    private static CreatorRsvpConfigurationResult Invalid(string key, string message) => new(CreatorRsvpConfigurationOutcome.Invalid,
        Errors: new Dictionary<string, string[]> { [key] = [message] });
}
