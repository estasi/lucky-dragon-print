using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

namespace LdPrint.Services;

public sealed record ThemeOption(string Id, string LocalizationKey);

/// <summary>
/// Manages the active WPF theme by replacing the merged ResourceDictionary
/// at the head of <c>Application.Current.Resources.MergedDictionaries</c>.
/// Listens to Windows theme changes via <see cref="SystemEvents"/> and
/// resolves the "auto" mode at startup and on change.
/// </summary>
public partial class ThemeService : ObservableObject
{
    public static ThemeService Current { get; } = new();

    public IReadOnlyList<ThemeOption> AvailableThemes { get; } = new[]
    {
        new ThemeOption("auto",  "theme_auto"),
        new ThemeOption("light", "theme_light"),
        new ThemeOption("dark",  "theme_dark"),
    };

    /// <summary>User-chosen mode: "auto" / "light" / "dark".</summary>
    [ObservableProperty]
    private string _selectedThemeId = "auto";

    /// <summary>Concretely applied theme: "light" or "dark".</summary>
    [ObservableProperty]
    private string _effectiveTheme = "light";

    private bool _systemEventsHooked;

    private ThemeService() { }

    /// <summary>
    /// Apply the given theme mode. "auto" reads the current Windows theme.
    /// Safe to call from any thread — marshals to the UI dispatcher.
    /// </summary>
    public void SetTheme(string themeId)
    {
        if (string.IsNullOrEmpty(themeId)) themeId = "auto";
        SelectedThemeId = themeId;

        var concrete = themeId == "auto" ? DetectSystemTheme() : themeId;
        ApplyResourceDictionary(concrete);
        EffectiveTheme = concrete;

        EnsureSystemEventsHook();
    }

    /// <summary>Read Windows "Apps mode" from the registry. Falls back to "light".</summary>
    public static string DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // AppsUseLightTheme = 1 → light, 0 → dark.
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0 ? "dark" : "light";
        }
        catch { /* ignore — fall through to default */ }
        return "light";
    }

    private void ApplyResourceDictionary(string concrete)
    {
        var app = Application.Current;
        if (app is null) return;

        var uri = new Uri(
            $"pack://application:,,,/Assets/Themes/{(concrete == "dark" ? "Dark" : "Light")}.xaml",
            UriKind.Absolute);

        var newDict = new ResourceDictionary { Source = uri };

        if (app.Dispatcher.CheckAccess())
            Swap(app, newDict);
        else
            app.Dispatcher.Invoke(() => Swap(app, newDict));

        static void Swap(Application app, ResourceDictionary newDict)
        {
            var dicts = app.Resources.MergedDictionaries;
            // Remove existing theme palette dictionaries (Light.xaml / Dark.xaml)
            // only — Controls.xaml stays put since its implicit styles reference
            // brushes from the palette via DynamicResource and don't change
            // with the theme.
            for (var i = dicts.Count - 1; i >= 0; i--)
            {
                var src = dicts[i].Source?.OriginalString ?? string.Empty;
                if (IsThemePaletteSource(src))
                    dicts.RemoveAt(i);
            }
            // Insert at the front so palette brushes are found before any
            // overrides defined later in the chain.
            dicts.Insert(0, newDict);
        }

        static bool IsThemePaletteSource(string src)
        {
            // Match only the palette files we own; spare Controls.xaml and
            // any other merged dictionary.
            return src.EndsWith("/Light.xaml", StringComparison.OrdinalIgnoreCase)
                || src.EndsWith("/Dark.xaml", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Hook the system-wide preference change event so the auto-mode reacts
    /// when the user flips the Windows theme. Hooked exactly once; the
    /// handler no-ops in non-auto mode.
    /// </summary>
    private void EnsureSystemEventsHook()
    {
        if (_systemEventsHooked) return;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _systemEventsHooked = true;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (SelectedThemeId != "auto") return;

        var concrete = DetectSystemTheme();
        if (concrete == EffectiveTheme) return;

        ApplyResourceDictionary(concrete);
        EffectiveTheme = concrete;
    }
}
