using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jorvis.Core;

/// <summary>
/// The rules for a project's label: lowercase letters and numbers in words joined by single dashes (kebab-case). Shared
/// by the API, which enforces them, and the web app, which suggests a label from the project's name.
/// </summary>
public static partial class ProjectLabels
{
    public const int MinLength = 2;
    public const int MaxLength = 40;

    public const string Invalid = "Choose a label of 2 to 40 lowercase letters or numbers, with single dashes between words, such as home-reno.";
    public const string Taken = "That label is taken.";

    public static bool TryNormalize(string? value, out string label)
    {
        label = value?.Trim().ToLowerInvariant() ?? "";
        return label.Length is >= MinLength and <= MaxLength && Pattern().IsMatch(label);
    }

    /// <summary>A label made from a name ("Home Reno!" becomes "home-reno"). It may still be too short.</summary>
    public static string Suggest(string? name)
    {
        var label = new StringBuilder();
        foreach (var c in (name ?? "").Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                label.Append(c);
            // Accents were split off their letters above ("é" is "e" + accent), so drop them rather than break the word.
            else if (char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && label.Length > 0 && label[^1] != '-')
                label.Append('-');
        }

        var suggestion = label.ToString().Trim('-');
        return suggestion.Length > MaxLength ? suggestion[..MaxLength].TrimEnd('-') : suggestion;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();
}
