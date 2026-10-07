namespace Davetiye.Domain.Modules.PlansAndEntitlements;

/// <summary>
/// The DB-managed commercial identity and price of a plan. Supported billing shapes are code-owned,
/// while price and currency remain editable DB values and therefore require no deployment.
/// </summary>
public sealed class Plan
{
    public const int DescriptionMaxLength = 2000;
    public const decimal MaxPriceAmount = 999_999_999_999_999.9999m;

    private Plan()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Stable machine key, e.g. "free", "standard". Unique.</summary>
    public string Key { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public decimal PriceAmount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public PlanBillingKind BillingKind { get; private set; }

    public long Revision { get; private set; }

    public static Plan Create(
        Guid id,
        string key,
        string displayName,
        bool isActive,
        decimal priceAmount,
        string currency,
        PlanBillingKind billingKind,
        string? description = null)
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

        var normalizedCurrency = ValidateCommercialTerms(priceAmount, currency, billingKind);
        var normalizedDescription = NormalizeDescription(description);

        return new Plan
        {
            Id = id,
            Key = key.Trim(),
            DisplayName = displayName.Trim(),
            IsActive = isActive,
            PriceAmount = priceAmount,
            Currency = normalizedCurrency,
            BillingKind = billingKind,
            Description = normalizedDescription
        };
    }

    public void SetActive(bool isActive)
    {
        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;
        Revision++;
    }

    public void UpdateCommercialTerms(decimal priceAmount, string currency, PlanBillingKind billingKind)
    {
        var normalizedCurrency = ValidateCommercialTerms(priceAmount, currency, billingKind);
        if (PriceAmount == priceAmount && Currency == normalizedCurrency && BillingKind == billingKind)
        {
            return;
        }

        Currency = normalizedCurrency;
        PriceAmount = priceAmount;
        BillingKind = billingKind;
        Revision++;
    }

    public void UpdateDescription(string? description)
    {
        var normalizedDescription = NormalizeDescription(description);
        if (Description == normalizedDescription)
        {
            return;
        }

        Description = normalizedDescription;
        Revision++;
    }

    public void UpdateAdminMetadata(string displayName, string? description, decimal priceAmount, PlanBillingKind billingKind)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200)
            throw new ArgumentException("Plan display name is required and must be at most 200 characters.", nameof(displayName));
        if (!Enum.IsDefined(billingKind))
            throw new ArgumentOutOfRangeException(nameof(billingKind));
        ValidatePrice(priceAmount, billingKind);
        if (billingKind != PlanBillingKind.Free && priceAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(priceAmount), priceAmount, "Paid plans must have a positive price.");
        var normalizedDescription = NormalizeDescription(description);
        var normalizedName = displayName.Trim();
        if (DisplayName == normalizedName && Description == normalizedDescription && PriceAmount == priceAmount && BillingKind == billingKind)
            return;
        DisplayName = normalizedName;
        Description = normalizedDescription;
        PriceAmount = priceAmount;
        BillingKind = billingKind;
        Revision++;
    }

    public void AdvanceRevision() => Revision++;

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var normalized = description.Trim();
        if (normalized.Length > DescriptionMaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(description), description, $"Plan description must be at most {DescriptionMaxLength} characters.");
        }

        return normalized;
    }

    private static string ValidateCommercialTerms(
        decimal priceAmount,
        string currency,
        PlanBillingKind billingKind)
    {
        if (!Enum.IsDefined(billingKind))
        {
            throw new ArgumentOutOfRangeException(nameof(billingKind), billingKind, "Unsupported billing kind.");
        }

        ValidatePrice(priceAmount, billingKind);

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Plan currency is required.", nameof(currency));
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || normalizedCurrency.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Plan currency must be a three-letter ISO-style code.", nameof(currency));
        }

        return normalizedCurrency;
    }

    private static void ValidatePrice(decimal priceAmount, PlanBillingKind billingKind)
    {
        if (priceAmount < 0 || priceAmount > MaxPriceAmount || decimal.Round(priceAmount, 4) != priceAmount)
            throw new ArgumentOutOfRangeException(nameof(priceAmount), priceAmount,
                $"Plan price must fit numeric(19,4), between 0 and {MaxPriceAmount}.");
        if (billingKind == PlanBillingKind.Free && priceAmount != 0)
            throw new ArgumentException("A free plan must have a zero price.", nameof(priceAmount));
    }
}
