using Davetiye.Domain.Modules.PlansAndEntitlements;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class SystemSettingTests
{
    [Fact]
    public void Create_rejects_a_non_integer_value_for_an_integer_setting()
    {
        Assert.Throws<ArgumentException>(() => SystemSetting.Create(
            Guid.NewGuid(),
            "deletedInvitationRetentionDays",
            SystemSettingValueType.Integer,
            "not-a-number"));
    }

    [Fact]
    public void Create_rejects_a_non_boolean_value_for_a_boolean_setting()
    {
        Assert.Throws<ArgumentException>(() => SystemSetting.Create(
            Guid.NewGuid(),
            "someToggle",
            SystemSettingValueType.Boolean,
            "not-a-boolean"));
    }

    [Fact]
    public void AsInteger_reads_back_a_valid_integer_setting()
    {
        var setting = SystemSetting.Create(
            Guid.NewGuid(),
            "deletedInvitationRetentionDays",
            SystemSettingValueType.Integer,
            "3");

        Assert.Equal(3, setting.AsInteger());
    }

    [Fact]
    public void AsInteger_throws_when_the_setting_is_not_typed_as_integer()
    {
        var setting = SystemSetting.Create(
            Guid.NewGuid(),
            "someToggle",
            SystemSettingValueType.Boolean,
            "true");

        Assert.Throws<InvalidOperationException>(() => setting.AsInteger());
    }

    [Fact]
    public void UpdateValue_re_validates_against_the_declared_type()
    {
        var setting = SystemSetting.Create(
            Guid.NewGuid(),
            "deletedInvitationRetentionDays",
            SystemSettingValueType.Integer,
            "3");

        Assert.Throws<ArgumentException>(() => setting.UpdateValue("not-a-number"));
    }
}
