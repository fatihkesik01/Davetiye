namespace Davetiye.Domain.Modules.Rsvp;

public sealed class RsvpAnswer
{
    private readonly List<RsvpAnswerOption> _selectedOptions = [];

    private RsvpAnswer() { }

    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid QuestionId { get; private set; }
    public string QuestionPromptSnapshot { get; private set; } = string.Empty;
    public RsvpQuestionType QuestionTypeSnapshot { get; private set; }
    public bool IsRequiredSnapshot { get; private set; }
    public RsvpQuestionSemanticRole? SemanticRoleSnapshot { get; private set; }
    public string? TextValue { get; private set; }
    public decimal? NumberValue { get; private set; }
    public bool? BooleanValue { get; private set; }
    public IReadOnlyCollection<RsvpAnswerOption> SelectedOptions => _selectedOptions.AsReadOnly();

    internal static RsvpAnswer Create(Guid id, Guid submissionId, RsvpQuestion question,
        string? textValue, decimal? numberValue, bool? booleanValue,
        IReadOnlyCollection<RsvpQuestionOption>? selectedOptions)
    {
        if (id == Guid.Empty || submissionId == Guid.Empty) throw new ArgumentException("Answer identifiers must not be empty.");
        var selected = selectedOptions?.ToArray() ?? [];
        ValidateValue(question, textValue, numberValue, booleanValue, selected);
        var answer = new RsvpAnswer
        {
            Id = id,
            SubmissionId = submissionId,
            QuestionId = question.Id,
            QuestionPromptSnapshot = question.Prompt,
            QuestionTypeSnapshot = question.Type,
            IsRequiredSnapshot = question.IsRequired,
            SemanticRoleSnapshot = question.SemanticRole,
            TextValue = textValue,
            NumberValue = numberValue,
            BooleanValue = booleanValue
        };
        foreach (var option in selected)
            answer._selectedOptions.Add(RsvpAnswerOption.Create(Guid.NewGuid(), answer.Id, option));
        return answer;
    }

    private static void ValidateValue(RsvpQuestion question, string? text, decimal? number, bool? boolean,
        IReadOnlyCollection<RsvpQuestionOption> options)
    {
        var hasText = !string.IsNullOrWhiteSpace(text);
        var valid = question.Type switch
        {
            RsvpQuestionType.ShortText or RsvpQuestionType.LongText =>
                hasText && number is null && boolean is null && options.Count == 0,
            RsvpQuestionType.Number => number is not null && text is null && boolean is null && options.Count == 0,
            RsvpQuestionType.YesNo => boolean is not null && text is null && number is null && options.Count == 0,
            RsvpQuestionType.SingleChoice => options.Count == 1 && text is null && number is null && boolean is null,
            RsvpQuestionType.MultipleChoice => options.Count > 0 && text is null && number is null && boolean is null,
            _ => false
        };
        if (!valid) throw new ArgumentException("Answer value does not match the question type.");
        if (options.Select(option => option.Id).Distinct().Count() != options.Count ||
            options.Any(option => !question.Options.Any(known => known.Id == option.Id && known.IsActive)))
            throw new ArgumentException("Selected options must be unique active options belonging to the question.", nameof(options));
    }
}
