namespace Davetiye.Infrastructure.Modules.Media;

/// <summary>Process-local keyset cursor so bounded reconciliation batches make progress across all assets.</summary>
public sealed class MediaReconciliationCursor
{
    private readonly object gate = new();
    private DateTimeOffset? lastCreatedAt;
    private Guid lastId;
    private Guid? lastTerminalMessageId;

    public (DateTimeOffset? CreatedAt, Guid Id) Read()
    {
        lock (gate) return (lastCreatedAt, lastId);
    }

    public void Advance(DateTimeOffset createdAt, Guid id)
    {
        lock (gate)
        {
            lastCreatedAt = createdAt;
            lastId = id;
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            lastCreatedAt = null;
            lastId = Guid.Empty;
        }
    }

    public Guid? ReadTerminal()
    {
        lock (gate) return lastTerminalMessageId;
    }

    public void AdvanceTerminal(Guid messageId)
    {
        lock (gate) lastTerminalMessageId = messageId;
    }

    public void ResetTerminal()
    {
        lock (gate) lastTerminalMessageId = null;
    }
}
