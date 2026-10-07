using Davetiye.Domain.Modules.Rsvp;
using Davetiye.Infrastructure.Modules.Rsvp;
using Microsoft.Extensions.Options;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class RsvpInputLimitsValidatorTests
{
    private readonly RsvpInputLimitsValidator validator = new();

    [Fact]
    public void Approved_defaults_and_tightened_overrides_are_valid()
    {
        Assert.True(validator.Validate(null, new RsvpInputLimits()).Succeeded);
        Assert.True(validator.Validate(null, new RsvpInputLimits
        {
            MaxActiveQuestionsPerInvitation = 8,
            MaxQuestionPromptCharacters = 150,
            MaxOptionLabelCharacters = 80,
            MaxDefinedOptionsPerChoiceQuestion = 15,
            MaxShortTextAnswerCharacters = 120,
            MaxLongTextAnswerCharacters = 1_000,
            MaximumParticipantCount = 12,
            MaxMultipleChoiceSelections = 8
        }).Succeeded);
    }

    [Theory]
    [InlineData("active-question-limit")]
    [InlineData("prompt-length")]
    [InlineData("option-label-length")]
    [InlineData("defined-options")]
    [InlineData("short-text")]
    [InlineData("long-text")]
    [InlineData("participant-maximum")]
    [InlineData("multiple-selections")]
    [InlineData("multiple-over-defined")]
    public void Overrides_cannot_widen_approved_hard_ceilings(string field)
    {
        var options = field switch
        {
            "active-question-limit" => new RsvpInputLimits { MaxActiveQuestionsPerInvitation = 21 },
            "prompt-length" => new RsvpInputLimits { MaxQuestionPromptCharacters = 201 },
            "option-label-length" => new RsvpInputLimits { MaxOptionLabelCharacters = 101 },
            "defined-options" => new RsvpInputLimits { MaxDefinedOptionsPerChoiceQuestion = 21 },
            "short-text" => new RsvpInputLimits { MaxShortTextAnswerCharacters = 201 },
            "long-text" => new RsvpInputLimits { MaxLongTextAnswerCharacters = 2_001 },
            "participant-maximum" => new RsvpInputLimits { MaximumParticipantCount = 21 },
            "multiple-selections" => new RsvpInputLimits { MaxMultipleChoiceSelections = 11 },
            _ => new RsvpInputLimits { MaxDefinedOptionsPerChoiceQuestion = 8, MaxMultipleChoiceSelections = 9 }
        };

        Assert.False(validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void Active_question_limit_cannot_be_lower_than_the_four_seeded_questions()
    {
        Assert.False(validator.Validate(null, new RsvpInputLimits { MaxActiveQuestionsPerInvitation = 3 }).Succeeded);
    }

    [Fact]
    public void Participant_count_minimum_stays_zero_and_maximum_may_only_be_tightened()
    {
        Assert.False(validator.Validate(null, new RsvpInputLimits { MinimumParticipantCount = 1 }).Succeeded);
        Assert.False(validator.Validate(null, new RsvpInputLimits { MaximumParticipantCount = -1 }).Succeeded);
        Assert.True(validator.Validate(null, new RsvpInputLimits { MaximumParticipantCount = 0 }).Succeeded);
    }
}
