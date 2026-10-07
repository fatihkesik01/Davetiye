namespace Davetiye.Domain.Modules.Rsvp;

public sealed class RsvpQuestionOption
{
    private RsvpQuestionOption() { }

    public Guid Id { get; private set; }
    public Guid QuestionId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public bool IsActive => ArchivedAt is null;

    internal void SetSortOrder(int sortOrder)
    {
        if (!IsActive) throw new InvalidOperationException("Archived options cannot be reordered.");
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        SortOrder = sortOrder;
    }

    internal void Update(string label, int sortOrder)
    {
        if (!IsActive) throw new InvalidOperationException("Archived options cannot be updated.");
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Option label is required.", nameof(label));
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        Label = label.Trim();
        SortOrder = sortOrder;
    }

    internal static RsvpQuestionOption Create(Guid id, Guid questionId, string label, int sortOrder, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || questionId == Guid.Empty)
            throw new ArgumentException("Option and question identifiers must not be empty.");
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Option label is required.", nameof(label));
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        if (createdAt.Offset != TimeSpan.Zero) throw new ArgumentException("RSVP timestamps must be UTC.", nameof(createdAt));
        return new RsvpQuestionOption { Id = id, QuestionId = questionId, Label = label.Trim(), SortOrder = sortOrder, CreatedAt = createdAt };
    }

    internal void Archive(DateTimeOffset archivedAt)
    {
        if (archivedAt.Offset != TimeSpan.Zero) throw new ArgumentException("RSVP timestamps must be UTC.", nameof(archivedAt));
        if (archivedAt < CreatedAt) throw new ArgumentException("RSVP timestamps must be monotonic.", nameof(archivedAt));
        ArchivedAt ??= archivedAt;
    }
}
