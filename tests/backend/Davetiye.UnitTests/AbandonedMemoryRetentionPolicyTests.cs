using Davetiye.Domain.Modules.Memories;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class AbandonedMemoryRetentionPolicyTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("30", 30)]
    [InlineData("365", 365)]
    public void Integer_days_within_configured_range_are_accepted(string value, int expected) =>
        Assert.Equal(expected, AbandonedMemoryRetentionPolicy.ReadDays("Integer", value));

    [Theory]
    [InlineData("Boolean", "30")]
    [InlineData("String", "30")]
    [InlineData("Integer", "-1")]
    [InlineData("Integer", "366")]
    [InlineData("Integer", "thirty")]
    public void Wrong_type_or_out_of_range_value_is_rejected(string type, string value) =>
        Assert.Null(AbandonedMemoryRetentionPolicy.ReadDays(type, value));
}
