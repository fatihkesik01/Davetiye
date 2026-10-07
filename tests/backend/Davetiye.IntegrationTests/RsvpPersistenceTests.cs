using System.Diagnostics;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Infrastructure.Modules.Rsvp;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class RsvpPersistenceTests(PostgreSqlFixture postgreSql)
{
    [Fact]
    public async Task RSVP_graph_round_trips_and_purge_is_atomic_without_cross_module_invitation_foreign_keys()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var invitationId = Guid.NewGuid(); // Deliberately no Invitations row: RSVP owns this ID reference only.
        var now = DateTimeOffset.UtcNow;
        var configuration = RsvpConfiguration.Create(Guid.NewGuid(), invitationId, now);
        var countQuestion = configuration.AddQuestion(Guid.NewGuid(), "How many?", RsvpQuestionType.Number,
            true, 0, RsvpQuestionSemanticRole.ParticipantCount, now);
        var choiceQuestion = configuration.AddQuestion(Guid.NewGuid(), "Meal", RsvpQuestionType.SingleChoice,
            true, 1, null, now);
        var mealOption = choiceQuestion.AddOption(Guid.NewGuid(), "Vegetarian", 0, now);

        var submission = RsvpSubmission.Create(Guid.NewGuid(), invitationId, now);
        submission.AddAnswer(Guid.NewGuid(), configuration, countQuestion.Id, now, numberValue: 2m);
        submission.AddAnswer(Guid.NewGuid(), configuration, choiceQuestion.Id, now,
            selectedOptionIds: [mealOption.Id]);
        var capability = RsvpManageCapability.Create(Guid.NewGuid(), submission.Id, RsvpManageCapability.RequiredPurpose,
            3, new byte[RsvpManageCapability.HmacSha256DigestLength], now, now.AddMinutes(5));

        await using (var seedContext = CreateDbContext(connectionString))
        {
            seedContext.RsvpConfigurations.Add(configuration);
            seedContext.RsvpSubmissions.Add(submission);
            seedContext.RsvpManageCapabilities.Add(capability);
            await seedContext.SaveChangesAsync();
        }

        await using (var verifyContext = CreateDbContext(connectionString))
        {
            var loadedConfiguration = await verifyContext.RsvpConfigurations
                .Include(item => item.Questions).ThenInclude(item => item.Options)
                .SingleAsync(item => item.Id == configuration.Id);
            Assert.Equal(2, loadedConfiguration.Questions.Count);
            Assert.Single(loadedConfiguration.Questions.Single(item => item.Id == choiceQuestion.Id).Options);

            var loadedSubmission = await verifyContext.RsvpSubmissions
                .Include(item => item.Answers).ThenInclude(item => item.SelectedOptions)
                .SingleAsync(item => item.Id == submission.Id);
            Assert.Equal(2, loadedSubmission.Answers.Count);
            Assert.Equal("Meal", loadedSubmission.Answers.Single(item => item.QuestionId == choiceQuestion.Id).QuestionPromptSnapshot);
            Assert.Equal("Vegetarian", Assert.Single(
                loadedSubmission.Answers.Single(item => item.QuestionId == choiceQuestion.Id).SelectedOptions).LabelSnapshot);
            Assert.Equal(2m, loadedSubmission.Answers.Single(item => item.QuestionId == countQuestion.Id).NumberValue);

            var storedCapability = await verifyContext.RsvpManageCapabilities.SingleAsync(item => item.Id == capability.Id);
            Assert.Equal(RsvpManageCapability.RequiredPurpose, storedCapability.Purpose);
            Assert.Equal(RsvpManageCapability.HmacSha256DigestLength, storedCapability.HmacDigest.Length);
        }

        // The caller owns the transaction. A failed invitation purge must restore every RSVP row.
        await using (var rollbackContext = CreateDbContext(connectionString))
        {
            await using var transaction = await rollbackContext.Database.BeginTransactionAsync();
            await new RsvpPurgeCoordinator(rollbackContext).PurgeForInvitationAsync(invitationId, CancellationToken.None);
            await transaction.RollbackAsync();
        }

        await using (var afterRollback = CreateDbContext(connectionString))
        {
            Assert.True(await afterRollback.RsvpSubmissions.AnyAsync(item => item.Id == submission.Id));
            Assert.True(await afterRollback.RsvpConfigurations.AnyAsync(item => item.Id == configuration.Id));
            Assert.True(await afterRollback.RsvpAnswers.AnyAsync(item => item.SubmissionId == submission.Id));
            Assert.True(await afterRollback.RsvpManageCapabilities.AnyAsync(item => item.SubmissionId == submission.Id));
        }

        await using (var purgeContext = CreateDbContext(connectionString))
        {
            await using var transaction = await purgeContext.Database.BeginTransactionAsync();
            await new RsvpPurgeCoordinator(purgeContext).PurgeForInvitationAsync(invitationId, CancellationToken.None);
            await transaction.CommitAsync();
        }

        await using var afterPurge = CreateDbContext(connectionString);
        Assert.False(await afterPurge.RsvpSubmissions.AnyAsync(item => item.InvitationId == invitationId));
        Assert.False(await afterPurge.RsvpConfigurations.AnyAsync(item => item.InvitationId == invitationId));
        Assert.False(await afterPurge.RsvpAnswers.AnyAsync(item => item.SubmissionId == submission.Id));
        Assert.False(await afterPurge.RsvpAnswerOptions.AnyAsync(item => item.AnswerId == submission.Answers.Last().Id));
        Assert.False(await afterPurge.RsvpManageCapabilities.AnyAsync(item => item.SubmissionId == submission.Id));
    }

    [Fact]
    public async Task RSVP_constraints_enforce_unique_active_ordinals_participant_count_and_capability_scope()
    {
        var connectionString = await postgreSql.CreateEmptyDatabaseAsync();
        await RunMigratorAsync(connectionString);

        var configurationId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var answerId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO rsvp_configurations (id, invitation_id, is_enabled, created_at, updated_at, revision)
                VALUES (@id, @invitation_id, FALSE, now(), now(), 0);
                INSERT INTO rsvp_questions (id, configuration_id, prompt, type, is_required, sort_order, semantic_role, created_at)
                VALUES (@question_id, @configuration_id, 'Count', 6, TRUE, 0, 1, now());
                INSERT INTO rsvp_submissions (id, invitation_id, submitted_at, updated_at, revision)
                VALUES (@submission_id, @invitation_id, now(), now(), 0);
                INSERT INTO rsvp_answers (
                    id, submission_id, question_id, question_prompt_snapshot, question_type_snapshot,
                    is_required_snapshot, semantic_role_snapshot, number_value)
                VALUES (@answer_id, @submission_id, @question_id, 'Count', 6, TRUE, 1, 2.5);
                """;
            command.Parameters.AddWithValue("id", configurationId);
            command.Parameters.AddWithValue("invitation_id", invitationId);
            command.Parameters.AddWithValue("question_id", questionId);
            command.Parameters.AddWithValue("configuration_id", configurationId);
            command.Parameters.AddWithValue("submission_id", submissionId);
            command.Parameters.AddWithValue("answer_id", answerId);
            await command.ExecuteNonQueryAsync();
        }

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_configurations (id, invitation_id, is_enabled, created_at, updated_at, revision)
            VALUES (@id, @invitation_id, FALSE, now(), now(), 0)
            """, "23505", ("id", Guid.NewGuid()), ("invitation_id", invitationId));

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_questions (id, configuration_id, prompt, type, is_required, sort_order, semantic_role, created_at)
            VALUES (@id, @configuration_id, 'Not a number', 1, TRUE, 2, 1, now())
            """, "23514", ("id", Guid.NewGuid()), ("configuration_id", configurationId));

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_questions (id, configuration_id, prompt, type, is_required, sort_order, created_at)
            VALUES (@id, @configuration_id, 'Duplicate order', 1, TRUE, 0, now())
            """, "23505", ("id", Guid.NewGuid()), ("configuration_id", configurationId));

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_questions (id, configuration_id, prompt, type, is_required, sort_order, semantic_role, created_at)
            VALUES (@id, @configuration_id, 'Second participant count', 6, TRUE, 1, 1, now())
            """, "23505", ("id", Guid.NewGuid()), ("configuration_id", configurationId));

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_answers (
                id, submission_id, question_id, question_prompt_snapshot, question_type_snapshot,
                is_required_snapshot, semantic_role_snapshot, number_value)
            VALUES (@id, @submission_id, @question_id, 'Count', 6, TRUE, 1, 3)
            """, "23505", ("id", Guid.NewGuid()), ("submission_id", submissionId), ("question_id", questionId));

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_answers (
                id, submission_id, question_id, question_prompt_snapshot, question_type_snapshot,
                is_required_snapshot, semantic_role_snapshot, text_value)
            VALUES (@id, @submission_id, @question_id, 'Count', 6, TRUE, 1, 'two')
            """, "23514", ("id", Guid.NewGuid()), ("submission_id", submissionId), ("question_id", Guid.NewGuid()));

        await using (var archiveQuestion = connection.CreateCommand())
        {
            archiveQuestion.CommandText = "UPDATE rsvp_questions SET archived_at = now() WHERE id = @id";
            archiveQuestion.Parameters.AddWithValue("id", questionId);
            await archiveQuestion.ExecuteNonQueryAsync();
        }
        await using (var reuseArchivedOrder = connection.CreateCommand())
        {
            reuseArchivedOrder.CommandText = """
                INSERT INTO rsvp_questions (id, configuration_id, prompt, type, is_required, sort_order, created_at)
                VALUES (@id, @configuration_id, 'Reused order', 1, TRUE, 0, now())
                """;
            reuseArchivedOrder.Parameters.AddWithValue("id", Guid.NewGuid());
            reuseArchivedOrder.Parameters.AddWithValue("configuration_id", configurationId);
            await reuseArchivedOrder.ExecuteNonQueryAsync();
        }

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_manage_capabilities (
                id, submission_id, purpose, hmac_key_version, hmac_digest, created_at, expires_at)
            VALUES (@id, @submission_id, 'wrong-purpose', 1, @digest, now(), now() + interval '5 minutes')
            """, "23514", ("id", Guid.NewGuid()), ("submission_id", submissionId), ("digest", new byte[32]));

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_manage_capabilities (
                id, submission_id, purpose, hmac_key_version, hmac_digest, created_at, expires_at)
            VALUES (@id, @submission_id, 'rsvp-manage', 1, @digest, now(), now())
            """, "23514", ("id", Guid.NewGuid()), ("submission_id", submissionId), ("digest", new byte[32]));

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_manage_capabilities (
                id, submission_id, purpose, hmac_key_version, hmac_digest, created_at, expires_at)
            VALUES (@id, @submission_id, 'rsvp-manage', 1, @digest, now(), now() + interval '5 minutes')
            """, "23514", ("id", Guid.NewGuid()), ("submission_id", submissionId), ("digest", new byte[31]));

        var validDigest = new byte[32];
        validDigest[0] = 1;
        await using (var validCapability = connection.CreateCommand())
        {
            validCapability.CommandText = """
                INSERT INTO rsvp_manage_capabilities (
                    id, submission_id, purpose, hmac_key_version, hmac_digest, created_at, expires_at)
                VALUES (@id, @submission_id, 'rsvp-manage', 1, @digest, now(), now() + interval '5 minutes')
                """;
            validCapability.Parameters.AddWithValue("id", Guid.NewGuid());
            validCapability.Parameters.AddWithValue("submission_id", submissionId);
            validCapability.Parameters.AddWithValue("digest", validDigest);
            await validCapability.ExecuteNonQueryAsync();
        }

        await AssertSqlStateAsync(connection, """
            INSERT INTO rsvp_manage_capabilities (
                id, submission_id, purpose, hmac_key_version, hmac_digest, created_at, expires_at)
            VALUES (@id, @submission_id, 'rsvp-manage', 2, @digest, now(), now() + interval '5 minutes')
            """, "23505", ("id", Guid.NewGuid()), ("submission_id", submissionId), ("digest", CreateDigest(2)));
    }

    private static async Task AssertSqlStateAsync(NpgsqlConnection connection, string sql, string expectedState,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(expectedState, exception.SqlState);
    }

    private static byte[] CreateDigest(byte firstByte)
    {
        var digest = new byte[32];
        digest[0] = firstByte;
        return digest;
    }

    private static DavetiyeDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DavetiyeDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName))
            .Options;
        return new DavetiyeDbContext(options);
    }

    private static async Task RunMigratorAsync(string connectionString)
    {
        var migratorAssembly = FindMigratorAssembly();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(migratorAssembly);
        startInfo.Environment["Database__ConnectionString"] = connectionString;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the database migrator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Migrator exited with {process.ExitCode}.{Environment.NewLine}{await stdout}{Environment.NewLine}{await stderr}");
    }

    private static string FindMigratorAssembly()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Davetiye.slnx"))) current = current.Parent;
        Assert.NotNull(current);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var path = Path.Combine(current!.FullName, "tools", "Davetiye.DatabaseMigrator", "bin", configuration, "net10.0", "Davetiye.DatabaseMigrator.dll");
        Assert.True(File.Exists(path), $"Migrator assembly was not built: {path}");
        return path;
    }
}
