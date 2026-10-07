using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Davetiye.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class InvitationLifecycleTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "TestPassw0rd1";
    private const string Content = """{"headline":"Original public","startsAt":"2026-10-10T12:00:00Z","timeZoneId":"Europe/Istanbul","venue":{"name":"Hall","address":"Street"},"message":"Welcome","hostNames":["Ada"]}""";

    [Fact]
    public async Task Status_is_readonly_and_does_not_issue_a_phantom_free_grant()
    {
        await using var h = await CreateAsync();
        var first = await h.Status();
        var second = await h.Status();
        Assert.Equal("Draft", first.EffectiveState);
        Assert.Equal(first.Expected, second.Expected);
        Assert.Contains(first.GrantChoices, choice => choice.Kind == "Free" || choice.GrantId is null);
        await using var db = h.Db();
        Assert.False(await db.AccountPlanGrants.AnyAsync());
        Assert.False(await db.PublicationWindows.AnyAsync());
        Assert.False(await db.PublishedContents.AnyAsync());
    }

    [Fact]
    public async Task Autosave_is_private_until_explicit_update_then_pause_and_resume_preserve_end()
    {
        await using var h = await CreateAsync();
        var published = await h.Action("publish", publication: Window());
        var window = published.CurrentWindow!;
        await h.EditWorking("Private change");
        Assert.True((await h.Status()).HasPendingChanges);
        Assert.Equal("Original public", await h.PublishedHeadline());
        var updated = await h.Action("update");
        Assert.Equal("Private change", await h.PublishedHeadline());
        Assert.Equal(1, updated.Published!.SourceWorkingRevision);
        Assert.False(updated.HasPendingChanges);
        var paused = await h.Action("pause");
        Assert.Equal("Paused", paused.EffectiveState);
        Assert.Equal(window.EndsAtUtc, paused.CurrentWindow!.EndsAtUtc);
        await h.EditWorking("Still private");
        var resumed = await h.Action("resume");
        Assert.Equal("Active", resumed.EffectiveState);
        Assert.Equal(window.Id, resumed.CurrentWindow!.Id);
        Assert.Equal(window.EndsAtUtc, resumed.CurrentWindow.EndsAtUtc);
        Assert.Equal("Private change", await h.PublishedHeadline());
        Assert.True(resumed.HasPendingChanges);
    }

    [Fact]
    public async Task Creator_RSVP_edits_are_rejected_while_active_and_allowed_when_paused()
    {
        await using var h = await CreateAsync();
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await Login(owner, h.Email);
        var csrf = await Csrf(owner);
        var path = $"/api/v1/invitations/{h.Invitation}/rsvp";

        var enable = await SendRsvp(owner, HttpMethod.Put, path, new SetRsvpEnabledRequest(0, true), csrf);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        using var enabledBody = JsonDocument.Parse(await enable.Content.ReadAsStringAsync());
        var rsvpRevision = enabledBody.RootElement.GetProperty("revision").GetInt64();
        Assert.True(enabledBody.RootElement.GetProperty("enabled").GetBoolean());
        var inputLimits = enabledBody.RootElement.GetProperty("inputLimits");
        Assert.Equal(20, inputLimits.GetProperty("maxActiveQuestionsPerInvitation").GetInt32());
        Assert.Equal(200, inputLimits.GetProperty("maxQuestionPromptCharacters").GetInt32());
        Assert.Equal(100, inputLimits.GetProperty("maxOptionLabelCharacters").GetInt32());
        Assert.Equal(20, inputLimits.GetProperty("maxDefinedOptionsPerChoiceQuestion").GetInt32());

        await h.Action("publish", publication: Window());
        var activeEdit = await SendRsvp(owner, HttpMethod.Put, path,
            new SetRsvpEnabledRequest(rsvpRevision, false), csrf);
        Assert.Equal(HttpStatusCode.Conflict, activeEdit.StatusCode);
        using (var activeError = JsonDocument.Parse(await activeEdit.Content.ReadAsStringAsync()))
            Assert.Equal("Active", activeError.RootElement.GetProperty("effectiveState").GetString());
        using (var activeRead = await owner.GetAsync(new Uri(path, UriKind.Relative)))
        using (var activeBody = JsonDocument.Parse(await activeRead.Content.ReadAsStringAsync()))
        {
            Assert.Equal(HttpStatusCode.OK, activeRead.StatusCode);
            Assert.True(activeBody.RootElement.GetProperty("enabled").GetBoolean());
            Assert.Equal(rsvpRevision, activeBody.RootElement.GetProperty("revision").GetInt64());
        }

        await h.Action("pause");
        var pausedEdit = await SendRsvp(owner, HttpMethod.Put, path,
            new SetRsvpEnabledRequest(rsvpRevision, false), csrf);
        Assert.Equal(HttpStatusCode.OK, pausedEdit.StatusCode);
        using var pausedBody = JsonDocument.Parse(await pausedEdit.Content.ReadAsStringAsync());
        Assert.Equal("Paused", pausedBody.RootElement.GetProperty("effectiveState").GetString());
        Assert.False(pausedBody.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData("invitation")]
    [InlineData("working")]
    [InlineData("published")]
    [InlineData("windowId")]
    [InlineData("windowRevision")]
    public async Task Every_stale_revision_component_is_rejected_without_mutation(string component)
    {
        await using var h = await CreateAsync();
        var status = await h.Action("publish", publication: Window());
        var expected = status.Expected;
        expected = component switch
        {
            "invitation" => expected with { InvitationRevision = expected.InvitationRevision - 1 },
            "working" => expected with { WorkingContentRevision = expected.WorkingContentRevision + 1 },
            "published" => expected with { PublishedContentRevision = expected.PublishedContentRevision + 1 },
            "windowId" => expected with { WindowId = Guid.NewGuid() },
            _ => expected with { WindowRevision = expected.WindowRevision + 1 }
        };
        var result = await h.Execute(new PublicationActionRequest("pause", expected));
        Assert.Equal("Conflict", result.Code);
        var after = await h.Status();
        Assert.Equal("Active", after.EffectiveState);
        Assert.Equal(status.Expected, after.Expected);
    }

    [Fact]
    public async Task Started_scheduled_uses_active_actions_even_when_worker_has_not_changed_stored_state()
    {
        await using var h = await CreateAsync();
        var scheduled = await h.Action("publish", publication: Window(true));
        await h.EditWorking("Private after scheduling");
        h.Clock.Current = scheduled.CurrentWindow!.StartsAtUtc;
        var effective = await h.Status();
        Assert.Equal("Scheduled", effective.StoredState);
        Assert.Equal("Active", effective.EffectiveState);
        Assert.DoesNotContain("cancelSchedule", effective.AllowedActions);
        Assert.Contains("pause", effective.AllowedActions);
        Assert.Equal("Original public", await h.PublishedHeadline());
        Assert.Equal("InvalidState", (await h.Execute(new("cancelSchedule", effective.Expected))).Code);
        var paused = await h.Action("pause");
        Assert.Equal("Paused", paused.EffectiveState);
        Assert.Equal(scheduled.CurrentWindow.EndsAtUtc, paused.CurrentWindow!.EndsAtUtc);
        await using var db = h.Db();
        Assert.NotNull((await db.AccountPlanGrants.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task Prestart_cancel_releases_single_free_right_and_republish_replaces_snapshot_singleton()
    {
        await using var h = await CreateAsync();
        var scheduled = await h.Action("publish", publication: Window(true));
        var cancelled = await h.Action("cancelSchedule");
        Assert.Equal("Draft", cancelled.EffectiveState);
        await using (var db = h.Db())
        {
            var grant = await db.AccountPlanGrants.SingleAsync();
            Assert.Null(grant.ConsumedAt);
            Assert.Null(grant.ReservedAt);
            Assert.Null(grant.AssignedInvitationId);
        }
        await h.EditWorking("Republished content");
        var republished = await h.Action("publish", publication: Window());
        Assert.Equal(scheduled.PublicCode, republished.PublicCode);
        Assert.NotEqual(scheduled.CurrentWindow!.Id, republished.CurrentWindow!.Id);
        Assert.Equal("Republished content", await h.PublishedHeadline());
        await using var verify = h.Db();
        Assert.Equal(1, await verify.AccountPlanGrants.CountAsync());
        Assert.Equal(1, await verify.PublishedContents.CountAsync());
        Assert.Equal(1, await verify.PublicationWindows.CountAsync(w => w.IsCurrent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoked_prestart_grant_can_be_cancelled_over_HTTP_without_restoring_its_usability(bool paid)
    {
        await using var h = await CreateAsync();
        var requestedGrant = paid ? await h.AddPaidGrant() : (Guid?)null;
        var scheduled = await h.Action("publish", publication: Window(true, requestedGrant));
        var grantId = scheduled.CurrentWindow!.GrantId;
        await using (var db = h.Db())
        {
            (await db.AccountPlanGrants.SingleAsync(grant => grant.Id == grantId)).Revoke(Now);
            await db.SaveChangesAsync();
        }

        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true, BaseAddress = new Uri("https://localhost")
        });
        await Login(owner, h.Email);
        var csrf = await Csrf(owner);
        var path = $"/api/v1/invitations/{h.Invitation}/publication/actions";
        var response = await Send(owner, path, new("cancelSchedule", scheduled.Expected), csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await response.Content.ReadFromJsonAsync<PublicationStatus>();
        Assert.NotNull(cancelled);
        Assert.Equal("Draft", cancelled.EffectiveState);
        Assert.Null(cancelled.CurrentWindow);
        Assert.Equal(scheduled.Published, cancelled.Published);
        Assert.DoesNotContain(cancelled.GrantChoices, choice => choice.GrantId == grantId || (!paid && choice.Kind == "free"));
        await using (var verify = h.Db())
        {
            var grant = await verify.AccountPlanGrants.SingleAsync(candidate => candidate.Id == grantId);
            Assert.Equal(Now, grant.RevokedAt);
            Assert.Null(grant.AssignedInvitationId);
            Assert.Null(grant.ReservedAt);
            Assert.Null(grant.ConsumedAt);
            Assert.False((await verify.PublicationWindows.SingleAsync()).IsCurrent);
            Assert.Throws<InvalidOperationException>(() => grant.ReserveForInvitation(h.Invitation, Now));
            Assert.Throws<InvalidOperationException>(() => grant.ConsumeForInvitation(h.Invitation, Now));
        }

        var republish = await Send(owner, path,
            new("publish", cancelled.Expected, Window(false, grantId), ProceedWithRecommendedWarnings: true), csrf);
        Assert.Equal(HttpStatusCode.Conflict, republish.StatusCode);
        using var error = JsonDocument.Parse(await republish.Content.ReadAsStringAsync());
        Assert.Equal("GrantUnavailable", error.RootElement.GetProperty("code").GetString());
        Assert.Equal(cancelled.Expected, (await h.Status()).Expected);
        await using var final = h.Db();
        Assert.Equal(1, await final.PublicationWindows.CountAsync());
        Assert.False(await final.PublicationWindows.AnyAsync(window => window.IsCurrent));
    }

    [Fact]
    public async Task Whole_missed_scheduled_window_consumes_free_before_reactivation_with_new_grant()
    {
        await using var h = await CreateAsync();
        var scheduled = await h.Action("publish", publication: Window(true));
        h.Clock.Current = scheduled.CurrentWindow!.EndsAtUtc.AddHours(1);
        var expired = await h.Status();
        Assert.Equal("Expired", expired.EffectiveState);
        Assert.DoesNotContain(expired.GrantChoices, c => c.GrantId is null);
        var grant = await h.AddPaidGrant();
        var reactivated = await h.Action("reactivate", publication: Window(false, grant, h.Clock.Current));
        Assert.Equal("Active", reactivated.EffectiveState);
        Assert.Equal(scheduled.PublicCode, reactivated.PublicCode);
        Assert.NotEqual(scheduled.CurrentWindow.Id, reactivated.CurrentWindow!.Id);
        Assert.Equal(grant, reactivated.CurrentWindow.GrantId);
        await using var db = h.Db();
        Assert.NotNull((await db.AccountPlanGrants.SingleAsync(g => g.Source == GrantSource.Free)).ConsumedAt);
        Assert.Equal(1, await db.PublicationWindows.CountAsync(w => w.IsCurrent));
    }

    [Theory]
    [InlineData("reschedule")]
    [InlineData("publishNow")]
    public async Task Schedule_timing_actions_keep_frozen_snapshot_and_require_current_duration(string action)
    {
        await using var h = await CreateAsync();
        var grant = await h.AddPaidGrant();
        var scheduled = await h.Action("publish", publication: Window(true, grant));
        await h.EditWorking("Do not publish implicitly");
        var timing = action == "reschedule" ? Window(true, grant, Now.AddHours(1)) : null;
        var changed = await h.Action(action, publication: timing);
        Assert.Equal("Original public", await h.PublishedHeadline());
        Assert.Equal(scheduled.Published!.Revision, changed.Published!.Revision);
        Assert.True(changed.HasPendingChanges);
        Assert.Equal(action == "reschedule" ? Now.AddDays(2).AddHours(1) : scheduled.CurrentWindow!.EndsAtUtc, changed.CurrentWindow!.EndsAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resume_without_update_grandfathers_premium_snapshot_but_explicit_working_update_requires_entitlement(bool update)
    {
        await using var h = await CreateAsync("romantik-nisan");
        var grant = await h.AddPaidGrant("premium");
        await h.Action("publish", publication: Window(false, grant));
        await h.Action("pause");
        await h.EditWorking("New premium working");
        await using (var db = h.Db())
        {
            var plan = await db.Plans.SingleAsync(p => p.Key == "premium");
            (await db.PlanEntitlements.SingleAsync(e => e.PlanId == plan.Id && e.EntitlementKey == EntitlementCatalog.PremiumTemplatesEnabled)).UpdateValue(null, false);
            (await db.PlanEntitlements.SingleAsync(e => e.PlanId == plan.Id && e.EntitlementKey == EntitlementCatalog.MaxPublishDays)).UpdateValue(0, null);
            (await db.PlanEntitlements.SingleAsync(e => e.PlanId == plan.Id && e.EntitlementKey == EntitlementCatalog.MaxActiveInvitations)).UpdateValue(0, null);
            await db.SaveChangesAsync();
        }
        var before = await h.Status();
        var result = await h.Execute(new("resume", before.Expected, PublishWorkingContent: update, ProceedWithRecommendedWarnings: true));
        Assert.Equal(update ? "PremiumTemplateNotAllowed" : "Succeeded", result.Code);
        Assert.Equal("Original public", await h.PublishedHeadline());
        Assert.Equal(update ? "Paused" : "Active", (await h.Status()).EffectiveState);
        Assert.Equal(before.CurrentWindow!.EndsAtUtc, (await h.Status()).CurrentWindow!.EndsAtUtc);
    }

    [Theory]
    [InlineData("2027-03-28T02:30:00", "2027-03-28T04:30:00")]
    [InlineData("2027-10-31T02:30:00", "2027-10-31T04:30:00")]
    public async Task Invalid_or_ambiguous_DST_local_times_are_rejected_without_reservation(string start, string end)
    {
        await using var h = await CreateAsync();
        var result = await h.Execute(new("publish", (await h.Status()).Expected,
            new("Scheduled", start, end, "Europe/Berlin", null), ProceedWithRecommendedWarnings: true));
        Assert.Equal("InvalidRequest", result.Code);
        await using var db = h.Db();
        Assert.False(await db.AccountPlanGrants.AnyAsync());
        Assert.False(await db.PublicationWindows.AnyAsync());
    }

    [Fact]
    public async Task Competing_pause_and_update_have_one_revision_winner()
    {
        await using var h = await CreateAsync();
        await h.Action("publish", publication: Window());
        await h.EditWorking("Racing change");
        var expected = (await h.Status()).Expected;
        var results = await Task.WhenAll(h.Execute(new("pause", expected)), h.Execute(new("update", expected, ProceedWithRecommendedWarnings: true)));
        Assert.Single(results, r => r.Code == "Succeeded");
        Assert.Single(results, r => r.Code == "Conflict");
        var after = await h.Status();
        Assert.Equal(Now.AddDays(1), after.CurrentWindow!.EndsAtUtc);
    }

    [Fact]
    public async Task Explicit_resume_publishes_working_and_revocation_blocks_later_resume()
    {
        await using var h = await CreateAsync();
        var grant = await h.AddPaidGrant();
        await h.Action("publish", publication: Window(false, grant));
        await h.Action("pause");
        await h.EditWorking("Explicit resume snapshot");
        var before = await h.Status();
        var result = await h.Execute(new("resume", before.Expected, PublishWorkingContent: true, ProceedWithRecommendedWarnings: true));
        Assert.Equal("Succeeded", result.Code);
        Assert.Equal("Explicit resume snapshot", await h.PublishedHeadline());
        await h.Action("pause");
        await using (var db = h.Db())
        {
            (await db.AccountPlanGrants.SingleAsync()).Revoke(Now);
            await db.SaveChangesAsync();
        }
        result = await h.Execute(new("resume", (await h.Status()).Expected));
        Assert.Equal("GrantUnavailable", result.Code);
        Assert.Equal("Paused", (await h.Status()).EffectiveState);
    }

    [Theory]
    [InlineData("reschedule")]
    [InlineData("publishNow")]
    public async Task Schedule_timing_admission_uses_current_limits_and_keeps_original_window_on_denial(string action)
    {
        await using var h = await CreateAsync();
        var scheduled = await h.Action("publish", publication: Window(true));
        await using (var db = h.Db())
        {
            var free = await db.Plans.SingleAsync(p => p.Key == "free");
            (await db.PlanEntitlements.SingleAsync(e => e.PlanId == free.Id && e.EntitlementKey == EntitlementCatalog.MaxPublishDays)).UpdateValue(0, null);
            await db.SaveChangesAsync();
        }
        var before = await h.Status();
        Assert.Equal(scheduled.CurrentWindow!.EndsAtUtc, before.CurrentWindow!.EndsAtUtc);
        var result = await h.Execute(new(action, before.Expected, action == "reschedule" ? Window(true) : null, ProceedWithRecommendedWarnings: true));
        Assert.Equal("PublishDurationExceeded", result.Code);
        var after = await h.Status();
        Assert.Equal(before.Expected, after.Expected);
        Assert.Equal(before.CurrentWindow, after.CurrentWindow);
        await using var verify = h.Db();
        Assert.Null((await verify.AccountPlanGrants.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task Cancel_and_republish_cannot_make_previous_window_revision_tuple_valid_again()
    {
        await using var h = await CreateAsync();
        var old = await h.Action("publish", publication: Window(true));
        await h.Action("cancelSchedule");
        var current = await h.Action("publish", publication: Window(true));
        var oldWindowWithFreshOtherRevisions = current.Expected with { WindowId = old.Expected.WindowId, WindowRevision = old.Expected.WindowRevision };
        Assert.Equal("Conflict", (await h.Execute(new("cancelSchedule", oldWindowWithFreshOtherRevisions))).Code);
        Assert.Equal(current.Expected, (await h.Status()).Expected);
    }

    [Fact]
    public async Task Window_end_is_authoritative_for_commands_and_does_not_allow_resume_or_update()
    {
        await using var h = await CreateAsync();
        var active = await h.Action("publish", publication: Window());
        await h.Action("pause");
        h.Clock.Current = active.CurrentWindow!.EndsAtUtc;
        var expired = await h.Status();
        Assert.Equal("Expired", expired.EffectiveState);
        Assert.Equal("InvalidState", (await h.Execute(new("resume", expired.Expected))).Code);
        Assert.Equal("InvalidState", (await h.Execute(new("update", expired.Expected))).Code);
        Assert.Equal(active.CurrentWindow.EndsAtUtc, (await h.Status()).CurrentWindow!.EndsAtUtc);
    }

    [Fact]
    public async Task Lifecycle_HTTP_requires_cookie_CSRF_and_owner_and_returns_private_nostore_status()
    {
        await using var h = await CreateAsync();
        using var anonymous = h.CreateClient();
        var path = $"/api/v1/invitations/{h.Invitation}/publication";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(owner, h.Email);
        var statusResponse = await owner.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.True(statusResponse.Headers.CacheControl?.NoStore);
        var expected = (await h.Status()).Expected;
        var request = new PublicationActionRequest("publish", expected, Window(), ProceedWithRecommendedWarnings: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(new Uri(path + "/actions", UriKind.Relative), request)).StatusCode);
        var token = await Csrf(owner);
        var foreignEmail = await h.AddForeignAccount();
        using var foreign = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(foreign, foreignEmail);
        var foreignRead = await foreign.GetAsync(new Uri(path, UriKind.Relative));
        var unknownRead = await foreign.GetAsync(new Uri($"/api/v1/invitations/{Guid.NewGuid()}/publication", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Equal(unknownRead.StatusCode, foreignRead.StatusCode);
        Assert.True(foreignRead.Headers.CacheControl?.NoStore);
        var foreignWrite = await Send(foreign, path + "/actions", request, await Csrf(foreign));
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);
        Assert.DoesNotContain("Original public", await foreignWrite.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var success = await Send(owner, path + "/actions", request, token);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.True(success.Headers.CacheControl?.NoStore);
        var conflict = await Send(owner, path + "/actions", new PublicationActionRequest("pause", expected), token);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.True(conflict.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_cookie_cannot_publish_after_account_is_banned_or_email_becomes_unverified(bool banned)
    {
        await using var h = await CreateAsync();
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(owner, h.Email);
        var token = await Csrf(owner);
        var expected = (await h.Status()).Expected;
        await using (var db = h.Db())
        {
            if (banned) db.BanRecords.Add(BanRecord.Create(Guid.NewGuid(), h.Account, "Lifecycle ban", Now, Guid.NewGuid()));
            else (await db.Users.SingleAsync()).EmailConfirmed = false;
            await db.SaveChangesAsync();
        }
        var response = await Send(owner, $"/api/v1/invitations/{h.Invitation}/publication/actions",
            new PublicationActionRequest("publish", expected, Window(), ProceedWithRecommendedWarnings: true), token);
        Assert.Equal(banned ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        await using var verify = h.Db();
        Assert.False(await verify.AccountPlanGrants.AnyAsync());
        Assert.False(await verify.PublishedContents.AnyAsync());
    }

    [Fact]
    public async Task Authenticated_raw_HTTP_lifecycle_requests_are_rejected_in_production()
    {
        await using var h = await CreateAsync(environment: "Production");
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var login = await Login(owner, h.Email);
        var cookies = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]));
        using var raw = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://localhost/api/v1/invitations/{h.Invitation}/publication"));
        raw.Headers.Add("Cookie", cookies);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.SendAsync(raw)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_command_uses_effective_state_after_row_lock_wait(bool scheduledStart)
    {
        await using var h = await CreateAsync();
        var accepted = await h.Action("publish", publication: Window(scheduledStart));
        await using var blocker = h.Db();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM working_contents WHERE invitation_id = {h.Invitation} FOR UPDATE");
        await using var scope = h.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DavetiyeDbContext>();
        await context.Database.OpenConnectionAsync();
        var task = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>().ExecuteAsync(h.Account, h.Invitation,
            new(scheduledStart ? "cancelSchedule" : "pause", accepted.Expected), default);
        var pid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        await using var monitor = h.Db();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await monitor.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock') AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(20, timeout.Token);
        h.Clock.Current = scheduledStart ? accepted.CurrentWindow!.StartsAtUtc : accepted.CurrentWindow!.EndsAtUtc;
        await transaction.CommitAsync();
        Assert.Equal("InvalidState", (await task.WaitAsync(TimeSpan.FromSeconds(10))).Code);
        Assert.Equal(accepted.Expected, (await h.Status()).Expected);
        await using var verify = h.Db();
        var grant = await verify.AccountPlanGrants.SingleAsync();
        Assert.Equal(scheduledStart ? null : Now, grant.ConsumedAt);
    }

    [Fact]
    public async Task Paused_window_holds_account_slot_until_end_then_new_publish_is_allowed()
    {
        await using var h = await CreateAsync();
        var originalGrant = await h.AddPaidGrant();
        var active = await h.Action("publish", publication: Window(false, originalGrant));
        await h.Action("pause");
        var secondId = await h.AddOtherInvitation();
        var secondGrant = await h.AddPaidGrant();
        var before = await h.Status(secondId);
        var denied = await h.Execute(new("publish", before.Expected, Window(false, secondGrant), ProceedWithRecommendedWarnings: true), secondId);
        Assert.Equal("ActiveInvitationQuotaExceeded", denied.Code);
        Assert.Equal(before.Expected, (await h.Status(secondId)).Expected);
        await using (var verify = h.Db())
        {
            var grant = await verify.AccountPlanGrants.SingleAsync(g => g.Id == secondGrant);
            Assert.Null(grant.ReservedAt);
            Assert.Null(grant.ConsumedAt);
        }
        h.Clock.Current = active.CurrentWindow!.EndsAtUtc;
        var success = await h.Execute(new("publish", before.Expected, Window(false, secondGrant, h.Clock.Current), ProceedWithRecommendedWarnings: true), secondId);
        Assert.Equal("Succeeded", success.Code);
        Assert.Equal("Expired", (await h.Status()).EffectiveState);
    }

    [Fact]
    public async Task Reschedule_rechecks_account_quota_and_rolls_back_without_changing_accepted_dates_or_snapshot()
    {
        await using var h = await CreateAsync();
        var firstGrant = await h.AddPaidGrant();
        var first = await h.Action("publish", publication: Window(true, firstGrant));
        var secondId = await h.AddOtherInvitation();
        var secondGrant = await h.AddPaidGrant();
        var second = await h.Execute(new("publish", (await h.Status(secondId)).Expected,
            Window(true, secondGrant, Now.AddDays(1)), ProceedWithRecommendedWarnings: true), secondId);
        Assert.Equal("Succeeded", second.Code);
        var denied = await h.Execute(new("reschedule", first.Expected,
            Window(true, firstGrant, Now.AddDays(1)), ProceedWithRecommendedWarnings: true));
        Assert.Equal("ActiveInvitationQuotaExceeded", denied.Code);
        var after = await h.Status();
        Assert.Equal(first.Expected, after.Expected);
        Assert.Equal(first.CurrentWindow, after.CurrentWindow);
        Assert.Equal(first.Published, after.Published);
        await using var db = h.Db();
        Assert.Equal(2, await db.PublicationWindows.CountAsync());
        Assert.Equal(2, await db.PublicationWindows.CountAsync(w => w.IsCurrent));
        Assert.Equal(0, await db.AccountPlanGrants.CountAsync(g => g.ConsumedAt != null));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("reactivate")]
    public async Task Explicit_organization_subscription_grant_is_rejected_over_HTTP_without_publication_mutations(string action)
    {
        await using var h = await CreateAsync();
        if (action == "reactivate")
        {
            var accepted = await h.Action("publish", publication: Window());
            h.Clock.Current = accepted.CurrentWindow!.EndsAtUtc;
        }
        var orgGrant = await h.AddPaidGrant("organization", GrantSource.OrganizationSubscription);
        var before = await h.Status();
        Assert.DoesNotContain(before.GrantChoices, choice => choice.GrantId == orgGrant);
        using var owner = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await Login(owner, h.Email);
        var response = await Send(owner, $"/api/v1/invitations/{h.Invitation}/publication/actions",
            new PublicationActionRequest(action, before.Expected, Window(false, orgGrant, h.Clock.Current), ProceedWithRecommendedWarnings: true), await Csrf(owner));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GrantUnavailable", error.RootElement.GetProperty("code").GetString());
        var after = await h.Status();
        Assert.Equal(before.Expected, after.Expected);
        Assert.Equal(before.CurrentWindow, after.CurrentWindow);
        Assert.Equal(before.Published, after.Published);
        await using var db = h.Db();
        Assert.Equal(action == "publish" ? 0 : 1, await db.PublicationWindows.CountAsync());
        Assert.Equal(action == "publish" ? 0 : 1, await db.PublishedContents.CountAsync());
        var grant = await db.AccountPlanGrants.SingleAsync(g => g.Id == orgGrant);
        Assert.Null(grant.AssignedInvitationId);
        Assert.Null(grant.ReservedAt);
        Assert.Null(grant.ConsumedAt);
    }

    private static async Task<HttpResponseMessage> Login(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private static async Task<string> Csrf(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/v1/antiforgery/token", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, PublicationActionRequest payload, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendRsvp(HttpClient client, HttpMethod method, string path,
        SetRsvpEnabledRequest payload, string token)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private async Task<Harness> CreateAsync(string template = "zamansiz-dugun", string environment = "Development")
    {
        var connectionBuilder = new NpgsqlConnectionStringBuilder(await postgreSql.CreateEmptyDatabaseAsync()) { Pooling = false };
        var connection = connectionBuilder.ConnectionString;
        var h = new Harness(connection, environment);
        await using var db = h.Db();
        await db.Database.MigrateAsync();
        await new PlanCatalogInitializer(db).InitializeAsync(default);
        await new TemplateCatalogInitializer(db, new TemplateRendererRegistry()).InitializeAsync(default);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = h.Email, NormalizedUserName = h.Email.ToUpperInvariant(), Email = h.Email, NormalizedEmail = h.Email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
        db.Users.Add(user);
        db.Accounts.Add(Account.Create(h.Account, user.Id, AccountType.Individual, "Lifecycle tester", Now));
        db.AddAcknowledgedServiceNotice(h.Account, Now);
        var invitation = Invitation.Create(h.Invitation, h.Account, new CryptographicPublicCodeGenerator().Generate(), Now);
        invitation.PinTemplate(template, 1);
        db.Invitations.Add(invitation);
        db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), h.Invitation, 1, Content, Now));
        await db.SaveChangesAsync();
        return h;
    }

    private static PublicationWindowRequest Window(bool scheduled = false, Guid? grant = null, DateTimeOffset? start = null)
    {
        var instant = start ?? Now;
        return new(scheduled ? "Scheduled" : "Immediate", scheduled ? instant.AddDays(1).AddHours(3).ToString("yyyy-MM-ddTHH:mm:ss") : null,
            instant.AddDays(scheduled ? 2 : 1).AddHours(3).ToString("yyyy-MM-ddTHH:mm:ss"), "Europe/Istanbul", grant);
    }

    private sealed class MutableClock : IClock { public DateTimeOffset Current { get; set; } = Now; public DateTimeOffset UtcNow => Current; }

    private sealed class Harness(string connection, string environment) : WebApplicationFactory<Program>
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Invitation { get; } = Guid.NewGuid();
        public string Email { get; } = $"lifecycle-{Guid.NewGuid():N}@example.test";
        public MutableClock Clock { get; } = new();
        public DavetiyeDbContext Db() => new(new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(connection, o => o.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connection,
                ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
                ["PublicWeb:BaseUrl"] = "https://davetiye.example.test"
            }));
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(Clock));
        }
        public async Task<PublicationStatus> Status(Guid? invitation = null)
        {
            await using var scope = Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>().GetAsync(Account, invitation ?? Invitation, default);
            Assert.Equal("Succeeded", result.Code);
            return Assert.IsType<PublicationStatus>(result.Status);
        }
        public async Task<PublicationLifecycleResult> Execute(PublicationActionRequest request, Guid? invitation = null)
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>().ExecuteAsync(Account, invitation ?? Invitation, request, default);
        }
        public async Task<PublicationStatus> Action(string action, PublicationWindowRequest? publication = null)
        {
            var result = await Execute(new(action, (await Status()).Expected, publication, ProceedWithRecommendedWarnings: true));
            Assert.Equal("Succeeded", result.Code);
            return Assert.IsType<PublicationStatus>(result.Status);
        }
        public async Task EditWorking(string headline)
        {
            await using var db = Db();
            (await db.WorkingContents.SingleAsync()).ReplaceContent(Content.Replace("Original public", headline), 1, Clock.UtcNow);
            await db.SaveChangesAsync();
        }
        public async Task<string?> PublishedHeadline()
        {
            await using var db = Db();
            using var content = JsonDocument.Parse((await db.PublishedContents.SingleAsync()).Content);
            return content.RootElement.GetProperty("headline").GetString();
        }
        public async Task<Guid> AddPaidGrant(string planKey = "standard", GrantSource source = GrantSource.IndividualPurchase)
        {
            await using var db = Db();
            var grant = AccountPlanGrant.Create(Guid.NewGuid(), Account, (await db.Plans.SingleAsync(p => p.Key == planKey)).Id, source, Clock.UtcNow);
            db.AccountPlanGrants.Add(grant);
            await db.SaveChangesAsync();
            return grant.Id;
        }
        public async Task<string> AddForeignAccount()
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
        public async Task<Guid> AddOtherInvitation()
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
    }
}
