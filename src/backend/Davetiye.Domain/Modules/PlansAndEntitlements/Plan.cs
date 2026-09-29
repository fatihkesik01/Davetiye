namespace Davetiye.Domain.Modules.PlansAndEntitlements;

/// <summary>
/// The DB-managed commercial identity of a plan (key, display name, active flag) per ADR-0004:
/// "fiyat ve ticari limit değerleri PlanEntitlement olarak DB'dedir." Price itself is not modeled
/// here — seeding actual commercial values (docs/PRODUCT.md §19's Free/Standard/Premium/Organization
/// table) is a later business milestone's job, not this schema milestone's.
/// </summary>
public sealed class Plan
{
    private Plan()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Stable machine key, e.g. "free", "standard". Unique.</summary>
    public string Key { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public long Revision { get; private set; }

    public static Plan Create(Guid id, string key, string displayName, bool isActive)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Plan id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Plan key is required.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Plan display name is required.", nameof(displayName));
        }

        return new Plan
        {
            Id = id,
            Key = key.Trim(),
            DisplayName = displayName.Trim(),
            IsActive = isActive
        };
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
