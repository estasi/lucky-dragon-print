using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using LdPrint.Services;
using LdPrint.ViewModels;

namespace LdPrint;

public partial class MainWindow : Window
{
    private readonly PropertyChangedEventHandler _themeChangedHandler;

    public MainWindow()
    {
        InitializeComponent();

        _themeChangedHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeService.EffectiveTheme))
                WindowChrome.ApplyDarkTitleBar(this, ThemeService.Current.EffectiveTheme == "dark");
        };

        // SourceInitialized fires after the HWND is created but before the
        // window is shown — perfect spot to apply the immersive dark mode
        // attribute so the title bar paints with the correct theme on first
        // appearance.
        SourceInitialized += (_, _) =>
            WindowChrome.ApplyDarkTitleBar(this, ThemeService.Current.EffectiveTheme == "dark");

        // React to runtime theme changes (user picking from combobox or
        // Windows flipping system theme in Auto mode).
        ThemeService.Current.PropertyChanged += _themeChangedHandler;
        Closed += (_, _) => ThemeService.Current.PropertyChanged -= _themeChangedHandler;
    }

    /// <summary>
    /// Mouse-wheel anywhere on the list/preview area navigates between
    /// pages — wheel up = previous label, wheel down = next. Marked
    /// Handled so the ListBox doesn't try to scroll its (already-fitting)
    /// content at the same time.
    /// </summary>
    private void OnPreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var delta = e.Delta > 0 ? -1 : +1;
        vm.NavigatePage(delta);
        e.Handled = true;
    }
}
