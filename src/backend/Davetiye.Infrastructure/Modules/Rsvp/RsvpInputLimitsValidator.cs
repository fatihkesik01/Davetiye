using Davetiye.Domain.Modules.Rsvp;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Modules.Rsvp;

public sealed class RsvpInputLimitsValidator : IValidateOptions<RsvpInputLimits>
{
    public ValidateOptionsResult Validate(string? name, RsvpInputLimits options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        Range(nameof(options.MaxActiveQuestionsPerInvitation), options.MaxActiveQuestionsPerInvitation,
            RsvpInputLimits.SeededDefaultQuestionCount, RsvpInputLimits.ApprovedMaxActiveQuestionsPerInvitation, failures);
        Range(nameof(options.MaxQuestionPromptCharacters), options.MaxQuestionPromptCharacters, 1,
            RsvpInputLimits.ApprovedMaxQuestionPromptCharacters, failures);
        Range(nameof(options.MaxOptionLabelCharacters), options.MaxOptionLabelCharacters, 1,
            RsvpInputLimits.ApprovedMaxOptionLabelCharacters, failures);
        Range(nameof(options.MaxDefinedOptionsPerChoiceQuestion), options.MaxDefinedOptionsPerChoiceQuestion, 1,
            RsvpInputLimits.ApprovedMaxDefinedOptionsPerChoiceQuestion, failures);
        Range(nameof(options.MaxShortTextAnswerCharacters), options.MaxShortTextAnswerCharacters, 1,
            RsvpInputLimits.ApprovedMaxShortTextAnswerCharacters, failures);
        Range(nameof(options.MaxLongTextAnswerCharacters), options.MaxLongTextAnswerCharacters, 1,
            RsvpInputLimits.ApprovedMaxLongTextAnswerCharacters, failures);
        Range(nameof(options.MaxMultipleChoiceSelections), options.MaxMultipleChoiceSelections, 1,
            RsvpInputLimits.ApprovedMaxMultipleChoiceSelections, failures);
        if (options.MaxMultipleChoiceSelections > options.MaxDefinedOptionsPerChoiceQuestion)
            failures.Add($"{RsvpInputLimits.SectionName}:{nameof(options.MaxMultipleChoiceSelections)} must not exceed {nameof(options.MaxDefinedOptionsPerChoiceQuestion)}.");
        if (options.MinimumParticipantCount != RsvpInputLimits.ApprovedMinimumParticipantCount)
            failures.Add($"{RsvpInputLimits.SectionName}:{nameof(options.MinimumParticipantCount)} must remain 0 so guests can decline.");
        if (options.MaximumParticipantCount < options.MinimumParticipantCount ||
            options.MaximumParticipantCount > RsvpInputLimits.ApprovedMaximumParticipantCount)
            failures.Add($"{RsvpInputLimits.SectionName}:{nameof(options.MaximumParticipantCount)} must be between the minimum and {RsvpInputLimits.ApprovedMaximumParticipantCount}.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Range(string name, int value, int minimum, int maximum, ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
            failures.Add($"{RsvpInputLimits.SectionName}:{name} must be between {minimum} and {maximum}.");
    }
}
