using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class ApplicationUserUiPreferencesTests
{
    private static readonly string[] AvatarKeys =
        ["sunny", "mint", "berry", "sky", "coral", "lilac", "amber", "forest", "night", "rose", "slate", "peach"];

    [Fact]
    public void New_user_defaults_to_tr_kutlio_light()
    {
        var user = new ApplicationUser();

        Assert.Equal("tr", user.PreferredLocale);
        Assert.Equal("kutlio", user.PreferredColorTheme);
        Assert.Equal("light", user.PreferredAppearance);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("light")]
    [InlineData("dark")]
    public void Every_appearance_in_the_allow_list_remains_selectable(string appearance)
    {
        var user = new ApplicationUser();

        user.UpdateUiPreferences("tr", "kutlio", appearance, null);

        Assert.Equal(appearance, user.PreferredAppearance);
    }

    [Fact]
    public void New_user_has_no_avatar()
    {
        Assert.Null(new ApplicationUser().PreferredAvatar);
    }

    [Fact]
    public void Every_preset_avatar_key_is_accepted_and_null_clears_the_choice()
    {
        var user = new ApplicationUser();

        foreach (var key in AvatarKeys)
        {
            user.UpdateUiPreferences("tr", "kutlio", "system", key);
            Assert.Equal(key, user.PreferredAvatar);
        }

        user.UpdateUiPreferences("tr", "kutlio", "system", null);
        Assert.Null(user.PreferredAvatar);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sunny")]
    [InlineData(" sunny")]
    [InlineData("sunny ")]
    [InlineData("unknown")]
    public void Unsupported_avatar_values_are_rejected_without_changing_any_preference(string avatar)
    {
        var user = new ApplicationUser();
        user.UpdateUiPreferences("en", "sage", "dark", "mint");

        Assert.Throws<ArgumentOutOfRangeException>(() => user.UpdateUiPreferences("tr", "plum", "light", avatar));

        Assert.Equal("en", user.PreferredLocale);
        Assert.Equal("sage", user.PreferredColorTheme);
        Assert.Equal("dark", user.PreferredAppearance);
        Assert.Equal("mint", user.PreferredAvatar);
    }
}
