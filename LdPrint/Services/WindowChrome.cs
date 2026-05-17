using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LdPrint.Services;

/// <summary>
/// Applies Windows 11 immersive dark mode to a window's caption bar (title bar).
/// WPF's <c>Window</c> chrome is rendered by the OS, not by WPF — so the
/// caption stays light even when our <c>Window.Background</c> is dark unless
/// we explicitly tell DWM to use the dark theme for this HWND.
/// </summary>
public static class WindowChrome
{
    // Attribute IDs for DwmSetWindowAttribute. On Windows 10 builds 18985–19041
    // this was 19 (named DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1). From
    // 20H1 / Windows 11 onwards it's 20. We try 20 first, fall back to 19.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Toggle the dark title bar on the given window. Safe to call before the
    /// window is visible — uses the EnsureHandle path to grab the HWND. Silent
    /// no-op on systems older than Windows 10 1809.
    /// </summary>
    public static void ApplyDarkTitleBar(Window window, bool dark)
    {
        if (window is null) return;

        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        var value = dark ? 1 : 0;
        // Try the modern attribute id; if the OS rejects it (older build),
        // fall back to the legacy id. Either failing is fine — we just don't
        // get a dark title on that machine.
        var hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref value, sizeof(int));
        if (hr != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD,
                ref value, sizeof(int));
        }
    }
}
