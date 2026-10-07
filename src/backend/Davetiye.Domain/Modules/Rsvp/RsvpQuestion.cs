namespace Davetiye.Domain.Modules.Rsvp;

public sealed class RsvpQuestion
{
    private readonly List<RsvpQuestionOption> _options = [];

    private RsvpQuestion() { }

    public Guid Id { get; private set; }
    public Guid ConfigurationId { get; private set; }
    public string Prompt { get; private set; } = string.Empty;
    public RsvpQuestionType Type { get; private set; }
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }
    public RsvpQuestionSemanticRole? SemanticRole { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public bool IsActive => ArchivedAt is null;
    public IReadOnlyCollection<RsvpQuestionOption> Options => _options.AsReadOnly();

    internal static RsvpQuestion Create(
        Guid id, Guid configurationId, string prompt, RsvpQuestionType type,
        bool isRequired, int sortOrder, RsvpQuestionSemanticRole? semanticRole, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || configurationId == Guid.Empty)
            throw new ArgumentException("Question and configuration identifiers must not be empty.");
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (semanticRole is not null && !Enum.IsDefined(semanticRole.Value))
            throw new ArgumentOutOfRangeException(nameof(semanticRole));
        if (semanticRole == RsvpQuestionSemanticRole.ParticipantCount && type != RsvpQuestionType.Number)
            throw new InvalidOperationException("Participant count can only be assigned to a Number question.");
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        EnsureUtc(createdAt, nameof(createdAt));
        return new RsvpQuestion
        {
            Id = id,
            ConfigurationId = configurationId,
            Prompt = ValidatePrompt(prompt),
            Type = type,
            IsRequired = isRequired,
            SortOrder = sortOrder,
            SemanticRole = semanticRole,
            CreatedAt = createdAt
        };
    }

    public RsvpQuestionOption AddOption(Guid id, string label, int sortOrder, DateTimeOffset createdAt)
    {
        if (!IsActive) throw new InvalidOperationException("Options cannot be added to an archived question.");
        if (Type is not (RsvpQuestionType.SingleChoice or RsvpQuestionType.MultipleChoice))
            throw new InvalidOperationException("Only choice questions can have options.");
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        if (_options.Any(option => option.IsActive && option.SortOrder == sortOrder))
            throw new InvalidOperationException("Active option sort order must be unique within a question.");
        EnsureUtc(createdAt, nameof(createdAt));
        if (createdAt < CreatedAt) throw new ArgumentException("RSVP timestamps must be monotonic.", nameof(createdAt));
        var option = RsvpQuestionOption.Create(id, Id, label, sortOrder, createdAt);
        _options.Add(option);
        return option;
    }

    public void Update(string prompt, RsvpQuestionType type, bool isRequired,
        RsvpQuestionSemanticRole? semanticRole, DateTimeOffset changedAt)
    {
        if (!IsActive) throw new InvalidOperationException("Archived questions cannot be updated.");
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (semanticRole is not null && !Enum.IsDefined(semanticRole.Value))
            throw new ArgumentOutOfRangeException(nameof(semanticRole));
        if (semanticRole == RsvpQuestionSemanticRole.ParticipantCount && type != RsvpQuestionType.Number)
            throw new InvalidOperationException("Participant count can only be assigned to a Number question.");
        EnsureUtc(changedAt, nameof(changedAt));
        if (changedAt < CreatedAt || _options.Any(option => changedAt < option.CreatedAt))
            throw new ArgumentException("RSVP timestamps must be monotonic.", nameof(changedAt));
        if (type is not (RsvpQuestionType.SingleChoice or RsvpQuestionType.MultipleChoice) &&
            _options.Any(option => option.IsActive))
            foreach (var option in _options.Where(option => option.IsActive)) option.Archive(changedAt);
        Prompt = ValidatePrompt(prompt);
        Type = type;
        IsRequired = isRequired;
        SemanticRole = semanticRole;
    }

    internal void SetSortOrder(int sortOrder)
    {
        if (!IsActive) throw new InvalidOperationException("Archived questions cannot be reordered.");
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        SortOrder = sortOrder;
    }

    public void ArchiveOption(Guid optionId, DateTimeOffset archivedAt)
    {
        var option = _options.SingleOrDefault(item => item.Id == optionId)
            ?? throw new InvalidOperationException("Option does not belong to this question.");
        option.Archive(archivedAt);
    }

    public void PrepareOptionReplacement()
    {
        var active = _options.Where(option => option.IsActive).ToArray();
        foreach (var option in active)
            option.SetSortOrder(active.Length + option.SortOrder + 1);
    }

    public void ReplaceOptions(IReadOnlyList<(Guid? Id, string Label, int SortOrder)> options, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!IsActive) throw new InvalidOperationException("Options cannot be changed on an archived question.");
        if (Type is not (RsvpQuestionType.SingleChoice or RsvpQuestionType.MultipleChoice))
            throw new InvalidOperationException("Only choice questions can have options.");
        if (options.Count == 0 || options.Any(item => string.IsNullOrWhiteSpace(item.Label)))
            throw new ArgumentException("Choice questions require non-empty options.", nameof(options));
        if (options.Select(item => item.Id).Where(id => id is not null).Distinct().Count() != options.Count(item => item.Id is not null))
            throw new ArgumentException("Option identifiers must be unique.", nameof(options));
        if (options.Select(item => item.SortOrder).Distinct().Count() != options.Count ||
            options.Any(item => item.SortOrder < 0) ||
            !options.Select(item => item.SortOrder).Order().SequenceEqual(Enumerable.Range(0, options.Count)))
            throw new ArgumentException("Option order must be contiguous starting at zero.", nameof(options));
        EnsureUtc(changedAt, nameof(changedAt));
        if (changedAt < CreatedAt || _options.Any(option => changedAt < option.CreatedAt))
            throw new ArgumentException("RSVP timestamps must be monotonic.", nameof(changedAt));

        var requestedIds = options.Where(item => item.Id is not null).Select(item => item.Id!.Value).ToHashSet();
        var active = _options.Where(option => option.IsActive).ToArray();
        if (requestedIds.Any(id => active.All(option => option.Id != id)))
            throw new InvalidOperationException("Option is not an active member of this question.");
        foreach (var omitted in active.Where(option => !requestedIds.Contains(option.Id))) omitted.Archive(changedAt);
        foreach (var item in options)
        {
            if (item.Id is Guid id)
                _options.Single(option => option.Id == id).Update(item.Label, item.SortOrder);
            else
                _options.Add(RsvpQuestionOption.Create(Guid.NewGuid(), Id, item.Label, item.SortOrder, changedAt));
        }
    }

    internal IReadOnlyCollection<RsvpQuestionOption> GetActiveOptions(IReadOnlyCollection<Guid> optionIds)
    {
        ArgumentNullException.ThrowIfNull(optionIds);
        if (optionIds.Distinct().Count() != optionIds.Count)
            throw new ArgumentException("Selected option identifiers must be unique.", nameof(optionIds));
        var selected = _options.Where(option => optionIds.Contains(option.Id) && option.IsActive).ToArray();
        if (selected.Length != optionIds.Count)
            throw new ArgumentException("Selected options must be active members of the question.", nameof(optionIds));
        return selected;
    }

    internal void Archive(DateTimeOffset archivedAt)
    {
        if (!IsActive) return;
        EnsureCanArchiveAt(archivedAt);
        ArchivedAt = archivedAt;
        foreach (var option in _options) option.Archive(archivedAt);
    }

    internal void EnsureCanArchiveAt(DateTimeOffset archivedAt)
    {
        EnsureUtc(archivedAt, nameof(archivedAt));
        if (archivedAt < CreatedAt || _options.Any(option => archivedAt < option.CreatedAt))
            throw new ArgumentException("RSVP timestamps must be monotonic.", nameof(archivedAt));
    }

    private static string ValidatePrompt(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Question prompt is required.", nameof(value));
        return value.Trim();
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException("RSVP timestamps must be UTC.", parameterName);
    }
}
