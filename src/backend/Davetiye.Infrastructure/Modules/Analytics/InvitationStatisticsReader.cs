using Davetiye.Application.Modules.Analytics.Contracts;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Davetiye.Infrastructure.Modules.Analytics;

/// <summary>Reads invitation metrics as one bounded scalar query, never materializing guest-owned rows.</summary>
public sealed class InvitationStatisticsReader(DavetiyeDbContext dbContext) : IInvitationStatisticsReader
{
    internal const string StatisticsSql = """
        SELECT
            COALESCE((SELECT total FROM invitation_view_totals WHERE invitation_id = @invitation_id), 0)::bigint,
            (SELECT COUNT(*) FROM rsvp_submissions WHERE invitation_id = @invitation_id)::bigint,
            COALESCE((
                SELECT SUM(answer.number_value)
                FROM rsvp_answers AS answer
                INNER JOIN rsvp_submissions AS submission ON submission.id = answer.submission_id
                INNER JOIN rsvp_configurations AS configuration ON configuration.invitation_id = submission.invitation_id
                INNER JOIN rsvp_questions AS question ON question.id = answer.question_id
                    AND question.configuration_id = configuration.id
                WHERE submission.invitation_id = @invitation_id
                    AND question.archived_at IS NULL
                    AND question.semantic_role = @participant_count_role
                    AND question."type" = @number_type
                    AND answer.semantic_role_snapshot = @participant_count_role
                    AND answer.question_type_snapshot = @number_type
            ), 0)::bigint,
            (SELECT COUNT(*) FROM memories
                WHERE invitation_id = @invitation_id AND state IN ('Published', 'Hidden'))::bigint,
            (SELECT COUNT(*) FROM media_assets
                WHERE invitation_id = @invitation_id AND state = 'Ready'
                    AND quota_scope IN ('Creator', 'Guest'))::bigint,
            (SELECT COUNT(*) FROM gift_reservations WHERE invitation_id = @invitation_id)::bigint
        """;

    public async Task<InvitationAggregateStatistics> ReadAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = StatisticsSql;
            command.Parameters.Add(new NpgsqlParameter("invitation_id", invitationId));
            command.Parameters.Add(new NpgsqlParameter("participant_count_role", (int)RsvpQuestionSemanticRole.ParticipantCount));
            command.Parameters.Add(new NpgsqlParameter("number_type", (int)RsvpQuestionType.Number));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Invitation statistics query returned no aggregate row.");

            return new InvitationAggregateStatistics(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
        }
        finally
        {
            if (openedHere) await dbContext.Database.CloseConnectionAsync();
        }
    }
}
