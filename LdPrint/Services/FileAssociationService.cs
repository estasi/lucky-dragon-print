using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LdPrint.Services;

/// <summary>
/// Registers / unregisters LD Print as the handler for .tspl files in
/// HKCU\Software\Classes. Uses HKCU (per-user) so no UAC prompt is needed.
/// Notifies the shell after each change so Explorer picks the new
/// association up immediately.
/// </summary>
public static class FileAssociationService
{
    /// <summary>ProgId — the logical "file type" identifier in the registry.</summary>
    private const string ProgId = "LDPrint.TsplFile";

    private const string ProgIdDescription = "TSPL Label File";

    /// <summary>Extensions we own. Single source of truth.</summary>
    public static readonly string[] Extensions = { ".tspl" };

    /// <summary>True if HKCU\Software\Classes\.tspl currently points at our ProgId.</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Extensions[0]}");
            if (key is null) return false;
            return string.Equals(key.GetValue(null) as string, ProgId, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static void Register()
    {
        var exePath = GetExecutablePath()
            ?? throw new InvalidOperationException("Could not determine executable path.");

        using (var typeKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            typeKey.SetValue(null, ProgIdDescription);

        using (var iconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
            iconKey.SetValue(null, $"\"{exePath}\",0");

        using (var cmdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
            cmdKey.SetValue(null, $"\"{exePath}\" \"%1\"");

        foreach (var ext in Extensions)
        {
            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
            extKey.SetValue(null, ProgId);
        }

        // Inform the shell so Explorer picks up the new association immediately.
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    public static void Unregister()
    {
        foreach (var ext in Extensions)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ext}", throwOnMissingSubKey: false); }
            catch { /* best-effort */ }
        }
        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false); }
        catch { /* best-effort */ }

        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// For single-file published apps, AppContext.BaseDirectory points at the
    /// extracted bundle directory, while Environment.ProcessPath gives the
    /// .exe the user actually launched. We need the latter for the shell.
    /// </summary>
    private static string? GetExecutablePath() => Environment.ProcessPath;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
}
