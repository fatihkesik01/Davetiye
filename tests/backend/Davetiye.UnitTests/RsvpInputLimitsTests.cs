using Davetiye.Domain.Modules.Rsvp;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class RsvpInputLimitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Short_and_long_text_accept_the_configured_boundary_and_reject_one_character_over()
    {
        var shortQuestion = Question(RsvpQuestionType.ShortText);
        Answer(shortQuestion, text: new string('s', 200));
        Assert.Throws<ArgumentException>(() => Answer(shortQuestion, text: new string('s', 201)));

        var longQuestion = Question(RsvpQuestionType.LongText);
        Answer(longQuestion, text: new string('l', 2_000));
        Assert.Throws<ArgumentException>(() => Answer(longQuestion, text: new string('l', 2_001)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void Participant_count_accepts_both_inclusive_boundaries(int count) =>
        Answer(Question(RsvpQuestionType.Number, RsvpQuestionSemanticRole.ParticipantCount), number: count);

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    [InlineData(1.5)]
    public void Participant_count_rejects_out_of_range_or_fractional_values(double count) =>
        Assert.Throws<ArgumentException>(() => Answer(
            Question(RsvpQuestionType.Number, RsvpQuestionSemanticRole.ParticipantCount), number: (decimal)count));

    [Fact]
    public void Unmarked_number_keeps_the_full_decimal_value_range()
    {
        var question = Question(RsvpQuestionType.Number);
        Answer(question, number: decimal.MaxValue);
        Answer(question, number: decimal.MinValue);
    }

    [Fact]
    public void Multiple_choice_accepts_ten_selections_and_rejects_eleven()
    {
        var question = Question(RsvpQuestionType.MultipleChoice);
        var options = Enumerable.Range(0, 11)
            .Select(index => question.AddOption(Guid.NewGuid(), $"Choice {index}", index, Now))
            .ToArray();
        Answer(question, selected: options.Take(10).Select(option => option.Id).ToArray());
        Assert.Throws<ArgumentException>(() => Answer(question, selected: options.Select(option => option.Id).ToArray()));
    }

    [Fact]
    public void Creator_limit_defaults_match_the_accepted_values()
    {
        var limits = new RsvpInputLimits();
        Assert.True(limits.IsPromptWithinLimit(new string('p', 200)));
        Assert.False(limits.IsPromptWithinLimit(new string('p', 201)));
        Assert.True(limits.IsOptionLabelWithinLimit(new string('o', 100)));
        Assert.False(limits.IsOptionLabelWithinLimit(new string('o', 101)));
        Assert.True(limits.IsActiveQuestionCountWithinLimit(20));
        Assert.False(limits.IsActiveQuestionCountWithinLimit(21));
        Assert.True(limits.IsDefinedOptionCountWithinLimit(20));
        Assert.False(limits.IsDefinedOptionCountWithinLimit(21));
    }

    private static RsvpQuestion Question(RsvpQuestionType type, RsvpQuestionSemanticRole? role = null)
    {
        var config = RsvpConfiguration.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        return config.AddQuestion(Guid.NewGuid(), "Question", type, true, 0, role, Now);
    }

    private static void Answer(RsvpQuestion question, string? text = null, decimal? number = null,
        IReadOnlyCollection<Guid>? selected = null)
    {
        var configuration = RsvpConfiguration.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        var canonicalQuestion = configuration.AddQuestion(question.Id, question.Prompt, question.Type,
            question.IsRequired, 0, question.SemanticRole, Now);
        foreach (var option in question.Options.OrderBy(option => option.SortOrder))
            canonicalQuestion.AddOption(option.Id, option.Label, option.SortOrder, Now);
        var submission = RsvpSubmission.Create(Guid.NewGuid(), configuration.InvitationId, Now);
        submission.AddAnswer(Guid.NewGuid(), configuration, canonicalQuestion.Id, Now, textValue: text,
            numberValue: number, selectedOptionIds: selected);
    }
}
