using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Infrastructure.Modules.Administration;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.GiftRegistry;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.IntegrationFoundation;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.Administration;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.IntegrationFoundation;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.Templates;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.IntegrationFoundation.Contracts;
using Davetiye.Infrastructure.Modules.Media;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class TrashLifecycleIntegrationTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string RetentionKey = "deletedInvitationRetentionDays";
    private const string Password = "TestPassw0rd1";
    private const string Content = """{"headline":"Accepted snapshot","startsAt":"2026-10-10T12:00:00Z","timeZoneId":"Europe/Istanbul","venue":{"name":"Hall","address":"Street"},"message":"Welcome","hostNames":["Ada"]}""";

    [Fact]
    public async Task Prestart_release_crossing_accepted_start_rolls_back_then_retry_consumes()
    {
        await using var h = await CreateAsync(blockConsume: true);
        var scheduled = await h.Publish(true);
        var before = await h.Expected();
        h.ReleaseBlock.Enabled = true;
        var deleting = h.Delete(before);
        await h.ReleaseBlock.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Clock.Current = scheduled.CurrentWindow!.StartsAtUtc;
        h.ReleaseBlock.Release.TrySetResult();
        Assert.Equal("InvalidState", (await deleting.WaitAsync(TimeSpan.FromSeconds(10))).Code);
        Assert.Equal(before, await h.Expected());
        await using (var db = h.Db())
        {
            var grant = await db.AccountPlanGrants.SingleAsync();
            Assert.Equal(h.Invitation, grant.AssignedInvitationId);
            Assert.Equal(Now, grant.ReservedAt);
            Assert.Null(grant.ConsumedAt);
            Assert.Null((await db.Invitations.SingleAsync()).DeletedAt);
            Assert.True((await db.PublicationWindows.SingleAsync()).IsCurrent);
        }
        Assert.Equal("Succeeded", (await h.Delete(before)).Code);
        await using var verify = h.Db();
        var consumed = await verify.AccountPlanGrants.SingleAsync();
        Assert.Equal(scheduled.CurrentWindow.StartsAtUtc, consumed.ConsumedAt);
        Assert.Equal(h.Invitation, consumed.AssignedInvitationId);
        Assert.True((await verify.PublicationWindows.SingleAsync()).IsCurrent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prestart_trash_releases_grant_and_restore_requires_explicit_new_publication(bool paid)
    {
        await using var h = await CreateAsync();
        Guid? requested = null;
        if (paid)
        {
            await using var db = h.Db();
            var grant = AccountPlanGrant.Create(Guid.NewGuid(), h.Account, (await db.Plans.SingleAsync(p => p.Key == "standard")).Id, GrantSource.IndividualPurchase, Now);
            requested = grant.Id;
            db.AccountPlanGrants.Add(grant);
            await db.SaveChangesAsync();
        }
        var scheduled = await h.Publish(true, requested);
        Assert.Equal("Succeeded", (await h.Delete()).Code);
        await using (var db = h.Db())
        {
            var grant = await db.AccountPlanGrants.SingleAsync(g => g.Id == scheduled.CurrentWindow!.GrantId);
            Assert.Null(grant.ConsumedAt);
            Assert.Null(grant.ReservedAt);
            Assert.Null(grant.AssignedInvitationId);
            Assert.False((await db.PublicationWindows.SingleAsync()).IsCurrent);
        }
        var restored = await h.Restore();
        Assert.Equal("Succeeded", restored.Code);
        Assert.Equal("Draft", restored.Status!.EffectiveState);
        Assert.Null(restored.Status.CurrentWindow);
        Assert.Equal(new[] { "publish" }, restored.Status.AllowedActions);
        using var guest = h.CreateClient();
        var unavailable = await guest.GetFromJsonAsync<JsonElement>(h.PublicPath);
        Assert.Equal("unavailable", unavailable.GetProperty("status").GetString());
        var published = await h.Publish(requestedGrant: scheduled.CurrentWindow!.GrantId);
        Assert.NotEqual(scheduled.CurrentWindow.Id, published.CurrentWindow!.Id);
        Assert.Equal(scheduled.CurrentWindow.GrantId, published.CurrentWindow.GrantId);
        await using var verify = h.Db();
        Assert.Equal(h.Code, (await verify.Invitations.SingleAsync()).PublicCode);
        Assert.Equal(Now, (await verify.AccountPlanGrants.SingleAsync(g => g.Id == published.CurrentWindow.GrantId)).ConsumedAt);
        Assert.Equal(2, await verify.PublicationWindows.CountAsync());
    }

    [Fact]
    public async Task Scheduled_trash_at_exact_start_consumes_reservation_and_keeps_started_window()
    {
        await using var h = await CreateAsync();
        var accepted = await h.Publish(true);
        h.Clock.Current = accepted.CurrentWindow!.StartsAtUtc;
        Assert.Equal("Succeeded", (await h.Delete()).Code);
        await using var db = h.Db();
        var grant = await db.AccountPlanGrants.SingleAsync();
        Assert.Equal(accepted.CurrentWindow.StartsAtUtc, grant.ConsumedAt);
        Assert.Equal(h.Invitation, grant.AssignedInvitationId);
        Assert.True((await db.PublicationWindows.SingleAsync()).IsCurrent);
        var restored = await h.Restore();
        Assert.Contains("republish", restored.Status!.AllowedActions);
        Assert.DoesNotContain("publish", restored.Status.AllowedActions);
    }

    [Fact]
    public async Task Job_purges_prestart_trash_without_consuming_or_resetting_released_Free()
    {
        await using var h = await CreateAsync();
        var scheduled = await h.Publish(true);
        await using (var db = h.Db())
        {
            (await db.SystemSettings.SingleAsync(s => s.Key == RetentionKey)).UpdateValue("0");
            await db.SaveChangesAsync();
        }
        await h.Delete();
        Assert.True(h.Clock.UtcNow < scheduled.CurrentWindow!.StartsAtUtc);
        Assert.Equal(1, (await h.Jobs()).Purged);
        h.Clock.Current = scheduled.CurrentWindow.StartsAtUtc.AddDays(2);
        Assert.Equal(0, (await h.Jobs()).Purged);
        await using var verify = h.Db();
        Assert.False(await verify.Invitations.IgnoreQueryFilters().AnyAsync());
        Assert.False(await verify.PublicationWindows.AnyAsync());
        var grant = await verify.AccountPlanGrants.SingleAsync();
        Assert.Equal(GrantSource.Free, grant.Source);
        Assert.Null(grant.ConsumedAt);
        Assert.Null(grant.ReservedAt);
        Assert.Null(grant.AssignedInvitationId);
    }

    [Fact]
    public async Task Permanent_invitation_purge_atomically_removes_media_graph_and_enqueues_idempotent_provider_deletion()
    {
        await using var h = await CreateAsync();
        var assetId = Guid.NewGuid();
        var rejectedAssetId = Guid.NewGuid();
        var rsvpConfigurationId = Guid.NewGuid();
        var countQuestionId = Guid.NewGuid();
        var mealQuestionId = Guid.NewGuid();
        var mealOptionId = Guid.NewGuid();
        var rsvpSubmissionId = Guid.NewGuid();
        var mealAnswerId = Guid.NewGuid();
        var giftItemId = Guid.NewGuid();
        var giftSessionId = Guid.NewGuid();
        var giftReservationId = Guid.NewGuid();
        await using (var db = h.Db())
        {
            var asset = MediaAsset.CreateCreatorAsset(assetId, h.Invitation, MediaKind.Image, Now);
            asset.BeginProcessing("opaque-object");
            asset.MarkReady(new NormalizedImageVerificationEvidence("opaque-object", "image/webp", 4096), Now.AddSeconds(1));
            var expiredAsset = MediaAsset.CreateCreatorAsset(rejectedAssetId, h.Invitation, MediaKind.Video, Now.AddMinutes(-10));
            expiredAsset.BeginProcessing("opaque-rejected-object");
            expiredAsset.Reject();
            db.MediaAssets.AddRange(asset, expiredAsset);
            db.MediaPlacements.Add(asset.Place(Guid.NewGuid(), MediaPresentationRole.Cover, 0, Now.AddSeconds(1)));
            db.PendingUploads.Add(PendingUpload.Create(Guid.NewGuid(), assetId, Guid.NewGuid(), MediaPresentationRole.Cover,
                4096, new string('c', 64), Now.AddMinutes(-10), Now.AddMinutes(-5)));
            var expiredIntent = PendingUpload.Create(Guid.NewGuid(), rejectedAssetId, Guid.NewGuid(), MediaPresentationRole.Gallery,
                4096, new string('d', 64), Now.AddMinutes(-10), Now.AddMinutes(-5));
            expiredIntent.CancelExpired(Now);
            db.PendingUploads.Add(expiredIntent);

            var rsvpConfiguration = RsvpConfiguration.Create(rsvpConfigurationId, h.Invitation, Now);
            var countQuestion = rsvpConfiguration.AddQuestion(countQuestionId, "Guests", RsvpQuestionType.Number,
                true, 0, RsvpQuestionSemanticRole.ParticipantCount, Now);
            var mealQuestion = rsvpConfiguration.AddQuestion(mealQuestionId, "Meal", RsvpQuestionType.SingleChoice,
                true, 1, null, Now);
            var mealOption = mealQuestion.AddOption(mealOptionId, "Vegetarian", 0, Now);
            var rsvpSubmission = RsvpSubmission.Create(rsvpSubmissionId, h.Invitation, Now);
            rsvpSubmission.AddAnswer(Guid.NewGuid(), rsvpConfiguration, countQuestion.Id, Now, numberValue: 2m);
            rsvpSubmission.AddAnswer(mealAnswerId, rsvpConfiguration, mealQuestion.Id, Now,
                selectedOptionIds: [mealOption.Id]);
            db.RsvpConfigurations.Add(rsvpConfiguration);
            db.RsvpSubmissions.Add(rsvpSubmission);
            db.RsvpManageCapabilities.Add(RsvpManageCapability.Create(Guid.NewGuid(), rsvpSubmission.Id,
                RsvpManageCapability.RequiredPurpose, 1, new byte[RsvpManageCapability.HmacSha256DigestLength],
                Now, Now.AddMinutes(5)));

            // Gift Registry owns these rows without an Invitation FK. Permanent invitation purge
            // must invoke its narrow module contract in the same transaction, including guest PII.
            db.GiftItems.Add(GiftItem.Create(giftItemId, h.Invitation, "Tea set", 3, 0, Now));
            db.GuestGiftSessions.Add(GuestGiftSession.Create(giftSessionId, h.Invitation,
                GuestGiftSession.RequiredPurpose, 1, new byte[GuestGiftSession.HmacSha256DigestLength], Now));
            db.GiftReservations.Add(GiftReservation.Create(giftReservationId, h.Invitation, giftItemId,
                giftSessionId, 1, "Ada Lovelace", "ada@example.test", "+90 555 000 0000", Now));
            (await db.SystemSettings.SingleAsync(setting => setting.Key == RetentionKey)).UpdateValue("0");
            await db.SaveChangesAsync();
        }

        Assert.Equal("Succeeded", (await h.Delete(expectedDays: 0)).Code);
        Assert.Equal(1, (await h.Jobs()).Purged);
        Assert.Equal(0, (await h.Jobs()).Purged);
        await using var verify = h.Db();
        Assert.Equal(0, await verify.MediaAssets.IgnoreQueryFilters().CountAsync(asset => asset.Id == assetId || asset.Id == rejectedAssetId));
        Assert.Equal(0, await verify.MediaPlacements.CountAsync(placement => placement.MediaAssetId == assetId || placement.MediaAssetId == rejectedAssetId));
        Assert.Equal(0, await verify.PendingUploads.CountAsync(intent => intent.MediaAssetId == assetId || intent.MediaAssetId == rejectedAssetId));
        Assert.False(await verify.RsvpConfigurations.AnyAsync(item => item.InvitationId == h.Invitation));
        Assert.False(await verify.RsvpQuestions.AnyAsync(item => item.ConfigurationId == rsvpConfigurationId));
        Assert.False(await verify.RsvpQuestionOptions.AnyAsync(item => item.QuestionId == mealQuestionId));
        Assert.False(await verify.RsvpSubmissions.AnyAsync(item => item.InvitationId == h.Invitation));
        Assert.False(await verify.RsvpAnswers.AnyAsync(item => item.SubmissionId == rsvpSubmissionId));
        Assert.False(await verify.RsvpAnswerOptions.AnyAsync(item => item.AnswerId == mealAnswerId));
        Assert.False(await verify.RsvpManageCapabilities.AnyAsync(item => item.SubmissionId == rsvpSubmissionId));
        Assert.False(await verify.GiftReservations.AnyAsync(item => item.Id == giftReservationId));
        Assert.False(await verify.GiftReservations.AnyAsync(item => item.GuestFullName == "Ada Lovelace" ||
            item.Email == "ada@example.test" || item.Phone == "+90 555 000 0000"));
        Assert.False(await verify.GuestGiftSessions.AnyAsync(item => item.Id == giftSessionId));
        Assert.False(await verify.GiftItems.AnyAsync(item => item.Id == giftItemId));
        var messages = await verify.OutboxMessages.Where(message => message.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion).ToListAsync();
        Assert.Equal(2, messages.Count);
        var expectedAssetIds = new[] { assetId, rejectedAssetId }.ToHashSet();
        Assert.Equal(expectedAssetIds.Select(MediaPurgeCoordinator.StableMessageId).ToHashSet(), messages.Select(message => message.Id).ToHashSet());
        var payloadAssetIds = messages.Select(message =>
        {
            using var payload = JsonDocument.Parse(message.Payload);
            var root = payload.RootElement;
            var id = root.TryGetProperty("assetId", out var camelCaseId) ? camelCaseId : root.GetProperty("AssetId");
            return Guid.ParseExact(id.GetString()!, "N");
        }).ToHashSet();
        Assert.Equal(expectedAssetIds, payloadAssetIds);
        Assert.Equal(2, await verify.OutboxMessages.CountAsync(message => message.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion));

        var unrelated = OutboxMessage.Create(Guid.NewGuid(), "email.rsvp-confirmation", "{}", Now);
        verify.OutboxMessages.Add(unrelated);
        await verify.SaveChangesAsync();
        verify.ChangeTracker.Clear(); // Mirror the fresh scoped DbContext used for each worker run.
        var provider = new FakeMediaProvider { DeletionFailuresRemaining = 1 };
        var metrics = new FakeMediaLifecycleMetrics();
        var jobs = new MediaLifecycleJobs(verify, new OutboxWorkStore(verify), provider, metrics, h.Clock,
            Options.Create(new MediaLifecycleJobOptions { InitialRetryDelaySeconds = 1, MaximumRetryDelaySeconds = 5 }),
            new MediaReconciliationCursor(),
            NullLogger<MediaLifecycleJobs>.Instance);
        var firstAttempt = await jobs.RunBatchAsync(20, default);
        Assert.Equal(2, firstAttempt.DeletionMessagesClaimed);
        Assert.Equal(1, firstAttempt.DeletionsRetried);
        Assert.Equal(1, await verify.OutboxMessages.CountAsync(item =>
            item.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion && item.ProcessedAt == null));
        Assert.Null((await verify.OutboxMessages.SingleAsync(item => item.Id == unrelated.Id)).ProcessedAt);

        h.Clock.Current = Now.AddSeconds(10);
        verify.ChangeTracker.Clear();
        var retry = await jobs.RunBatchAsync(20, default);
        Assert.Equal(1, retry.DeletionsSucceeded);
        verify.ChangeTracker.Clear();
        Assert.Equal(2, await verify.OutboxMessages.CountAsync(item =>
            item.MessageType == MediaOutboxMessageTypes.PermanentAssetDeletion && item.ProcessedAt != null));
        Assert.Null((await verify.OutboxMessages.SingleAsync(item => item.Id == unrelated.Id)).ProcessedAt);
        Assert.Equal(new[] { false, true, true }, metrics.DeletionResults);
    }

    [Fact]
    public async Task Trash_requires_confirmed_retention_and_rejects_changed_policy_without_mutation()
    {
        await using var h = await CreateAsync();
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(owner, h.Email);
        var token = await Csrf(owner);
        var before = await h.Expected();
        var path = $"/api/v1/invitations/{h.Invitation}/trash";
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, path, new(before), token)).StatusCode);
        await using (var db = h.Db())
        {
            (await db.SystemSettings.SingleAsync(s => s.Key == RetentionKey)).UpdateValue("0");
            await db.SaveChangesAsync();
        }
        var denied = await Send(owner, path, new(before, 3), token);
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        Assert.Equal("RetentionChanged", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await h.Expected());
        await using (var db = h.Db())
        {
            var invitation = await db.Invitations.SingleAsync();
            Assert.Null(invitation.DeletedAt);
            Assert.Null(invitation.PurgeAfter);
        }
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, path, new(before, 0), token)).StatusCode);
    }

    [Fact]
    public async Task Restore_response_read_crossing_deadline_rolls_back_saved_overlay_and_state()
    {
        await using var h = await CreateAsync(blockStatus: true);
        await h.Publish();
        await h.Delete();
        var before = await h.Expected();
        h.StatusBlock.Enabled = true;
        var restoring = h.Restore(before);
        await h.StatusBlock.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Clock.Current = Now.AddDays(3);
        h.StatusBlock.Release.TrySetResult();
        Assert.Equal("RestoreExpired", (await restoring.WaitAsync(TimeSpan.FromSeconds(10))).Code);
        Assert.Equal(before, await h.Expected());
        await using var verify = h.Db();
        var invitation = await verify.Invitations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(Now, invitation.DeletedAt);
        Assert.Equal(Now.AddDays(3), invitation.PurgeAfter);
        Assert.Equal(InvitationStoredState.Active, invitation.State);
        Assert.Equal("Accepted snapshot", await h.Headline());
        Assert.True((await verify.PublicationWindows.SingleAsync()).IsCurrent);
    }

    [Fact]
    public async Task Retention_seed_is_idempotent_default_three_days_and_preserves_operator_override()
    {
        await using var h = await CreateAsync();
        await using var db = h.Db();
        var initializer = new InvitationRetentionSettingsInitializer(db);
        await initializer.InitializeAsync(default);
        await initializer.InitializeAsync(default);
        var setting = await db.SystemSettings.SingleAsync(s => s.Key == RetentionKey);
        Assert.Equal(SystemSettingValueType.Integer, setting.ValueType);
        Assert.Equal(3, setting.AsInteger());
        setting.UpdateValue("7");
        await db.SaveChangesAsync();
        await initializer.InitializeAsync(default);
        db.ChangeTracker.Clear();
        Assert.Equal(7, (await db.SystemSettings.SingleAsync(s => s.Key == RetentionKey)).AsInteger());
    }

    [Fact]
    public async Task Trash_closes_public_and_creator_access_restore_returns_Draft_without_automatic_publication()
    {
        await using var h = await CreateAsync();
        var accepted = await h.Publish();
        Assert.Equal("Succeeded", (await h.Delete()).Code);
        using var guest = h.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(h.PublicPath)).StatusCode);
        await using (var scope = h.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IInvitationDraftService>();
            Assert.Equal(InvitationDraftOutcome.NotFound, (await drafts.GetAsync(h.Account, h.Invitation, default)).Outcome);
            Assert.Equal(0, (await drafts.ListAsync(h.Account, 1, 20, default)).TotalCount);
        }
        await using (var db = h.Db())
        {
            var trash = await db.Invitations.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(Now, trash.DeletedAt);
            Assert.Equal(Now.AddDays(3), trash.PurgeAfter);
        }
        Assert.Equal("Succeeded", (await h.Restore()).Code);
        var restored = await h.Status();
        Assert.Equal("Draft", restored.EffectiveState);
        Assert.Equal(accepted.PublicCode, restored.PublicCode);
        Assert.Equal(accepted.CurrentWindow!.EndsAtUtc, restored.CurrentWindow!.EndsAtUtc);
        Assert.Equal("{\"status\":\"unavailable\"}", await guest.GetStringAsync(h.PublicPath));
        await using var verify = h.Db();
        Assert.Null((await verify.Invitations.SingleAsync()).DeletedAt);
        Assert.Null((await verify.Invitations.SingleAsync()).PurgeAfter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Remaining_started_window_republish_keeps_grant_id_window_and_end_and_controls_snapshot(bool update)
    {
        await using var h = await CreateAsync();
        var accepted = await h.Publish();
        await h.Delete();
        await h.Restore();
        await h.EditWorking("Later private changes");
        var restored = await h.Status();
        var result = await h.Execute(new("republish", restored.Expected, PublishWorkingContent: update, ProceedWithRecommendedWarnings: true));
        Assert.Equal("Succeeded", result.Code);
        Assert.Equal(accepted.PublicCode, result.Status!.PublicCode);
        Assert.Equal(accepted.CurrentWindow!.Id, result.Status.CurrentWindow!.Id);
        Assert.Equal(accepted.CurrentWindow.GrantId, result.Status.CurrentWindow.GrantId);
        Assert.Equal(accepted.CurrentWindow.EndsAtUtc, result.Status.CurrentWindow.EndsAtUtc);
        Assert.Equal(update ? "Later private changes" : "Accepted snapshot", await h.Headline());
        await using var db = h.Db();
        Assert.Equal(1, await db.AccountPlanGrants.CountAsync());
        Assert.NotNull((await db.AccountPlanGrants.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task Remaining_window_republish_grandfathers_accepted_duration_after_numeric_downgrade()
    {
        await using var h = await CreateAsync();
        var accepted = await h.Publish();
        await h.Delete();
        await h.Restore();
        await using (var db = h.Db())
        {
            var free = await db.Plans.SingleAsync(p => p.Key == "free");
            (await db.PlanEntitlements.SingleAsync(e => e.PlanId == free.Id && e.EntitlementKey == EntitlementCatalog.MaxPublishDays)).UpdateValue(0, null);
            await db.SaveChangesAsync();
        }
        var result = await h.Execute(new("republish", (await h.Status()).Expected));
        Assert.Equal("Succeeded", result.Code);
        Assert.Equal(accepted.CurrentWindow!.Id, result.Status!.CurrentWindow!.Id);
        Assert.Equal(accepted.CurrentWindow.EndsAtUtc, result.Status.CurrentWindow.EndsAtUtc);
    }

    [Fact]
    public async Task Retention_changes_do_not_extend_or_shorten_an_already_accepted_trash_deadline()
    {
        await using var h = await CreateAsync();
        await h.Delete();
        var expected = await h.Expected();
        await using (var db = h.Db())
        {
            (await db.SystemSettings.SingleAsync(s => s.Key == RetentionKey)).UpdateValue("365");
            await db.SaveChangesAsync();
        }
        h.Clock.Current = Now.AddDays(3);
        Assert.Equal("RestoreExpired", (await h.Restore(expected)).Code);
        await using var verify = h.Db();
        Assert.Equal(Now.AddDays(3), (await verify.Invitations.IgnoreQueryFilters().SingleAsync()).PurgeAfter);
    }

    [Fact]
    public async Task Trash_restore_trash_cycle_rejects_old_restore_tuple_even_when_window_identity_is_unchanged()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        await h.Delete();
        var old = await h.Expected();
        await h.Restore();
        await h.Delete();
        var current = await h.Expected();
        Assert.Equal(old.WindowId, current.WindowId);
        Assert.NotEqual(old.InvitationRevision, current.InvitationRevision);
        Assert.Equal("Conflict", (await h.Restore(old)).Code);
        Assert.Equal(current, await h.Expected());
    }

    [Fact]
    public async Task Restore_queued_behind_row_lock_rechecks_retention_deadline_after_waiting()
    {
        await using var h = await CreateAsync();
        await h.Delete();
        var expected = await h.Expected();
        await using var blocker = h.Db();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM invitations WHERE id = {h.Invitation} FOR UPDATE");
        await using var scope = h.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        await context.Database.OpenConnectionAsync();
        var restoring = scope.ServiceProvider.GetRequiredService<IInvitationTrashService>().RestoreAsync(h.Account, h.Invitation, new(expected), default);
        var pid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        await using var monitor = h.Db();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await monitor.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock') AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(20, timeout.Token);
        h.Clock.Current = Now.AddDays(3);
        await transaction.CommitAsync();
        Assert.Equal("RestoreExpired", (await restoring.WaitAsync(TimeSpan.FromSeconds(10))).Code);
        Assert.Equal(expected, await h.Expected());
    }

    [Fact]
    public async Task Restore_crossing_deadline_during_grant_reconciliation_rolls_back_consumption_and_overlay_changes()
    {
        await using var h = await CreateAsync(blockConsume: true);
        var accepted = await h.Publish(true);
        h.Clock.Current = accepted.CurrentWindow!.StartsAtUtc.AddHours(1);
        await h.Delete();
        var before = await h.Expected();
        DateTimeOffset deadline;
        await using (var db = h.Db())
        {
            deadline = (await db.Invitations.IgnoreQueryFilters().SingleAsync()).PurgeAfter!.Value;
            // A delayed-worker legacy reservation is reconciled by restore inside its transaction.
            await db.Database.ExecuteSqlRawAsync("UPDATE account_plan_grants SET consumed_at = NULL");
        }
        h.ConsumeBlock.Enabled = true;
        var restoring = h.Restore(before);
        await h.ConsumeBlock.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Clock.Current = deadline;
        h.ConsumeBlock.Release.TrySetResult();
        Assert.Equal("RestoreExpired", (await restoring.WaitAsync(TimeSpan.FromSeconds(10))).Code);
        Assert.Equal(before, await h.Expected());
        await using var verify = h.Db();
        Assert.NotNull((await verify.Invitations.IgnoreQueryFilters().SingleAsync()).DeletedAt);
        Assert.Null((await verify.AccountPlanGrants.SingleAsync()).ConsumedAt);
        Assert.True((await verify.PublicationWindows.SingleAsync()).IsCurrent);
    }

    [Fact]
    public async Task Republish_crossing_window_end_during_grant_reconciliation_cannot_promote_Working_or_reopen_state()
    {
        await using var h = await CreateAsync(blockConsume: true);
        var accepted = await h.Publish();
        await h.Delete();
        await h.Restore();
        await h.EditWorking("Do not promote after expiry");
        var before = await h.Status();
        h.ConsumeBlock.Enabled = true;
        var republishing = h.Execute(new("republish", before.Expected, PublishWorkingContent: true, ProceedWithRecommendedWarnings: true));
        await h.ConsumeBlock.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Clock.Current = accepted.CurrentWindow!.EndsAtUtc;
        h.ConsumeBlock.Release.TrySetResult();
        Assert.Equal("InvalidState", (await republishing.WaitAsync(TimeSpan.FromSeconds(10))).Code);
        Assert.Equal(before.Expected, (await h.Status()).Expected);
        Assert.Equal("Draft", (await h.Status()).StoredState);
        Assert.Equal("Accepted snapshot", await h.Headline());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("366")]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    public async Task Missing_or_invalid_retention_fails_closed_without_trashing(string invalid)
    {
        await using var h = await CreateAsync();
        _ = await h.Status();
        await using (var db = h.Db())
        {
            var setting = await db.SystemSettings.SingleAsync(s => s.Key == RetentionKey);
            if (invalid == "missing") db.SystemSettings.Remove(setting);
            else if (invalid == "wrong-type")
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE system_settings SET value_type = 'String', value = '3' WHERE key = {RetentionKey}");
            else setting.UpdateValue(invalid);
            await db.SaveChangesAsync();
        }
        Assert.Equal("InvalidConfiguration", (await h.Delete()).Code);
        await using var verify = h.Db();
        Assert.Null((await verify.Invitations.SingleAsync()).DeletedAt);
    }

    [Fact]
    public async Task Zero_day_retention_does_not_allow_restore_and_purge_is_idempotent()
    {
        await using var h = await CreateAsync();
        await using (var db = h.Db())
        {
            (await db.SystemSettings.SingleAsync(s => s.Key == RetentionKey)).UpdateValue("0");
            await db.SaveChangesAsync();
        }
        Assert.Equal("Succeeded", (await h.Delete()).Code);
        Assert.Equal("RestoreExpired", (await h.Restore()).Code);
        await h.Jobs();
        await h.Jobs();
        await using var verify = h.Db();
        Assert.False(await verify.Invitations.IgnoreQueryFilters().AnyAsync());
        Assert.False(await verify.WorkingContents.AnyAsync());
    }

    [Fact]
    public async Task Jobs_consume_scheduled_at_accepted_start_and_expire_at_end_without_extending_window()
    {
        await using var h = await CreateAsync();
        var accepted = await h.Publish(true);
        h.Clock.Current = accepted.CurrentWindow!.StartsAtUtc.AddHours(1);
        await h.Jobs();
        var active = await h.Status();
        Assert.Equal("Active", active.StoredState);
        await using (var db = h.Db())
            Assert.Equal(accepted.CurrentWindow.StartsAtUtc, (await db.AccountPlanGrants.SingleAsync()).ConsumedAt);
        var revisions = active.Expected;
        await h.Jobs();
        Assert.Equal(revisions, (await h.Status()).Expected);
        await h.Execute(new("pause", revisions));
        h.Clock.Current = accepted.CurrentWindow.EndsAtUtc;
        await h.Jobs();
        var expired = await h.Status();
        Assert.Equal("Expired", expired.StoredState);
        Assert.Equal(accepted.CurrentWindow.EndsAtUtc, expired.CurrentWindow!.EndsAtUtc);
        var expiredRevisions = expired.Expected;
        await h.Jobs();
        Assert.Equal(expiredRevisions, (await h.Status()).Expected);
    }

    [Fact]
    public async Task Jobs_reconcile_a_whole_missed_scheduled_window_and_consume_free_once()
    {
        await using var h = await CreateAsync();
        var scheduled = await h.Publish(true);
        h.Clock.Current = scheduled.CurrentWindow!.EndsAtUtc.AddDays(1);
        await h.Jobs();
        await h.Jobs();
        Assert.Equal("Expired", (await h.Status()).StoredState);
        await using var verify = h.Db();
        Assert.Equal(scheduled.CurrentWindow.StartsAtUtc, (await verify.AccountPlanGrants.SingleAsync()).ConsumedAt);
        Assert.Equal(1, await verify.AccountPlanGrants.CountAsync());
    }

    [Fact]
    public async Task Purge_deletes_invitation_graph_but_keeps_consumed_free_grant_and_never_issues_another_free_right()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        await h.Delete();
        h.Clock.Current = Now.AddDays(3);
        await h.Jobs();
        await h.Jobs();
        await using (var db = h.Db())
        {
            Assert.False(await db.Invitations.IgnoreQueryFilters().AnyAsync());
            Assert.False(await db.WorkingContents.AnyAsync());
            Assert.False(await db.PublishedContents.AnyAsync());
            Assert.False(await db.PublicationWindows.AnyAsync());
            var grant = await db.AccountPlanGrants.SingleAsync();
            Assert.Equal(GrantSource.Free, grant.Source);
            Assert.Equal(Now, grant.ConsumedAt);
            Assert.Equal(h.Invitation, grant.AssignedInvitationId);
        }
        var newId = await h.AddDraft();
        await using var scope = h.Services.CreateAsyncScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
        var status = (await lifecycle.GetAsync(h.Account, newId, default)).Status!;
        var result = await lifecycle.ExecuteAsync(h.Account, newId, new("publish", status.Expected,
            new("Immediate", null, h.Clock.Current.AddDays(1).AddHours(3).ToString("yyyy-MM-ddTHH:mm:ss"), "Europe/Istanbul", null), ProceedWithRecommendedWarnings: true), default);
        Assert.Equal("GrantUnavailable", result.Code);
    }

    [Theory]
    [InlineData(false, "invitation")]
    [InlineData(false, "working")]
    [InlineData(false, "published")]
    [InlineData(false, "windowId")]
    [InlineData(false, "windowRevision")]
    [InlineData(true, "invitation")]
    [InlineData(true, "working")]
    [InlineData(true, "published")]
    [InlineData(true, "windowId")]
    [InlineData(true, "windowRevision")]
    public async Task Trash_and_restore_enforce_every_expected_revision_without_partial_mutation(bool restore, string field)
    {
        await using var h = await CreateAsync();
        await h.Publish();
        if (restore) await h.Delete();
        var current = await h.Expected();
        var stale = field switch
        {
            "invitation" => current with { InvitationRevision = current.InvitationRevision + 1 },
            "working" => current with { WorkingContentRevision = current.WorkingContentRevision + 1 },
            "published" => current with { PublishedContentRevision = current.PublishedContentRevision + 1 },
            "windowId" => current with { WindowId = Guid.NewGuid() },
            _ => current with { WindowRevision = current.WindowRevision + 1 }
        };
        Assert.Equal("Conflict", (restore ? await h.Restore(stale) : await h.Delete(stale)).Code);
        Assert.Equal(current, await h.Expected());
        await using var db = h.Db();
        Assert.Equal(restore ? Now : null, (await db.Invitations.IgnoreQueryFilters().SingleAsync()).DeletedAt);
    }

    [Fact]
    public async Task Concurrent_jobs_purge_each_graph_once_without_resetting_consumed_grants()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        await h.Delete();
        h.Clock.Current = Now.AddDays(3);
        var jobs = await Task.WhenAll(h.Jobs(), h.Jobs());
        Assert.Equal(1, jobs.Sum(result => result.Purged));
        await using var db = h.Db();
        Assert.False(await db.Invitations.IgnoreQueryFilters().AnyAsync());
        Assert.False(await db.PublicationWindows.AnyAsync());
        Assert.Single(await db.AccountPlanGrants.ToListAsync());
        Assert.Equal(Now, (await db.AccountPlanGrants.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task Restore_and_purge_at_exact_deadline_cannot_revive_an_expired_trash_record()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        await h.Delete();
        var expected = await h.Expected();
        h.Clock.Current = Now.AddDays(3);
        var restoring = h.Restore(expected);
        var purging = h.Jobs();
        var restored = await restoring;
        await purging;
        Assert.Contains(restored.Code, new[] { "RestoreExpired", "NotFound" });
        await using var db = h.Db();
        Assert.False(await db.Invitations.IgnoreQueryFilters().AnyAsync());
        Assert.Equal(Now, (await db.AccountPlanGrants.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task Remaining_window_republish_rechecks_current_quota_and_does_not_extend_expired_restored_window()
    {
        await using var h = await CreateAsync();
        var old = await h.Publish();
        await h.Delete();
        var other = await h.AddDraft();
        await h.PublishOther(other);
        await h.Restore();
        var restored = await h.Status();
        var denied = await h.Execute(new("republish", restored.Expected));
        Assert.Equal("ActiveInvitationQuotaExceeded", denied.Code);
        Assert.Equal(restored.Expected, (await h.Status()).Expected);
        Assert.Equal(old.CurrentWindow!.EndsAtUtc, (await h.Status()).CurrentWindow!.EndsAtUtc);
        Assert.Equal("Succeeded", (await h.Delete()).Code);
        h.Clock.Current = old.CurrentWindow.EndsAtUtc;
        Assert.Equal("Succeeded", (await h.Restore()).Code);
        var expired = await h.Status();
        Assert.Equal("Draft", expired.EffectiveState);
        await using var db = h.Db();
        Assert.False(await db.PublicationWindows.AnyAsync(w => w.InvitationId == h.Invitation && w.IsCurrent));
        Assert.NotEqual("Succeeded", (await h.Execute(new("republish", expired.Expected))).Code);
    }

    [Fact]
    public async Task Restored_Draft_whose_remaining_window_expires_before_action_can_publish_with_new_grant_atomically()
    {
        await using var h = await CreateAsync();
        var accepted = await h.Publish();
        await h.Delete();
        await h.Restore();
        h.Clock.Current = accepted.CurrentWindow!.EndsAtUtc.AddHours(1);
        var before = await h.Status();
        Assert.Contains("publish", before.AllowedActions);
        Assert.DoesNotContain("republish", before.AllowedActions);
        Guid newGrant;
        await using (var db = h.Db())
        {
            var grant = AccountPlanGrant.Create(Guid.NewGuid(), h.Account, (await db.Plans.SingleAsync(p => p.Key == "standard")).Id, GrantSource.IndividualPurchase, h.Clock.Current);
            newGrant = grant.Id;
            db.AccountPlanGrants.Add(grant);
            await db.SaveChangesAsync();
        }
        var result = await h.Execute(new("publish", before.Expected,
            new("Immediate", null, h.Clock.Current.AddDays(1).AddHours(3).ToString("yyyy-MM-ddTHH:mm:ss"), "Europe/Istanbul", newGrant), ProceedWithRecommendedWarnings: true));
        Assert.Equal("Succeeded", result.Code);
        Assert.Equal(accepted.PublicCode, result.Status!.PublicCode);
        Assert.NotEqual(accepted.CurrentWindow.Id, result.Status.CurrentWindow!.Id);
        Assert.Equal(newGrant, result.Status.CurrentWindow.GrantId);
        Assert.Equal(h.Clock.Current, result.Status.CurrentWindow.StartsAtUtc);
        Assert.Equal(h.Clock.Current.AddDays(1), result.Status.CurrentWindow.EndsAtUtc);
        await using var verify = h.Db();
        Assert.Equal(2, await verify.PublicationWindows.CountAsync());
        Assert.Equal(1, await verify.PublicationWindows.CountAsync(w => w.IsCurrent));
        Assert.False((await verify.PublicationWindows.SingleAsync(w => w.Id == accepted.CurrentWindow.Id)).IsCurrent);
        Assert.Equal(Now, (await verify.AccountPlanGrants.SingleAsync(g => g.Source == GrantSource.Free)).ConsumedAt);
        Assert.Equal(h.Clock.Current, (await verify.AccountPlanGrants.SingleAsync(g => g.Id == newGrant)).ConsumedAt);
    }

    [Fact]
    public async Task Trash_HTTP_guards_cookies_CSRF_owner_and_private_cache_then_restore_is_explicit_Draft()
    {
        await using var h = await CreateAsync();
        var accepted = await h.Publish();
        using var anonymous = h.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/v1/invitations/trash", UriKind.Relative))).StatusCode);
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(owner, h.Email);
        var payload = new InvitationTrashRequest(accepted.Expected, 3);
        var path = $"/api/v1/invitations/{h.Invitation}/trash";
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(new Uri(path, UriKind.Relative), payload)).StatusCode);
        using var foreign = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(foreign, await h.AddForeign());
        var foreignResponse = await Send(foreign, path, payload, await Csrf(foreign));
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.True(foreignResponse.Headers.CacheControl?.NoStore);
        var token = await Csrf(owner);
        var deleted = await Send(owner, path, payload, token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.True(deleted.Headers.CacheControl?.NoStore);
        var trash = await owner.GetAsync(new Uri("/api/v1/invitations/trash", UriKind.Relative));
        Assert.True(trash.Headers.CacheControl?.NoStore);
        var page = await trash.Content.ReadFromJsonAsync<InvitationTrashPage>();
        Assert.Equal(1, page!.TotalCount);
        Assert.Equal(h.Invitation, Assert.Single(page.Items).InvitationId);
        Assert.Equal(Now, page.ServerNowUtc);
        var foreignList = await foreign.GetFromJsonAsync<InvitationTrashPage>(new Uri("/api/v1/invitations/trash", UriKind.Relative));
        Assert.Empty(foreignList!.Items);
        var foreignRestore = await Send(foreign, $"/api/v1/invitations/{h.Invitation}/restore", new InvitationTrashRequest(await h.Expected()), await Csrf(foreign));
        Assert.Equal(HttpStatusCode.NotFound, foreignRestore.StatusCode);
        var restored = await Send(owner, $"/api/v1/invitations/{h.Invitation}/restore", new InvitationTrashRequest(await h.Expected()), token);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.True(restored.Headers.CacheControl?.NoStore);
        Assert.Equal("Draft", (await h.Status()).EffectiveState);
        Assert.Equal("{\"status\":\"unavailable\"}", await anonymous.GetStringAsync(h.PublicPath));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Existing_cookie_cannot_trash_or_restore_after_ban_or_loss_of_verification(bool ban, bool restore)
    {
        await using var h = await CreateAsync();
        if (restore) await h.Delete();
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(owner, h.Email);
        var token = await Csrf(owner);
        var expected = await h.Expected();
        await using (var db = h.Db())
        {
            if (ban) db.BanRecords.Add(BanRecord.Create(Guid.NewGuid(), h.Account, "Trash ban", Now, Guid.NewGuid()));
            else (await db.Users.SingleAsync()).EmailConfirmed = false;
            await db.SaveChangesAsync();
        }
        var response = await Send(owner, $"/api/v1/invitations/{h.Invitation}/{(restore ? "restore" : "trash")}", new InvitationTrashRequest(expected, 3), token);
        Assert.Equal(ban ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        var list = await owner.GetAsync(new Uri("/api/v1/invitations/trash", UriKind.Relative));
        Assert.Equal(ban ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, list.StatusCode);
        Assert.DoesNotContain("Accepted snapshot", await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var verify = h.Db();
        Assert.Equal(restore ? Now : null, (await verify.Invitations.IgnoreQueryFilters().SingleAsync()).DeletedAt);
    }

    [Theory]
    [InlineData("page=2147483647&pageSize=20")]
    [InlineData("page=0&pageSize=20")]
    [InlineData("page=1&pageSize=0")]
    [InlineData("page=1&pageSize=101")]
    public async Task Trash_list_rejects_invalid_or_overflowing_pagination_before_database_query(string query)
    {
        await using var h = await CreateAsync();
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(owner, h.Email);
        var response = await owner.GetAsync(new Uri($"/api/v1/invitations/trash?{query}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Trash_and_restore_share_configurable_action_rate_limit_without_mutating_on_rejection()
    {
        await using var h = await CreateAsync(actionLimit: 1);
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(owner, h.Email);
        var token = await Csrf(owner);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, $"/api/v1/invitations/{h.Invitation}/trash", new InvitationTrashRequest(await h.Expected(), 3), token)).StatusCode);
        var expected = await h.Expected();
        var denied = await Send(owner, $"/api/v1/invitations/{h.Invitation}/restore", new InvitationTrashRequest(expected), token);
        Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
        Assert.DoesNotContain("Accepted snapshot", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(expected, await h.Expected());
        await using var verify = h.Db();
        Assert.NotNull((await verify.Invitations.IgnoreQueryFilters().SingleAsync()).DeletedAt);
    }

    [Fact]
    public async Task Authenticated_raw_HTTP_trash_list_is_rejected_in_production()
    {
        await using var h = await CreateAsync(environment: "Production");
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var login = await owner.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new { email = h.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://localhost/api/v1/invitations/trash"));
        request.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.SendAsync(request)).StatusCode);
    }

    private static async Task Login(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = Password })).StatusCode);
    }
    private static async Task<string> Csrf(HttpClient client)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/v1/antiforgery/token", UriKind.Relative)));
        return json.RootElement.GetProperty("token").GetString()!;
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, InvitationTrashRequest payload, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private async Task<Harness> CreateAsync(bool blockConsume = false, int actionLimit = 20, string environment = "Development", bool blockStatus = false)
    {
        var connectionBuilder = new NpgsqlConnectionStringBuilder(await postgreSql.CreateEmptyDatabaseAsync()) { Pooling = false };
        var connection = connectionBuilder.ConnectionString;
        var h = new Harness(connection, blockConsume, actionLimit, environment, blockStatus);
        await using var db = h.Db();
        await db.Database.MigrateAsync();
        await new PlanCatalogInitializer(db).InitializeAsync(default);
        await new TemplateCatalogInitializer(db, new TemplateRendererRegistry()).InitializeAsync(default);
        await new InvitationRetentionSettingsInitializer(db).InitializeAsync(default);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = h.Email, NormalizedUserName = h.Email.ToUpperInvariant(), Email = h.Email, NormalizedEmail = h.Email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
        db.Users.Add(user);
        db.Accounts.Add(Account.Create(h.Account, user.Id, AccountType.Individual, "Trash tester", Now));
        db.AddAcknowledgedServiceNotice(h.Account, Now);
        var invitation = Invitation.Create(h.Invitation, h.Account, h.Code, Now);
        invitation.PinTemplate("zamansiz-dugun", 1);
        db.Invitations.Add(invitation);
        db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), h.Invitation, 1, Content, Now));
        await db.SaveChangesAsync();
        return h;
    }

    private sealed class MutableClock : IClock { public DateTimeOffset Current { get; set; } = Now; public DateTimeOffset UtcNow => Current; }
    private sealed class FakeMediaLifecycleMetrics : IMediaLifecycleMetrics
    {
        public List<bool> DeletionResults { get; } = [];
        public void RecordExpiredIntents(int count) { }
        public void RecordDeletionResult(string outcome) => DeletionResults.Add(outcome is "deleted" or "already_absent");
        public void RecordReconciliation(string outcome) { }
        public void SetAssetSnapshot(string state, long count, long recordedBytes) { }
        public void SetDeletionBacklog(long pendingCount, double oldestAgeSeconds, long terminalCount) { }
    }
    private sealed class FakeMediaProvider : IMediaProviderAssetMaintenance
    {
        public int DeletionFailuresRemaining { get; set; }
        public Task<MediaProviderDeletionResult> DeleteAsync(Guid assetId, CancellationToken cancellationToken)
        {
            if (DeletionFailuresRemaining-- > 0) throw new HttpRequestException("Synthetic provider outage.");
            return Task.FromResult(MediaProviderDeletionResult.Deleted);
        }
        public Task<MediaProviderAssetPresence> InspectAsync(Guid assetId, MediaKind kind, CancellationToken cancellationToken) =>
            Task.FromResult(MediaProviderAssetPresence.Absent);
    }
    private sealed class ConsumeBlock
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class BlockingGrants(IPublicationGrantLifecycleService inner, ConsumeBlock block, ConsumeBlock releaseBlock) : IPublicationGrantLifecycleService
    {
        public Task<IReadOnlyList<PublicationGrantOption>> ListChoicesAsync(Guid account, Guid invitation, IReadOnlyCollection<Guid> started, CancellationToken token) => inner.ListChoicesAsync(account, invitation, started, token);
        public async Task<bool> ReleaseAsync(Guid account, Guid invitation, Guid grant, CancellationToken token)
        {
            var result = await inner.ReleaseAsync(account, invitation, grant, token);
            if (releaseBlock.Enabled)
            {
                releaseBlock.Ready.TrySetResult();
                await releaseBlock.Release.Task.WaitAsync(token);
            }
            return result;
        }
        public async Task<bool> ConsumeStartedAsync(Guid account, Guid invitation, Guid grant, DateTimeOffset start, CancellationToken token)
        {
            var result = await inner.ConsumeStartedAsync(account, invitation, grant, start, token);
            if (block.Enabled)
            {
                block.Ready.TrySetResult();
                await block.Release.Task.WaitAsync(token);
            }
            return result;
        }
    }
    private sealed class BlockingPublication(IPublicationLifecycleService inner, ConsumeBlock block) : IPublicationLifecycleService
    {
        public Task<PublicationLifecycleResult> ExecuteAsync(Guid account, Guid invitation, PublicationActionRequest request, CancellationToken token) => inner.ExecuteAsync(account, invitation, request, token);
        public async Task<PublicationLifecycleResult> GetAsync(Guid account, Guid invitation, CancellationToken token)
        {
            var result = await inner.GetAsync(account, invitation, token);
            if (block.Enabled)
            {
                block.Ready.TrySetResult();
                await block.Release.Task.WaitAsync(token);
            }
            return result;
        }
    }
    private sealed class Harness(string connection, bool blockConsume, int actionLimit, string environment, bool blockStatus) : WebApplicationFactory<Program>
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Invitation { get; } = Guid.NewGuid();
        public string Code { get; } = new CryptographicPublicCodeGenerator().Generate();
        public string Email { get; } = $"trash-{Guid.NewGuid():N}@example.test";
        public Uri PublicPath => new($"/api/v1/public/invitations/{Code}", UriKind.Relative);
        public MutableClock Clock { get; } = new();
        public ConsumeBlock ConsumeBlock { get; } = new();
        public ConsumeBlock ReleaseBlock { get; } = new();
        public ConsumeBlock StatusBlock { get; } = new();
        public DavetiyeDbContext Db() => new(new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(connection, o => o.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connection,
                ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                ["InvitationLifecycleJobs:Enabled"] = "false",
                ["AuthRateLimits:PublicationAction:PermitLimit"] = actionLimit.ToString()
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IClock>(Clock);
                if (blockStatus) services.AddScoped<IPublicationLifecycleService>(provider => new BlockingPublication(
                    ActivatorUtilities.CreateInstance<PublicationLifecycleService>(provider), StatusBlock));
                if (blockConsume) services.AddScoped<IPublicationGrantLifecycleService>(provider => new BlockingGrants(
                    new PublicationGrantLifecycleService(provider.GetRequiredService<DavetiyeDbContext>(), provider.GetRequiredService<IEffectiveEntitlementResolver>(), provider.GetRequiredService<IClock>()), ConsumeBlock, ReleaseBlock));
            });
        }
        public async Task<PublicationStatus> Status()
        {
            await using var scope = Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>().GetAsync(Account, Invitation, default);
            Assert.Equal("Succeeded", result.Code);
            return result.Status!;
        }
        public async Task<PublicationLifecycleResult> Execute(PublicationActionRequest request)
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>().ExecuteAsync(Account, Invitation, request, default);
        }
        public async Task<PublicationStatus> Publish(bool scheduled = false, Guid? requestedGrant = null)
        {
            var result = await Execute(new("publish", (await Status()).Expected,
                new(scheduled ? "Scheduled" : "Immediate", scheduled ? "2026-10-03T15:00:00" : null,
                    scheduled ? "2026-10-04T15:00:00" : "2026-10-03T15:00:00", "Europe/Istanbul", requestedGrant), ProceedWithRecommendedWarnings: true));
            Assert.Equal("Succeeded", result.Code);
            return result.Status!;
        }
        public async Task<PublicationRevisions> Expected()
        {
            await using var db = Db();
            var invitation = await db.Invitations.IgnoreQueryFilters().SingleAsync(i => i.Id == Invitation);
            var working = await db.WorkingContents.SingleAsync(c => c.InvitationId == Invitation);
            var published = await db.PublishedContents.SingleOrDefaultAsync(c => c.InvitationId == Invitation);
            var window = await db.PublicationWindows.SingleOrDefaultAsync(w => w.InvitationId == Invitation && w.IsCurrent);
            return new(invitation.Revision, working.Revision, published?.Revision, window?.Id, window?.Revision);
        }
        public async Task<InvitationTrashResult> Delete(PublicationRevisions? expected = null, int? expectedDays = null)
        {
            expected ??= await Expected();
            if (expectedDays is null)
            {
                await using var db = Db();
                var setting = await db.SystemSettings.SingleOrDefaultAsync(s => s.Key == RetentionKey);
                expectedDays = setting?.ValueType == SystemSettingValueType.Integer && int.TryParse(setting.Value, out var days) && days is >= 0 and <= 365 ? days : 3;
            }
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IInvitationTrashService>().DeleteAsync(Account, Invitation, new(expected, expectedDays), default);
        }
        public async Task<InvitationTrashResult> Restore(PublicationRevisions? expected = null)
        {
            expected ??= await Expected();
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IInvitationTrashService>().RestoreAsync(Account, Invitation, new(expected), default);
        }
        public async Task<InvitationLifecycleJobResult> Jobs()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IInvitationLifecycleJobs>().RunBatchAsync(20, default);
        }
        public async Task EditWorking(string headline)
        {
            await using var db = Db();
            (await db.WorkingContents.SingleAsync()).ReplaceContent(Content.Replace("Accepted snapshot", headline), 1, Clock.UtcNow);
            await db.SaveChangesAsync();
        }
        public async Task<string?> Headline()
        {
            await using var db = Db();
            using var content = JsonDocument.Parse((await db.PublishedContents.SingleAsync()).Content);
            return content.RootElement.GetProperty("headline").GetString();
        }
        public async Task<Guid> AddDraft()
        {
            await using var db = Db();
            var id = Guid.NewGuid();
            var invitation = Davetiye.Domain.Modules.Invitations.Invitation.Create(id, Account, new CryptographicPublicCodeGenerator().Generate(), Clock.UtcNow);
            invitation.PinTemplate("zamansiz-dugun", 1);
            db.Invitations.Add(invitation);
            db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), id, 1, Content, Clock.UtcNow));
            await db.SaveChangesAsync();
            return id;
        }
        public async Task PublishOther(Guid id)
        {
            Guid grantId;
            await using (var db = Db())
            {
                var grant = AccountPlanGrant.Create(Guid.NewGuid(), Account, (await db.Plans.SingleAsync(p => p.Key == "standard")).Id, GrantSource.IndividualPurchase, Clock.UtcNow);
                grantId = grant.Id;
                db.AccountPlanGrants.Add(grant);
                await db.SaveChangesAsync();
            }
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, id, default)).Status!;
            Assert.Equal("Succeeded", (await service.ExecuteAsync(Account, id, new("publish", status.Expected,
                new("Immediate", null, "2026-10-03T15:00:00", "Europe/Istanbul", grantId), ProceedWithRecommendedWarnings: true), default)).Code);
        }
        public async Task<string> AddForeign()
        {
            await using var db = Db();
            var email = $"foreign-{Guid.NewGuid():N}@example.test";
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
            db.Users.Add(user);
            var accountId = Guid.NewGuid();
            db.Accounts.Add(Davetiye.Domain.Modules.IdentityAndAccounts.Account.Create(accountId, user.Id, AccountType.Individual, "Foreign tester", Now));
            db.AddAcknowledgedServiceNotice(accountId, Now);
            await db.SaveChangesAsync();
            return email;
        }
    }
}
