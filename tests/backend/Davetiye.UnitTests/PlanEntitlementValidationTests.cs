using Davetiye.Domain.Modules.PlansAndEntitlements;
using Xunit;

namespace Davetiye.UnitTests;

/// <summary>
/// Proves Phase 1 task 8's acceptance criterion: hard-ceiling and value-type limits on
/// <see cref="PlanEntitlement"/> are genuinely enforced, not just documented. Both rules live in
/// the entity itself (see PlanEntitlement.ValidateValue), so these are pure Domain tests with no
/// database involved — the same guard runs whether or not a test hits PostgreSQL.
/// </summary>
public sealed class PlanEntitlementValidationTests
{
    [Fact]
    public void Create_accepts_a_numeric_value_at_or_below_the_hard_ceiling()
    {
        var definition = EntitlementCatalog.Require(EntitlementCatalog.MaxPublishDays);

        var entitlement = PlanEntitlement.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntitlementCatalog.MaxPublishDays,
            numericValue: definition.HardCeiling,
            booleanValue: null);

        Assert.Equal(definition.HardCeiling, entitlement.NumericValue);
        Assert.Null(entitlement.BooleanValue);
    }

    [Fact]
    public void Create_rejects_a_numeric_value_above_the_hard_ceiling()
    {
        var definition = EntitlementCatalog.Require(EntitlementCatalog.MaxPublishDays);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => PlanEntitlement.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntitlementCatalog.MaxPublishDays,
            numericValue: definition.HardCeiling + 1,
            booleanValue: null));

        Assert.Contains("hard ceiling", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_rejects_a_negative_numeric_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanEntitlement.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntitlementCatalog.MaxImages,
            numericValue: -1,
            booleanValue: null));
    }

    [Fact]
    public void Create_rejects_a_boolean_value_for_a_numeric_entitlement()
    {
        var exception = Assert.Throws<ArgumentException>(() => PlanEntitlement.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntitlementCatalog.MaxImages,
            numericValue: null,
            booleanValue: true));

        Assert.Contains("numeric", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_rejects_a_numeric_value_for_a_boolean_entitlement()
    {
        var exception = Assert.Throws<ArgumentException>(() => PlanEntitlement.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntitlementCatalog.MemoriesEnabled,
            numericValue: 1,
            booleanValue: null));

        Assert.Contains("boolean", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_accepts_a_boolean_value_for_a_boolean_entitlement()
    {
        var entitlement = PlanEntitlement.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntitlementCatalog.PremiumTemplatesEnabled,
            numericValue: null,
            booleanValue: true);

        Assert.True(entitlement.BooleanValue);
        Assert.Null(entitlement.NumericValue);
    }

    [Fact]
    public void Create_rejects_an_unsupported_entitlement_key()
    {
        Assert.Throws<ArgumentException>(() => PlanEntitlement.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "notARealEntitlementKey",
            numericValue: 1,
            booleanValue: null));
    }

    [Fact]
    public void UpdateValue_re_validates_the_hard_ceiling()
    {
        var entitlement = PlanEntitlement.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntitlementCatalog.MaxActiveInvitations,
            numericValue: 5,
            booleanValue: null);
        var definition = EntitlementCatalog.Require(EntitlementCatalog.MaxActiveInvitations);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => entitlement.UpdateValue(definition.HardCeiling + 1, null));
    }

    [Theory]
    [InlineData(EntitlementCatalog.MaxPublishDays, EntitlementValueType.Numeric)]
    [InlineData(EntitlementCatalog.MaxActiveInvitations, EntitlementValueType.Numeric)]
    [InlineData(EntitlementCatalog.MaxImages, EntitlementValueType.Numeric)]
    [InlineData(EntitlementCatalog.MaxVideos, EntitlementValueType.Numeric)]
    [InlineData(EntitlementCatalog.MaxImageSizeMb, EntitlementValueType.Numeric)]
    [InlineData(EntitlementCatalog.MaxVideoSizeMb, EntitlementValueType.Numeric)]
    [InlineData(EntitlementCatalog.MaxVideoDurationSeconds, EntitlementValueType.Numeric)]
    [InlineData(EntitlementCatalog.MaxRsvpResponses, EntitlementValueType.Numeric)]
    [InlineData(EntitlementCatalog.MemoriesEnabled, EntitlementValueType.Boolean)]
    [InlineData(EntitlementCatalog.GiftRegistryEnabled, EntitlementValueType.Boolean)]
    [InlineData(EntitlementCatalog.PremiumTemplatesEnabled, EntitlementValueType.Boolean)]
    public void Catalog_declares_the_expected_value_type_for_every_supported_key(
        string key,
        EntitlementValueType expectedValueType)
    {
        var definition = EntitlementCatalog.Require(key);

        Assert.Equal(expectedValueType, definition.ValueType);
    }
}
