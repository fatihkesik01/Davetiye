namespace Davetiye.Domain.Modules.Media;

/// <summary>
/// Separates Creator invitation-media usage from future Guest-memory usage. This is only a
/// quota partition; it does not define or enable a Guest upload workflow.
/// </summary>
public enum MediaQuotaScope
{
    Creator,
    Guest
}
