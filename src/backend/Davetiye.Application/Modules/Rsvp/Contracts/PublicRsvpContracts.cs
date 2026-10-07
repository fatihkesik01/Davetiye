namespace Davetiye.Application.Modules.Rsvp.Contracts;

public interface IPublicRsvpService
{
    Task<PublicRsvpConfigurationResult> GetConfigurationAsync(string publicCode, CancellationToken cancellationToken);
    Task<PublicRsvpGuestSubmissionResult> GetSubmissionAsync(string publicCode, Guid submissionId, string? manageToken,
        CancellationToken cancellationToken);
    Task<PublicRsvpSubmissionResult> SubmitAsync(string publicCode, SubmitPublicRsvpRequest request,
        CancellationToken cancellationToken);
    Task<PublicRsvpSubmissionResult> UpdateAsync(string publicCode, Guid submissionId, string? manageToken,
        SubmitPublicRsvpRequest request, CancellationToken cancellationToken);
}

public sealed record SubmitPublicRsvpRequest(IReadOnlyList<PublicRsvpAnswerInput>? Answers);
public sealed record PublicRsvpAnswerInput(Guid QuestionId, string? TextValue = null, decimal? NumberValue = null,
    bool? BooleanValue = null, IReadOnlyList<Guid>? SelectedOptionIds = null);

public sealed record PublicRsvpConfiguration(string Status, IReadOnlyList<PublicRsvpQuestion> Questions,
    PublicRsvpAnswerLimits AnswerLimits);
public sealed record PublicRsvpAnswerLimits(int MaxShortTextAnswerCharacters, int MaxLongTextAnswerCharacters,
    int MinimumParticipantCount, int MaximumParticipantCount, int MaxMultipleChoiceSelections);
public sealed record PublicRsvpQuestion(Guid Id, string Prompt, string Type, bool IsRequired, int SortOrder,
    IReadOnlyList<PublicRsvpOption> Options, decimal? MinimumNumberValue = null, decimal? MaximumNumberValue = null);
public sealed record PublicRsvpOption(Guid Id, string Label, int SortOrder);
public sealed record PublicRsvpGuestAnswer(Guid QuestionId, string? TextValue, string? NumberValue,
    bool? BooleanValue, IReadOnlyList<Guid> SelectedOptionIds);
public sealed record PublicRsvpGuestSubmission(Guid SubmissionId, DateTimeOffset UpdatedAt,
    IReadOnlyList<PublicRsvpGuestAnswer> Answers);

public enum PublicRsvpOutcome { Available, NotFound, Unavailable, QuotaReached, Invalid }
public sealed record PublicRsvpConfigurationResult(PublicRsvpOutcome Outcome, PublicRsvpConfiguration? Configuration = null);
public sealed record PublicRsvpGuestSubmissionResult(PublicRsvpOutcome Outcome, PublicRsvpGuestSubmission? Submission = null);
public sealed record PublicRsvpSubmissionResult(PublicRsvpOutcome Outcome, Guid? SubmissionId = null,
    DateTimeOffset? SubmittedAt = null, DateTimeOffset? CapabilityExpiresAt = null, DateTimeOffset? UpdatedAt = null,
    IReadOnlyDictionary<string, string[]>? Errors = null, string? ManageToken = null);
