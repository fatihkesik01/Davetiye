namespace Davetiye.Domain.Modules.Rsvp;

/// <summary>Anonymous response root. InvitationId is an ownership reference, not authorization.</summary>
public sealed class RsvpSubmission
{
    private readonly List<RsvpAnswer> _answers = [];

    private RsvpSubmission() { }

    public Guid Id { get; private set; }
    public Guid InvitationId { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyCollection<RsvpAnswer> Answers => _answers.AsReadOnly();

    /// <summary>Replaces the answer set through the submission capability without changing the original submit time.</summary>
    public void BeginAnswerReplacement(DateTimeOffset updatedAt)
    {
        EnsureUtc(updatedAt, nameof(updatedAt));
        if (updatedAt < UpdatedAt) throw new ArgumentException("RSVP timestamps must be monotonic.", nameof(updatedAt));
        _answers.Clear();
        UpdatedAt = updatedAt;
        Revision++;
    }

    public static RsvpSubmission Create(Guid id, Guid invitationId, DateTimeOffset submittedAt)
    {
        if (id == Guid.Empty || invitationId == Guid.Empty)
            throw new ArgumentException("Submission and invitation identifiers must not be empty.");
        EnsureUtc(submittedAt, nameof(submittedAt));
        return new RsvpSubmission { Id = id, InvitationId = invitationId, SubmittedAt = submittedAt, UpdatedAt = submittedAt };
    }

    public RsvpAnswer AddAnswer(Guid answerId, RsvpConfiguration configuration, Guid questionId,
        DateTimeOffset answeredAt, string? textValue = null,
        decimal? numberValue = null, bool? booleanValue = null,
        IReadOnlyCollection<Guid>? selectedOptionIds = null, RsvpInputLimits? inputLimits = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureUtc(answeredAt, nameof(answeredAt));
        if (answeredAt < UpdatedAt)
            throw new ArgumentException("RSVP timestamps must be monotonic.", nameof(answeredAt));
        if (configuration.InvitationId != InvitationId)
            throw new InvalidOperationException("RSVP configuration belongs to a different invitation.");
        var question = configuration.GetActiveQuestion(questionId);
        if (!question.IsActive) throw new InvalidOperationException("A new answer cannot target an archived question.");
        if (_answers.Any(answer => answer.QuestionId == question.Id))
            throw new InvalidOperationException("A submission can have only one answer per question.");
        var requestedOptionIds = selectedOptionIds ?? [];
        var limitFailure = (inputLimits ?? new RsvpInputLimits()).ValidateAnswer(question, textValue, numberValue, requestedOptionIds);
        if (limitFailure is not null) throw new ArgumentException(limitFailure, nameof(textValue));
        var selectedOptions = question.GetActiveOptions(requestedOptionIds);
        var answer = RsvpAnswer.Create(answerId, Id, question, textValue, numberValue, booleanValue, selectedOptions);
        _answers.Add(answer);
        UpdatedAt = answeredAt;
        Revision++;
        return answer;
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException("RSVP timestamps must be UTC.", parameterName);
    }
}
