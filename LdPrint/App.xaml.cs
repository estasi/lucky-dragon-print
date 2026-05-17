using System.Windows;
using LdPrint.Services;
using LdPrint.ViewModels;

namespace LdPrint;

public partial class App : Application
{
    /// <summary>
    /// Settings loaded once at startup. Mutated by the language picker and
    /// saved back on demand.
    /// </summary>
    public static SettingsService Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Restore persisted user prefs before any UI binding evaluates strings.
        Settings = SettingsService.Load();
        LocalizationService.Current.SetLanguage(Settings.Language);
        // Apply theme before MainWindow is constructed so its DynamicResource
        // brushes resolve to the right palette immediately, avoiding a flash.
        ThemeService.Current.SetTheme(Settings.Theme);

        // If launched with an argument (Explorer double-click on a .tspl
        // file → shell\open\command passes "%1"), open that file.
        string? initialFile = null;
        if (e.Args.Length > 0 && System.IO.File.Exists(e.Args[0]))
            initialFile = e.Args[0];

        // Override the StartupUri behavior so we can pass the file path.
        var window = new MainWindow
        {
            DataContext = new MainViewModel(initialFile),
        };
        window.Show();

        base.OnStartup(e);
    }
}
