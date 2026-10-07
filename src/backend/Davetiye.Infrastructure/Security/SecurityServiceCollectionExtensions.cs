using System.Security.Claims;
using System.Threading.RateLimiting;
using Davetiye.Application.Modules.IdentityAndAccounts.Contracts;
using Davetiye.Infrastructure.Modules.IdentityAndAccounts;
using Davetiye.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Davetiye.Infrastructure.Security;

/// <summary>
/// M6a/M6b's security foundation wiring: Identity DI (via <c>AddIdentityCore</c>, not the full
/// <c>AddIdentity</c> — see <see cref="ApplicationUser"/>'s doc comment for why), cookie
/// authentication, Google external login, the "SuperAdminOnly"/"MfaComplete" authorization
/// policies, antiforgery, CORS, route-class rate limiting and persistent Data Protection.
/// Everything here is configuration-driven through the validated options types in this namespace;
/// no magic number is hardcoded at the point of use. Admin bootstrap itself
/// (<c>tools/Davetiye.AdminBootstrap</c>) and the MFA enrollment/verification endpoints are wired
/// elsewhere (<c>DependencyInjection.AddInfrastructure</c>, Davetiye.Api's composition root); this
/// file only owns the DI/authentication/authorization plumbing they depend on.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddAuthSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var isProduction = environment.IsProduction();

        services.AddAuthSecurityOptions(configuration, isProduction);
        AddDataProtection(services, configuration, environment);

        services.AddHttpContextAccessor();

        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<DavetiyeDbContext>()
            .AddSignInManager<DavetiyeSignInManager>()
            .AddDefaultTokenProviders();

        // AddSignInManager<T> only registers T as the base SignInManager<ApplicationUser> service
        // (see IdentityBuilder.AddSignInManager<T>'s implementation). Explicitly register the
        // concrete DavetiyeSignInManager too, resolving to that exact same per-scope instance, so
        // callers that need the Admin-MFA-specific members (AuthAccountService, AdminMfaService) can
        // depend on it directly without a second, inconsistent SignInManager instance existing in the
        // same request scope.
        services.AddScoped(serviceProvider =>
            (DavetiyeSignInManager)serviceProvider.GetRequiredService<SignInManager<ApplicationUser>>());

        services.AddOptions<IdentityOptions>().Configure<IOptions<IdentityPolicyOptions>>(
            (identityOptions, policyOptions) =>
            {
                var policy = policyOptions.Value;

                identityOptions.Password.RequiredLength = policy.PasswordRequiredLength;
                identityOptions.Password.RequireDigit = policy.PasswordRequireDigit;
                identityOptions.Password.RequireUppercase = policy.PasswordRequireUppercase;
                identityOptions.Password.RequireLowercase = policy.PasswordRequireLowercase;
                identityOptions.Password.RequireNonAlphanumeric = policy.PasswordRequireNonAlphanumeric;
                identityOptions.Password.RequiredUniqueChars = 1;

                identityOptions.Lockout.MaxFailedAccessAttempts = policy.MaxFailedAccessAttempts;
                identityOptions.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(policy.LockoutDurationMinutes);
                identityOptions.Lockout.AllowedForNewUsers = true;

                // A duplicate email would otherwise let two different Identity users share the same
                // address; combined with AccountConfiguration's DB-level "one Account per Identity
                // user" constraint, this keeps email the effectively-unique login identifier.
                identityOptions.User.RequireUniqueEmail = true;

                // Without this, RequireUniqueEmail's own uniqueness guarantee would let an attacker
                // permanently squat a victim's real email address by registering it and never
                // confirming - the account would still fully authenticate, and the real owner could
                // never register that address themselves. Setting this makes
                // SignInManager.CheckPasswordSignInAsync return SignInResult.IsNotAllowed for an
                // unconfirmed account instead of ever completing sign-in (see AuthAccountService.LoginAsync).
                identityOptions.SignIn.RequireConfirmedAccount = true;
            });

        services.AddOptions<DataProtectionTokenProviderOptions>().Configure<IOptions<EmailTokenOptions>>(
            (tokenProviderOptions, emailTokenOptions) =>
            {
                tokenProviderOptions.TokenLifespan = TimeSpan.FromMinutes(emailTokenOptions.Value.TokenLifetimeMinutes);
            });

        AddCookieAuthentication(services, isProduction);
        AddGoogleAuthentication(services, configuration);
        AddAuthorizationPolicies(services);
        AddAntiforgery(services, isProduction);
        AddCors(services);
        AddRateLimiting(services);

        return services;
    }

    /// <summary>
    /// Registers and binds M6a's 7 validated <c>IOptions&lt;T&gt;</c> security configuration types
    /// (with <c>ValidateOnStart()</c>), without wiring the rest of the security pipeline (Identity,
    /// cookie auth, antiforgery, CORS, rate limiting, Data Protection). Split out from
    /// <see cref="AddAuthSecurity"/> specifically so unit tests can exercise each validator's
    /// boundary behavior (see <c>AuthSecurityOptionsTests</c>) through this narrower, cheaper
    /// registration surface instead of the full security pipeline, following the same pattern
    /// already used by <c>AddApiHostingOptions</c>/<c>ApiHostingOptionsTests</c>.
    /// </summary>
    public static IServiceCollection AddAuthSecurityOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<IdentityPolicyOptions>()
            .Bind(configuration.GetSection(IdentityPolicyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityPolicyOptions>, IdentityPolicyOptionsValidator>();

        services.AddOptions<AuthCookieOptions>()
            .Bind(configuration.GetSection(AuthCookieOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthCookieOptions>, AuthCookieOptionsValidator>();

        services.AddOptions<EmailTokenOptions>()
            .Bind(configuration.GetSection(EmailTokenOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailTokenOptions>, EmailTokenOptionsValidator>();

        services.AddOptions<DavetiyeCorsOptions>()
            .Bind(configuration.GetSection(DavetiyeCorsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DavetiyeCorsOptions>>(new DavetiyeCorsOptionsValidator(isProduction));

        services.AddOptions<AuthRateLimitOptions>()
            .Bind(configuration.GetSection(AuthRateLimitOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthRateLimitOptions>, AuthRateLimitOptionsValidator>();

        services.AddOptions<DataProtectionKeyRingOptions>()
            .Bind(configuration.GetSection(DataProtectionKeyRingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DataProtectionKeyRingOptions>, DataProtectionKeyRingOptionsValidator>();

        services.AddOptions<PublicWebOptions>()
            .Bind(configuration.GetSection(PublicWebOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PublicWebOptions>>(new PublicWebOptionsValidator(isProduction));

        services.AddOptions<GoogleAuthOptions>()
            .Bind(configuration.GetSection(GoogleAuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GoogleAuthOptions>>(new GoogleAuthOptionsValidator(isProduction));

        services.AddOptions<MfaOptions>()
            .Bind(configuration.GetSection(MfaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MfaOptions>, MfaOptionsValidator>();

        return services;
    }

    private static void AddDataProtection(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var defaultDirectory = new DataProtectionKeyRingOptions().KeyRingDirectory;
        var configuredDirectory = configuration.GetSection(DataProtectionKeyRingOptions.SectionName)
            .GetValue(nameof(DataProtectionKeyRingOptions.KeyRingDirectory), defaultDirectory)
            ?? defaultDirectory;

        var keyRingDirectory = Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.Combine(environment.ContentRootPath, configuredDirectory);

        services.AddDataProtection()
            .SetApplicationName("Davetiye")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingDirectory));
    }

    private static void AddCookieAuthentication(IServiceCollection services, bool isProduction)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, cookieOptions =>
            {
                // __Host- is only valid when Secure, host-only (no Domain) and Path=/ all hold at
                // once (docs/THREAT_MODEL.md §5); it is unusable over plain local-dev HTTP because
                // Secure would then make the browser refuse to store the cookie at all, so the name
                // itself is environment-conditional rather than always "__Host-...".
                cookieOptions.Cookie.Name = isProduction ? "__Host-davetiye-auth" : "davetiye-auth-dev";
                cookieOptions.Cookie.HttpOnly = true;
                cookieOptions.Cookie.SecurePolicy = isProduction
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
                cookieOptions.Cookie.Path = "/";
                cookieOptions.Cookie.Domain = null;

                cookieOptions.SlidingExpiration = true;

                // Re-checks the principal's security stamp against the database on the interval
                // configured via AuthCookieOptions/SecurityStampValidatorOptions below, so a
                // ban/password/security-stamp change actually revokes already-issued cookies
                // instead of only preventing new logins (docs/THREAT_MODEL.md §5).
                //
                // SecurityStampValidator's own regeneration path (ValidatePrincipalAsync, invoked on
                // every request under this milestone's ValidationInterval=0 default) rebuilds the
                // principal purely from persisted claims (UserManager.GetClaimsAsync) - it has no
                // notion of the per-session, never-persisted "amr=mfa" claim
                // (SuperAdminClaimNames.AuthenticationMethodReference) that
                // DavetiyeSignInManager.TwoFactorSignInWithAmrClaimAsync adds at two-factor
                // completion. Left alone, that marker would be silently dropped on the very next
                // request after a successful two-factor completion, permanently breaking the
                // "MfaComplete" policy for that session. This wrapper preserves it across
                // regeneration the same way SignInManager.RefreshSignInAsync already preserves
                // ClaimTypes.AuthenticationMethod/"amr" across an explicit refresh.
                cookieOptions.Events.OnValidatePrincipal = async context =>
                {
                    var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (Guid.TryParse(userId, out var identityUserId))
                    {
                        // SecurityStampValidator is intentionally interval-based. Bans are an
                        // immediate access revocation, so check the authoritative ban overlay on
                        // every authenticated request even when the configurable stamp interval is
                        // nonzero. Login denial alone does not revoke an already-issued cookie.
                        var dbContext = context.HttpContext.RequestServices.GetRequiredService<DavetiyeDbContext>();
                        var banState = await dbContext.Accounts.AsNoTracking()
                            .Where(account => account.IdentityUserId == identityUserId)
                            .Select(account => new
                            {
                                account.DeletionStartedAtUtc,
                                IsBanned = dbContext.BanRecords.Any(ban =>
                                    ban.AccountId == account.Id && ban.RevokedAt == null),
                                HasBanHistory = dbContext.BanRecords.Any(ban => ban.AccountId == account.Id)
                            })
                            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                        if (banState?.IsBanned == true)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                            return;
                        }

                        // Ban history marks this identity as having had an explicitly revoked
                        // session. Super Admins also require immediate stamp checks: a trusted
                        // lost-factor recovery must revoke every existing privileged cookie on its
                        // next request even when the general stamp interval is configured > 0.
                        var isSuperAdmin = context.Principal?.HasClaim(
                            SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue) == true;
                        if (!isSuperAdmin && banState?.DeletionStartedAtUtc is not null)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                            return;
                        }
                        if (banState?.HasBanHistory == true || isSuperAdmin)
                        {
                            var signInManager = context.HttpContext.RequestServices
                                .GetRequiredService<SignInManager<ApplicationUser>>();
                            if (await signInManager.ValidateSecurityStampAsync(context.Principal) is null)
                            {
                                context.RejectPrincipal();
                                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                                return;
                            }
                        }
                    }

                    var hadMfaClaim = context.Principal?.HasClaim(
                        SuperAdminClaimNames.AuthenticationMethodReference, SuperAdminClaimNames.MfaAmrValue) ?? false;

                    await SecurityStampValidator.ValidatePrincipalAsync(context);

                    if (hadMfaClaim &&
                        context.Principal?.Identity is ClaimsIdentity identity &&
                        !context.Principal.HasClaim(
                            SuperAdminClaimNames.AuthenticationMethodReference, SuperAdminClaimNames.MfaAmrValue))
                    {
                        identity.AddClaim(new Claim(
                            SuperAdminClaimNames.AuthenticationMethodReference, SuperAdminClaimNames.MfaAmrValue));
                        context.ShouldRenew = true;
                    }
                };

                // This is an API, not a page app: never redirect an unauthenticated/forbidden
                // request to an HTML login page.
                cookieOptions.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                cookieOptions.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            // AddIdentityCore (rather than the full AddIdentity) registers only the Application
            // cookie scheme above; these three extra schemes exist for two reasons. First,
            // SignInManager.SignOutAsync() - which SecurityStampValidator.ValidatePrincipalAsync
            // calls internally whenever it rejects a stale-security-stamp principal -
            // unconditionally calls HttpContext.SignOutAsync() for all four Identity cookie schemes,
            // and ASP.NET Core throws if no handler is registered for a scheme being signed out of;
            // without these, a real ban/password/security-stamp revocation would crash the very
            // request that is supposed to reject it with 500 instead of a clean 401 - the opposite
            // of what docs/THREAT_MODEL.md §5 requires. Second, as of M6b these are no longer purely
            // inert: ExternalScheme is where the Google handler below actually signs in (read back
            // by IGoogleSignInService via GetExternalLoginInfoAsync), and TwoFactorUserIdScheme is
            // where DavetiyeSignInManager.SignInOrRequireTwoFactorAsync stores a pending two-factor
            // challenge for a 2FA-enabled account. TwoFactorRememberMeScheme remains unused: this
            // milestone does not implement a "remember this device" feature, so two-factor is always
            // required when enabled.
            .AddCookie(IdentityConstants.ExternalScheme)
            .AddCookie(IdentityConstants.TwoFactorRememberMeScheme)
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme);

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<AuthCookieOptions>>((cookieOptions, authCookieOptions) =>
            {
                cookieOptions.ExpireTimeSpan = TimeSpan.FromHours(authCookieOptions.Value.ExpirationHours);
            });

        // These cookies contain only an intermediate Google identity or a pending second-factor
        // identity. Keep them short-lived and non-sliding so browser activity cannot extend an
        // incomplete authentication flow indefinitely.
        ConfigureIntermediateCookie(services, IdentityConstants.ExternalScheme);
        ConfigureIntermediateCookie(services, IdentityConstants.TwoFactorUserIdScheme);

        services.AddOptions<SecurityStampValidatorOptions>().Configure<IOptions<AuthCookieOptions>>(
            (stampOptions, authCookieOptions) =>
            {
                stampOptions.ValidationInterval =
                    TimeSpan.FromSeconds(authCookieOptions.Value.SecurityStampValidationIntervalSeconds);
            });
    }

    private static void ConfigureIntermediateCookie(IServiceCollection services, string scheme) =>
        services.AddOptions<CookieAuthenticationOptions>(scheme)
            .Configure(options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = false;
            });

    /// <summary>
    /// Reads <see cref="GoogleAuthOptions"/> directly from raw configuration (bypassing the validated
    /// <c>IOptions&lt;GoogleAuthOptions&gt;</c> registered above) purely to decide whether to wire the
    /// Google handler at all — the same pattern <see cref="AddDataProtection"/> already uses to read
    /// its key-ring directory ahead of the container being built. This does NOT skip validation:
    /// the separately registered, <c>ValidateOnStart()</c>'d <c>IOptions&lt;GoogleAuthOptions&gt;</c>
    /// still fails host startup outright for an enabled-but-misconfigured Production setting
    /// (docs/THREAT_MODEL.md §6/§12 gate 3), regardless of whether the handler below ever gets wired.
    /// </summary>
    private static void AddGoogleAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var googleAuthOptions = configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>()
            ?? new GoogleAuthOptions();

        if (!googleAuthOptions.Enabled)
        {
            return;
        }

        services.AddAuthentication().AddGoogle(GoogleAuthenticationSchemeNames.Google, googleOptions =>
        {
            googleOptions.ClientId = googleAuthOptions.ClientId;
            googleOptions.ClientSecret = googleAuthOptions.ClientSecret;

            // Signs the resulting external identity into the External cookie scheme rather than the
            // main Application scheme, matching ASP.NET Core Identity's standard external-login
            // pattern — IGoogleSignInService reads it back via SignInManager.GetExternalLoginInfoAsync.
            googleOptions.SignInScheme = IdentityConstants.ExternalScheme;

            // The path Google's OAuth handler intercepts directly, distinct from this API's own
            // "/api/v1/auth/google/complete" endpoint (see GoogleAuthOptions's doc comment).
            googleOptions.CallbackPath = new PathString(new Uri(googleAuthOptions.CallbackBaseUrl).AbsolutePath);

            // docs/THREAT_MODEL.md §6: minimal scope, PKCE where the library supports it. State/
            // correlation validation is the framework's own default behavior and is never disabled.
            googleOptions.Scope.Clear();
            googleOptions.Scope.Add("openid");
            googleOptions.Scope.Add("email");
            googleOptions.Scope.Add("profile");
            googleOptions.UsePkce = true;

            // Not mapped by GoogleHandler's own default ClaimActions. Without this,
            // GoogleSignInService has no way to check docs/THREAT_MODEL.md §6's "Unverified ...
            // email ile hesap merge edilmez" requirement for the Google identity path, which would
            // reintroduce the same email-squatting risk RequireConfirmedAccount closes for
            // email/password registration.
            googleOptions.ClaimActions.MapJsonKey(
                GoogleClaimTypes.EmailVerified, "email_verified", ClaimValueTypes.Boolean);

            // docs/THREAT_MODEL.md §6: "Refresh/access token uygulamanın ihtiyacı yoksa kalıcı
            // saklanmaz" — this milestone has no use for Google's tokens beyond the immediate
            // callback, so they are never persisted.
            googleOptions.SaveTokens = false;
        });
    }

    private static void AddAuthorizationPolicies(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicyNames.SuperAdminOnly, policy => policy
                .RequireClaim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue));

            options.AddPolicy(AuthorizationPolicyNames.MfaComplete, policy => policy
                .RequireClaim(SuperAdminClaimNames.SuperAdmin, SuperAdminClaimNames.SuperAdminClaimValue)
                .RequireClaim(SuperAdminClaimNames.AuthenticationMethodReference, SuperAdminClaimNames.MfaAmrValue));
        });
    }

    private static void AddAntiforgery(IServiceCollection services, bool isProduction)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });
    }

    private static void AddCors(IServiceCollection services)
    {
        services.AddCors();

        services.AddOptions<CorsOptions>().Configure<IOptions<DavetiyeCorsOptions>>(
            (corsOptions, davetiyeCorsOptions) =>
            {
                corsOptions.AddPolicy(CorsPolicyNames.Default, policy => policy
                    .WithOrigins(davetiyeCorsOptions.Value.AllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
            });
    }

    private static void AddRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                context.HttpContext.Response.Headers.Pragma = "no-cache";
                context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
                return ValueTask.CompletedTask;
            };

            options.AddPolicy(AuthRateLimitPolicyNames.Register, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.Register));

            options.AddPolicy(AuthRateLimitPolicyNames.Login, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.Login));

            options.AddPolicy(AuthRateLimitPolicyNames.PasswordResetRequest, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PasswordResetRequest));

            options.AddPolicy(AuthRateLimitPolicyNames.EmailConfirmation, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.EmailConfirmation));

            options.AddPolicy(AuthRateLimitPolicyNames.PasswordResetConfirm, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PasswordResetConfirm));

            options.AddPolicy(AuthRateLimitPolicyNames.AntiforgeryToken, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.AntiforgeryToken));

            options.AddPolicy(AuthRateLimitPolicyNames.TwoFactorLoginComplete, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.TwoFactorLoginComplete));

            options.AddPolicy(AuthRateLimitPolicyNames.AdminMfaVerify, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.AdminMfaVerify));
            options.AddPolicy(AuthRateLimitPolicyNames.PublicationRead, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PublicationRead));
            options.AddPolicy(AuthRateLimitPolicyNames.PublicationAction, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PublicationAction));
            options.AddPolicy(AuthRateLimitPolicyNames.PublicInvitationRead, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PublicInvitationRead));
            options.AddPolicy(AuthRateLimitPolicyNames.PublicRsvpSubmission, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PublicRsvpSubmission));
            options.AddPolicy(AuthRateLimitPolicyNames.CreatorMediaIntentIp, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.CreatorMediaIntentIp));
            options.AddPolicy(AuthRateLimitPolicyNames.CreatorRsvpReadIp, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.CreatorRsvpReadIp));
            options.AddPolicy(AuthRateLimitPolicyNames.CreatorRsvpWriteIp, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.CreatorRsvpWriteIp));
            options.AddPolicy(AuthRateLimitPolicyNames.PublicMemorySubmission, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PublicMemorySubmission));
            options.AddPolicy(AuthRateLimitPolicyNames.PublicMemoryMediaDelivery, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PublicMemoryMediaDelivery));
            options.AddPolicy(AuthRateLimitPolicyNames.PublicMemoryUploadCreate, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PublicMemoryUploadCreate));
            options.AddPolicy(AuthRateLimitPolicyNames.PublicMemoryUploadIntent, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PublicMemoryUploadIntent));
            options.AddPolicy(AuthRateLimitPolicyNames.PublicMemoryUploadFinalize, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.PublicMemoryUploadFinalize));
            options.AddPolicy(AuthRateLimitPolicyNames.CreatorMemoriesReadIp, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.CreatorMemoriesReadIp));
            options.AddPolicy(AuthRateLimitPolicyNames.CreatorMemoriesWriteIp, httpContext =>
                CreatePerIpFixedWindowPartition(httpContext, limits => limits.CreatorMemoriesWriteIp));
        });
    }

    /// <summary>IPv4 (and IPv4-mapped IPv6) keys stay exact; native IPv6 addresses collapse to their /64 so one host cannot rotate through a whole prefix.</summary>
    public static string NormalizeClientKey(System.Net.IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new System.Net.IPAddress(bytes).ToString() + "/64";
    }

    /// <summary>
    /// Resolves <see cref="AuthRateLimitOptions"/> from the current request's service provider
    /// (rather than a value captured once at startup), so the always-validated, current
    /// configuration value is used for every partition without needing a service provider to be
    /// built ahead of the DI container being finished.
    /// </summary>
    private static RateLimitPartition<string> CreatePerIpFixedWindowPartition(
        HttpContext httpContext,
        Func<AuthRateLimitOptions, AuthRateLimitOptions.RouteRateLimit> selectRouteLimit)
    {
        var routeLimit = selectRouteLimit(
            httpContext.RequestServices.GetRequiredService<IOptionsMonitor<AuthRateLimitOptions>>().CurrentValue);
        var partitionKey = NormalizeClientKey(httpContext.Connection.RemoteIpAddress);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = routeLimit.PermitLimit,
                Window = TimeSpan.FromSeconds(routeLimit.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }
}
