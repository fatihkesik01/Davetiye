using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Davetiye.Application.Modules.Invitations.Contracts;
using Davetiye.Application.Modules.PlansAndEntitlements.Contracts;
using Davetiye.Application.Modules.Rsvp.Contracts;
using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Domain.Modules.SharedKernel;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Rsvp;

public sealed class RsvpCapabilityOptions
{
    public const string SectionName = "RsvpCapabilities";
    public string HmacKeyBase64 { get; init; } = string.Empty;
    public int HmacKeyVersion { get; init; } = 1;
}

/// <summary>Public RSVP projection, submission, and submission-scoped capability operations.</summary>
public sealed class PublicRsvpService(
    DavetiyeDbContext dbContext,
    IRsvpGuestInvitationAccessReader invitationAccess,
    IEffectiveEntitlementResolver entitlements,
    IClock clock,
    IOptions<RsvpCapabilityOptions> capabilityOptions,
    IOptions<RsvpInputLimits> inputLimitsOptions) : IPublicRsvpService
{
    private readonly RsvpInputLimits inputLimits = inputLimitsOptions.Value;

    public async Task<PublicRsvpConfigurationResult> GetConfigurationAsync(string publicCode,
        CancellationToken cancellationToken)
    {
        var access = await invitationAccess.ReadAsync(publicCode, cancellationToken);
        if (access is null) return new(PublicRsvpOutcome.NotFound);
        var entitlement = await ResolveRsvpEntitlementAsync(access, cancellationToken);
        if (!entitlement.IsGranted) return new(PublicRsvpOutcome.NotFound);
        var config = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (config is null || !config.IsEnabled ||
            !inputLimits.IsActiveQuestionCountWithinLimit(config.Questions.Count(question => question.IsActive)))
            return new(PublicRsvpOutcome.Unavailable);
        return new(PublicRsvpOutcome.Available, ToPublicConfiguration(config, inputLimits));
    }

    public async Task<PublicRsvpGuestSubmissionResult> GetSubmissionAsync(string publicCode, Guid submissionId,
        string? manageToken, CancellationToken cancellationToken)
    {
        if (submissionId == Guid.Empty || !TryDecodeManageToken(manageToken, out _))
            return new(PublicRsvpOutcome.NotFound);
        var access = await invitationAccess.ReadAsync(publicCode, cancellationToken);
        if (access is null || access.WindowEndsAt <= clock.UtcNow.ToUniversalTime())
            return new(PublicRsvpOutcome.NotFound);
        var entitlement = await ResolveRsvpEntitlementAsync(access, cancellationToken);
        if (!entitlement.IsGranted) return new(PublicRsvpOutcome.NotFound);
        var config = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (config is null || !config.IsEnabled ||
            !inputLimits.IsActiveQuestionCountWithinLimit(config.Questions.Count(question => question.IsActive)))
            return new(PublicRsvpOutcome.NotFound);

        var (key, version) = ReadSigningKey(capabilityOptions.Value);
        var now = clock.UtcNow.ToUniversalTime();
        var capability = await dbContext.RsvpManageCapabilities.AsNoTracking().SingleOrDefaultAsync(item =>
            item.SubmissionId == submissionId && item.Purpose == RsvpManageCapability.RequiredPurpose &&
            item.HmacKeyVersion == version && item.RevokedAt == null && item.ExpiresAt > now,
            cancellationToken);
        if (capability is null || capability.ExpiresAt > access.WindowEndsAt ||
            !DigestMatches(capability.HmacDigest, key, submissionId, manageToken!))
            return new(PublicRsvpOutcome.NotFound);

        var submission = await dbContext.RsvpSubmissions.AsNoTracking()
            .Include(item => item.Answers).ThenInclude(answer => answer.SelectedOptions)
            .SingleOrDefaultAsync(item => item.Id == submissionId && item.InvitationId == access.InvitationId,
                cancellationToken);
        if (submission is null) return new(PublicRsvpOutcome.NotFound);
        var projection = new PublicRsvpGuestSubmission(submission.Id, submission.UpdatedAt,
            submission.Answers.OrderBy(answer => answer.QuestionId).Select(answer => new PublicRsvpGuestAnswer(
                answer.QuestionId, answer.TextValue, answer.NumberValue?.ToString(CultureInfo.InvariantCulture), answer.BooleanValue,
                answer.SelectedOptions.Select(option => option.OptionId).ToArray())).ToArray());
        return new(PublicRsvpOutcome.Available, projection);
    }

    public async Task<PublicRsvpSubmissionResult> SubmitAsync(string publicCode, SubmitPublicRsvpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAnswers(request, out var answers, out var invalid)) return invalid!;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var access = await invitationAccess.LockAndReadAsync(publicCode, cancellationToken);
        if (access is null) return new(PublicRsvpOutcome.NotFound);
        var now = clock.UtcNow.ToUniversalTime();
        if (access.WindowEndsAt <= now) return new(PublicRsvpOutcome.NotFound);

        var entitlement = await ResolveRsvpEntitlementAsync(access, cancellationToken);
        if (!entitlement.IsGranted) return new(PublicRsvpOutcome.NotFound);
        var configuration = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (configuration is null || !configuration.IsEnabled) return new(PublicRsvpOutcome.Unavailable);
        if (!ValidateAnswers(configuration, answers, out invalid)) return invalid!;

        var maxResponses = entitlement.Entitlements!.MaxRsvpResponses;
        var responseCount = await dbContext.RsvpSubmissions.CountAsync(item =>
            item.InvitationId == access.InvitationId, cancellationToken);
        if (responseCount >= maxResponses) return new(PublicRsvpOutcome.QuotaReached);

        var submission = RsvpSubmission.Create(Guid.NewGuid(), access.InvitationId, now);
        if (!AddAnswers(submission, configuration, answers, now, out invalid)) return invalid!;
        var (key, version) = ReadSigningKey(capabilityOptions.Value);
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var digest = ComputeDigest(key, submission.Id, rawToken);
        var capability = RsvpManageCapability.Create(Guid.NewGuid(), submission.Id,
            RsvpManageCapability.RequiredPurpose, version, digest, now, access.WindowEndsAt);

        dbContext.RsvpSubmissions.Add(submission);
        dbContext.RsvpManageCapabilities.Add(capability);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PublicRsvpOutcome.Available, submission.Id, now, access.WindowEndsAt,
            UpdatedAt: now, ManageToken: rawToken);
    }

    public async Task<PublicRsvpSubmissionResult> UpdateAsync(string publicCode, Guid submissionId,
        string? manageToken, SubmitPublicRsvpRequest request, CancellationToken cancellationToken)
    {
        if (submissionId == Guid.Empty || !TryDecodeManageToken(manageToken, out _))
            return new(PublicRsvpOutcome.NotFound);
        if (!TryGetAnswers(request, out var answers, out var invalid)) return invalid!;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var access = await invitationAccess.LockAndReadAsync(publicCode, cancellationToken);
        if (access is null) return new(PublicRsvpOutcome.NotFound);
        var now = clock.UtcNow.ToUniversalTime();
        if (access.WindowEndsAt <= now) return new(PublicRsvpOutcome.NotFound);
        var entitlement = await ResolveRsvpEntitlementAsync(access, cancellationToken);
        if (!entitlement.IsGranted) return new(PublicRsvpOutcome.NotFound);

        var configuration = await LoadConfigurationAsync(access.InvitationId, cancellationToken);
        if (configuration is null || !configuration.IsEnabled) return new(PublicRsvpOutcome.NotFound);
        if (!ValidateAnswers(configuration, answers, out invalid)) return invalid!;

        // Lock and consume the active capability in the same transaction. The invitation lock serializes
        // every capability operation for this invitation; this row lock also documents the scoped invariant.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM rsvp_manage_capabilities WHERE submission_id = {submissionId} AND revoked_at IS NULL FOR UPDATE",
            cancellationToken);
        var (key, version) = ReadSigningKey(capabilityOptions.Value);
        var capability = await dbContext.RsvpManageCapabilities.SingleOrDefaultAsync(item =>
            item.SubmissionId == submissionId && item.Purpose == RsvpManageCapability.RequiredPurpose &&
            item.HmacKeyVersion == version && item.RevokedAt == null && item.ExpiresAt > now,
            cancellationToken);
        if (capability is null || !DigestMatches(capability.HmacDigest, key, submissionId, manageToken!))
            return new(PublicRsvpOutcome.NotFound);
        if (capability.ExpiresAt > access.WindowEndsAt) return new(PublicRsvpOutcome.NotFound);

        var submission = await dbContext.RsvpSubmissions
            .Include(item => item.Answers).ThenInclude(answer => answer.SelectedOptions)
            .SingleOrDefaultAsync(item => item.Id == submissionId && item.InvitationId == access.InvitationId,
                cancellationToken);
        if (submission is null) return new(PublicRsvpOutcome.NotFound);

        // Revoke and persist before inserting the replacement because the database enforces one active
        // capability per submission. The enclosing transaction keeps both writes atomic.
        capability.Revoke(now);
        submission.BeginAnswerReplacement(now);
        if (!AddAnswers(submission, configuration, answers, now, out invalid)) return invalid!;
        await dbContext.SaveChangesAsync(cancellationToken);

        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var rotated = RsvpManageCapability.Create(Guid.NewGuid(), submission.Id,
            RsvpManageCapability.RequiredPurpose, version, ComputeDigest(key, submission.Id, rawToken), now,
            capability.ExpiresAt);
        dbContext.RsvpManageCapabilities.Add(rotated);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PublicRsvpOutcome.Available, submission.Id, submission.SubmittedAt,
            capability.ExpiresAt, UpdatedAt: now, ManageToken: rawToken);
    }

    private async Task<EntitlementResolutionResult> ResolveRsvpEntitlementAsync(
        InvitationRsvpGuestAccess access, CancellationToken cancellationToken) =>
        await entitlements.ResolveAsync(new EntitlementResolutionContext(access.AccountId, access.GrantId,
            access.InvitationId, PublicationEntitlementAction.RsvpSubmission), cancellationToken);

    private bool TryGetAnswers(SubmitPublicRsvpRequest request, out PublicRsvpAnswerInput[] answers,
        out PublicRsvpSubmissionResult? invalid)
    {
        answers = [];
        invalid = null;
        if (request.Answers is null)
        {
            invalid = Invalid("answers", "Answers are required.");
            return false;
        }
        if (request.Answers.Any(answer => answer is null))
        {
            invalid = Invalid("answers", "Answers cannot contain null entries.");
            return false;
        }
        answers = request.Answers.Select(answer => answer!).ToArray();
        return true;
    }

    private bool ValidateAnswers(RsvpConfiguration configuration, IReadOnlyCollection<PublicRsvpAnswerInput> answers,
        out PublicRsvpSubmissionResult? invalid)
    {
        invalid = null;
        var activeQuestions = configuration.Questions.Where(item => item.IsActive).ToDictionary(item => item.Id);
        if (!inputLimits.IsActiveQuestionCountWithinLimit(activeQuestions.Count))
        {
            invalid = Invalid("answers", $"The RSVP form may have at most {inputLimits.MaxActiveQuestionsPerInvitation} active questions.");
            return false;
        }
        if (answers.Count > activeQuestions.Count || answers.Select(answer => answer.QuestionId).Distinct().Count() != answers.Count ||
            answers.Any(answer => !activeQuestions.ContainsKey(answer.QuestionId)))
        {
            invalid = Invalid("answers", "Answers must reference unique active questions in this invitation.");
            return false;
        }
        var supplied = answers.Select(answer => answer.QuestionId).ToHashSet();
        if (activeQuestions.Values.Any(question => question.IsRequired && !supplied.Contains(question.Id)))
        {
            invalid = Invalid("answers", "All required questions must be answered.");
            return false;
        }
        return true;
    }

    private bool AddAnswers(RsvpSubmission submission, RsvpConfiguration configuration,
        IEnumerable<PublicRsvpAnswerInput> answers, DateTimeOffset now, out PublicRsvpSubmissionResult? invalid)
    {
        invalid = null;
        foreach (var input in answers)
        {
            try
            {
                var answer = submission.AddAnswer(Guid.NewGuid(), configuration, input.QuestionId, now, input.TextValue,
                    input.NumberValue, input.BooleanValue, input.SelectedOptionIds ?? [], inputLimits);
                // When replacing a tracked submission graph, new children carry client-generated ids.
                // Explicitly mark the replacement graph Added so EF does not infer UPDATEs for rows
                // that do not exist yet (the original submission create path is an untracked graph).
                if (dbContext.Entry(submission).State != EntityState.Detached)
                {
                    dbContext.Entry(answer).State = EntityState.Added;
                    foreach (var selectedOption in answer.SelectedOptions)
                        dbContext.Entry(selectedOption).State = EntityState.Added;
                }
            }
            catch (ArgumentException exception)
            {
                invalid = Invalid("answers", exception.Message);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                invalid = Invalid("answers", exception.Message);
                return false;
            }
        }
        return true;
    }

    private Task<RsvpConfiguration?> LoadConfigurationAsync(Guid invitationId, CancellationToken cancellationToken) =>
        dbContext.RsvpConfigurations.AsNoTracking().Include(item => item.Questions).ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(item => item.InvitationId == invitationId, cancellationToken);

    private static PublicRsvpConfiguration ToPublicConfiguration(RsvpConfiguration configuration, RsvpInputLimits limits) =>
        new("available", configuration.Questions.Where(question => question.IsActive).OrderBy(question => question.SortOrder)
            .Select(question => new PublicRsvpQuestion(question.Id, question.Prompt, question.Type.ToString(),
                question.IsRequired, question.SortOrder, question.Options.Where(option => option.IsActive)
                    .OrderBy(option => option.SortOrder)
                    .Select(option => new PublicRsvpOption(option.Id, option.Label, option.SortOrder)).ToArray(),
                question.SemanticRole == RsvpQuestionSemanticRole.ParticipantCount ? limits.MinimumParticipantCount : null,
                question.SemanticRole == RsvpQuestionSemanticRole.ParticipantCount ? limits.MaximumParticipantCount : null)).ToArray(),
            new PublicRsvpAnswerLimits(limits.MaxShortTextAnswerCharacters, limits.MaxLongTextAnswerCharacters,
                limits.MinimumParticipantCount, limits.MaximumParticipantCount, limits.MaxMultipleChoiceSelections));

    private static bool TryDecodeManageToken(string? token, out byte[] tokenBytes)
    {
        tokenBytes = [];
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            tokenBytes = WebEncoders.Base64UrlDecode(token);
            return tokenBytes.Length == 32 && string.Equals(WebEncoders.Base64UrlEncode(tokenBytes), token,
                StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static (byte[] Key, int Version) ReadSigningKey(RsvpCapabilityOptions options)
    {
        if (options.HmacKeyVersion <= 0 || string.IsNullOrWhiteSpace(options.HmacKeyBase64))
            throw new InvalidOperationException("RsvpCapabilities:HmacKeyBase64 and a positive HmacKeyVersion are required.");
        byte[] key;
        try { key = Convert.FromBase64String(options.HmacKeyBase64); }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("RsvpCapabilities:HmacKeyBase64 must be valid base64.", exception);
        }
        if (key.Length < 32) throw new InvalidOperationException("RSVP capability HMAC key must contain at least 256 bits.");
        return (key, options.HmacKeyVersion);
    }

    private static byte[] ComputeDigest(byte[] key, Guid submissionId, string token)
    {
        var value = Encoding.UTF8.GetBytes($"{RsvpManageCapability.RequiredPurpose}\n{submissionId:D}\n{token}");
        return HMACSHA256.HashData(key, value);
    }

    private static bool DigestMatches(byte[] storedDigest, byte[] key, Guid submissionId, string token) =>
        CryptographicOperations.FixedTimeEquals(storedDigest, ComputeDigest(key, submissionId, token));

    private static PublicRsvpSubmissionResult Invalid(string key, string message) => new(PublicRsvpOutcome.Invalid,
        Errors: new Dictionary<string, string[]> { [key] = [message] });
}
