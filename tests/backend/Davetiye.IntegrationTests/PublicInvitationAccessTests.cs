using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Domain.Modules.IdentityAndAccounts;
using Davetiye.Domain.Modules.Invitations;
using Davetiye.Domain.Modules.Media;
using Davetiye.Domain.Modules.PlansAndEntitlements;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Modules.Invitations;
using Davetiye.Infrastructure.Modules.PlansAndEntitlements;
using Davetiye.Infrastructure.Modules.Templates;
using Davetiye.Infrastructure.Persistence;
using Davetiye.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
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
public sealed class PublicInvitationAccessTests(PostgreSqlFixture postgreSql)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "TestPassw0rd1";
    private const string PublicHeadline = "Accepted published headline";
    private const string PrivateHeadline = "Secret Working edit never public";
    private const string Content = """{"eventType":"dugun","headline":"Accepted published headline","startsAt":"2026-10-10T12:00:00Z","timeZoneId":"Europe/Istanbul","venue":{"name":"Accepted venue","address":"Published address","mapUrl":"https://maps.example.test/private-map-token"},"message":"Welcome","hostNames":["Ada"],"programItems":[{"title":"Reception","startsAt":"2026-10-10T13:00:00Z"}]}""";

    [Fact]
    public async Task Active_projection_is_allowlisted_Published_content_without_Working_or_internal_authority()
    {
        await using var h = await CreateAsync();
        var mediaId = Guid.NewGuid();
        await using (var db = h.Db())
        {
            var asset = MediaAsset.CreateCreatorAsset(mediaId, h.Invitation, MediaKind.Image, Now);
            asset.BeginProcessing("opaque-provider-reference");
            asset.MarkReady(new NormalizedImageVerificationEvidence("opaque-provider-reference", "image/webp", 4096), Now.AddSeconds(1));
            db.MediaAssets.Add(asset);
            db.MediaPlacements.Add(asset.Place(Guid.NewGuid(), MediaPresentationRole.Cover, 0, Now.AddSeconds(1)));
            await db.SaveChangesAsync();
        }
        await h.Publish();
        await using (var db = h.Db())
        {
            (await db.WorkingContents.SingleAsync()).ReplaceContent(Content.Replace(PublicHeadline, PrivateHeadline), 1, Now.AddHours(1));
            await db.SaveChangesAsync();
        }
        using var guest = h.CreateClient();
        var response = await guest.GetAsync(h.PublicPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("active", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(new[] { "content", "contentSchemaVersion", "media", "rendererVersion", "status", "templateKey" }, json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var media = Assert.Single(json.RootElement.GetProperty("media").EnumerateArray());
        Assert.Equal(new[] { "assetId", "kind", "role", "sortOrder" }, media.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(mediaId, media.GetProperty("assetId").GetGuid());
        Assert.Equal("Image", media.GetProperty("kind").GetString());
        Assert.Equal("Cover", media.GetProperty("role").GetString());
        Assert.Equal("zamansiz-dugun", json.RootElement.GetProperty("templateKey").GetString());
        Assert.Equal(PublicHeadline, json.RootElement.GetProperty("content").GetProperty("headline").GetString());
        Assert.Equal("Europe/Istanbul", json.RootElement.GetProperty("content").GetProperty("timeZoneId").GetString());
        Assert.DoesNotContain(PrivateHeadline, body, StringComparison.Ordinal);
        Assert.DoesNotContain("mapUrl", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private-map-token", body, StringComparison.Ordinal);
        foreach (var id in await h.InternalIds()) Assert.DoesNotContain(id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        foreach (var forbidden in new[] { "accountId", "invitationId", "grantId", "publicCode", "revision", "working", "storedState", "signedUrl", "streamUid", "providerObjectReference", "deliveryUrl", "opaque-provider-reference" })
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Public_RSVP_read_and_submission_require_origin_and_antiforgery_and_keep_manage_token_out_of_json()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        var questionIds = await h.EnableRsvp();
        using var guest = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var path = $"/api/v1/public/invitations/{h.Code}/rsvp";
        using (var read = await guest.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            AssertHeaders(read);
            var json = await read.Content.ReadAsStringAsync();
            Assert.Contains("\"type\":\"Number\"", json, StringComparison.Ordinal);
            Assert.Contains("\"prompt\":\"Guests\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("invitationId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accountId", json, StringComparison.OrdinalIgnoreCase);
            using var payload = JsonDocument.Parse(json);
            var answerLimits = payload.RootElement.GetProperty("answerLimits");
            Assert.Equal(200, answerLimits.GetProperty("maxShortTextAnswerCharacters").GetInt32());
            Assert.Equal(2_000, answerLimits.GetProperty("maxLongTextAnswerCharacters").GetInt32());
            Assert.False(answerLimits.TryGetProperty("maxActiveQuestionsPerInvitation", out _));
        }

        var answers = new SubmitPublicRsvpRequest([
            new(questionIds.Name, TextValue: "Ada"),
            new(questionIds.Attending, BooleanValue: true),
            new(questionIds.Count, NumberValue: 2)
        ]);
        var submitPath = path + "/submissions";
        using (var noOrigin = await guest.PostAsJsonAsync(submitPath, answers))
            Assert.Equal(HttpStatusCode.Forbidden, noOrigin.StatusCode);
        using (var noCsrf = await PostRsvp(guest, submitPath, answers, token: null))
            Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);

        var csrf = await guest.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
        using (var nullAnswer = await PostRawRsvp(guest, submitPath, "{\"answers\":[null]}", csrf!.Token))
            Assert.Equal(HttpStatusCode.BadRequest, nullAnswer.StatusCode);
        await using (var beforeValid = h.Db())
            Assert.Empty(await beforeValid.RsvpSubmissions.ToListAsync());
        using var submitted = await PostRsvp(guest, submitPath, answers, csrf!.Token);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var body = await submitted.Content.ReadAsStringAsync();
        Assert.DoesNotContain("manageToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rsvp-manage", body, StringComparison.Ordinal);
        Assert.True(submitted.Headers.TryGetValues("Set-Cookie", out var cookies));
        var manageCookie = Assert.Single(cookies!, value => value.StartsWith("__Host-davetiye-rsvp=", StringComparison.Ordinal));
        Assert.Contains("httponly", manageCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", manageCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", manageCookie, StringComparison.OrdinalIgnoreCase);

        await using var db = h.Db();
        var stored = await db.RsvpManageCapabilities.SingleAsync();
        Assert.Equal(32, stored.HmacDigest.Length);
        Assert.DoesNotContain(Convert.ToBase64String(stored.HmacDigest), body, StringComparison.Ordinal);
        Assert.Equal(1, await db.RsvpSubmissions.CountAsync(item => item.InvitationId == h.Invitation));
    }

    [Fact]
    public async Task Creator_RSVP_results_are_private_paginated_aggregated_and_delete_cascades_capability_history()
    {
        await using var h = await CreateAsync(superAdminTestAuthentication: true);
        var questionIds = await h.EnableRsvp();
        await using (var emptyScope = h.Services.CreateAsyncScope())
        {
            var emptyList = await emptyScope.ServiceProvider.GetRequiredService<ICreatorRsvpResultsService>()
                .ListAsync(h.Account, h.Invitation, 1, 25, default);
            Assert.Equal(0, emptyList.Page!.Summary.ResponseCount);
            Assert.Equal(0m, emptyList.Page.Summary.TotalParticipants);
            Assert.True(emptyList.Page.Summary.ParticipantCountAvailable);
        }
        Guid mealQuestionId;
        Guid mealOptionId;
        Guid firstSubmissionId;
        Guid secondSubmissionId;
        Guid thirdSubmissionId;
        Guid currentMealOptionId;
        Guid[] firstAnswerOptionIds;
        Guid[] capabilityIds;
        using var creator = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using (var login = await creator.PostAsJsonAsync("/api/v1/auth/login", new { email = h.Email, password = Password }))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await using (var db = h.Db())
        {
            var configuration = await db.RsvpConfigurations.Include(item => item.Questions).ThenInclude(item => item.Options)
                .SingleAsync(item => item.InvitationId == h.Invitation);
            var meal = configuration.AddQuestion(Guid.NewGuid(), "Meal", RsvpQuestionType.SingleChoice,
                true, 3, null, Now);
            var vegetarian = meal.AddOption(Guid.NewGuid(), "Vegetarian", 0, Now);
            db.RsvpQuestions.Add(meal);
            db.RsvpQuestionOptions.Add(vegetarian);
            mealQuestionId = meal.Id;
            mealOptionId = vegetarian.Id;

            var firstTime = Now.AddMinutes(1);
            var secondTime = Now.AddMinutes(2);
            var first = RsvpSubmission.Create(Guid.NewGuid(), h.Invitation, firstTime);
            first.AddAnswer(Guid.NewGuid(), configuration, questionIds.Name, firstTime, textValue: "Ada");
            first.AddAnswer(Guid.NewGuid(), configuration, questionIds.Attending, firstTime, booleanValue: true);
            first.AddAnswer(Guid.NewGuid(), configuration, questionIds.Count, firstTime, numberValue: 2m);
            first.AddAnswer(Guid.NewGuid(), configuration, mealQuestionId, firstTime, selectedOptionIds: [mealOptionId]);
            firstSubmissionId = first.Id;

            var second = RsvpSubmission.Create(Guid.NewGuid(), h.Invitation, secondTime);
            second.AddAnswer(Guid.NewGuid(), configuration, questionIds.Name, secondTime, textValue: "Grace");
            second.AddAnswer(Guid.NewGuid(), configuration, questionIds.Attending, secondTime, booleanValue: false);
            second.AddAnswer(Guid.NewGuid(), configuration, questionIds.Count, secondTime, numberValue: 3m);
            second.AddAnswer(Guid.NewGuid(), configuration, mealQuestionId, secondTime, selectedOptionIds: [mealOptionId]);
            secondSubmissionId = second.Id;

            var capabilities = new List<RsvpManageCapability>();
            foreach (var (submission, createdAt) in new[] { (first, firstTime), (second, secondTime) })
            {
                for (var index = 0; index < 2; index++)
                {
                    var capability = RsvpManageCapability.Create(Guid.NewGuid(), submission.Id,
                        RsvpManageCapability.RequiredPurpose, 1,
                        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"))),
                        createdAt, createdAt.AddDays(1));
                    if (index == 0) capability.Revoke(createdAt.AddSeconds(1));
                    capabilities.Add(capability);
                }
            }
            capabilityIds = capabilities.Select(item => item.Id).ToArray();
            db.RsvpSubmissions.AddRange(first, second);
            db.RsvpManageCapabilities.AddRange(capabilities);
            await db.SaveChangesAsync();
            firstAnswerOptionIds = await db.RsvpAnswerOptions.Where(item =>
                    db.RsvpAnswers.Any(answer => answer.Id == item.AnswerId && answer.SubmissionId == firstSubmissionId))
                .Select(item => item.Id).ToArrayAsync();
        }

        await using (var editScope = h.Services.CreateAsyncScope())
        {
            var configurationService = editScope.ServiceProvider.GetRequiredService<ICreatorRsvpConfigurationService>();
            var current = (await configurationService.GetAsync(h.Account, h.Invitation, default)).Configuration!;
            Assert.Equal(CreatorRsvpConfigurationOutcome.Succeeded,
                (await configurationService.UpdateQuestionAsync(h.Account, h.Invitation, mealQuestionId,
                    new SaveRsvpQuestionRequest(current.Revision, "Food preference", "SingleChoice", true, null,
                        [new RsvpQuestionOptionInput(null, "Vegan", 0)]), default)).Outcome);
        }
        await using (var db = h.Db())
        {
            var configuration = await db.RsvpConfigurations.Include(item => item.Questions).ThenInclude(item => item.Options)
                .SingleAsync(item => item.InvitationId == h.Invitation);
            currentMealOptionId = configuration.Questions.Single(item => item.Id == mealQuestionId)
                .Options.Single(item => item.IsActive).Id;
            var submittedAt = Now.AddMinutes(3);
            var third = RsvpSubmission.Create(Guid.NewGuid(), h.Invitation, submittedAt);
            third.AddAnswer(Guid.NewGuid(), configuration, questionIds.Name, submittedAt, textValue: "Lin");
            third.AddAnswer(Guid.NewGuid(), configuration, questionIds.Attending, submittedAt, booleanValue: true);
            third.AddAnswer(Guid.NewGuid(), configuration, questionIds.Count, submittedAt, numberValue: 1m);
            third.AddAnswer(Guid.NewGuid(), configuration, mealQuestionId, submittedAt,
                selectedOptionIds: [currentMealOptionId]);
            thirdSubmissionId = third.Id;
            db.RsvpSubmissions.Add(third);
            await db.SaveChangesAsync();
        }

        await using (var scope = h.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICreatorRsvpResultsService>();
            var firstPage = await service.ListAsync(h.Account, h.Invitation, page: 1, pageSize: 1, default);
            Assert.Equal(CreatorRsvpResultsOutcome.Succeeded, firstPage.Outcome);
            Assert.Equal(thirdSubmissionId, Assert.Single(firstPage.Page!.Submissions).SubmissionId);
            Assert.Equal(3, firstPage.Page.TotalCount);
            Assert.Equal(3, firstPage.Page.Summary.ResponseCount);
            Assert.Equal(6m, firstPage.Page.Summary.TotalParticipants);
            Assert.True(firstPage.Page.Summary.ParticipantCountAvailable);
            var yesNo = Assert.Single(firstPage.Page.Summary.Questions, item => item.QuestionId == questionIds.Attending);
            Assert.Equal(3, yesNo.AnsweredCount);
            Assert.Equal(2, Assert.Single(yesNo.ValueCounts, item => item.Value == "true").Count);
            Assert.Equal(1, Assert.Single(yesNo.ValueCounts, item => item.Value == "false").Count);
            var mealSummaries = firstPage.Page.Summary.Questions.Where(item => item.QuestionId == mealQuestionId).ToArray();
            Assert.Equal(2, mealSummaries.Length);
            var oldMealSnapshot = Assert.Single(mealSummaries, item => item.Prompt == "Meal");
            Assert.Equal(2, Assert.Single(oldMealSnapshot.ValueCounts,
                item => item.Value == mealOptionId.ToString() && item.Label == "Vegetarian").Count);
            var currentMealSnapshot = Assert.Single(mealSummaries, item => item.Prompt == "Food preference");
            Assert.Equal(1, Assert.Single(currentMealSnapshot.ValueCounts,
                item => item.Value == currentMealOptionId.ToString() && item.Label == "Vegan").Count);

            var configurationService = scope.ServiceProvider.GetRequiredService<ICreatorRsvpConfigurationService>();
            var currentConfiguration = (await configurationService.GetAsync(h.Account, h.Invitation, default)).Configuration!;
            Assert.Equal(CreatorRsvpConfigurationOutcome.Succeeded,
                (await configurationService.SetEnabledAsync(h.Account, h.Invitation,
                    new SetRsvpEnabledRequest(currentConfiguration.Revision, false), default)).Outcome);
            Assert.Equal(3, (await service.ListAsync(h.Account, h.Invitation, 1, 1, default))
                .Page!.Summary.ResponseCount);

            var secondPage = await service.ListAsync(h.Account, h.Invitation, page: 2, pageSize: 1, default);
            Assert.Equal(secondSubmissionId, Assert.Single(secondPage.Page!.Submissions).SubmissionId);
            var thirdPage = await service.ListAsync(h.Account, h.Invitation, page: 3, pageSize: 1, default);
            Assert.Equal(firstSubmissionId, Assert.Single(thirdPage.Page!.Submissions).SubmissionId);
            Assert.Equal(CreatorRsvpResultsOutcome.NotFound,
                (await service.ListAsync(Guid.NewGuid(), h.Invitation, 1, 20, default)).Outcome);
            Assert.Equal(CreatorRsvpResultsOutcome.NotFound,
                (await service.GetAsync(h.Account, h.Invitation, Guid.NewGuid(), default)).Outcome);
            Assert.Equal(CreatorRsvpResultsOutcome.NotFound,
                (await service.GetAsync(Guid.NewGuid(), h.Invitation, firstSubmissionId, default)).Outcome);

            var detail = await service.GetAsync(h.Account, h.Invitation, firstSubmissionId, default);
            Assert.Equal(CreatorRsvpResultsOutcome.Succeeded, detail.Outcome);
            var mealAnswer = Assert.Single(detail.Submission!.Answers, item => item.QuestionId == mealQuestionId);
            var selectedOption = Assert.Single(mealAnswer.SelectedOptions);
            Assert.Equal(mealOptionId, selectedOption.OptionId);
            Assert.Equal("Vegetarian", selectedOption.Label);

            using (var httpList = await creator.GetAsync($"/api/v1/invitations/{h.Invitation}/rsvp/submissions?page=1&pageSize=1"))
            {
                Assert.Equal(HttpStatusCode.OK, httpList.StatusCode);
                Assert.True(httpList.Headers.CacheControl?.NoStore);
                using var body = JsonDocument.Parse(await httpList.Content.ReadAsStringAsync());
                Assert.Equal(3, body.RootElement.GetProperty("summary").GetProperty("responseCount").GetInt32());
                Assert.Equal(6m, body.RootElement.GetProperty("summary").GetProperty("totalParticipants").GetDecimal());
            }
            using (var httpDetail = await creator.GetAsync($"/api/v1/invitations/{h.Invitation}/rsvp/submissions/{firstSubmissionId}"))
            {
                Assert.Equal(HttpStatusCode.OK, httpDetail.StatusCode);
                Assert.True(httpDetail.Headers.CacheControl?.NoStore);
            }
            var csrf = await creator.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
            using (var deleteRequest = new HttpRequestMessage(HttpMethod.Delete,
                       $"/api/v1/invitations/{h.Invitation}/rsvp/submissions/{firstSubmissionId}"))
            {
                deleteRequest.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
                using var deleteResponse = await creator.SendAsync(deleteRequest);
                Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
                Assert.True(deleteResponse.Headers.CacheControl?.NoStore);
            }
            Assert.Equal(CreatorRsvpResultsOutcome.NotFound,
                (await service.GetAsync(h.Account, h.Invitation, firstSubmissionId, default)).Outcome);
            var afterDelete = await service.ListAsync(h.Account, h.Invitation, 1, 20, default);
            Assert.Equal(2, afterDelete.Page!.Summary.ResponseCount);
            Assert.Equal(4m, afterDelete.Page.Summary.TotalParticipants);
        }

        await using (var verify = h.Db())
        {
            Assert.False(await verify.RsvpSubmissions.AnyAsync(item => item.Id == firstSubmissionId));
            Assert.False(await verify.RsvpAnswers.AnyAsync(item => item.SubmissionId == firstSubmissionId));
            Assert.False(await verify.RsvpAnswerOptions.AnyAsync(item => firstAnswerOptionIds.Contains(item.Id)));
            Assert.False(await verify.RsvpManageCapabilities.AnyAsync(item => capabilityIds.Take(2).Contains(item.Id)));
            Assert.True(await verify.RsvpManageCapabilities.AnyAsync(item => capabilityIds.Skip(2).Contains(item.Id)));
        }

        using var guest = h.CreateClient();
        var privatePath = $"/api/v1/invitations/{h.Invitation}/rsvp/submissions";
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(privatePath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.DeleteAsync(privatePath + "/" + secondSubmissionId)).StatusCode);
        using (var admin = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }))
        {
            admin.DefaultRequestHeaders.Add("X-Test-SuperAdmin", "true");
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(privatePath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await admin.GetAsync(privatePath + "/" + secondSubmissionId)).StatusCode);
            var adminCsrf = await admin.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
            using var adminDelete = new HttpRequestMessage(HttpMethod.Delete, privatePath + "/" + secondSubmissionId);
            adminDelete.Headers.Add("X-CSRF-TOKEN", adminCsrf!.Token);
            using var adminDeleteResponse = await admin.SendAsync(adminDelete);
            Assert.True(adminDeleteResponse.StatusCode == HttpStatusCode.Forbidden,
                $"Expected SuperAdmin delete to be forbidden, got {(int)adminDeleteResponse.StatusCode}: {await adminDeleteResponse.Content.ReadAsStringAsync()}");
        }
        using (var ownerScope = h.Services.CreateAsyncScope())
        {
            await using var db = h.Db();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE invitations SET deleted_at = {Now} WHERE id = {h.Invitation}");
            var service = ownerScope.ServiceProvider.GetRequiredService<ICreatorRsvpResultsService>();
            Assert.Equal(CreatorRsvpResultsOutcome.NotFound,
                (await service.ListAsync(h.Account, h.Invitation, 1, 25, default)).Outcome);
        }
    }

    [Fact]
    public async Task Creator_RSVP_account_limits_share_read_bucket_separate_writes_and_partition_other_accounts()
    {
        await using var h = await CreateAsync(creatorRsvpReadAccountRateLimit: 2, creatorRsvpWriteAccountRateLimit: 1,
            creatorRsvpReadIpRateLimit: 100, creatorRsvpWriteIpRateLimit: 100);
        using var creator = await h.CreateAuthenticatedCreatorAsync();
        var path = $"/api/v1/invitations/{h.Invitation}/rsvp";
        using (var firstRead = await creator.GetAsync(path))
            Assert.Equal(HttpStatusCode.OK, firstRead.StatusCode);
        using (var nextRead = await creator.GetAsync(path + "/submissions"))
            Assert.Equal(HttpStatusCode.OK, nextRead.StatusCode);
        using (var throttledRead = await creator.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, throttledRead.StatusCode);
            Assert.True(throttledRead.Headers.CacheControl?.NoStore);
        }

        var csrf = await creator.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
        using var write = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(new { expectedRevision = 0, isEnabled = false })
        };
        write.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf!.Token);
        using (var firstWrite = await creator.SendAsync(write))
            Assert.Equal(HttpStatusCode.OK, firstWrite.StatusCode);

        var secondCsrf = await creator.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
        using var secondWriteRequest = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(new { expectedRevision = 1, isEnabled = false })
        };
        secondWriteRequest.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", secondCsrf!.Token);
        using (var secondWrite = await creator.SendAsync(secondWriteRequest))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, secondWrite.StatusCode);
            Assert.True(secondWrite.Headers.CacheControl?.NoStore);
        }

        var (otherAccount, otherInvitation, otherEmail) = await h.CreateSecondCreatorAsync();
        using var other = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using (var login = await other.PostAsJsonAsync("/api/v1/auth/login", new { email = otherEmail, password = Password }))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var otherRead = await other.GetAsync($"/api/v1/invitations/{otherInvitation}/rsvp");
        Assert.Equal(HttpStatusCode.OK, otherRead.StatusCode);
        using var foreignRead = await other.GetAsync($"/api/v1/invitations/{h.Invitation}/rsvp");
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.NotEqual(h.Account, otherAccount);
    }

    [Fact]
    public async Task Creator_RSVP_ip_policy_returns_private_429_as_an_abuse_layer()
    {
        await using var h = await CreateAsync(creatorRsvpReadAccountRateLimit: 100,
            creatorRsvpReadIpRateLimit: 1, creatorRsvpWriteIpRateLimit: 100);
        using var creator = await h.CreateAuthenticatedCreatorAsync();
        var path = $"/api/v1/invitations/{h.Invitation}/rsvp";
        using (var first = await creator.GetAsync(path))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var throttled = await creator.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
            Assert.True(throttled.Headers.CacheControl?.NoStore);
            Assert.Equal("no-cache", Assert.Single(throttled.Headers.GetValues("Pragma")));
        }
    }

    [Fact]
    public async Task Creator_RSVP_write_ip_policy_returns_private_429_after_configured_limit()
    {
        await using var h = await CreateAsync(creatorRsvpReadAccountRateLimit: 100,
            creatorRsvpWriteAccountRateLimit: 100, creatorRsvpReadIpRateLimit: 100,
            creatorRsvpWriteIpRateLimit: 1);
        using var creator = await h.CreateAuthenticatedCreatorAsync();
        var path = $"/api/v1/invitations/{h.Invitation}/rsvp";

        async Task<HttpResponseMessage> UpdateAsync(long revision)
        {
            var csrf = await creator.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
            using var request = new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = JsonContent.Create(new { expectedRevision = revision, isEnabled = false })
            };
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf!.Token);
            return await creator.SendAsync(request);
        }

        using (var first = await UpdateAsync(0))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var throttled = await UpdateAsync(1))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
            Assert.True(throttled.Headers.CacheControl?.NoStore);
            Assert.Equal("no-cache", Assert.Single(throttled.Headers.GetValues("Pragma")));
        }
    }

    [Fact]
    public async Task Guest_RSVP_capability_reads_own_answers_and_updates_with_atomic_token_rotation()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        var questionIds = await h.EnableRsvp();
        using var guest = h.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var formPath = $"/api/v1/public/invitations/{h.Code}/rsvp";
        using (var form = await guest.GetAsync(formPath))
        {
            Assert.Equal(HttpStatusCode.OK, form.StatusCode);
            using var publicForm = JsonDocument.Parse(await form.Content.ReadAsStringAsync());
            var participantCount = Assert.Single(publicForm.RootElement.GetProperty("questions").EnumerateArray(),
                question => question.GetProperty("prompt").GetString() == "Guests");
            Assert.Equal(0, participantCount.GetProperty("minimumNumberValue").GetDecimal());
            Assert.Equal(20, participantCount.GetProperty("maximumNumberValue").GetDecimal());
            Assert.False(participantCount.TryGetProperty("semanticRole", out _));
        }

        var csrf = await guest.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
        var firstAnswers = MakeAnswers(questionIds);
        var submitPath = formPath + "/submissions";
        using var created = await PostRsvp(guest, submitPath, firstAnswers, csrf!.Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var submissionId = createdJson.RootElement.GetProperty("submissionId").GetGuid();
        var oldCookie = Assert.Single(created.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("__Host-davetiye-rsvp=", StringComparison.Ordinal));
        var oldToken = oldCookie.Split(';', 2)[0]["__Host-davetiye-rsvp=".Length..];

        var submissionPath = $"{submitPath}/{submissionId:D}";
        await AssertGuestCapabilityCannotReadAnotherSubmissionAsync(h, questionIds, oldToken);
        await AssertGuestCapabilityCannotReadAnotherInvitationAsync(oldToken);

        using var ownRequest = new HttpRequestMessage(HttpMethod.Get, submissionPath);
        ownRequest.Headers.TryAddWithoutValidation("Cookie", $"__Host-davetiye-rsvp={oldToken}");
        using (var ownSubmission = await guest.SendAsync(ownRequest))
        {
            Assert.Equal(HttpStatusCode.OK, ownSubmission.StatusCode);
            AssertHeaders(ownSubmission);
            using var json = JsonDocument.Parse(await ownSubmission.Content.ReadAsStringAsync());
            Assert.Equal(submissionId, json.RootElement.GetProperty("submissionId").GetGuid());
            Assert.Equal("Ada", Assert.Single(json.RootElement.GetProperty("answers").EnumerateArray(), answer =>
                answer.GetProperty("questionId").GetGuid() == questionIds.Name).GetProperty("textValue").GetString());
            Assert.DoesNotContain("manageToken", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }

        var replacement = new SubmitPublicRsvpRequest([
            new(questionIds.Name, TextValue: "Grace"),
            new(questionIds.Attending, BooleanValue: false),
            new(questionIds.Count, NumberValue: 0)
        ]);
        using var updated = await PutRsvp(guest, submissionPath, replacement, csrf.Token, oldToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updateBody = await updated.Content.ReadAsStringAsync();
        Assert.DoesNotContain("manageToken", updateBody, StringComparison.OrdinalIgnoreCase);
        using var updateJson = JsonDocument.Parse(updateBody);
        Assert.Equal(submissionId, updateJson.RootElement.GetProperty("submissionId").GetGuid());
        Assert.True(updated.Headers.TryGetValues("Set-Cookie", out var rotatedCookies));
        var rotatedCookie = Assert.Single(rotatedCookies!, value =>
            value.StartsWith("__Host-davetiye-rsvp=", StringComparison.Ordinal));
        var rotatedToken = rotatedCookie.Split(';', 2)[0]["__Host-davetiye-rsvp=".Length..];
        Assert.NotEqual(oldToken, rotatedToken);

        using var refreshedRequest = new HttpRequestMessage(HttpMethod.Get, submissionPath);
        refreshedRequest.Headers.TryAddWithoutValidation("Cookie", $"__Host-davetiye-rsvp={rotatedToken}");
        using (var refreshed = await guest.SendAsync(refreshedRequest))
        {
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            using var json = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
            Assert.Equal("Grace", Assert.Single(json.RootElement.GetProperty("answers").EnumerateArray(), answer =>
                answer.GetProperty("questionId").GetGuid() == questionIds.Name).GetProperty("textValue").GetString());
        }
        using (var foreignSubmission = await guest.GetAsync($"{submitPath}/{Guid.NewGuid():D}"))
            Assert.Equal(HttpStatusCode.NotFound, foreignSubmission.StatusCode);
        using (var missingCookieClient = h.CreateClient())
            Assert.Equal(HttpStatusCode.NotFound, (await missingCookieClient.GetAsync(submissionPath)).StatusCode);
        using (var staleCookieClient = h.CreateClient())
        using (var staleRequest = new HttpRequestMessage(HttpMethod.Get, submissionPath))
        {
            staleRequest.Headers.TryAddWithoutValidation("Cookie", $"__Host-davetiye-rsvp={oldToken}");
            using var staleRead = await staleCookieClient.SendAsync(staleRequest);
            Assert.Equal(HttpStatusCode.NotFound, staleRead.StatusCode);
        }
        using (var staleUpdate = await PutRsvp(guest, submissionPath, replacement, csrf.Token, oldToken))
            Assert.Equal(HttpStatusCode.NotFound, staleUpdate.StatusCode);

        var concurrentOne = PutRsvp(guest, submissionPath, replacement, csrf.Token, rotatedToken);
        var concurrentTwo = PutRsvp(guest, submissionPath, replacement, csrf.Token, rotatedToken);
        var concurrentResults = await Task.WhenAll(concurrentOne, concurrentTwo);
        Assert.Single(concurrentResults, result => result.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrentResults, result => result.StatusCode == HttpStatusCode.NotFound);
        foreach (var result in concurrentResults) result.Dispose();

        await using var db = h.Db();
        var submission = await db.RsvpSubmissions.Include(item => item.Answers)
            .SingleAsync(item => item.Id == submissionId);
        Assert.Equal("Grace", Assert.Single(submission.Answers, answer => answer.QuestionId == questionIds.Name).TextValue);
        var capabilities = await db.RsvpManageCapabilities.Where(item => item.SubmissionId == submissionId).ToArrayAsync();
        Assert.Equal(3, capabilities.Length);
        Assert.Single(capabilities, item => item.RevokedAt is null);
        Assert.Equal(2, capabilities.Count(item => item.RevokedAt is not null));
        Assert.All(capabilities, item => Assert.Equal(32, item.HmacDigest.Length));
    }

    private async Task AssertGuestCapabilityCannotReadAnotherSubmissionAsync(
        Harness invitation, (Guid Name, Guid Attending, Guid Count) questionIds, string capability)
    {
        using var otherGuest = invitation.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var path = $"/api/v1/public/invitations/{invitation.Code}/rsvp";
        var csrf = await otherGuest.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
        using var submitted = await PostRsvp(otherGuest, path + "/submissions", MakeAnswers(questionIds), csrf!.Token);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        using var body = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
        var otherSubmissionId = body.RootElement.GetProperty("submissionId").GetGuid();

        using var capabilityProbe = invitation.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}/submissions/{otherSubmissionId:D}");
        request.Headers.TryAddWithoutValidation("Cookie", $"__Host-davetiye-rsvp={capability}");
        using var response = await capabilityProbe.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task AssertGuestCapabilityCannotReadAnotherInvitationAsync(string capability)
    {
        await using var otherInvitation = await CreateAsync();
        await otherInvitation.Publish();
        var otherQuestionIds = await otherInvitation.EnableRsvp();
        using var otherGuest = otherInvitation.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var path = $"/api/v1/public/invitations/{otherInvitation.Code}/rsvp";
        var csrf = await otherGuest.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
        using var submitted = await PostRsvp(otherGuest, path + "/submissions", MakeAnswers(otherQuestionIds), csrf!.Token);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        using var body = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
        var otherSubmissionId = body.RootElement.GetProperty("submissionId").GetGuid();

        using var capabilityProbe = otherInvitation.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{path}/submissions/{otherSubmissionId:D}");
        request.Headers.TryAddWithoutValidation("Cookie", $"__Host-davetiye-rsvp={capability}");
        using var response = await capabilityProbe.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("paused")]
    [InlineData("scheduled")]
    [InlineData("unsupported-template")]
    public async Task Public_RSVP_is_unavailable_when_disabled_or_effective_module_gate_fails(string gate)
    {
        await using var h = await CreateAsync();
        await h.Publish(scheduled: gate == "scheduled");
        var questions = await h.EnableRsvp(enabled: gate != "disabled");
        if (gate == "paused") await h.Command("pause");
        if (gate == "unsupported-template")
        {
            await using var db = h.Db();
            var template = await db.TemplateDefinitions.SingleAsync(item => item.Key == "zamansiz-dugun");
            template.UpdateMetadata(template.Name, template.Category, template.IsPremium, template.PreviewImageUrl,
                "[\"hero\"]", template.RequiredFields, template.RecommendedFields);
            await db.SaveChangesAsync();
        }

        using var guest = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var path = $"/api/v1/public/invitations/{h.Code}/rsvp";
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(path)).StatusCode);
        var csrf = await guest.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
        var body = MakeAnswers(questions);
        using var submit = await PostRsvp(guest, path + "/submissions", body, csrf!.Token);
        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
        await using var verify = h.Db();
        Assert.Empty(await verify.RsvpSubmissions.ToListAsync());
    }

    [Fact]
    public async Task Public_RSVP_submission_per_ip_policy_returns_429_after_configured_limit()
    {
        await using var h = await CreateAsync(rsvpRateLimit: 1);
        await h.Publish();
        var questions = await h.EnableRsvp();
        using var guest = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var path = $"/api/v1/public/invitations/{h.Code}/rsvp/submissions";
        var csrf = await guest.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
        using var first = await PostRsvp(guest, path, MakeAnswers(questions), csrf!.Token);
        using var second = await PostRsvp(guest, path, MakeAnswers(questions), csrf.Token);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        await using var verify = h.Db();
        Assert.Equal(1, await verify.RsvpSubmissions.CountAsync(item => item.InvitationId == h.Invitation));
    }

    [Fact]
    public async Task Concurrent_public_RSVP_submissions_cannot_exceed_configured_plan_quota()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        var questions = await h.EnableRsvp();
        await using (var db = h.Db())
        {
            var grant = await db.AccountPlanGrants.SingleAsync(item => item.AssignedInvitationId == h.Invitation);
            var quota = await db.PlanEntitlements.SingleAsync(item => item.PlanId == grant.PlanId &&
                item.EntitlementKey == "maxRSVPResponses");
            quota.UpdateValue(1, null);
            await db.SaveChangesAsync();
        }
        using var guest = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var path = $"/api/v1/public/invitations/{h.Code}/rsvp/submissions";
        var csrf = await guest.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");
        var firstTask = PostRsvp(guest, path, MakeAnswers(questions), csrf!.Token);
        var secondTask = PostRsvp(guest, path, MakeAnswers(questions), csrf.Token);
        using var first = await firstTask;
        using var second = await secondTask;
        Assert.Equal(1, new[] { first.StatusCode, second.StatusCode }.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(1, new[] { first.StatusCode, second.StatusCode }.Count(status => status == HttpStatusCode.Conflict));
        await using var verify = h.Db();
        Assert.Equal(1, await verify.RsvpSubmissions.CountAsync(item => item.InvitationId == h.Invitation));
    }

    [Fact]
    public async Task Creator_RSVP_question_writes_enforce_prompt_label_option_and_active_question_boundaries()
    {
        await using var h = await CreateAsync();
        await using var scope = h.Services.CreateAsyncScope();
        var creator = scope.ServiceProvider.GetRequiredService<ICreatorRsvpConfigurationService>();
        var revision = (await creator.GetAsync(h.Account, h.Invitation, default)).Configuration!.Revision;

        var prompt200 = await creator.AddQuestionAsync(h.Account, h.Invitation,
            new SaveRsvpQuestionRequest(revision, new string('p', 200), "ShortText", false, null, null), default);
        Assert.Equal(CreatorRsvpConfigurationOutcome.Succeeded, prompt200.Outcome);
        revision = prompt200.Configuration!.Revision;
        var prompt201 = await creator.AddQuestionAsync(h.Account, h.Invitation,
            new SaveRsvpQuestionRequest(revision, new string('p', 201), "ShortText", false, null, null), default);
        Assert.Equal(CreatorRsvpConfigurationOutcome.Invalid, prompt201.Outcome);
        Assert.Equal(revision, prompt201.Configuration?.Revision ?? revision);

        var choice20 = Enumerable.Range(0, 20)
            .Select(index => new RsvpQuestionOptionInput(null, new string('o', 100), index)).ToArray();
        var optionsBoundary = await creator.AddQuestionAsync(h.Account, h.Invitation,
            new SaveRsvpQuestionRequest(revision, "Choices", "SingleChoice", false, null, choice20), default);
        Assert.Equal(CreatorRsvpConfigurationOutcome.Succeeded, optionsBoundary.Outcome);
        revision = optionsBoundary.Configuration!.Revision;

        var option21 = Enumerable.Range(0, 21)
            .Select(index => new RsvpQuestionOptionInput(null, "Choice", index)).ToArray();
        var tooManyOptions = await creator.AddQuestionAsync(h.Account, h.Invitation,
            new SaveRsvpQuestionRequest(revision, "Too many", "SingleChoice", false, null, option21), default);
        Assert.Equal(CreatorRsvpConfigurationOutcome.Invalid, tooManyOptions.Outcome);
        var longOption = new[] { new RsvpQuestionOptionInput(null, new string('o', 101), 0) };
        var option101 = await creator.AddQuestionAsync(h.Account, h.Invitation,
            new SaveRsvpQuestionRequest(revision, "Long label", "SingleChoice", false, null, longOption), default);
        Assert.Equal(CreatorRsvpConfigurationOutcome.Invalid, option101.Outcome);

        var activeCount = optionsBoundary.Configuration!.Questions.Count;
        while (activeCount < 20)
        {
            var next = await creator.AddQuestionAsync(h.Account, h.Invitation,
                new SaveRsvpQuestionRequest(revision, $"Question {activeCount}", "ShortText", false, null, null), default);
            Assert.Equal(CreatorRsvpConfigurationOutcome.Succeeded, next.Outcome);
            revision = next.Configuration!.Revision;
            activeCount = next.Configuration.Questions.Count;
        }
        var question21 = await creator.AddQuestionAsync(h.Account, h.Invitation,
            new SaveRsvpQuestionRequest(revision, "Question 21", "ShortText", false, null, null), default);
        Assert.Equal(CreatorRsvpConfigurationOutcome.Invalid, question21.Outcome);
        Assert.Equal(20, (await creator.GetAsync(h.Account, h.Invitation, default)).Configuration!.Questions.Count);
    }

    [Fact]
    public async Task Guest_submission_enforces_answer_boundaries_and_preserves_decimal_range_for_unmarked_numbers()
    {
        await using var h = await CreateAsync(rsvpRateLimit: 20);
        var defaults = await h.EnableRsvp();
        Guid longTextId;
        Guid multiChoiceId;
        Guid genericNumberId;
        Guid[] choiceIds;
        await using (var scope = h.Services.CreateAsyncScope())
        {
            var creator = scope.ServiceProvider.GetRequiredService<ICreatorRsvpConfigurationService>();
            var revision = (await creator.GetAsync(h.Account, h.Invitation, default)).Configuration!.Revision;
            var longText = await creator.AddQuestionAsync(h.Account, h.Invitation,
                new SaveRsvpQuestionRequest(revision, "Long note", "LongText", false, null, null), default);
            Assert.Equal(CreatorRsvpConfigurationOutcome.Succeeded, longText.Outcome);
            longTextId = longText.Configuration!.Questions.Single(question => question.Prompt == "Long note").Id;
            revision = longText.Configuration.Revision;
            var options = Enumerable.Range(0, 20).Select(index =>
                new RsvpQuestionOptionInput(null, $"Choice {index}", index)).ToArray();
            var multiple = await creator.AddQuestionAsync(h.Account, h.Invitation,
                new SaveRsvpQuestionRequest(revision, "Select up to ten", "MultipleChoice", false, null, options), default);
            Assert.Equal(CreatorRsvpConfigurationOutcome.Succeeded, multiple.Outcome);
            var choiceQuestion = multiple.Configuration!.Questions.Single(question => question.Prompt == "Select up to ten");
            multiChoiceId = choiceQuestion.Id;
            choiceIds = choiceQuestion.Options.Select(option => option.Id).ToArray();
            var genericNumber = await creator.AddQuestionAsync(h.Account, h.Invitation,
                new SaveRsvpQuestionRequest(multiple.Configuration.Revision, "Decimal", "Number", false, null, null), default);
            Assert.Equal(CreatorRsvpConfigurationOutcome.Succeeded, genericNumber.Outcome);
            genericNumberId = genericNumber.Configuration!.Questions.Single(question => question.Prompt == "Decimal").Id;
        }
        await h.Publish();
        using var guest = h.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var path = $"/api/v1/public/invitations/{h.Code}/rsvp/submissions";
        var csrf = await guest.GetFromJsonAsync<CsrfResponse>("/api/v1/antiforgery/token");

        async Task<HttpResponseMessage> Submit(string shortText, string longText, decimal participantCount,
            int selectedCount, decimal genericNumber = 1m)
        {
            var answer = new SubmitPublicRsvpRequest([
                new(defaults.Name, TextValue: shortText),
                new(defaults.Attending, BooleanValue: true),
                new(defaults.Count, NumberValue: participantCount),
                new(longTextId, TextValue: longText),
                new(multiChoiceId, SelectedOptionIds: choiceIds.Take(selectedCount).ToArray()),
                new(genericNumberId, NumberValue: genericNumber)
            ]);
            return await PostRsvp(guest, path, answer, csrf!.Token);
        }

        using (var zeroParticipants = await Submit(new string('s', 200), new string('l', 2_000), 0, 10, decimal.MaxValue))
            Assert.Equal(HttpStatusCode.Created, zeroParticipants.StatusCode);
        using var maxParticipants = await Submit(new string('s', 200), new string('l', 2_000), 20, 10, decimal.MinValue);
        Assert.Equal(HttpStatusCode.Created, maxParticipants.StatusCode);
        using var created = JsonDocument.Parse(await maxParticipants.Content.ReadAsStringAsync());
        var submissionId = created.RootElement.GetProperty("submissionId").GetGuid();
        var manageCookie = Assert.Single(maxParticipants.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("__Host-davetiye-rsvp=", StringComparison.Ordinal));
        var manageToken = manageCookie.Split(';', 2)[0]["__Host-davetiye-rsvp=".Length..];
        using var ownRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/public/invitations/{h.Code}/rsvp/submissions/{submissionId:D}");
        ownRequest.Headers.TryAddWithoutValidation("Cookie", $"__Host-davetiye-rsvp={manageToken}");
        using var ownSubmission = await guest.SendAsync(ownRequest);
        Assert.Equal(HttpStatusCode.OK, ownSubmission.StatusCode);
        using var ownJson = JsonDocument.Parse(await ownSubmission.Content.ReadAsStringAsync());
        var exactNumber = Assert.Single(ownJson.RootElement.GetProperty("answers").EnumerateArray(), answer =>
            answer.GetProperty("questionId").GetGuid() == genericNumberId).GetProperty("numberValue");
        Assert.Equal(JsonValueKind.String, exactNumber.ValueKind);
        Assert.Equal("-79228162514264337593543950335", exactNumber.GetString());
        foreach (var invalid in new[]
        {
            await Submit(new string('s', 201), "ok", 1, 1),
            await Submit("ok", new string('l', 2_001), 1, 1),
            await Submit("ok", "ok", -1, 1),
            await Submit("ok", "ok", 21, 1),
            await Submit("ok", "ok", 1.5m, 1),
            await Submit("ok", "ok", 1, 11),
        })
        {
            using (invalid) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        await using var verify = h.Db();
        Assert.Equal(2, await verify.RsvpSubmissions.CountAsync(item => item.InvitationId == h.Invitation));
    }

    private static SubmitPublicRsvpRequest MakeAnswers((Guid Name, Guid Attending, Guid Count) questionIds) => new([
        new(questionIds.Name, TextValue: "Ada"),
        new(questionIds.Attending, BooleanValue: true),
        new(questionIds.Count, NumberValue: 2)
    ]);

    private static async Task<HttpResponseMessage> PostRsvp(HttpClient client, string path,
        SubmitPublicRsvpRequest request, string? token)
        => await PostRawRsvp(client, path, JsonSerializer.Serialize(request), token);

    private static async Task<HttpResponseMessage> PostRawRsvp(HttpClient client, string path, string json, string? token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        message.Headers.TryAddWithoutValidation("Origin", "https://allowed.example.test");
        if (token is not null) message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
        return await client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> PutRsvp(HttpClient client, string path,
        SubmitPublicRsvpRequest request, string token, string manageToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, path)
        { Content = JsonContent.Create(request) };
        message.Headers.TryAddWithoutValidation("Origin", "https://allowed.example.test");
        message.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
        message.Headers.TryAddWithoutValidation("Cookie", $"__Host-davetiye-rsvp={manageToken}");
        return await client.SendAsync(message);
    }

    private sealed record CsrfResponse(string Token);

    [Theory]
    [InlineData("Draft")]
    [InlineData("Scheduled")]
    [InlineData("Paused")]
    [InlineData("Expired")]
    [InlineData("Banned")]
    [InlineData("Unverified")]
    [InlineData("MissingOwner")]
    [InlineData("Revoked")]
    public async Task Inactive_and_account_or_grant_denials_return_the_identical_PII_free_shell(string denial)
    {
        await using var h = await CreateAsync(acknowledgeServiceNotice: denial != "MissingOwner");
        if (denial != "Draft") await h.Publish(denial == "Scheduled");
        if (denial == "Paused") await h.Command("pause");
        if (denial == "Expired") h.Clock.Current = Now.AddDays(1);
        await using (var db = h.Db())
        {
            if (denial == "Banned") db.BanRecords.Add(BanRecord.Create(Guid.NewGuid(), h.Account, "Guest test ban", Now, Guid.NewGuid()));
            if (denial == "Unverified") (await db.Users.SingleAsync()).EmailConfirmed = false;
            if (denial == "MissingOwner") db.Accounts.Remove(await db.Accounts.SingleAsync());
            if (denial == "Revoked") (await db.AccountPlanGrants.SingleAsync()).Revoke(Now);
            await db.SaveChangesAsync();
        }
        using var guest = h.CreateClient();
        var response = await guest.GetAsync(h.PublicPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"unavailable\"}", await response.Content.ReadAsStringAsync());
        AssertHeaders(response);
    }

    [Fact]
    public async Task Scheduled_boundaries_are_authoritative_without_worker_and_public_GET_never_consumes_grant()
    {
        await using var h = await CreateAsync();
        var scheduled = await h.Publish(true);
        using var guest = h.CreateClient();
        h.Clock.Current = scheduled.CurrentWindow!.StartsAtUtc.AddTicks(-1);
        Assert.Equal("{\"status\":\"unavailable\"}", await guest.GetStringAsync(h.PublicPath));
        h.Clock.Current = scheduled.CurrentWindow.StartsAtUtc;
        Assert.Contains(PublicHeadline, await guest.GetStringAsync(h.PublicPath), StringComparison.Ordinal);
        h.Clock.Current = scheduled.CurrentWindow.EndsAtUtc.AddTicks(-1);
        Assert.Contains(PublicHeadline, await guest.GetStringAsync(h.PublicPath), StringComparison.Ordinal);
        h.Clock.Current = scheduled.CurrentWindow.EndsAtUtc;
        Assert.Equal("{\"status\":\"unavailable\"}", await guest.GetStringAsync(h.PublicPath));
        await using var db = h.Db();
        Assert.Equal(InvitationStoredState.Scheduled, (await db.Invitations.SingleAsync()).State);
        Assert.Null((await db.AccountPlanGrants.SingleAsync()).ConsumedAt);
        Assert.Equal(scheduled.Expected.InvitationRevision, (await db.Invitations.SingleAsync()).Revision);
        Assert.Equal(scheduled.Expected.WindowRevision, (await db.PublicationWindows.SingleAsync()).Revision);
    }

    [Theory]
    [InlineData("foreign-account")]
    [InlineData("foreign-assignment")]
    [InlineData("organization")]
    [InlineData("missing-grant")]
    [InlineData("missing-window")]
    [InlineData("missing-snapshot")]
    [InlineData("late-reservation")]
    [InlineData("future-consumption")]
    [InlineData("bad-pin")]
    [InlineData("bad-schema")]
    [InlineData("bad-json-shape")]
    [InlineData("null-program-item")]
    public async Task Inconsistent_grant_snapshot_or_renderer_data_fails_closed(string corruption)
    {
        await using var h = await CreateAsync();
        await h.Publish();
        await using (var db = h.Db())
        {
            switch (corruption)
            {
                case "foreign-account":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE account_plan_grants SET account_id = {Guid.NewGuid()}"); break;
                case "foreign-assignment":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE account_plan_grants SET assigned_invitation_id = {Guid.NewGuid()}"); break;
                case "organization":
                    var org = await db.Plans.SingleAsync(p => p.Key == "organization");
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE account_plan_grants SET source = 'OrganizationSubscription', plan_id = {org.Id}, assigned_invitation_id = NULL, reserved_at = NULL, consumed_at = NULL"); break;
                case "missing-grant":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE publication_windows SET grant_id = {Guid.NewGuid()}"); break;
                case "missing-window": await db.Database.ExecuteSqlRawAsync("DELETE FROM publication_windows"); break;
                case "missing-snapshot": await db.Database.ExecuteSqlRawAsync("DELETE FROM published_contents"); break;
                case "late-reservation":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE account_plan_grants SET reserved_at = {Now.AddHours(1)}, consumed_at = NULL"); break;
                case "future-consumption":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE account_plan_grants SET consumed_at = {Now.AddHours(1)}"); break;
                case "bad-pin": await db.Database.ExecuteSqlRawAsync("UPDATE published_contents SET renderer_version = 999"); break;
                case "bad-schema": await db.Database.ExecuteSqlRawAsync("UPDATE published_contents SET content_schema_version = 999"); break;
                case "bad-json-shape":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE published_contents SET content = CAST({"{\"headline\":42}"} AS jsonb)"); break;
                case "null-program-item":
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE published_contents SET content = CAST({"{\"headline\":\"Sensitive published\",\"timeZoneId\":\"Europe/Istanbul\",\"programItems\":[null]}"} AS jsonb)"); break;
            }
        }
        using var guest = h.CreateClient();
        var response = await guest.GetAsync(h.PublicPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"unavailable\"}", await response.Content.ReadAsStringAsync());
        AssertHeaders(response);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("not-a-public-code")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Unknown_or_malformed_locator_returns_404_without_content(string code)
    {
        await using var h = await CreateAsync();
        using var guest = h.CreateClient();
        var response = await guest.GetAsync(new Uri($"/api/v1/public/invitations/{code}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(PublicHeadline, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        AssertHeaders(response);
    }

    [Fact]
    public async Task Conditional_GET_is_always_rechecked_and_cannot_return_stale_304_after_ban()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        using var guest = h.CreateClient();
        var first = await guest.GetAsync(h.PublicPath);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Null(first.Headers.ETag);
        await using (var db = h.Db())
        {
            db.BanRecords.Add(BanRecord.Create(Guid.NewGuid(), h.Account, "Ban between reads", Now, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, h.PublicPath);
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Headers.IfModifiedSince = Now.AddDays(1);
        var second = await guest.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("{\"status\":\"unavailable\"}", await second.Content.ReadAsStringAsync());
        AssertHeaders(second);
    }

    [Fact]
    public async Task Unknown_private_JSON_fields_have_no_public_projection_slot()
    {
        await using var h = await CreateAsync();
        await h.Publish();
        await using (var db = h.Db())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE published_contents SET content = CAST({Content[..^1] + ",\"secretFutureField\":\"Internal secret\"}"} AS jsonb)");
        using var guest = h.CreateClient();
        var body = await guest.GetStringAsync(h.PublicPath);
        Assert.Contains(PublicHeadline, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Internal secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secretFutureField", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_numeric_premium_and_catalog_downgrades_preserve_accepted_premium_snapshot()
    {
        await using var h = await CreateAsync("romantik-nisan");
        await h.Publish(paid: true);
        await using (var db = h.Db())
        {
            var plan = await db.Plans.SingleAsync(p => p.Key == "premium");
            foreach (var entitlement in await db.PlanEntitlements.Where(e => e.PlanId == plan.Id).ToListAsync())
                if (entitlement.NumericValue is not null) entitlement.UpdateValue(0, null);
                else entitlement.UpdateValue(null, false);
            plan.SetActive(false);
            (await db.TemplateDefinitions.SingleAsync(t => t.Key == "romantik-nisan")).SetActive(false);
            await db.SaveChangesAsync();
        }
        using var guest = h.CreateClient();
        var response = await guest.GetAsync(h.PublicPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(PublicHeadline, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        AssertHeaders(response);
    }

    [Fact]
    public async Task Public_locator_grants_no_creator_read_or_mutation_authority()
    {
        await using var h = await CreateAsync();
        var status = await h.Publish();
        using var guest = h.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(h.PublicPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(new Uri($"/api/v1/invitations/{h.Invitation}/publication", UriKind.Relative))).StatusCode);
        var action = await guest.PostAsJsonAsync(new Uri($"/api/v1/invitations/{h.Invitation}/publication/actions", UriKind.Relative), new PublicationActionRequest("pause", status.Expected));
        Assert.Equal(HttpStatusCode.Unauthorized, action.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(new Uri($"/api/v1/invitations/{h.Code}/publication", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Public_rate_limit_returns_429_with_private_cache_and_indexing_headers()
    {
        await using var h = await CreateAsync(rateLimit: 2);
        using var guest = h.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(h.PublicPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(h.PublicPath)).StatusCode);
        var throttled = await guest.GetAsync(h.PublicPath);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        AssertHeaders(throttled);
        Assert.DoesNotContain(PublicHeadline, await throttled.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_grant_read_uses_one_final_clock_for_window_end_and_future_revocation(bool revoke)
    {
        await using var h = await CreateAsync(blockGrantRead: true);
        var published = await h.Publish();
        var boundary = revoke ? Now.AddHours(1) : published.CurrentWindow!.EndsAtUtc;
        if (revoke)
        {
            await using var db = h.Db();
            (await db.AccountPlanGrants.SingleAsync()).Revoke(boundary);
            await db.SaveChangesAsync();
        }
        using var guest = h.CreateClient();
        var task = guest.GetAsync(h.PublicPath);
        await h.ReadBlock.ReadReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Clock.Current = boundary;
        h.ReadBlock.Release.TrySetResult();
        var response = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"unavailable\"}", await response.Content.ReadAsStringAsync());
        AssertHeaders(response);
    }

    [Fact]
    public async Task Deleted_overlay_returns_public_404_and_excludes_all_creator_reads_commands_and_quota()
    {
        await using var h = await CreateAsync();
        var accepted = await h.Publish();
        await using (var db = h.Db())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE invitations SET deleted_at = {Now} WHERE id = {h.Invitation}");
        using var guest = h.CreateClient();
        var gone = await guest.GetAsync(h.PublicPath);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        AssertHeaders(gone);
        await using (var scope = h.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IInvitationDraftService>();
            var lifecycle = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            Assert.Equal(0, (await drafts.ListAsync(h.Account, 1, 20, default)).TotalCount);
            Assert.Equal(InvitationDraftOutcome.NotFound, (await drafts.GetAsync(h.Account, h.Invitation, default)).Outcome);
            Assert.Null(await drafts.GetValidationAsync(h.Account, h.Invitation, default));
            Assert.Equal(InvitationDraftOutcome.NotFound, (await drafts.AutosaveAsync(h.Account, h.Invitation, new(1, new DraftContentInput { Headline = "Must not write" }, 0), default)).Outcome);
            Assert.Equal(InvitationDraftOutcome.NotFound, (await drafts.SelectTemplateAsync(h.Account, h.Invitation, new("modern-mezuniyet", accepted.Expected.InvitationRevision), default)).Outcome);
            Assert.Equal("NotFound", (await lifecycle.GetAsync(h.Account, h.Invitation, default)).Code);
            Assert.False(await scope.ServiceProvider.GetRequiredService<IInvitationOwnershipValidator>().IsOwnedByAccountAsync(h.Account, h.Invitation, default));
            foreach (var action in new[] { "publish", "update", "pause", "resume", "cancelSchedule", "reschedule", "publishNow", "reactivate" })
            {
                var dates = action is "publish" or "reschedule" or "reactivate"
                    ? new PublicationWindowRequest(action == "reschedule" ? "Scheduled" : "Immediate", action == "reschedule" ? "2026-10-03T15:00:00" : null, "2026-10-04T15:00:00", "Europe/Istanbul", null) : null;
                Assert.Equal("NotFound", (await lifecycle.ExecuteAsync(h.Account, h.Invitation, new(action, accepted.Expected, dates), default)).Code);
            }
        }
        var newId = Guid.NewGuid();
        Guid newGrant;
        await using (var db = h.Db())
        {
            var invitation = Invitation.Create(newId, h.Account, new CryptographicPublicCodeGenerator().Generate(), Now);
            invitation.PinTemplate("zamansiz-dugun", 1);
            db.Invitations.Add(invitation);
            db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), newId, 1, Content, Now));
            var grant = AccountPlanGrant.Create(Guid.NewGuid(), h.Account, (await db.Plans.SingleAsync(p => p.Key == "standard")).Id, GrantSource.IndividualPurchase, Now);
            newGrant = grant.Id;
            db.AccountPlanGrants.Add(grant);
            await db.SaveChangesAsync();
        }
        await using (var scope = h.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await lifecycle.GetAsync(h.Account, newId, default)).Status!;
            var result = await lifecycle.ExecuteAsync(h.Account, newId, new("publish", status.Expected,
                new("Immediate", null, "2026-10-03T15:00:00", "Europe/Istanbul", newGrant), ProceedWithRecommendedWarnings: true), default);
            Assert.Equal("Succeeded", result.Code);
        }
        await using var verify = h.Db();
        Assert.Equal(2, await verify.Invitations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await verify.Invitations.CountAsync());
        Assert.Equal(2, await verify.PublicationWindows.CountAsync(w => w.IsCurrent));
        Assert.Equal(accepted.Expected.InvitationRevision, (await verify.Invitations.IgnoreQueryFilters().SingleAsync(i => i.Id == h.Invitation)).Revision);
    }

    private static void AssertHeaders(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.TryGetValues("X-Robots-Tag", out var values));
        Assert.Contains("noindex", string.Join(",", values!), StringComparison.OrdinalIgnoreCase);
        Assert.Null(response.Headers.ETag);
    }

    private async Task<Harness> CreateAsync(string template = "zamansiz-dugun", int? rateLimit = null,
        bool blockGrantRead = false, int? rsvpRateLimit = null, bool superAdminTestAuthentication = false,
        int? creatorRsvpReadAccountRateLimit = null, int? creatorRsvpWriteAccountRateLimit = null,
        int? creatorRsvpReadIpRateLimit = null, int? creatorRsvpWriteIpRateLimit = null,
        bool acknowledgeServiceNotice = true)
    {
        var connection = new NpgsqlConnectionStringBuilder(await postgreSql.CreateEmptyDatabaseAsync()) { Pooling = false }.ConnectionString;
        var h = new Harness(connection, rateLimit, blockGrantRead, rsvpRateLimit, superAdminTestAuthentication,
            creatorRsvpReadAccountRateLimit, creatorRsvpWriteAccountRateLimit, creatorRsvpReadIpRateLimit, creatorRsvpWriteIpRateLimit);
        await using var db = h.Db();
        await db.Database.MigrateAsync();
        await new PlanCatalogInitializer(db).InitializeAsync(default);
        await new TemplateCatalogInitializer(db, new TemplateRendererRegistry()).InitializeAsync(default);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = h.Email, NormalizedUserName = h.Email.ToUpperInvariant(), Email = h.Email, NormalizedEmail = h.Email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
        db.Users.Add(user);
        db.Accounts.Add(Account.Create(h.Account, user.Id, AccountType.Individual, "Public tester", Now));
        // These fixtures model existing Creators who have completed the required M4 notice gate.
        if (acknowledgeServiceNotice)
            db.AccountConsentRecords.Add(AccountConsentRecord.Create(Guid.NewGuid(), h.Account,
                AccountConsentKind.ServiceNoticeAcknowledgement, true,
                AccountConsentVersions.ServiceNotice, AccountConsentSource.ExistingAccountAcknowledgement, Now));
        var invitation = Invitation.Create(h.Invitation, h.Account, h.Code, Now);
        invitation.PinTemplate(template, 1);
        db.Invitations.Add(invitation);
        db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), h.Invitation, 1, Content, Now));
        await db.SaveChangesAsync();
        return h;
    }

    private sealed class MutableClock : IClock { public DateTimeOffset Current { get; set; } = Now; public DateTimeOffset UtcNow => Current; }
    private sealed class ReadBlock
    {
        public TaskCompletionSource ReadReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class BlockingGrantReader(IPublicationGrantAccessValidator inner, ReadBlock block) : IPublicationGrantAccessValidator
    {
        public Task<IReadOnlyCollection<Guid>> FindExpiredOrganizationGrantIdsWithCurrentWindowsAsync(
            DateTimeOffset evaluatedAtUtc, int limit, CancellationToken cancellationToken) =>
            inner.FindExpiredOrganizationGrantIdsWithCurrentWindowsAsync(evaluatedAtUtc, limit, cancellationToken);

        public async Task<PublicationGrantAccessSnapshot?> ReadAsync(Guid account, Guid grant, CancellationToken token)
        {
            var snapshot = await inner.ReadAsync(account, grant, token);
            block.ReadReady.TrySetResult();
            await block.Release.Task.WaitAsync(token);
            return snapshot;
        }
    }
    private sealed class Harness(string connection, int? rateLimit, bool blockGrantRead, int? rsvpRateLimit,
        bool superAdminTestAuthentication, int? creatorRsvpReadAccountRateLimit, int? creatorRsvpWriteAccountRateLimit,
        int? creatorRsvpReadIpRateLimit, int? creatorRsvpWriteIpRateLimit) : WebApplicationFactory<Program>
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Invitation { get; } = Guid.NewGuid();
        public string Code { get; } = new CryptographicPublicCodeGenerator().Generate();
        public Uri PublicPath => new($"/api/v1/public/invitations/{Code}", UriKind.Relative);
        public string Email { get; } = $"public-{Guid.NewGuid():N}@example.test";
        public MutableClock Clock { get; } = new();
        public ReadBlock ReadBlock { get; } = new();
        public DavetiyeDbContext Db() => new(new DbContextOptionsBuilder<DavetiyeDbContext>().UseNpgsql(connection, o => o.MigrationsAssembly(PersistenceConstants.MigrationsAssemblyName)).Options);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connection, ["Cors:AllowedOrigins:0"] = "https://allowed.example.test", ["PublicWeb:BaseUrl"] = "https://davetiye.example.test",
                ["RsvpCapabilities:HmacKeyBase64"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()),
                ["RsvpCapabilities:HmacKeyVersion"] = "1",
                ["AuthRateLimits:PublicInvitationRead:PermitLimit"] = (rateLimit ?? 60).ToString(), ["AuthRateLimits:PublicInvitationRead:WindowSeconds"] = "60",
                ["AuthRateLimits:PublicRsvpSubmission:PermitLimit"] = (rsvpRateLimit ?? 10).ToString(),
                ["AuthRateLimits:PublicRsvpSubmission:WindowSeconds"] = "60",
                ["AuthRateLimits:CreatorRsvpReadAccount:PermitLimit"] = (creatorRsvpReadAccountRateLimit ?? 120).ToString(),
                ["AuthRateLimits:CreatorRsvpReadAccount:WindowSeconds"] = "60",
                ["AuthRateLimits:CreatorRsvpWriteAccount:PermitLimit"] = (creatorRsvpWriteAccountRateLimit ?? 30).ToString(),
                ["AuthRateLimits:CreatorRsvpWriteAccount:WindowSeconds"] = "60",
                ["AuthRateLimits:CreatorRsvpReadIp:PermitLimit"] = (creatorRsvpReadIpRateLimit ?? 300).ToString(),
                ["AuthRateLimits:CreatorRsvpReadIp:WindowSeconds"] = "60",
                ["AuthRateLimits:CreatorRsvpWriteIp:PermitLimit"] = (creatorRsvpWriteIpRateLimit ?? 60).ToString(),
                ["AuthRateLimits:CreatorRsvpWriteIp:WindowSeconds"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IClock>(Clock);
                if (superAdminTestAuthentication)
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = SuperAdminTestAuthenticationHandler.RouterScheme;
                        options.DefaultChallengeScheme = SuperAdminTestAuthenticationHandler.RouterScheme;
                        options.DefaultForbidScheme = SuperAdminTestAuthenticationHandler.RouterScheme;
                    }).AddPolicyScheme(SuperAdminTestAuthenticationHandler.RouterScheme,
                        "Select the normal cookie or an explicit test-only SuperAdmin principal", options =>
                        {
                            options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey("X-Test-SuperAdmin")
                                ? SuperAdminTestAuthenticationHandler.TestScheme
                                : IdentityConstants.ApplicationScheme;
                        }).AddScheme<AuthenticationSchemeOptions, SuperAdminTestAuthenticationHandler>(
                        SuperAdminTestAuthenticationHandler.TestScheme, _ => { });
                }
                if (blockGrantRead) services.AddScoped<IPublicationGrantAccessValidator>(provider =>
                    new BlockingGrantReader(new PublicationGrantAccessValidator(
                        provider.GetRequiredService<DavetiyeDbContext>(),
                        new OrganizationSubscriptionEntitlementReader(provider.GetRequiredService<DavetiyeDbContext>())), ReadBlock));
            });
        }
        public async Task<PublicationStatus> Publish(bool scheduled = false, bool paid = false)
        {
            Guid? grantId = null;
            if (paid)
            {
                await using var db = Db();
                var grant = AccountPlanGrant.Create(Guid.NewGuid(), Account, (await db.Plans.SingleAsync(p => p.Key == "premium")).Id, GrantSource.IndividualPurchase, Now);
                db.AccountPlanGrants.Add(grant);
                await db.SaveChangesAsync();
                grantId = grant.Id;
            }
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, Invitation, default)).Status!;
            var times = new PublicationWindowRequest(scheduled ? "Scheduled" : "Immediate", scheduled ? "2026-10-03T15:00:00" : null,
                scheduled ? "2026-10-04T15:00:00" : "2026-10-03T15:00:00", "Europe/Istanbul", grantId);
            var result = await service.ExecuteAsync(Account, Invitation, new("publish", status.Expected, times, ProceedWithRecommendedWarnings: true), default);
            Assert.Equal("Succeeded", result.Code);
            return result.Status!;
        }
        public async Task<HttpClient> CreateAuthenticatedCreatorAsync()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }
        public async Task<(Guid AccountId, Guid InvitationId, string Email)> CreateSecondCreatorAsync()
        {
            var accountId = Guid.NewGuid();
            var invitationId = Guid.NewGuid();
            var code = new CryptographicPublicCodeGenerator().Generate();
            var email = $"rsvp-other-{Guid.NewGuid():N}@example.test";
            await using var db = Db();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Password);
            db.Users.Add(user);
            db.Accounts.Add(Davetiye.Domain.Modules.IdentityAndAccounts.Account.Create(accountId, user.Id,
                AccountType.Individual, "Other RSVP tester", Now));
            db.AccountConsentRecords.Add(AccountConsentRecord.Create(Guid.NewGuid(), accountId,
                AccountConsentKind.ServiceNoticeAcknowledgement, true,
                AccountConsentVersions.ServiceNotice, AccountConsentSource.ExistingAccountAcknowledgement, Now));
            var invitation = Davetiye.Domain.Modules.Invitations.Invitation.Create(invitationId, accountId, code, Now);
            invitation.PinTemplate("zamansiz-dugun", 1);
            db.Invitations.Add(invitation);
            db.WorkingContents.Add(WorkingContent.Create(Guid.NewGuid(), invitationId, 1, Content, Now));
            await db.SaveChangesAsync();
            return (accountId, invitationId, email);
        }
        public async Task<(Guid Name, Guid Attending, Guid Count)> EnableRsvp(bool enabled = true)
        {
            var name = Guid.NewGuid(); var attending = Guid.NewGuid(); var count = Guid.NewGuid();
            await using var db = Db();
            var configuration = RsvpConfiguration.Create(Guid.NewGuid(), Invitation, Now);
            configuration.AddQuestion(name, "Name", RsvpQuestionType.ShortText, true, 0, null, Now);
            configuration.AddQuestion(attending, "Attending", RsvpQuestionType.YesNo, true, 1, null, Now);
            configuration.AddQuestion(count, "Guests", RsvpQuestionType.Number, true, 2,
                RsvpQuestionSemanticRole.ParticipantCount, Now);
            configuration.SetEnabled(enabled, Now);
            db.RsvpConfigurations.Add(configuration);
            await db.SaveChangesAsync();
            return (name, attending, count);
        }
        public async Task Command(string action)
        {
            await using var scope = Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPublicationLifecycleService>();
            var status = (await service.GetAsync(Account, Invitation, default)).Status!;
            Assert.Equal("Succeeded", (await service.ExecuteAsync(Account, Invitation, new(action, status.Expected), default)).Code);
        }
        public async Task<Guid[]> InternalIds()
        {
            await using var db = Db();
            return [Account, Invitation, (await db.AccountPlanGrants.SingleAsync()).Id, (await db.PublicationWindows.SingleAsync()).Id,
                (await db.PublishedContents.SingleAsync()).Id, (await db.WorkingContents.SingleAsync()).Id];
        }
    }

    private sealed class SuperAdminTestAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string RouterScheme = "RsvpM5TestRouter";
        public const string TestScheme = "RsvpM5TestSuperAdmin";
        private static readonly Guid UserId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-SuperAdmin"))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
                new Claim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue)
            ], TestScheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
