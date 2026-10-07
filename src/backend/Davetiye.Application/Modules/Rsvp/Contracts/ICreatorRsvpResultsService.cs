namespace Davetiye.Application.Modules.Rsvp.Contracts;

public interface ICreatorRsvpResultsService
{
    Task<CreatorRsvpSubmissionPageResult> ListAsync(Guid accountId, Guid invitationId, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<CreatorRsvpSubmissionDetailResult> GetAsync(Guid accountId, Guid invitationId, Guid submissionId,
        CancellationToken cancellationToken);
    Task<CreatorRsvpSubmissionDeleteResult> DeleteAsync(Guid accountId, Guid invitationId, Guid submissionId,
        CancellationToken cancellationToken);
}

public sealed record CreatorRsvpSubmissionPage(Guid InvitationId, int Page, int PageSize, int TotalCount,
    CreatorRsvpSummary Summary, IReadOnlyList<CreatorRsvpSubmissionListItem> Submissions);

public sealed record CreatorRsvpSummary(int ResponseCount, IReadOnlyList<CreatorRsvpQuestionSummary> Questions,
    decimal? TotalParticipants, bool ParticipantCountAvailable);

public sealed record CreatorRsvpQuestionSummary(Guid QuestionId, string Prompt, string Type, int AnsweredCount,
    IReadOnlyList<CreatorRsvpValueCount> ValueCounts);

public sealed record CreatorRsvpValueCount(string Value, int Count, string? Label = null);

public sealed record CreatorRsvpSubmissionListItem(Guid SubmissionId, DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreatorRsvpSubmissionDetail(Guid SubmissionId, DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt, IReadOnlyList<CreatorRsvpSubmissionAnswer> Answers);

public sealed record CreatorRsvpSubmissionAnswer(Guid QuestionId, string Prompt, string Type,
    string? SemanticRole, string? TextValue, string? NumberValue, bool? BooleanValue,
    IReadOnlyList<CreatorRsvpSelectedOption> SelectedOptions);

public sealed record CreatorRsvpSelectedOption(Guid OptionId, string Label);

public enum CreatorRsvpResultsOutcome { Succeeded, NotFound }

public sealed record CreatorRsvpSubmissionPageResult(CreatorRsvpResultsOutcome Outcome,
    CreatorRsvpSubmissionPage? Page = null);

public sealed record CreatorRsvpSubmissionDetailResult(CreatorRsvpResultsOutcome Outcome,
    CreatorRsvpSubmissionDetail? Submission = null);

public sealed record CreatorRsvpSubmissionDeleteResult(CreatorRsvpResultsOutcome Outcome);
