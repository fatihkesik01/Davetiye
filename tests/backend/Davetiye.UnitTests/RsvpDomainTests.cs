using Davetiye.Domain.Modules.Rsvp;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class RsvpDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Participant_count_role_requires_number_and_only_one_active_question_per_form()
    {
        var configuration = CreateConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.AddQuestion(
            Guid.NewGuid(), "How many?", RsvpQuestionType.ShortText, true, 0,
            RsvpQuestionSemanticRole.ParticipantCount, Now));

        var first = configuration.AddQuestion(Guid.NewGuid(), "Attendees", RsvpQuestionType.Number,
            true, 0, RsvpQuestionSemanticRole.ParticipantCount, Now);
        Assert.Throws<InvalidOperationException>(() => configuration.AddQuestion(
            Guid.NewGuid(), "Other attendees", RsvpQuestionType.Number, true, 1,
            RsvpQuestionSemanticRole.ParticipantCount, Now.AddMinutes(1)));

        configuration.ArchiveQuestion(first.Id, Now.AddMinutes(2));
        var replacement = configuration.AddQuestion(Guid.NewGuid(), "Attendees", RsvpQuestionType.Number,
            true, 0, RsvpQuestionSemanticRole.ParticipantCount, Now.AddMinutes(3));

        Assert.False(first.IsActive);
        Assert.True(replacement.IsActive);
        Assert.Equal(3, configuration.Revision);
    }

    [Fact]
    public void Active_question_and_option_order_are_unique_but_archived_slots_can_be_reused()
    {
        var configuration = CreateConfiguration();
        var question = configuration.AddQuestion(Guid.NewGuid(), "Meal", RsvpQuestionType.SingleChoice,
            false, 0, null, Now);
        var option = question.AddOption(Guid.NewGuid(), "Vegetarian", 0, Now);

        Assert.Throws<InvalidOperationException>(() => configuration.AddQuestion(
            Guid.NewGuid(), "Duplicate order", RsvpQuestionType.ShortText, false, 0, null, Now));
        Assert.Throws<InvalidOperationException>(() => question.AddOption(Guid.NewGuid(), "Meat", 0, Now));

        question.ArchiveOption(option.Id, Now.AddMinutes(1));
        var replacement = question.AddOption(Guid.NewGuid(), "Meat", 0, Now.AddMinutes(2));
        Assert.False(option.IsActive);
        Assert.True(replacement.IsActive);

        configuration.ArchiveQuestion(question.Id, Now.AddMinutes(3));
        Assert.False(replacement.IsActive);
    }

    [Fact]
    public void Creator_option_replacement_keeps_retained_ids_and_archives_omitted_options_without_changing_answer_snapshots()
    {
        var configuration = CreateConfiguration();
        var question = configuration.AddQuestion(Guid.NewGuid(), "Meal", RsvpQuestionType.SingleChoice,
            true, 0, null, Now);
        var retained = question.AddOption(Guid.NewGuid(), "Vegetarian", 0, Now);
        var removed = question.AddOption(Guid.NewGuid(), "Meat", 1, Now);
        var submission = RsvpSubmission.Create(Guid.NewGuid(), configuration.InvitationId, Now);
        var answer = submission.AddAnswer(Guid.NewGuid(), configuration, question.Id, Now.AddMinutes(1),
            selectedOptionIds: [retained.Id]);

        question.PrepareOptionReplacement();
        question.ReplaceOptions([(retained.Id, "Plant based", 0), (null, "Vegan", 1)], Now.AddMinutes(2));

        Assert.Equal(retained.Id, Assert.Single(question.Options, option => option.IsActive && option.Label == "Plant based").Id);
        Assert.False(removed.IsActive);
        Assert.Equal("Vegetarian", answer.SelectedOptions.Single(option => option.OptionId == retained.Id).LabelSnapshot);
    }

    [Fact]
    public void Creator_question_reordering_requires_exact_active_set_and_updates_contiguous_positions()
    {
        var configuration = CreateConfiguration();
        var first = configuration.AddQuestion(Guid.NewGuid(), "First", RsvpQuestionType.ShortText, true, 0, null, Now);
        var second = configuration.AddQuestion(Guid.NewGuid(), "Second", RsvpQuestionType.LongText, false, 1, null, Now);

        Assert.Throws<InvalidOperationException>(() => configuration.ReorderQuestions([first.Id], Now.AddMinutes(1)));
        configuration.PrepareQuestionReorder();
        configuration.ReorderQuestions([second.Id, first.Id], Now.AddMinutes(1));

        Assert.Equal(0, second.SortOrder);
        Assert.Equal(1, first.SortOrder);
        Assert.Equal(3, configuration.Revision);
    }

    [Fact]
    public void Submission_keeps_question_and_option_snapshots_and_rejects_wrong_question_or_duplicate_answer()
    {
        var configuration = CreateConfiguration();
        var question = configuration.AddQuestion(Guid.NewGuid(), "Meal", RsvpQuestionType.SingleChoice,
            true, 0, null, Now);
        var option = question.AddOption(Guid.NewGuid(), "Vegetarian", 0, Now);
        var submission = RsvpSubmission.Create(Guid.NewGuid(), configuration.InvitationId, Now);

        var answer = submission.AddAnswer(Guid.NewGuid(), configuration, question.Id, Now.AddMinutes(1),
            selectedOptionIds: [option.Id]);
        Assert.Throws<InvalidOperationException>(() => submission.AddAnswer(
            Guid.NewGuid(), configuration, question.Id, Now.AddMinutes(1), selectedOptionIds: [option.Id]));
        question.ArchiveOption(option.Id, Now.AddMinutes(2));
        configuration.ArchiveQuestion(question.Id, Now.AddMinutes(3));

        Assert.Equal("Meal", answer.QuestionPromptSnapshot);
        Assert.Equal(RsvpQuestionType.SingleChoice, answer.QuestionTypeSnapshot);
        Assert.True(answer.IsRequiredSnapshot);
        Assert.Equal("Vegetarian", Assert.Single(answer.SelectedOptions).LabelSnapshot);
        Assert.Equal(option.Id, Assert.Single(answer.SelectedOptions).OptionId);
        Assert.Throws<InvalidOperationException>(() => RsvpSubmission.Create(Guid.NewGuid(), configuration.InvitationId, Now)
            .AddAnswer(Guid.NewGuid(), configuration, question.Id, Now, selectedOptionIds: [option.Id]));
    }

    [Fact]
    public void Submission_answer_replacement_clears_old_answers_and_preserves_original_submission_time()
    {
        var configuration = CreateConfiguration();
        var name = configuration.AddQuestion(Guid.NewGuid(), "Name", RsvpQuestionType.ShortText, true, 0, null, Now);
        var submission = RsvpSubmission.Create(Guid.NewGuid(), configuration.InvitationId, Now);
        submission.AddAnswer(Guid.NewGuid(), configuration, name.Id, Now.AddMinutes(1), textValue: "Ada");

        submission.BeginAnswerReplacement(Now.AddMinutes(2));
        submission.AddAnswer(Guid.NewGuid(), configuration, name.Id, Now.AddMinutes(2), textValue: "Grace");

        Assert.Equal(Now, submission.SubmittedAt);
        Assert.Equal(Now.AddMinutes(2), submission.UpdatedAt);
        Assert.Equal("Grace", Assert.Single(submission.Answers).TextValue);
        Assert.Equal(3, submission.Revision);
        Assert.Throws<ArgumentException>(() => submission.BeginAnswerReplacement(Now.AddMinutes(1)));
    }

    [Fact]
    public void Answer_values_must_match_question_type_and_selected_options_belong_to_question()
    {
        var configuration = CreateConfiguration();
        var number = configuration.AddQuestion(Guid.NewGuid(), "Guests", RsvpQuestionType.Number,
            true, 0, RsvpQuestionSemanticRole.ParticipantCount, Now);
        var submission = RsvpSubmission.Create(Guid.NewGuid(), configuration.InvitationId, Now);

        Assert.Throws<ArgumentException>(() => submission.AddAnswer(Guid.NewGuid(), configuration, number.Id,
            Now.AddMinutes(1), textValue: "two"));
        var answer = submission.AddAnswer(Guid.NewGuid(), configuration, number.Id,
            Now.AddMinutes(1), numberValue: 2m);
        Assert.Equal(2m, answer.NumberValue);
        Assert.Equal(RsvpQuestionSemanticRole.ParticipantCount, answer.SemanticRoleSnapshot);

        var otherQuestion = configuration.AddQuestion(Guid.NewGuid(), "Drink", RsvpQuestionType.SingleChoice,
            false, 1, null, Now.AddMinutes(2));
        var foreignOption = otherQuestion.AddOption(Guid.NewGuid(), "Tea", 0, Now.AddMinutes(2));
        var choice = configuration.AddQuestion(Guid.NewGuid(), "Food", RsvpQuestionType.SingleChoice,
            false, 2, null, Now.AddMinutes(3));
        Assert.Throws<ArgumentException>(() => RsvpSubmission.Create(Guid.NewGuid(), configuration.InvitationId, Now)
            .AddAnswer(Guid.NewGuid(), configuration, choice.Id, Now.AddMinutes(4),
                selectedOptionIds: [foreignOption.Id]));
    }

    [Fact]
    public void Submission_cannot_answer_a_question_from_another_invitation_configuration()
    {
        var owningInvitationId = Guid.NewGuid();
        var owningConfiguration = RsvpConfiguration.Create(Guid.NewGuid(), owningInvitationId, Now);
        var otherConfigurationForSameInvitation = RsvpConfiguration.Create(Guid.NewGuid(), owningInvitationId, Now);
        var otherQuestion = otherConfigurationForSameInvitation.AddQuestion(Guid.NewGuid(), "Guests",
            RsvpQuestionType.Number, true, 0, RsvpQuestionSemanticRole.ParticipantCount, Now);
        var sameInvitationSubmission = RsvpSubmission.Create(Guid.NewGuid(), owningInvitationId, Now);

        Assert.Throws<InvalidOperationException>(() => sameInvitationSubmission.AddAnswer(
            Guid.NewGuid(), owningConfiguration, otherQuestion.Id, Now.AddMinutes(1), numberValue: 2));

        var foreignConfiguration = RsvpConfiguration.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        var foreignQuestion = foreignConfiguration.AddQuestion(Guid.NewGuid(), "Guests", RsvpQuestionType.Number,
            true, 0, RsvpQuestionSemanticRole.ParticipantCount, Now);
        var submission = RsvpSubmission.Create(Guid.NewGuid(), owningInvitationId, Now);

        Assert.Throws<InvalidOperationException>(() => submission.AddAnswer(
            Guid.NewGuid(), foreignConfiguration, foreignQuestion.Id, Now.AddMinutes(1), numberValue: 2));
    }

    [Fact]
    public void Manage_capability_accepts_only_fixed_purpose_and_sha256_digest_not_raw_token()
    {
        var digest = Enumerable.Range(0, RsvpManageCapability.HmacSha256DigestLength).Select(value => (byte)value).ToArray();
        var capability = RsvpManageCapability.Create(Guid.NewGuid(), Guid.NewGuid(),
            RsvpManageCapability.RequiredPurpose, 1, digest, Now, Now.AddDays(1));

        Assert.Equal(RsvpManageCapability.RequiredPurpose, capability.Purpose);
        Assert.Equal(1, capability.HmacKeyVersion);
        Assert.Equal(Now.AddDays(1), capability.ExpiresAt);
        Assert.Equal(digest, capability.HmacDigest);
        capability.HmacDigest[0] ^= 0xff;
        Assert.Equal(digest, capability.HmacDigest);
        Assert.Null(typeof(RsvpManageCapability).GetProperty("Token"));
        Assert.Throws<ArgumentException>(() => RsvpManageCapability.Create(Guid.NewGuid(), Guid.NewGuid(),
            "gift-manage", 1, digest, Now, Now.AddDays(1)));
        Assert.Throws<ArgumentException>(() => RsvpManageCapability.Create(Guid.NewGuid(), Guid.NewGuid(),
            RsvpManageCapability.RequiredPurpose, 1, [1, 2, 3], Now, Now.AddDays(1)));
        Assert.Throws<ArgumentException>(() => RsvpManageCapability.Create(Guid.NewGuid(), Guid.NewGuid(),
            RsvpManageCapability.RequiredPurpose, 1, digest, Now, Now));

        capability.Revoke(Now.AddMinutes(1));
        capability.Revoke(Now.AddMinutes(2));
        Assert.Equal(Now.AddMinutes(1), capability.RevokedAt);
    }

    private static RsvpConfiguration CreateConfiguration() =>
        RsvpConfiguration.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
}
