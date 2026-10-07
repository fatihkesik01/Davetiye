namespace Davetiye.Domain.Modules.Rsvp;

/// <summary>Typed, overrideable RSVP validation limits. Product-approved defaults are independent of plan quotas.</summary>
public sealed class RsvpInputLimits
{
    public const string SectionName = "RsvpValidation";
    public const int ApprovedMaxActiveQuestionsPerInvitation = 20;
    public const int SeededDefaultQuestionCount = 4;
    public const int ApprovedMaxQuestionPromptCharacters = 200;
    public const int ApprovedMaxOptionLabelCharacters = 100;
    public const int ApprovedMaxDefinedOptionsPerChoiceQuestion = 20;
    public const int ApprovedMaxShortTextAnswerCharacters = 200;
    public const int ApprovedMaxLongTextAnswerCharacters = 2_000;
    public const int ApprovedMinimumParticipantCount = 0;
    public const int ApprovedMaximumParticipantCount = 20;
    public const int ApprovedMaxMultipleChoiceSelections = 10;

    public int MaxActiveQuestionsPerInvitation { get; init; } = ApprovedMaxActiveQuestionsPerInvitation;
    public int MaxQuestionPromptCharacters { get; init; } = ApprovedMaxQuestionPromptCharacters;
    public int MaxOptionLabelCharacters { get; init; } = ApprovedMaxOptionLabelCharacters;
    public int MaxDefinedOptionsPerChoiceQuestion { get; init; } = ApprovedMaxDefinedOptionsPerChoiceQuestion;
    public int MaxShortTextAnswerCharacters { get; init; } = ApprovedMaxShortTextAnswerCharacters;
    public int MaxLongTextAnswerCharacters { get; init; } = ApprovedMaxLongTextAnswerCharacters;
    public int MinimumParticipantCount { get; init; } = ApprovedMinimumParticipantCount;
    public int MaximumParticipantCount { get; init; } = ApprovedMaximumParticipantCount;
    public int MaxMultipleChoiceSelections { get; init; } = ApprovedMaxMultipleChoiceSelections;

    public bool IsPromptWithinLimit(string? prompt) => prompt is not null &&
        prompt.Trim().Length <= MaxQuestionPromptCharacters;

    public bool IsOptionLabelWithinLimit(string? label) => label is not null &&
        label.Trim().Length <= MaxOptionLabelCharacters;

    public bool IsActiveQuestionCountWithinLimit(int count) => count <= MaxActiveQuestionsPerInvitation;
    public bool IsDefinedOptionCountWithinLimit(int count) => count <= MaxDefinedOptionsPerChoiceQuestion;

    /// <summary>Returns a validation message, or null when a supplied answer fits the canonical question.</summary>
    public string? ValidateAnswer(RsvpQuestion question, string? textValue, decimal? numberValue,
        IReadOnlyCollection<Guid> selectedOptionIds)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(selectedOptionIds);

        if (question.Type == RsvpQuestionType.ShortText && textValue is not null &&
            textValue.Length > MaxShortTextAnswerCharacters)
            return $"Short text answers may contain at most {MaxShortTextAnswerCharacters} characters.";
        if (question.Type == RsvpQuestionType.LongText && textValue is not null &&
            textValue.Length > MaxLongTextAnswerCharacters)
            return $"Long text answers may contain at most {MaxLongTextAnswerCharacters} characters.";
        if (question.SemanticRole == RsvpQuestionSemanticRole.ParticipantCount && numberValue is decimal count &&
            (decimal.Truncate(count) != count || count < MinimumParticipantCount || count > MaximumParticipantCount))
            return $"Participant count must be an integer between {MinimumParticipantCount} and {MaximumParticipantCount}.";
        if (question.Type == RsvpQuestionType.MultipleChoice && selectedOptionIds.Count > MaxMultipleChoiceSelections)
            return $"At most {MaxMultipleChoiceSelections} choices may be selected.";
        return null;
    }
}
