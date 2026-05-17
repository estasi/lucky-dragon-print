using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LdPrint.Services;

/// <summary>
/// Persists user preferences (UI language, etc.) to
/// %APPDATA%\LDPrint\settings.json so they survive between sessions.
///
/// Implementation note: the type is mutable by design — callers update
/// properties and call <see cref="Save"/>. No locking; only the UI thread
/// touches settings.
/// </summary>
public sealed class SettingsService
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "ru";

    /// <summary>"auto" (follow Windows), "light", or "dark".</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "auto";

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LDPrint");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static SettingsService Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new SettingsService();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<SettingsService>(json, Options) ?? new SettingsService();
        }
        catch
        {
            // Corrupt or unreadable — fall back to defaults rather than crashing.
            return new SettingsService();
        }
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDir)) Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, Options);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Silently ignore — saving prefs is best-effort, not critical.
        }
    }
}
