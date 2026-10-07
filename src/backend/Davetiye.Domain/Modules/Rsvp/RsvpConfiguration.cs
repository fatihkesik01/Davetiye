namespace Davetiye.Domain.Modules.Rsvp;

/// <summary>RSVP-owned configuration for one invitation. Invitation is referenced by ID only.</summary>
public sealed class RsvpConfiguration
{
    private readonly List<RsvpQuestion> _questions = [];

    private RsvpConfiguration() { }

    public Guid Id { get; private set; }
    public Guid InvitationId { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyCollection<RsvpQuestion> Questions => _questions.AsReadOnly();

    public static RsvpConfiguration Create(Guid id, Guid invitationId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || invitationId == Guid.Empty)
            throw new ArgumentException("Configuration and invitation identifiers must not be empty.");
        EnsureUtc(createdAt, nameof(createdAt));
        return new RsvpConfiguration
        {
            Id = id,
            InvitationId = invitationId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    public void SetEnabled(bool enabled, DateTimeOffset changedAt)
    {
        EnsureUtc(changedAt, nameof(changedAt));
        EnsureNotBefore(changedAt, UpdatedAt, nameof(changedAt));
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        Touch(changedAt);
    }

    public void ReorderQuestions(IReadOnlyList<Guid> orderedQuestionIds, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(orderedQuestionIds);
        EnsureUtc(changedAt, nameof(changedAt));
        EnsureNotBefore(changedAt, UpdatedAt, nameof(changedAt));
        var active = _questions.Where(question => question.IsActive).ToArray();
        if (orderedQuestionIds.Count != active.Length || orderedQuestionIds.Distinct().Count() != active.Length ||
            active.Any(question => !orderedQuestionIds.Contains(question.Id)))
            throw new InvalidOperationException("Order must contain each active question exactly once.");
        for (var index = 0; index < orderedQuestionIds.Count; index++)
            GetActiveQuestion(orderedQuestionIds[index]).SetSortOrder(index);
        TouchRevision(changedAt);
    }

    public void PrepareQuestionReorder()
    {
        var active = _questions.Where(question => question.IsActive).ToArray();
        foreach (var question in active)
            question.SetSortOrder(active.Length + question.SortOrder + 1);
    }

    public void UpdateQuestion(Guid questionId, string prompt, RsvpQuestionType type, bool isRequired,
        RsvpQuestionSemanticRole? semanticRole, DateTimeOffset changedAt)
    {
        EnsureUtc(changedAt, nameof(changedAt));
        EnsureNotBefore(changedAt, UpdatedAt, nameof(changedAt));
        var question = GetActiveQuestion(questionId);
        if (semanticRole == RsvpQuestionSemanticRole.ParticipantCount && type != RsvpQuestionType.Number)
            throw new InvalidOperationException("Participant count can only be assigned to a Number question.");
        if (semanticRole == RsvpQuestionSemanticRole.ParticipantCount &&
            _questions.Any(other => other.IsActive && other.Id != questionId &&
                other.SemanticRole == RsvpQuestionSemanticRole.ParticipantCount))
            throw new InvalidOperationException("Only one active participant-count question is allowed per configuration.");
        question.Update(prompt, type, isRequired, semanticRole, changedAt);
        TouchRevision(changedAt);
    }

    public RsvpQuestion AddQuestion(
        Guid questionId, string prompt, RsvpQuestionType type, bool isRequired,
        int sortOrder, RsvpQuestionSemanticRole? semanticRole, DateTimeOffset createdAt)
    {
        EnsureUtc(createdAt, nameof(createdAt));
        EnsureNotBefore(createdAt, UpdatedAt, nameof(createdAt));
        EnsureAvailableOrder(sortOrder);
        if (semanticRole == RsvpQuestionSemanticRole.ParticipantCount &&
            _questions.Any(question => question.IsActive &&
                question.SemanticRole == RsvpQuestionSemanticRole.ParticipantCount))
            throw new InvalidOperationException("Only one active participant-count question is allowed per configuration.");

        var question = RsvpQuestion.Create(
            questionId, Id, prompt, type, isRequired, sortOrder, semanticRole, createdAt);
        _questions.Add(question);
        Touch(createdAt);
        return question;
    }

    public void ArchiveQuestion(Guid questionId, DateTimeOffset archivedAt)
    {
        EnsureUtc(archivedAt, nameof(archivedAt));
        EnsureNotBefore(archivedAt, UpdatedAt, nameof(archivedAt));
        var question = _questions.SingleOrDefault(item => item.Id == questionId)
            ?? throw new InvalidOperationException("Question does not belong to this RSVP configuration.");
        if (!question.IsActive) return;
        question.Archive(archivedAt);
        Touch(archivedAt);
    }

    /// <summary>Returns the canonical active question owned by this configuration.</summary>
    public RsvpQuestion GetActiveQuestion(Guid questionId) =>
        _questions.SingleOrDefault(question => question.Id == questionId && question.IsActive)
        ?? throw new InvalidOperationException("Question is not an active member of this RSVP configuration.");

    private void EnsureAvailableOrder(int sortOrder)
    {
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        if (_questions.Any(question => question.IsActive && question.SortOrder == sortOrder))
            throw new InvalidOperationException("Active question sort order must be unique within a configuration.");
    }

    private void Touch(DateTimeOffset at)
    {
        TouchRevision(at);
    }

    private void TouchRevision(DateTimeOffset at)
    {
        UpdatedAt = at;
        Revision++;
    }

    private static void EnsureNotBefore(DateTimeOffset value, DateTimeOffset minimum, string parameterName)
    {
        if (value < minimum) throw new ArgumentException("RSVP timestamps must be monotonic.", parameterName);
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("RSVP timestamps must be UTC.", parameterName);
    }
}
