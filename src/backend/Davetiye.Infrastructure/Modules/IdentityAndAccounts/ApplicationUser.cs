using Microsoft.AspNetCore.Identity;

namespace Davetiye.Infrastructure.Modules.IdentityAndAccounts;

/// <summary>
/// The ASP.NET Core Identity authentication-user type composed into
/// <see cref="Davetiye.Infrastructure.Persistence.DavetiyeDbContext"/>. This is Infrastructure's
/// persistence concern, not Domain's: Domain's <c>Account</c> entity stays framework-free and
/// references this row only by its <c>Guid</c> id (see Account.IdentityUserId), never by
/// navigation.
///
/// Registration/login/password-reset/Google-OAuth endpoints and <c>SignInManager</c>/cookie
/// wiring are explicitly out of this milestone (M5A) — that is Milestone M6's job. This type and
/// its EF configuration exist only so the persistence schema Identity needs is in place.
///
/// UI preferences live on the authenticated Identity row so they are available to Creator and
/// Super Admin users, including users without a Domain Account.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string PreferredLocale { get; private set; } = "tr";

    public string PreferredColorTheme { get; private set; } = "kutlio";

    public string PreferredAppearance { get; private set; } = "light";

    public string? PreferredAvatar { get; private set; }

    public void UpdateUiPreferences(string locale, string colorTheme, string appearance, string? avatar)
    {
        if (locale is not ("tr" or "en"))
        {
            throw new ArgumentOutOfRangeException(nameof(locale), locale, "Locale must be 'tr' or 'en'.");
        }

        if (colorTheme is not ("kutlio" or "sage" or "rose" or "ocean" or "plum" or "gold"))
        {
            throw new ArgumentOutOfRangeException(nameof(colorTheme), colorTheme, "Color theme is not supported.");
        }

        if (appearance is not ("system" or "light" or "dark"))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance), appearance, "Appearance must be 'system', 'light', or 'dark'.");
        }

        if (avatar is not null and not ("sunny" or "mint" or "berry" or "sky" or "coral" or "lilac" or
            "amber" or "forest" or "night" or "rose" or "slate" or "peach"))
        {
            throw new ArgumentOutOfRangeException(nameof(avatar), avatar, "Avatar is not supported.");
        }

        PreferredLocale = locale;
        PreferredColorTheme = colorTheme;
        PreferredAppearance = appearance;
        PreferredAvatar = avatar;
    }
}
