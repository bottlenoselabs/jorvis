using System.Threading.RateLimiting;
using Jorvis.Infrastructure.Data;
using Jorvis.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace Jorvis.Api.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddJorvisAuth(
        this IServiceCollection services, IConfiguration configuration, string[] allowedOrigins)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies(cookies => cookies.ApplicationCookie!.Configure(ConfigureCookie));

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Passkeys live in their own table, which exists from schema version 3.
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddEntityFrameworkStores<JorvisDatabaseContext>()
            .AddSignInManager();

        ConfigurePasskeys(services, configuration, allowedOrigins);

        // Recheck the user against the database often so removing a user or changing their password ends their sessions.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        // Every endpoint requires a signed-in user unless it explicitly opts out with AllowAnonymous.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<InviteService>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthEndpoints.RateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = int.TryParse(context.RequestServices.GetRequiredService<IConfiguration>()["Auth:RateLimitPerMinute"], out var limit)
                            ? limit
                            : 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        // Session cookies are encrypted with these keys; persist them so restarts do not sign everyone out.
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            services.AddDataProtection()
                .SetApplicationName("Jorvis")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        return services;
    }

    private static void ConfigurePasskeys(IServiceCollection services, IConfiguration configuration, string[] allowedOrigins)
    {
        var publicUrl = new Uri(configuration["Jorvis:PublicBaseUrl"] ?? "http://localhost:5200");

        // A passkey is bound to this domain: it works at the public address and nowhere else.
        var domain = configuration["Passkey:ServerDomain"] ?? publicUrl.Host;
        var origins = new HashSet<string>(allowedOrigins, StringComparer.Ordinal) { publicUrl.GetLeftPart(UriPartial.Authority) };

        services.Configure<IdentityPasskeyOptions>(options =>
        {
            options.ServerDomain = domain;
            // Discoverable credentials, so signing in needs no username, and always a fingerprint/face/screen-lock check.
            options.ResidentKeyRequirement = "required";
            options.UserVerificationRequirement = "required";
            options.ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && origins.Contains(context.Origin));
        });
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "jorvis.auth";
        options.Cookie.HttpOnly = true;
        // Secure whenever the request arrived over HTTPS (via X-Forwarded-Proto behind the tunnel); plain http only on localhost.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;

        // This is an API: answer with status codes, never redirects.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }
}
