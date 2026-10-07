namespace Davetiye.Domain.Modules.Rsvp;

/// <summary>Immutable option identity/label/order snapshot retained with the answer.</summary>
public sealed class RsvpAnswerOption
{
    private RsvpAnswerOption() { }

    public Guid Id { get; private set; }
    public Guid AnswerId { get; private set; }
    public Guid OptionId { get; private set; }
    public string LabelSnapshot { get; private set; } = string.Empty;
    public int SortOrderSnapshot { get; private set; }

    internal static RsvpAnswerOption Create(Guid id, Guid answerId, RsvpQuestionOption option) => new()
    {
        Id = id,
        AnswerId = answerId,
        OptionId = option.Id,
        LabelSnapshot = option.Label,
        SortOrderSnapshot = option.SortOrder
    };
}
