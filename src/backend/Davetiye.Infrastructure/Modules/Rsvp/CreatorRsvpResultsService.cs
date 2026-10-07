using System.Globalization;
using System.Data;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Davetiye.Infrastructure.Modules.Rsvp;

public sealed class CreatorRsvpResultsService(
    DavetiyeDbContext dbContext,
    IRsvpCreatorInvitationAccessReader invitationAccess)
    : ICreatorRsvpResultsService
{
    public async Task<CreatorRsvpSubmissionPageResult> ListAsync(Guid accountId, Guid invitationId, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100) return new(CreatorRsvpResultsOutcome.NotFound);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,
            cancellationToken);
        if (await invitationAccess.GetOwnedEffectiveStateAsync(accountId, invitationId, cancellationToken) is null)
            return new(CreatorRsvpResultsOutcome.NotFound);

        var totalCount = await dbContext.RsvpSubmissions.AsNoTracking()
            .CountAsync(item => item.InvitationId == invitationId, cancellationToken);
        var offset = (long)(page - 1) * pageSize;
        var rows = await dbContext.RsvpSubmissions.AsNoTracking()
            .Where(item => item.InvitationId == invitationId)
            .OrderByDescending(item => item.SubmittedAt).ThenByDescending(item => item.Id)
            .Skip((int)Math.Min(offset, int.MaxValue)).Take(pageSize)
            .Select(item => new CreatorRsvpSubmissionListItem(item.Id, item.SubmittedAt, item.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        var summary = await BuildSummaryAsync(invitationId, totalCount, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(CreatorRsvpResultsOutcome.Succeeded,
            new CreatorRsvpSubmissionPage(invitationId, page, pageSize, totalCount, summary, rows));
    }

    public async Task<CreatorRsvpSubmissionDetailResult> GetAsync(Guid accountId, Guid invitationId,
        Guid submissionId, CancellationToken cancellationToken)
    {
        if (submissionId == Guid.Empty ||
            await invitationAccess.GetOwnedEffectiveStateAsync(accountId, invitationId, cancellationToken) is null)
            return new(CreatorRsvpResultsOutcome.NotFound);

        var submission = await dbContext.RsvpSubmissions.AsNoTracking()
            .Include(item => item.Answers).ThenInclude(answer => answer.SelectedOptions)
            .SingleOrDefaultAsync(item => item.Id == submissionId && item.InvitationId == invitationId,
                cancellationToken);
        if (submission is null) return new(CreatorRsvpResultsOutcome.NotFound);

        var answers = submission.Answers.OrderBy(answer => answer.QuestionPromptSnapshot, StringComparer.Ordinal)
            .ThenBy(answer => answer.QuestionId)
            .Select(answer => new CreatorRsvpSubmissionAnswer(answer.QuestionId, answer.QuestionPromptSnapshot,
                answer.QuestionTypeSnapshot.ToString(), answer.SemanticRoleSnapshot?.ToString(), answer.TextValue,
                answer.NumberValue?.ToString(CultureInfo.InvariantCulture), answer.BooleanValue,
                answer.SelectedOptions.Select(option => new CreatorRsvpSelectedOption(option.OptionId,
                    option.LabelSnapshot)).ToArray()))
            .ToArray();
        return new(CreatorRsvpResultsOutcome.Succeeded,
            new CreatorRsvpSubmissionDetail(submission.Id, submission.SubmittedAt, submission.UpdatedAt, answers));
    }

    public async Task<CreatorRsvpSubmissionDeleteResult> DeleteAsync(Guid accountId, Guid invitationId,
        Guid submissionId, CancellationToken cancellationToken)
    {
        if (submissionId == Guid.Empty) return new(CreatorRsvpResultsOutcome.NotFound);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await invitationAccess.LockOwnedAndGetEffectiveStateAsync(accountId, invitationId, cancellationToken) is null)
            return new(CreatorRsvpResultsOutcome.NotFound);

        var submission = await dbContext.RsvpSubmissions.SingleOrDefaultAsync(item =>
            item.Id == submissionId && item.InvitationId == invitationId, cancellationToken);
        if (submission is null) return new(CreatorRsvpResultsOutcome.NotFound);

        dbContext.RsvpSubmissions.Remove(submission);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(CreatorRsvpResultsOutcome.Succeeded);
    }

    private async Task<CreatorRsvpSummary> BuildSummaryAsync(Guid invitationId, int responseCount,
        CancellationToken cancellationToken)
    {
        var questions = await dbContext.RsvpConfigurations.AsNoTracking()
            .Where(item => item.InvitationId == invitationId)
            .SelectMany(item => item.Questions.Where(question => question.ArchivedAt == null))
            .Select(question => new { question.Id, question.Prompt, question.Type, question.SemanticRole, question.SortOrder })
            .ToArrayAsync(cancellationToken);
        questions = questions.OrderBy(question => question.SortOrder).ThenBy(question => question.Id).ToArray();
        if (questions.Length == 0)
            return new CreatorRsvpSummary(responseCount, [], null, false);

        var result = new List<CreatorRsvpQuestionSummary>(questions.Length);
        decimal participantTotal = 0;
        var participantCountAvailable = questions.Any(question =>
            question.SemanticRole == RsvpQuestionSemanticRole.ParticipantCount && question.Type == RsvpQuestionType.Number);
        foreach (var question in questions)
        {
            var questionAnswers = dbContext.RsvpAnswers.AsNoTracking().Where(answer =>
                answer.QuestionId == question.Id && dbContext.RsvpSubmissions.Any(submission =>
                    submission.Id == answer.SubmissionId && submission.InvitationId == invitationId));
            var snapshots = await questionAnswers
                .GroupBy(answer => new { answer.QuestionPromptSnapshot, answer.QuestionTypeSnapshot })
                .Select(group => new
                {
                    group.Key.QuestionPromptSnapshot,
                    group.Key.QuestionTypeSnapshot,
                    AnsweredCount = group.Count()
                })
                .ToArrayAsync(cancellationToken);
            foreach (var snapshot in snapshots.OrderBy(item => item.QuestionPromptSnapshot, StringComparer.Ordinal)
                         .ThenBy(item => item.QuestionTypeSnapshot))
            {
                var matchingSnapshot = questionAnswers.Where(answer =>
                    answer.QuestionPromptSnapshot == snapshot.QuestionPromptSnapshot &&
                    answer.QuestionTypeSnapshot == snapshot.QuestionTypeSnapshot);
                var values = Array.Empty<CreatorRsvpValueCount>();
                if (snapshot.QuestionTypeSnapshot == RsvpQuestionType.YesNo)
                {
                    var counts = await matchingSnapshot.Where(answer => answer.BooleanValue != null)
                        .GroupBy(answer => answer.BooleanValue)
                        .Select(group => new { Value = group.Key, Count = group.Count() })
                        .ToArrayAsync(cancellationToken);
                    values = counts.Select(item => new CreatorRsvpValueCount(item.Value == true ? "true" : "false", item.Count))
                        .OrderBy(item => item.Value, StringComparer.Ordinal).ToArray();
                }
                else if (snapshot.QuestionTypeSnapshot is RsvpQuestionType.SingleChoice or RsvpQuestionType.MultipleChoice)
                {
                    var counts = await (from selected in dbContext.RsvpAnswerOptions.AsNoTracking()
                                        join answer in matchingSnapshot on selected.AnswerId equals answer.Id
                                        group selected by new { selected.OptionId, selected.LabelSnapshot } into grouped
                                        select new { grouped.Key.OptionId, grouped.Key.LabelSnapshot, Count = grouped.Count() })
                        .ToArrayAsync(cancellationToken);
                    values = counts.Select(item => new CreatorRsvpValueCount(item.OptionId.ToString(), item.Count,
                            item.LabelSnapshot))
                        .OrderBy(item => item.Label, StringComparer.Ordinal).ThenBy(item => item.Value, StringComparer.Ordinal)
                        .ToArray();
                }

                result.Add(new CreatorRsvpQuestionSummary(question.Id, snapshot.QuestionPromptSnapshot,
                    snapshot.QuestionTypeSnapshot.ToString(), snapshot.AnsweredCount, values));
            }

            if (question.SemanticRole == RsvpQuestionSemanticRole.ParticipantCount &&
                question.Type == RsvpQuestionType.Number)
            {
                var validCounts = await questionAnswers.Where(answer =>
                        answer.SemanticRoleSnapshot == RsvpQuestionSemanticRole.ParticipantCount &&
                        answer.QuestionTypeSnapshot == RsvpQuestionType.Number &&
                        answer.NumberValue != null && answer.NumberValue >= 0 && answer.NumberValue <= 20 &&
                        answer.NumberValue == decimal.Truncate(answer.NumberValue.Value))
                    .Select(answer => answer.NumberValue!.Value).ToArrayAsync(cancellationToken);
                participantTotal += validCounts.Sum();
            }
        }

        return new CreatorRsvpSummary(responseCount, result,
            participantCountAvailable ? participantTotal : null, participantCountAvailable);
    }
}
