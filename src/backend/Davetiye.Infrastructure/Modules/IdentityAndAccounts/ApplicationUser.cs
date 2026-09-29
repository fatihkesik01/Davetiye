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
/// No custom members are added beyond <see cref="IdentityUser{TKey}"/> in this milestone;
/// M6 can extend this type if the auth foundation needs additional per-user columns.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>;
