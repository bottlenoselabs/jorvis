using System.Text.RegularExpressions;

namespace Jorvis.Api.Auth;

/// <summary>The rules for the username someone picks when joining. Uniqueness is enforced separately, ignoring case.</summary>
public static partial class Usernames
{
    public const string Invalid = "Choose a username of 3 to 32 letters, numbers, dots, dashes or underscores.";
    public const string Taken = "That username is taken.";

    public static bool TryNormalize(string? value, out string username)
    {
        username = value?.Trim() ?? "";
        return Pattern().IsMatch(username);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,31}$")]
    private static partial Regex Pattern();
}
