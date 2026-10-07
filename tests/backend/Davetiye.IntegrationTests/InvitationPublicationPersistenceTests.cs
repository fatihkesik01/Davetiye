using System.Text.Json;
using Davetiye.Application.Modules.Invitations;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Media.Contracts;
using Davetiye.Application.Modules.Memories.Contracts;
using Davetiye.Application.Modules.GiftRegistry.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.Payments;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.Memories;
using Davetiye.Infrastructure.Modules.Media;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class InvitationPublicationPersistenceTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string Content = """{"headline":"Original celebration","startsAt":"2026-10-10T12:00:00Z","timeZoneId":"Europe/Istanbul","venue":{"name":"Original venue"},"message":"Welcome","hostNames":["Ada"]}""";

    [Fact]
    public async Task Organization_grant_is_shared_across_invitations_and_stays_valid_for_schedule_cancel_and_start()
    {
        var seed = await SeedAsync(accountType: AccountType.Organization);
        var secondInvitation = Guid.Empty;
        var organizationPlanId = Guid.Empty;
        var grantId = Guid.NewGuid();
        var paidThrough = Now.AddDays(30);
        await using (var setup = Context(seed.Connection))
        {
            organizationPlanId = await setup.Plans.Where(plan => plan.Key == "organization")
                .Select(plan => plan.Id).SingleAsync();
            secondInvitation = AddDraft(setup, seed.Account);
            setup.AccountPlanGrants.Add(AccountPlanGrant.Create(grantId, seed.Account, organizationPlanId,
                GrantSource.OrganizationSubscription, Now));
            setup.OrganizationSubscriptions.Add(OrganizationSubscription.ActivateFromVerifiedInitialPayment(
                Guid.NewGuid(), seed.Account, organizationPlanId, 2499m, "test-provider", "org-subscription",
                "org-cycle-initial", Now, paidThrough, Now));
            await setup.SaveChangesAsync();
        }

        await using (var context = Context(seed.Connection))
        {
            var request = Request(scheduled: true) with { RequestedGrantId = grantId };
            Assert.Equal(InitialPublicationOutcome.Succeeded,
                (await Service(context).PublishAsync(seed.Account, seed.Invitation, request, default)).Outcome);
            Assert.Equal(InitialPublicationOutcome.Succeeded,
                (await Service(context).PublishAsync(seed.Account, secondInvitation, request with
                {
                    ExpectedInvitationRevision = 1,
                    ExpectedWorkingContentRevision = 0
                }, default)).Outcome);

            var clock = new AdjustableClock { Current = Now };
            var resolver = new EffectiveEntitlementResolver(
                new EntitlementGrantReader(context, new OrganizationSubscriptionEntitlementReader(context)), clock);
            var lifecycle = new PublicationGrantLifecycleService(context, resolver, clock);
            var choices = await lifecycle.ListChoicesAsync(seed.Account, seed.Invitation, [grantId], default);
            Assert.Contains(choices, choice => choice.GrantId == grantId && choice.Kind == "organizationSubscription");

            var runner = new AccountQuotaTransactionRunner(context);
            var cancellationReleased = await runner.ExecuteAsync(seed.Account,
                token => lifecycle.ReleaseAsync(seed.Account, seed.Invitation, grantId, token), default);
            Assert.True(cancellationReleased);

            clock.Current = Now.AddDays(1);
            var started = await runner.ExecuteAsync(seed.Account,
                token => lifecycle.ConsumeStartedAsync(seed.Account, secondInvitation, grantId, Now.AddDays(1), token), default);
            Assert.True(started);

            clock.Current = paidThrough;
            var expiredChoices = await lifecycle.ListChoicesAsync(seed.Account, secondInvitation, [grantId], default);
            var expiredStart = await runner.ExecuteAsync(seed.Account,
                token => lifecycle.ConsumeStartedAsync(seed.Account, secondInvitation, grantId, Now.AddDays(1), token), default);
            var expiredScheduleCanceled = await runner.ExecuteAsync(seed.Account,
                token => lifecycle.ReleaseAsync(seed.Account, secondInvitation, grantId, token), default);
            Assert.DoesNotContain(expiredChoices, choice => choice.GrantId == grantId);
            Assert.False(expiredStart);
            Assert.True(expiredScheduleCanceled);
        }

        await using var verify = Context(seed.Connection);
        var sharedGrant = await verify.AccountPlanGrants.SingleAsync(grant => grant.Id == grantId);
        Assert.Null(sharedGrant.AssignedInvitationId);
        Assert.Null(sharedGrant.ReservedAt);
        Assert.Null(sharedGrant.ConsumedAt);
        Assert.Equal(2, await verify.PublicationWindows.CountAsync(window => window.GrantId == grantId));
    }

    [Fact]
    public async Task Organization_paid_through_boundary_expires_current_invitation_and_archives_window()
    {
        var seed = await SeedAsync(accountType: AccountType.Organization);
        var paidThrough = Now.AddDays(1);
        var grantId = Guid.NewGuid();
        await using (var setup = Context(seed.Connection))
        {
            var planId = await setup.Plans.Where(plan => plan.Key == "organization").Select(plan => plan.Id).SingleAsync();
            setup.AccountPlanGrants.Add(AccountPlanGrant.Create(grantId, seed.Account, planId,
                GrantSource.OrganizationSubscription, Now));
            setup.OrganizationSubscriptions.Add(OrganizationSubscription.ActivateFromVerifiedInitialPayment(
                Guid.NewGuid(), seed.Account, planId, 2499m, "test-provider", "expiry-subscription",
                "expiry-cycle", Now, paidThrough, Now));
            var invitation = await setup.Invitations.SingleAsync(row => row.Id == seed.Invitation);
            invitation.BeginInitialPublication(scheduled: false);
            setup.PublishedContents.Add(PublishedContent.Create(Guid.NewGuid(), seed.Invitation,
                invitation.TemplateKey!, invitation.RendererVersion!.Value, 1, Content, 0, Now));
            setup.PublicationWindows.Add(PublicationWindow.Create(Guid.NewGuid(), seed.Invitation, grantId,
                Now, Now.AddDays(30), "Europe/Istanbul", Now));
            await setup.SaveChangesAsync();
        }

        await using var context = Context(seed.Connection);
        var clock = new AdjustableClock { Current = paidThrough };
        var runner = new AccountQuotaTransactionRunner(context);
        var resolver = new EffectiveEntitlementResolver(
            new EntitlementGrantReader(context, new OrganizationSubscriptionEntitlementReader(context)), clock);
        var grants = new PublicationGrantLifecycleService(context, resolver, clock);
        var access = new PublicationGrantAccessValidator(context, new OrganizationSubscriptionEntitlementReader(context));
        var jobs = new InvitationLifecycleJobs(context, runner, grants, access,
            new NoopMediaPurgeCoordinator(), new NoopRsvpPurgeCoordinator(),
            new NoopMemoriesPurgeCoordinator(), new NoopGiftRegistryPurgeCoordinator(), clock,
            NullLogger<InvitationLifecycleJobs>.Instance);

        var result = await jobs.RunBatchAsync(20, default);

        Assert.Equal(1, result.Expired);
        context.ChangeTracker.Clear();
        Assert.Equal(InvitationStoredState.Expired,
            (await context.Invitations.SingleAsync(row => row.Id == seed.Invitation)).State);
        Assert.False((await context.PublicationWindows.SingleAsync(row => row.InvitationId == seed.Invitation)).IsCurrent);
        Assert.Single(await context.PublishedContents.ToListAsync());
    }

    [Fact]
    public async Task Initial_publication_captures_only_ready_media_and_creator_delete_leaves_snapshot_unchanged()
    {
        var seed = await SeedAsync();
        Guid selectedAssetId;
        await using (var context = Context(seed.Connection))
        {
            var selected = ReadyImage(seed.Invitation, "selected-image", Now);
            var unplaced = ReadyImage(seed.Invitation, "unplaced-image", Now);
            selectedAssetId = selected.Id;
            context.MediaAssets.AddRange(selected, unplaced);
            context.MediaPlacements.Add(selected.Place(Guid.NewGuid(), MediaPresentationRole.Cover, 0, Now));
            await context.SaveChangesAsync();

            var published = await Service(context).PublishAsync(seed.Account, seed.Invitation, Request(), default);
            Assert.Equal(InitialPublicationOutcome.Succeeded, published.Outcome);
        }

        await using (var context = Context(seed.Connection))
        {
            var published = await context.PublishedContents.SingleAsync(item => item.InvitationId == seed.Invitation);
            var snapshot = JsonSerializer.Deserialize<PublicSnapshotMediaPlacement[]>(published.MediaPlacements)!;
            Assert.Equal([selectedAssetId], snapshot.Select(item => item.AssetId));

            var library = new CreatorMediaLibraryService(context, new AccountReferenceValidator(context),
                new Davetiye.Infrastructure.Modules.Templates.TemplateSelectionResolver(context),
                new CreatorMediaInvitationOwnerReader(context), new FixedClock());
            var deleted = await library.DeleteAsync(new(seed.Account, seed.Invitation, selectedAssetId), default);
            Assert.Equal("Deleting", deleted.Outcome);
            Assert.Empty(await context.MediaPlacements.Where(item => item.MediaAssetId == selectedAssetId).ToListAsync());

            var afterDelete = await context.PublishedContents.AsNoTracking().SingleAsync(item => item.InvitationId == seed.Invitation);
            var stillPublished = JsonSerializer.Deserialize<PublicSnapshotMediaPlacement[]>(afterDelete.MediaPlacements)!;
            Assert.Equal([selectedAssetId], stillPublished.Select(item => item.AssetId));
            Assert.Equal(MediaAssetState.PendingDeletion,
                (await context.MediaAssets.AsNoTracking().SingleAsync(item => item.Id == selectedAssetId)).State);
        }
    }

    [Fact]
    public async Task Deactivated_template_stays_usable_by_existing_pinned_draft_but_not_new_selection()
    {
        var seed = await SeedAsync();
        await using (var setup = Context(seed.Connection))
        {
            var template = await setup.TemplateDefinitions.SingleAsync(item => item.Key == "zamansiz-dugun");
            template.SetActive(false);
            var invitation = await setup.Invitations.SingleAsync(item => item.Id == seed.Invitation);
            invitation.PinTemplate(template.Key, template.CurrentRendererVersion);
            await setup.SaveChangesAsync();
        }

        await using (var context = Context(seed.Connection))
        {
            var templates = new TemplateCatalogService(context);
            Assert.DoesNotContain(await templates.ListActiveAsync(default), item => item.Key == "zamansiz-dugun");
            Assert.NotNull(await templates.ResolvePinnedAsync("zamansiz-dugun", default));
            Assert.Null(await new TemplateSelectionResolver(context).ResolveActiveAsync("zamansiz-dugun", default));

            var drafts = new InvitationDraftService(context, new TemplateSelectionResolver(context), templates,
                new CryptographicPublicCodeGenerator(), NullLogger<InvitationDraftService>.Instance, new FixedClock());
            var validation = await drafts.GetValidationAsync(seed.Account, seed.Invitation, default);
            Assert.NotNull(validation);
            Assert.True(validation.TemplateAvailable);

            var autosave = await drafts.AutosaveAsync(seed.Account, seed.Invitation,
                new AutosaveInvitationDraftRequest(1,
                    new DraftContentInput
                    {
                        EventType = "dugun",
                        Headline = "Updated while template is hidden",
                        StartsAt = Now.AddDays(8),
                        TimeZoneId = "Europe/Istanbul",
                        Venue = new DraftVenueInput { Name = "Original venue" },
                        Message = "Welcome",
                        HostNames = ["Ada"]
                    }, 0), default);
            Assert.Equal(InvitationDraftOutcome.Succeeded, autosave.Outcome);

            var invitation = await context.Invitations.SingleAsync(item => item.Id == seed.Invitation);
            var working = await context.WorkingContents.SingleAsync(item => item.InvitationId == seed.Invitation);
            var result = await Service(context).PublishAsync(seed.Account, seed.Invitation,
                Request() with
                {
                    ExpectedInvitationRevision = invitation.Revision,
                    ExpectedWorkingContentRevision = working.Revision
                }, default);
            Assert.Equal(InitialPublicationOutcome.Succeeded, result.Outcome);

            var published = await context.PublishedContents.SingleAsync(item => item.InvitationId == seed.Invitation);
            Assert.Equal("zamansiz-dugun", published.TemplateKey);
            Assert.NotNull(new TemplateRendererRegistry().Resolve(published.TemplateKey, published.RendererVersion));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_atomically_persists_snapshot_window_state_and_free_assignment(bool scheduled)
    {
        var seed = await SeedAsync();
        await using (var context = Context(seed.Connection))
        {
            var result = await Service(context).PublishAsync(seed.Account, seed.Invitation, Request(scheduled), default);
            Assert.Equal(InitialPublicationOutcome.Succeeded, result.Outcome);
            Assert.Equal(seed.Code, result.PublicCode);
        }

        await using var verification = Context(seed.Connection);
        var invitation = await verification.Invitations.SingleAsync();
        var snapshot = await verification.PublishedContents.SingleAsync();
        var window = await verification.PublicationWindows.SingleAsync();
        var grant = await verification.AccountPlanGrants.SingleAsync();
        Assert.Equal(scheduled ? InvitationStoredState.Scheduled : InvitationStoredState.Active, invitation.State);
        Assert.Equal(2, invitation.Revision);
        Assert.Equal(seed.Invitation, snapshot.InvitationId);
        Assert.Equal("zamansiz-dugun", snapshot.TemplateKey);
        Assert.Equal(1, snapshot.RendererVersion);
        Assert.Equal(1, snapshot.ContentSchemaVersion);
        Assert.Equal(0, snapshot.SourceWorkingRevision);
        Assert.Equal("Original celebration", JsonDocument.Parse(snapshot.Content).RootElement.GetProperty("headline").GetString());
        Assert.Equal(Now, snapshot.PublishedAt);
        Assert.Equal(seed.Invitation, window.InvitationId);
        Assert.Equal(grant.Id, window.GrantId);
        Assert.Equal(scheduled ? Now.AddDays(1) : Now, window.StartsAt);
        Assert.Equal(Now.AddDays(scheduled ? 2 : 1), window.EndsAt);
        Assert.Equal("Europe/Istanbul", window.TimeZoneId);
        Assert.True(window.IsCurrent);
        Assert.Equal(seed.Invitation, grant.AssignedInvitationId);
        Assert.Equal(Now, grant.ReservedAt);
        Assert.Equal(scheduled ? null : Now, grant.ConsumedAt);
    }

    [Fact]
    public async Task Scheduled_snapshot_keeps_its_content_and_template_pin_when_working_changes()
    {
        var seed = await SeedAsync();
        await using (var context = Context(seed.Connection))
            Assert.Equal(InitialPublicationOutcome.Succeeded,
                (await Service(context).PublishAsync(seed.Account, seed.Invitation, Request(true), default)).Outcome);

        await using (var editor = Context(seed.Connection))
        {
            (await editor.WorkingContents.SingleAsync()).ReplaceContent("""{"headline":"Later private edit"}""", 1, Now.AddHours(1));
            (await editor.Invitations.SingleAsync()).PinTemplate("modern-mezuniyet", 1);
            await editor.SaveChangesAsync();
        }
        await using var verification = Context(seed.Connection);
        var snapshot = await verification.PublishedContents.SingleAsync();
        Assert.Equal("zamansiz-dugun", snapshot.TemplateKey);
        Assert.Equal(1, snapshot.RendererVersion);
        Assert.Equal(0, snapshot.SourceWorkingRevision);
        Assert.Equal("Original celebration", JsonDocument.Parse(snapshot.Content).RootElement.GetProperty("headline").GetString());
        Assert.Equal("modern-mezuniyet", (await verification.Invitations.SingleAsync()).TemplateKey);
        Assert.Equal(seed.Code, (await verification.Invitations.SingleAsync()).PublicCode);
    }

    [Fact]
    public async Task Publication_migration_preserves_legacy_drafts_and_backfills_unique_public_locators()
    {
        var connection = Unpooled(await postgreSql.CreateEmptyDatabaseAsync());
        var account = Guid.NewGuid();
        var id = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await using (var context = Context(connection))
        {
            await context.Database.MigrateAsync("20261001120058_P3M2_EntitlementsAndGrantReservations");
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO invitations (id, account_id, template_key, renderer_version, created_at, revision)
                VALUES ({id}, {account}, 'zamansiz-dugun', 1, {Now}, 7),
                       ({secondId}, {account}, NULL, NULL, {Now}, 0);
                INSERT INTO working_contents (id, invitation_id, content_schema_version, content, updated_at, revision)
                VALUES ({Guid.NewGuid()}, {id}, 1, CAST({Content} AS jsonb), {Now}, 4)
                """);
            await context.Database.MigrateAsync();
        }
        await using var verify = Context(connection);
        var invitation = await verify.Invitations.SingleAsync(i => i.Id == id);
        Assert.Equal(id, invitation.Id);
        Assert.Equal(account, invitation.AccountId);
        PublicInvitationCode.EnsureValid(invitation.PublicCode, "publicCode");
        Assert.NotEqual(invitation.PublicCode, (await verify.Invitations.SingleAsync(i => i.Id == secondId)).PublicCode);
        Assert.Equal(7, invitation.Revision);
        Assert.Equal(InvitationStoredState.Draft, invitation.State);
        Assert.Equal("zamansiz-dugun", invitation.TemplateKey);
        Assert.Equal("Original celebration", JsonDocument.Parse((await verify.WorkingContents.SingleAsync()).Content).RootElement.GetProperty("headline").GetString());
        Assert.Equal(4, (await verify.WorkingContents.SingleAsync()).Revision);
        Assert.False(await verify.PublishedContents.AnyAsync());
        Assert.False(await verify.PublicationWindows.AnyAsync());
    }

    [Fact]
    public async Task Denied_publication_inside_caller_transaction_is_rolled_back_even_when_caller_commits()
    {
        var seed = await SeedAsync("romantik-nisan");
        await using (var context = Context(seed.Connection))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            Assert.Equal(InitialPublicationOutcome.PremiumTemplateNotAllowed,
                (await Service(context).PublishAsync(seed.Account, seed.Invitation, Request(), default)).Outcome);
            await transaction.CommitAsync();
        }
        await AssertUnpublishedAsync(seed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_expiring_while_waiting_for_row_lock_is_revalidated_before_free_allocation(bool scheduled)
    {
        var seed = await SeedAsync();
        await using var blocker = Context(seed.Connection);
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM working_contents WHERE invitation_id = {seed.Invitation} FOR UPDATE");
        var counter = new ClockReadSignal();
        await using var publishing = Context(seed.Connection);
        await publishing.Database.OpenConnectionAsync();
        var task = Service(publishing, counter).PublishAsync(seed.Account, seed.Invitation, Request(scheduled), default);
        await WaitForDatabaseLockAsync(seed.Connection, ((NpgsqlConnection)publishing.Database.GetDbConnection()).ProcessID);
        counter.Current = Now.AddDays(scheduled ? 1 : 2);
        await transaction.CommitAsync();
        Assert.Equal(InitialPublicationOutcome.InvalidRequest, (await task.WaitAsync(TimeSpan.FromSeconds(10))).Outcome);
        await AssertUnpublishedAsync(seed);
    }

    [Fact]
    public async Task Account_banned_while_paid_publish_waits_for_row_lock_cannot_publish()
    {
        var seed = await SeedAsync();
        Guid grantId;
        await using (var edit = Context(seed.Connection))
        {
            grantId = AddGrant(edit, seed.Account, (await edit.Plans.SingleAsync(p => p.Key == "standard")).Id);
            await edit.SaveChangesAsync();
        }
        await using var blocker = Context(seed.Connection);
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM working_contents WHERE invitation_id = {seed.Invitation} FOR UPDATE");
        await using var publishing = Context(seed.Connection);
        await publishing.Database.OpenConnectionAsync();
        var task = Service(publishing).PublishAsync(seed.Account, seed.Invitation, Request() with { RequestedGrantId = grantId }, default);
        await WaitForDatabaseLockAsync(seed.Connection, ((NpgsqlConnection)publishing.Database.GetDbConnection()).ProcessID);
        await using (var admin = Context(seed.Connection))
        {
            admin.BanRecords.Add(BanRecord.Create(Guid.NewGuid(), seed.Account, "Ban during admission", Now, Guid.NewGuid()));
            await admin.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        Assert.Equal(InitialPublicationOutcome.AccountInactive, (await task.WaitAsync(TimeSpan.FromSeconds(10))).Outcome);
        await using var verify = Context(seed.Connection);
        Assert.False(await verify.PublishedContents.AnyAsync());
        Assert.False(await verify.PublicationWindows.AnyAsync());
        Assert.Equal(InvitationStoredState.Draft, (await verify.Invitations.SingleAsync()).State);
        var grant = await verify.AccountPlanGrants.SingleAsync();
        Assert.Null(grant.ReservedAt);
        Assert.Null(grant.ConsumedAt);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Template_selection_uses_effective_active_state_and_keeps_published_pin(bool scheduled, bool started, bool allowed)
    {
        var seed = await SeedAsync();
        await using (var publish = Context(seed.Connection))
            Assert.Equal(InitialPublicationOutcome.Succeeded, (await Service(publish).PublishAsync(seed.Account, seed.Invitation, Request(scheduled), default)).Outcome);
        await using var editor = Context(seed.Connection);
        var clock = new ClockReadSignal { Current = started ? Now.AddDays(1) : Now };
        var drafts = new InvitationDraftService(editor, new TemplateSelectionResolver(editor), new TemplateCatalogService(editor),
            new CryptographicPublicCodeGenerator(), NullLogger<InvitationDraftService>.Instance, clock);
        var result = await drafts.SelectTemplateAsync(seed.Account, seed.Invitation, new SelectInvitationTemplateRequest("modern-mezuniyet", 2), default);
        Assert.Equal(allowed ? InvitationDraftOutcome.Succeeded : InvitationDraftOutcome.Invalid, result.Outcome);
        await using var verify = Context(seed.Connection);
        Assert.Equal(allowed ? "modern-mezuniyet" : "zamansiz-dugun", (await verify.Invitations.SingleAsync()).TemplateKey);
        Assert.Equal("zamansiz-dugun", (await verify.PublishedContents.SingleAsync()).TemplateKey);
        Assert.Equal(seed.Code, (await verify.Invitations.SingleAsync()).PublicCode);
    }

    private static async Task WaitForDatabaseLockAsync(string connectionString, int processId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = @pid AND wait_event_type = 'Lock')", connection);
            command.Parameters.AddWithValue("pid", processId);
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) return;
            await Task.Delay(20, timeout.Token);
        }
    }

    [Theory]
    [InlineData("foreign", InitialPublicationOutcome.NotFound)]
    [InlineData("unverified", InitialPublicationOutcome.AccountInactive)]
    [InlineData("banned", InitialPublicationOutcome.AccountInactive)]
    [InlineData("invitation-revision", InitialPublicationOutcome.Conflict)]
    [InlineData("working-revision", InitialPublicationOutcome.Conflict)]
    [InlineData("required", InitialPublicationOutcome.RequiredFieldsMissing)]
    [InlineData("recommended", InitialPublicationOutcome.RecommendedFieldsRequireConfirmation)]
    public async Task Admission_guards_leave_draft_and_free_right_untouched(string guard, InitialPublicationOutcome outcome)
    {
        var seed = await SeedAsync();
        var account = seed.Account;
        var request = Request();
        await using (var edit = Context(seed.Connection))
        {
            if (guard == "foreign") { account = Guid.NewGuid(); AddAccount(edit, account); }
            if (guard == "unverified") (await edit.Users.SingleAsync()).EmailConfirmed = false;
            if (guard == "banned") edit.BanRecords.Add(BanRecord.Create(Guid.NewGuid(), account, "Test ban", Now, Guid.NewGuid()));
            if (guard == "invitation-revision") request = request with { ExpectedInvitationRevision = 0 };
            if (guard == "working-revision") request = request with { ExpectedWorkingContentRevision = 1 };
            if (guard is "required" or "recommended")
                (await edit.WorkingContents.SingleAsync()).ReplaceContent(
                    guard == "required" ? "{}" : """{"headline":"Event","startsAt":"2026-10-10T12:00:00Z","venue":{"name":"Hall"}}""", 1, Now);
            if (guard is "required" or "recommended") request = request with { ExpectedWorkingContentRevision = 1, ProceedWithRecommendedWarnings = false };
            await edit.SaveChangesAsync();
        }
        await using (var context = Context(seed.Connection))
            Assert.Equal(outcome, (await Service(context).PublishAsync(account, seed.Invitation, request, default)).Outcome);
        await AssertUnpublishedAsync(seed);
    }

    [Theory]
    [InlineData("premium", false, InitialPublicationOutcome.PremiumTemplateNotAllowed)]
    [InlineData("premium", true, InitialPublicationOutcome.PremiumTemplateNotAllowed)]
    [InlineData("duration", false, InitialPublicationOutcome.PublishDurationExceeded)]
    [InlineData("duration", true, InitialPublicationOutcome.PublishDurationExceeded)]
    [InlineData("quota", false, InitialPublicationOutcome.ActiveInvitationQuotaExceeded)]
    [InlineData("quota", true, InitialPublicationOutcome.ActiveInvitationQuotaExceeded)]
    public async Task Post_allocation_denial_rolls_back_free_consumption_and_reservation(string denial, bool scheduled, InitialPublicationOutcome outcome)
    {
        var seed = await SeedAsync(denial == "premium" ? "romantik-nisan" : "zamansiz-dugun");
        var request = Request(scheduled);
        if (denial == "duration") request = request with { EndsAtUtc = Now.AddDays(366) };
        if (denial == "quota")
        {
            await using var edit = Context(seed.Connection);
            var plan = await edit.Plans.SingleAsync(p => p.Key == "free");
            (await edit.PlanEntitlements.SingleAsync(e => e.PlanId == plan.Id && e.EntitlementKey == EntitlementCatalog.MaxActiveInvitations)).UpdateValue(0, null);
            await edit.SaveChangesAsync();
        }
        await using (var context = Context(seed.Connection))
        {
            Assert.Equal(outcome, (await Service(context).PublishAsync(seed.Account, seed.Invitation, request, default)).Outcome);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        await AssertUnpublishedAsync(seed);
    }

    [Fact]
    public async Task Save_failure_rolls_back_snapshot_window_state_and_consumed_free_grant()
    {
        var seed = await SeedAsync();
        await using (var context = Context(seed.Connection, new RejectPublicationSave()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context).PublishAsync(seed.Account, seed.Invitation, Request(), default));
            Assert.Empty(context.ChangeTracker.Entries());
        }
        await AssertUnpublishedAsync(seed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_publish_has_one_winner_for_account_overlap_or_same_invitation(bool sameInvitation)
    {
        var seed = await SeedAsync();
        Guid secondInvitation;
        Guid firstGrant;
        Guid secondGrant;
        await using (var edit = Context(seed.Connection))
        {
            secondInvitation = sameInvitation ? seed.Invitation : AddDraft(edit, seed.Account);
            var plan = await edit.Plans.SingleAsync(p => p.Key == "standard");
            firstGrant = AddGrant(edit, seed.Account, plan.Id);
            secondGrant = AddGrant(edit, seed.Account, plan.Id);
            await edit.SaveChangesAsync();
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<InitialPublicationResult> Publish(Guid invitation, Guid grant)
        {
            await start.Task;
            await using var context = Context(seed.Connection);
            return await Service(context).PublishAsync(seed.Account, invitation, Request(true) with { RequestedGrantId = grant }, default);
        }
        var first = Publish(seed.Invitation, firstGrant);
        var second = Publish(secondInvitation, secondGrant);
        start.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, r => r.Outcome == InitialPublicationOutcome.Succeeded);
        Assert.Single(results, r => r.Outcome == (sameInvitation ? InitialPublicationOutcome.InvalidState : InitialPublicationOutcome.ActiveInvitationQuotaExceeded));
        await using var verification = Context(seed.Connection);
        Assert.Equal(1, await verification.PublishedContents.CountAsync());
        Assert.Equal(1, await verification.PublicationWindows.CountAsync());
        Assert.Equal(1, await verification.AccountPlanGrants.CountAsync(g => g.ReservedAt != null));
        Assert.Equal(0, await verification.AccountPlanGrants.CountAsync(g => g.ConsumedAt != null));
    }

    [Fact]
    public async Task Adjacent_half_open_publication_intervals_use_one_slot_without_conflict()
    {
        var seed = await SeedAsync();
        Guid secondInvitation;
        Guid firstGrant;
        Guid secondGrant;
        await using (var edit = Context(seed.Connection))
        {
            secondInvitation = AddDraft(edit, seed.Account);
            var plan = await edit.Plans.SingleAsync(p => p.Key == "standard");
            firstGrant = AddGrant(edit, seed.Account, plan.Id);
            secondGrant = AddGrant(edit, seed.Account, plan.Id);
            await edit.SaveChangesAsync();
        }
        await using var context = Context(seed.Connection);
        Assert.Equal(InitialPublicationOutcome.Succeeded, (await Service(context).PublishAsync(seed.Account, seed.Invitation,
            Request(true) with { RequestedGrantId = firstGrant }, default)).Outcome);
        Assert.Equal(InitialPublicationOutcome.Succeeded, (await Service(context).PublishAsync(seed.Account, secondInvitation,
            Request(true) with { RequestedGrantId = secondGrant, ScheduledStartsAtUtc = Now.AddDays(2), EndsAtUtc = Now.AddDays(3) }, default)).Outcome);
        Assert.Equal(2, await context.PublicationWindows.CountAsync());
    }

    [Fact]
    public async Task Quota_counts_peak_concurrency_allowing_bridge_interval_but_rejecting_third_simultaneous_window()
    {
        var seed = await SeedAsync();
        Guid secondInvitation;
        Guid bridgeInvitation;
        Guid rejectedInvitation;
        Guid firstGrant;
        Guid secondGrant;
        Guid bridgeGrant;
        Guid rejectedGrant;
        await using (var edit = Context(seed.Connection))
        {
            secondInvitation = AddDraft(edit, seed.Account);
            bridgeInvitation = AddDraft(edit, seed.Account);
            rejectedInvitation = AddDraft(edit, seed.Account);
            var plan = await edit.Plans.SingleAsync(p => p.Key == "standard");
            (await edit.PlanEntitlements.SingleAsync(e => e.PlanId == plan.Id && e.EntitlementKey == EntitlementCatalog.MaxActiveInvitations)).UpdateValue(2, null);
            firstGrant = AddGrant(edit, seed.Account, plan.Id);
            secondGrant = AddGrant(edit, seed.Account, plan.Id);
            bridgeGrant = AddGrant(edit, seed.Account, plan.Id);
            rejectedGrant = AddGrant(edit, seed.Account, plan.Id);
            await edit.SaveChangesAsync();
        }
        await using (var publishing = Context(seed.Connection))
        {
            var service = Service(publishing);
            Assert.Equal(InitialPublicationOutcome.Succeeded, (await service.PublishAsync(seed.Account, seed.Invitation,
                Request(true) with { RequestedGrantId = firstGrant }, default)).Outcome);
            Assert.Equal(InitialPublicationOutcome.Succeeded, (await service.PublishAsync(seed.Account, secondInvitation,
                Request(true) with { RequestedGrantId = secondGrant, ScheduledStartsAtUtc = Now.AddDays(2), EndsAtUtc = Now.AddDays(3) }, default)).Outcome);
            Assert.Equal(InitialPublicationOutcome.Succeeded, (await service.PublishAsync(seed.Account, bridgeInvitation,
                Request(true) with { RequestedGrantId = bridgeGrant, EndsAtUtc = Now.AddDays(3) }, default)).Outcome);
            Assert.Equal(InitialPublicationOutcome.ActiveInvitationQuotaExceeded, (await service.PublishAsync(seed.Account, rejectedInvitation,
                Request(true) with { RequestedGrantId = rejectedGrant, ScheduledStartsAtUtc = Now.AddDays(1).AddHours(1), EndsAtUtc = Now.AddDays(1).AddHours(2) }, default)).Outcome);
        }
        await using var verify = Context(seed.Connection);
        Assert.Equal(3, await verify.PublishedContents.CountAsync());
        Assert.Equal(3, await verify.PublicationWindows.CountAsync());
        Assert.Equal(3, await verify.AccountPlanGrants.CountAsync(g => g.ReservedAt != null));
        Assert.Equal(InvitationStoredState.Draft, (await verify.Invitations.SingleAsync(i => i.Id == rejectedInvitation)).State);
        var deniedGrant = await verify.AccountPlanGrants.SingleAsync(g => g.Id == rejectedGrant);
        Assert.Null(deniedGrant.AssignedInvitationId);
        Assert.Null(deniedGrant.ReservedAt);
        Assert.Null(deniedGrant.ConsumedAt);
    }

    [Fact]
    public async Task Pretracked_working_content_cannot_bypass_revision_check_after_external_autosave()
    {
        var seed = await SeedAsync();
        await using var context = Context(seed.Connection);
        _ = await context.WorkingContents.SingleAsync();
        await using (var editor = Context(seed.Connection))
        {
            (await editor.WorkingContents.SingleAsync()).ReplaceContent(Content.Replace("Original celebration", "New accepted draft"), 1, Now);
            await editor.SaveChangesAsync();
        }
        var result = await Service(context).PublishAsync(seed.Account, seed.Invitation, Request(), default);
        Assert.Equal(InitialPublicationOutcome.Conflict, result.Outcome);
        Assert.Equal(1, result.CurrentWorkingContentRevision);
        await AssertUnpublishedAsync(seed);
    }

    [Fact]
    public async Task Database_rejects_orphan_snapshot_duplicate_public_code_and_second_current_window()
    {
        var seed = await SeedAsync();
        await using (var orphan = Context(seed.Connection))
        {
            orphan.PublishedContents.Add(PublishedContent.Create(Guid.NewGuid(), Guid.NewGuid(), "zamansiz-dugun", 1, 1, Content, 0, Now));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => orphan.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using (var duplicate = Context(seed.Connection))
        {
            duplicate.Invitations.Add(Invitation.Create(Guid.NewGuid(), seed.Account, seed.Code, Now));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using (var publish = Context(seed.Connection))
            Assert.Equal(InitialPublicationOutcome.Succeeded, (await Service(publish).PublishAsync(seed.Account, seed.Invitation, Request(), default)).Outcome);
        await using var duplicateWindow = Context(seed.Connection);
        var window = await duplicateWindow.PublicationWindows.SingleAsync();
        duplicateWindow.PublicationWindows.Add(PublicationWindow.Create(Guid.NewGuid(), seed.Invitation, window.GrantId, Now, Now.AddDays(1), "Europe/Istanbul", Now));
        var constraint = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateWindow.SaveChangesAsync());
        Assert.Equal("ux_publication_windows_current_invitation", Assert.IsType<PostgresException>(constraint.InnerException).ConstraintName);
    }

    private async Task<Seed> SeedAsync(string template = "zamansiz-dugun", AccountType accountType = AccountType.Individual)
    {
        var connection = Unpooled(await postgreSql.CreateEmptyDatabaseAsync());
        await using var context = Context(connection);
        await context.Database.MigrateAsync();
        await new PlanCatalogInitializer(context).InitializeAsync(default);
        await new TemplateCatalogInitializer(context, new TemplateRendererRegistry()).InitializeAsync(default);
        var account = Guid.NewGuid();
        AddAccount(context, account, accountType);
        var invitation = AddDraft(context, account, template);
        await context.SaveChangesAsync();
        return new Seed(connection, account, invitation, (await context.Invitations.SingleAsync()).PublicCode);
    }

    private static Guid AddDraft(DavetiyeDbContext context, Guid account, string template = "zamansiz-dugun")
    {
        var id = Guid.NewGuid();
        var invitation = Invitation.Create(id, account, new CryptographicPublicCodeGenerator().Generate(), Now);
        invitation.PinTemplate(template, 1);
        context.Invitations.Add(invitation);
        context.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), id, 1, Content, Now));
        return id;
    }

    private static Guid AddGrant(DavetiyeDbContext context, Guid account, Guid plan)
    {
        var id = Guid.NewGuid();
        context.AccountPlanGrants.Add(AccountPlanGrant.Create(id, account, plan, GrantSource.IndividualPurchase, Now.AddHours(-1)));
        return id;
    }

    private static void AddAccount(DavetiyeDbContext context, Guid id, AccountType accountType = AccountType.Individual)
    {
        var user = Guid.NewGuid();
        context.Users.Add(new ApplicationUser { Id = user, UserName = user.ToString(), NormalizedUserName = user.ToString(), Email = $"{user:N}@example.test", EmailConfirmed = true });
        context.Accounts.Add(Account.Create(id, user, accountType, "Publication tests", Now));
    }

    private static InitialPublicationRequest Request(bool scheduled = false) =>
        new(null, scheduled ? InitialPublicationMode.Scheduled : InitialPublicationMode.Immediate,
            scheduled ? Now.AddDays(1) : null, Now.AddDays(scheduled ? 2 : 1), "Europe/Istanbul", 1, 0, true);

    private static InitialPublicationService Service(DavetiyeDbContext context, IClock? suppliedClock = null)
    {
        var clock = suppliedClock ?? new FixedClock();
        var accountValidator = new AccountReferenceValidator(context);
        var runner = new AccountQuotaTransactionRunner(context);
        var freeService = new FreePublicationGrantService(accountValidator, new InvitationOwnershipValidator(context), runner, new FreeGrantReservationStore(context, runner));
        var allocator = new PublicationGrantAllocator(context, freeService, new EffectiveEntitlementResolver(
            new EntitlementGrantReader(context, new OrganizationSubscriptionEntitlementReader(context)), clock));
        return new InitialPublicationService(accountValidator, new InitialPublicationStore(context, new MediaPublicationSnapshotReader(context)), new InitialPublicationPreflightValidator(new TemplateCatalogService(context), new TemplateRendererRegistry()), new IanaTimeZoneValidator(), allocator, runner, clock);
    }

    private static MediaAsset ReadyImage(Guid invitationId, string reference, DateTimeOffset at)
    {
        var asset = MediaAsset.CreateCreatorAsset(Guid.NewGuid(), invitationId, MediaKind.Image, at);
        asset.BeginProcessing(reference);
        asset.MarkReady(new NormalizedImageVerificationEvidence(reference, "image/webp", 512), at.AddSeconds(1));
        return asset;
    }

    private static async Task AssertUnpublishedAsync(Seed seed)
    {
        await using var verification = Context(seed.Connection);
        Assert.Equal(InvitationStoredState.Draft, (await verification.Invitations.SingleAsync(i => i.Id == seed.Invitation)).State);
        Assert.False(await verification.PublishedContents.AnyAsync());
        Assert.False(await verification.PublicationWindows.AnyAsync());
        Assert.False(await verification.AccountPlanGrants.AnyAsync());
    }

    private static DavetiyeDbContext Context(string connection, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(Unpooled(connection), options => options.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).AddInterceptors(interceptors).Options);

    // Each case owns a separate database. Retaining a connection pool per database would exhaust
    // the fixture server's connections when the complete integration suite shares this process.
    private static string Unpooled(string connection) => new NpgsqlConnectionStringBuilder(connection) { Pooling = false }.ConnectionString;

    private sealed record Seed(string Connection, Guid Account, Guid Invitation, string Code);
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class AdjustableClock : IClock { public DateTimeOffset Current { get; set; } = Now; public DateTimeOffset UtcNow => Current; }
    private sealed class NoopMediaPurgeCoordinator : IMediaPurgeCoordinator
    {
        public Task<MediaPurgeResult> EnqueueAndRemoveInvitationAssetsAsync(Guid invitationId, CancellationToken cancellationToken) =>
            Task.FromResult(new MediaPurgeResult(0));
    }
    private sealed class NoopRsvpPurgeCoordinator : IRsvpPurgeCoordinator
    {
        public Task PurgeForInvitationAsync(Guid invitationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class NoopMemoriesPurgeCoordinator : IMemoriesPurgeCoordinator
    {
        public Task PurgeForInvitationAsync(Guid invitationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class NoopGiftRegistryPurgeCoordinator : IGiftRegistryPurgeCoordinator
    {
        public Task PurgeForInvitationAsync(Guid invitationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class ClockReadSignal : IClock
    {
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset Current { get; set; } = Now;
        public DateTimeOffset UtcNow { get { var instant = Current; FirstRead.TrySetResult(); return instant; } }
    }
    private sealed class RejectPublicationSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PublishedContent>().Any()) throw new InvalidOperationException("Injected publication save failure");
            return ValueTask.FromResult(result);
        }
    }
}
