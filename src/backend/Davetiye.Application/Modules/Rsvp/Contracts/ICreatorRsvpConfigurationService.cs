namespace Davetiye.Application.Modules.Rsvp.Contracts;

public interface ICreatorRsvpConfigurationService
{
    Task<CreatorRsvpConfigurationResult> GetAsync(Guid accountId, Guid invitationId, CancellationToken cancellationToken);
    Task<CreatorRsvpConfigurationResult> SetEnabledAsync(Guid accountId, Guid invitationId, SetRsvpEnabledRequest request, CancellationToken cancellationToken);
    Task<CreatorRsvpConfigurationResult> AddQuestionAsync(Guid accountId, Guid invitationId, SaveRsvpQuestionRequest request, CancellationToken cancellationToken);
    Task<CreatorRsvpConfigurationResult> UpdateQuestionAsync(Guid accountId, Guid invitationId, Guid questionId, SaveRsvpQuestionRequest request, CancellationToken cancellationToken);
    Task<CreatorRsvpConfigurationResult> ArchiveQuestionAsync(Guid accountId, Guid invitationId, Guid questionId, RsvpRevisionRequest request, CancellationToken cancellationToken);
    Task<CreatorRsvpConfigurationResult> ReorderQuestionsAsync(Guid accountId, Guid invitationId, ReorderRsvpQuestionsRequest request, CancellationToken cancellationToken);
}

public sealed record SetRsvpEnabledRequest(long ExpectedRevision, bool IsEnabled);
public sealed record RsvpRevisionRequest(long ExpectedRevision);
public sealed record ReorderRsvpQuestionsRequest(long ExpectedRevision, IReadOnlyList<Guid>? QuestionIds);
public sealed record SaveRsvpQuestionRequest(long ExpectedRevision, string? Prompt, string? Type, bool IsRequired,
    string? SemanticRole, IReadOnlyList<RsvpQuestionOptionInput>? Options);
public sealed record RsvpQuestionOptionInput(Guid? Id, string? Label, int SortOrder);

public sealed record CreatorRsvpQuestion(Guid Id, string Prompt, string Type, bool IsRequired, int SortOrder,
    string? SemanticRole, IReadOnlyList<CreatorRsvpQuestionOption> Options);
public sealed record CreatorRsvpQuestionOption(Guid Id, string Label, int SortOrder);
public sealed record CreatorRsvpConfiguration(Guid InvitationId, bool Enabled, long Revision,
    string EffectiveState, IReadOnlyList<CreatorRsvpQuestion> Questions, CreatorRsvpInputLimits InputLimits);
public sealed record CreatorRsvpInputLimits(int MaxActiveQuestionsPerInvitation, int MaxQuestionPromptCharacters,
    int MaxOptionLabelCharacters, int MaxDefinedOptionsPerChoiceQuestion, int MaxShortTextAnswerCharacters,
    int MaxLongTextAnswerCharacters, int MinimumParticipantCount, int MaximumParticipantCount,
    int MaxMultipleChoiceSelections);

public enum CreatorRsvpConfigurationOutcome { Succeeded, NotFound, Invalid, Conflict, LifecycleConflict }
public sealed record CreatorRsvpConfigurationResult(CreatorRsvpConfigurationOutcome Outcome,
    CreatorRsvpConfiguration? Configuration = null, IReadOnlyDictionary<string, string[]>? Errors = null,
    long? CurrentRevision = null, string? EffectiveState = null);
