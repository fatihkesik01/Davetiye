namespace Davetiye.Infrastructure.Modules.Notifications;

/// <summary>Bounds provider retries to less than Resend's documented 24-hour idempotency retention.</summary>
public static class EmailOutboxDeliveryPolicy
{
    public static bool IsOverdue(DateTimeOffset createdAt, DateTimeOffset now, int maximumDeliveryAgeHours)
    {
        if (createdAt.Offset != TimeSpan.Zero || now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Email outbox timestamps must be UTC.");
        var safeHours = Math.Clamp(maximumDeliveryAgeHours, 1, 23);
        return now >= createdAt.AddHours(safeHours);
    }
}
