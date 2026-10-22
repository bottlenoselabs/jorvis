using System.Security.Claims;
using System.Text.Json;
using Jorvis.Core.Contracts;
using Jorvis.Infrastructure.Data;
using Jorvis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jorvis.Api.Auth;

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    private const int MaxEmailLength = 256;
    private const string InvalidSignIn = "That passkey was not accepted.";
    private const string InvalidInvite = "This invite is invalid or has expired.";
    private const string InvalidPasskey = "The passkey could not be verified. Try again.";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        // Each passkey ceremony is two requests: the server issues a challenge (its state rides in a protected cookie),
        // then verifies the signed response.
        auth.MapPost("/sign-in/start", SignInOptionsAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/sign-in/finish", SignInAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/join/start", JoinOptionsAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/join/finish", JoinAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/sign-out", SignOutAsync);
        // Also used by nginx (auth_request) to decide whether a browser may download the app at all.
        auth.MapGet("/session", (HttpContext http) => Results.Ok(
            new SessionResponse(http.User.Identity?.Name ?? "", http.User.FindFirstValue(ClaimTypes.Email) ?? "")));

        return app;
    }

    private static async Task<IResult> SignInOptionsAsync(SignInManager<AppUser> signIn)
    {
        // No user: the passkey itself says who is signing in.
        var options = await signIn.MakePasskeyRequestOptionsAsync(null!);
        return Results.Content(options, "application/json");
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        SignInManager<AppUser> signIn,
        UserManager<AppUser> users)
    {
        if (!TryGetCredentialJson(request.Credential, out var credentialJson))
            return Results.Problem(InvalidSignIn, statusCode: StatusCodes.Status401Unauthorized);

        var assertion = await TryAsync(() => signIn.PerformPasskeyAssertionAsync(credentialJson));
        if (assertion is not { Succeeded: true } || !await signIn.CanSignInAsync(assertion.User))
            return Results.Problem(InvalidSignIn, statusCode: StatusCodes.Status401Unauthorized);

        // Stores the passkey's updated signature counter, which is how cloned authenticators get noticed.
        var saved = await users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey);
        if (!saved.Succeeded)
            return Results.Problem(InvalidSignIn, statusCode: StatusCodes.Status401Unauthorized);

        await signIn.SignInAsync(assertion.User, isPersistent: true);
        return Results.Ok(new SessionResponse(assertion.User.UserName!, assertion.User.Email!));
    }

    private static async Task<IResult> JoinOptionsAsync(
        JoinOptionsRequest request,
        InviteService invites,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        CancellationToken ct)
    {
        var resolved = await ResolveInviteAsync(request.Token, request.Email, request.Username, invites, users, ct);
        if (resolved.Error is not null)
            return resolved.Error;

        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            // A recovery keeps the account's existing id, so the new passkey belongs to the same user.
            Id = (resolved.Existing?.Id ?? Guid.NewGuid()).ToString(),
            Name = resolved.Username,
            // Also what ties the challenge to the email it was issued for.
            DisplayName = resolved.Email
        });
        return Results.Content(options, "application/json");
    }

    private static async Task<IResult> JoinAsync(
        JoinRequest request,
        InviteService invites,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        JorvisDatabaseContext db,
        CancellationToken ct)
    {
        var resolved = await ResolveInviteAsync(request.Token, request.Email, request.Username, invites, users, ct);
        if (resolved.Error is not null)
            return resolved.Error;

        if (!TryGetCredentialJson(request.Credential, out var credentialJson))
            return Results.Problem(InvalidPasskey, statusCode: StatusCodes.Status400BadRequest);

        // Verify the passkey before touching anything: this has no side effects and fails for anything but the
        // challenge this browser was just given.
        var attestation = await TryAsync(() => signIn.PerformPasskeyAttestationAsync(credentialJson));
        if (attestation is not { Succeeded: true }
            || !Guid.TryParse(attestation.UserEntity.Id, out var userId)
            || !string.Equals(attestation.UserEntity.Name, resolved.Username, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(attestation.UserEntity.DisplayName, resolved.Email, StringComparison.OrdinalIgnoreCase)
            || (resolved.Existing is not null && resolved.Existing.Id != userId))
        {
            return Results.Problem(InvalidPasskey, statusCode: StatusCodes.Status400BadRequest);
        }

        // Claim the invite. UsedAt is a concurrency token, so only one of two simultaneous requests wins.
        var invite = resolved.Invite!;
        invite.UsedAt = invites.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(InvalidInvite, statusCode: StatusCodes.Status400BadRequest);
        }

        var created = resolved.Existing is null;
        var user = resolved.Existing ?? new AppUser { Id = userId, UserName = resolved.Username, Email = resolved.Email, EmailConfirmed = true };

        if (!await TrySaveAsync(users, user, attestation.Passkey, created))
        {
            // Hand the invite back so a failed attempt does not burn it.
            invite.UsedAt = null;
            await db.SaveChangesAsync(CancellationToken.None);
            // Someone else may have taken the username between the two steps.
            var message = created && await users.FindByNameAsync(resolved.Username) is not null
                ? Usernames.Taken
                : "Could not save the passkey. Try again.";
            return Results.Problem(message, statusCode: StatusCodes.Status400BadRequest);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        return Results.Created("/api/auth/session", new SessionResponse(user.UserName!, user.Email!));
    }

    private static async Task<bool> TrySaveAsync(UserManager<AppUser> users, AppUser user, UserPasskeyInfo passkey, bool created)
    {
        if (created)
        {
            if (!(await users.CreateAsync(user)).Succeeded)
                return false;
        }
        else
        {
            // Recovery: the old device is gone or untrusted, so none of its passkeys survive.
            foreach (var old in await users.GetPasskeysAsync(user))
                await users.RemovePasskeyAsync(user, old.CredentialId);
        }

        if ((await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded)
        {
            // Ends every other session, which matters after a recovery.
            await users.UpdateSecurityStampAsync(user);
            return true;
        }

        if (created)
            await users.DeleteAsync(user);
        return false;
    }

    private static async Task<IResult> SignOutAsync(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private sealed record ResolvedInvite(Invite? Invite, AppUser? Existing, string Email, string Username, IResult? Error);

    /// <summary>Checks the invite and email, the same way for both steps of joining.</summary>
    private static async Task<ResolvedInvite> ResolveInviteAsync(
        string? token, string? email, string? username, InviteService invites, UserManager<AppUser> users, CancellationToken ct)
    {
        static ResolvedInvite Fail(string message) =>
            new(null, null, "", "", Results.Problem(message, statusCode: StatusCodes.Status400BadRequest));

        if (string.IsNullOrWhiteSpace(token) || token.Length > 256 || string.IsNullOrWhiteSpace(email) || email.Length > MaxEmailLength)
            return Fail(InvalidInvite);

        email = email.Trim();
        var invite = await invites.FindRedeemableAsync(token, ct);
        if (invite is null || (invite.Email is not null && !string.Equals(invite.Email, email, StringComparison.OrdinalIgnoreCase)))
            return Fail(InvalidInvite);

        var existing = await users.FindByEmailAsync(email);
        return (invite.Purpose, existing) switch
        {
            (InvitePurpose.Recover, null) => Fail(InvalidInvite),
            (InvitePurpose.Join, not null) => Fail("An account for that email already exists."),
            (InvitePurpose.Join, null) when !IsEmail(email) => Fail("Enter a valid email address."),
            (InvitePurpose.Join, null) when !Usernames.TryNormalize(username, out _) => Fail(Usernames.Invalid),
            (InvitePurpose.Join, null) when await users.FindByNameAsync(username!.Trim()) is not null => Fail(Usernames.Taken),
            // A recovery keeps the name the account already has.
            (_, not null) => new ResolvedInvite(invite, existing, email, existing.UserName!, null),
            _ => new ResolvedInvite(invite, existing, email, username!.Trim(), null)
        };
    }

    private static bool IsEmail(string value) =>
        System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value;

    /// <summary>
    /// Verification throws when no ceremony is underway (a request that skipped the challenge) or the body is malformed.
    /// Both are just failed attempts.
    /// </summary>
    private static async Task<T?> TryAsync<T>(Func<Task<T>> verify)
        where T : class
    {
        try
        {
            return await verify();
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException)
        {
            return null;
        }
    }

    private static bool TryGetCredentialJson(JsonElement? credential, out string json)
    {
        json = credential is { ValueKind: JsonValueKind.Object } element ? element.GetRawText() : "";
        return json.Length is > 0 and <= 64 * 1024;
    }
}
