using Jorvis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Jorvis.Api.Auth;

/// <summary>
/// Owner-only administration, run on the host with `docker compose exec`. There is deliberately no HTTP surface for
/// creating invites or removing users, so there is nothing to attack from the Internet.
/// </summary>
public static class AdminCommands
{
    private const string Usage = """
        Usage:
          invite [--email <address>] [--hours <n>]   Create a single-use invite link (default 72 hours)
          recover <email> [--hours <n>]               Create a link that replaces ALL of that account's passkeys
                                                      (lost or stolen phone); their other sessions end
          users                                       List accounts and how many passkeys each has
          set-username <email> <username>             Change an account's username
          remove-user <email>                         Delete an account and end its sessions
        """;

    /// <summary>Returns an exit code when args were an admin command, or null to start the web server normally.</summary>
    public static async Task<int?> TryRunAsync(WebApplication app, string[] args)
    {
        if (args.Length == 0 || args[0] is not ("invite" or "recover" or "users" or "set-username" or "remove-user" or "help"))
            return null;

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        switch (args[0])
        {
            case "invite":
                return await InviteAsync(services, app.Configuration, args[1..]);
            case "recover":
                return await RecoverAsync(services, app.Configuration, args[1..]);
            case "users":
                return await ListUsersAsync(services);
            case "set-username":
                return await SetUsernameAsync(services, args[1..]);
            case "remove-user":
                return await RemoveUserAsync(services, args[1..]);
            default:
                Console.WriteLine(Usage);
                return 0;
        }
    }

    private static async Task<int> InviteAsync(IServiceProvider services, IConfiguration config, string[] args)
    {
        if (!TryParseOptions(args, out var email, out var hours))
            return 1;

        var token = await services.GetRequiredService<InviteService>()
            .CreateAsync(email, TimeSpan.FromHours(hours), CancellationToken.None);

        PrintLink(config, token, $"Invite valid for {hours:0.##} hours, single use{(email is null ? "" : $", for {email}")}:");
        return 0;
    }

    private static async Task<int> RecoverAsync(IServiceProvider services, IConfiguration config, string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal) || !TryParseOptions(args[1..], out _, out var hours, allowEmail: false))
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var email = args[0];
        var user = await services.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
        if (user is null)
        {
            Console.Error.WriteLine($"No account for {email}.");
            return 1;
        }

        var token = await services.GetRequiredService<InviteService>()
            .CreateAsync(user.Email, TimeSpan.FromHours(hours), CancellationToken.None, InvitePurpose.Recover);

        PrintLink(config, token, $"Recovery link for {user.Email}, valid {hours:0.##} hours, single use. Opening it REPLACES all of their passkeys:");
        return 0;
    }

    private static async Task<int> ListUsersAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<AppUser>>();
        foreach (var user in users.Users.OrderBy(u => u.Email).ToList())
        {
            var passkeys = (await users.GetPasskeysAsync(user)).Count;
            Console.WriteLine($"{user.UserName}  <{user.Email}>  ({passkeys} passkey{(passkeys == 1 ? "" : "s")})");
        }

        return 0;
    }

    private static async Task<int> SetUsernameAsync(IServiceProvider services, string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        if (!Usernames.TryNormalize(args[1], out var username))
        {
            Console.Error.WriteLine(Usernames.Invalid);
            return 1;
        }

        var users = services.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(args[0]);
        if (user is null)
        {
            Console.Error.WriteLine($"No account for {args[0]}.");
            return 1;
        }

        if (await users.FindByNameAsync(username) is { } other && other.Id != user.Id)
        {
            Console.Error.WriteLine(Usernames.Taken);
            return 1;
        }

        var result = await users.SetUserNameAsync(user, username);
        Console.WriteLine(result.Succeeded ? $"{args[0]} is now {username}." : string.Join("; ", result.Errors.Select(e => e.Description)));
        return result.Succeeded ? 0 : 1;
    }

    private static bool TryParseOptions(string[] args, out string? email, out double hours, bool allowEmail = true)
    {
        email = null;
        hours = InviteService.DefaultLifetime.TotalHours;
        for (var i = 0; i < args.Length; i++)
        {
            if (allowEmail && args[i] == "--email" && i + 1 < args.Length)
                email = args[++i];
            else if (args[i] == "--hours" && i + 1 < args.Length && double.TryParse(args[++i], out var parsed) && parsed > 0)
                hours = parsed;
            else
            {
                Console.Error.WriteLine(Usage);
                return false;
            }
        }

        return true;
    }

    private static void PrintLink(IConfiguration config, string token, string description)
    {
        var baseUrl = (config["Jorvis:PublicBaseUrl"] ?? "http://localhost:5200").TrimEnd('/');
        // The token sits in the URL fragment so it is never sent to a server, written to access logs or leaked in Referer.
        Console.WriteLine(description);
        Console.WriteLine($"{baseUrl}/join#token={token}");
    }

    private static async Task<int> RemoveUserAsync(IServiceProvider services, string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var users = services.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(args[0]);
        if (user is null)
        {
            Console.Error.WriteLine($"No account for {args[0]}.");
            return 1;
        }

        var result = await users.DeleteAsync(user);
        Console.WriteLine(result.Succeeded ? $"Removed {args[0]}. Their sessions end within a minute." : string.Join("; ", result.Errors.Select(e => e.Description)));
        return result.Succeeded ? 0 : 1;
    }
}
