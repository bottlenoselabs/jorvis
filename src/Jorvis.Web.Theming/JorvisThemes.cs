using MudBlazor;

namespace Jorvis.Web.Theming;

public sealed record JorvisThemeOption(string Id, string Name, string Accent, MudTheme Theme);

/// <summary>The looks of Jorvis, shared by the Sign In app and the main app so they cannot drift apart.</summary>
public static class JorvisThemes
{
    public static IReadOnlyList<JorvisThemeOption> All { get; } =
    [
        new("teal", "Ink and teal", "#2BB5A0", Create(
            new PaletteLight
            {
                Primary = "#0F8F7E", Secondary = "#C98A12", Background = "#F6F8FA", Surface = "#FFFFFF"
            },
            new PaletteDark
            {
                Primary = "#2BB5A0", PrimaryContrastText = "#0B1015", Secondary = "#E8A838", Background = "#0F1419",
                Surface = "#171D25", AppbarBackground = "#171D25", DrawerBackground = "#131920"
            })),
        new("indigo", "Indigo", "#6C63FF", Create(
            new PaletteLight
            {
                Primary = "#5B52E8", Secondary = "#E0457B", Background = "#F7F7FB", Surface = "#FFFFFF"
            },
            new PaletteDark
            {
                Primary = "#6C63FF", Secondary = "#FF6B9D", Background = "#12131A", Surface = "#1B1C25",
                AppbarBackground = "#1B1C25", DrawerBackground = "#161720"
            })),
        new("graphite", "Graphite", "#4ADE80", Create(
            new PaletteLight
            {
                Primary = "#16A34A", Secondary = "#64748B", Background = "#F5F5F5", Surface = "#FFFFFF"
            },
            new PaletteDark
            {
                Primary = "#4ADE80", PrimaryContrastText = "#0A0A0A", Secondary = "#94A3B8", Background = "#121212",
                Surface = "#1C1C1C", AppbarBackground = "#1C1C1C", DrawerBackground = "#161616"
            }))
    ];

    public static JorvisThemeOption Default => All[0];

    public static JorvisThemeOption Find(string? id) => All.FirstOrDefault(theme => theme.Id == id) ?? Default;

    private static MudTheme Create(PaletteLight light, PaletteDark dark) => new()
    {
        PaletteLight = light,
        PaletteDark = dark,
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "10px" },
        Typography = new Typography { Button = new ButtonTypography { TextTransform = "none" } }
    };
}
