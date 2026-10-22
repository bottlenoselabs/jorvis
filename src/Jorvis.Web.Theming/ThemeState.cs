using Microsoft.JSInterop;

namespace Jorvis.Web.Theming;

/// <summary>
/// The chosen look and light or dark mode. They are kept in localStorage, which the Sign In app and the main app share
/// because they are on the same origin, so signing out and back in keeps them.
/// </summary>
public sealed class ThemeState(IJSRuntime js)
{
    private const string ThemeKey = "jorvis-theme";
    private const string ModeKey = "jorvis-mode";

    private bool _hasModePreference;

    public JorvisThemeOption Current { get; private set; } = JorvisThemes.Default;

    public bool IsDarkMode { get; private set; }

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            Current = JorvisThemes.Find(await js.InvokeAsync<string?>("localStorage.getItem", ThemeKey));

            var mode = await js.InvokeAsync<string?>("localStorage.getItem", ModeKey);
            _hasModePreference = mode is "dark" or "light";
            if (_hasModePreference)
                IsDarkMode = mode == "dark";
        }
        catch (JSException)
        {
        }
    }

    /// <summary>The system setting, used until the person picks a mode themselves.</summary>
    public void UseSystemMode(bool isDark)
    {
        if (_hasModePreference)
            return;

        IsDarkMode = isDark;
        Changed?.Invoke();
    }

    public async Task SetAsync(JorvisThemeOption theme)
    {
        Current = theme;
        Changed?.Invoke();
        await SaveAsync(ThemeKey, theme.Id);
    }

    public async Task SetDarkModeAsync(bool isDark)
    {
        IsDarkMode = isDark;
        _hasModePreference = true;
        Changed?.Invoke();
        await SaveAsync(ModeKey, isDark ? "dark" : "light");
    }

    private async Task SaveAsync(string key, string value)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch (JSException)
        {
        }
    }
}
