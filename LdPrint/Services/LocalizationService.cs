using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LdPrint.Services;

/// <summary>
/// UI string localization. Singleton ObservableObject — XAML binds to its
/// indexer, and changing <see cref="CurrentLanguage"/> triggers
/// notifications on the indexer so every binding refreshes at once.
///
/// Translations are loaded from JSON resource files bundled in the assembly
/// (Assets/i18n/{lang}.json). Missing keys fall back to the key itself so
/// developers immediately see what's untranslated.
/// </summary>
public partial class LocalizationService : ObservableObject
{
    public static LocalizationService Current { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _translations = new();

    [ObservableProperty]
    private string _currentLanguage = "ru";

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = new[]
    {
        new LanguageOption("ru", "Русский"),
        new LanguageOption("en", "English"),
    };

    /// <summary>XAML indexer binding hits this for every key.</summary>
    public string this[string key]
    {
        get
        {
            if (_translations.TryGetValue(CurrentLanguage, out var dict)
                && dict.TryGetValue(key, out var value))
                return value;
            // Last-resort fallback to ru, then to the key itself.
            if (CurrentLanguage != "ru"
                && _translations.TryGetValue("ru", out var ruDict)
                && ruDict.TryGetValue(key, out var ruValue))
                return ruValue;
            return key;
        }
    }

    private LocalizationService()
    {
        LoadTranslations("ru");
        LoadTranslations("en");
    }

    public void SetLanguage(string lang)
    {
        if (!_translations.ContainsKey(lang)) return;
        CurrentLanguage = lang;
    }

    partial void OnCurrentLanguageChanged(string value)
    {
        // Tell WPF that every indexer binding should re-evaluate.
        OnPropertyChanged("Item[]");
    }

    private void LoadTranslations(string lang)
    {
        try
        {
            // Resources live as pack URIs inside the assembly.
            var uri = new Uri($"pack://application:,,,/Assets/i18n/{lang}.json", UriKind.Absolute);
            var info = Application.GetResourceStream(uri);
            if (info is null) return;
            using var reader = new StreamReader(info.Stream);
            var json = reader.ReadToEnd();
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is not null) _translations[lang] = dict;
        }
        catch
        {
            // Missing resource — leave language absent from _translations,
            // which causes fallback to ru / raw key.
        }
    }
}

public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>
/// XAML markup extension: <c>{loc:T open_file}</c> produces a one-way
/// binding to <c>LocalizationService.Current[open_file]</c>.
/// </summary>
public class TExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public TExtension() { }
    public TExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Current,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// IValueConverter that turns a 0-based index into a 1-based display value.
/// Used for "Page N" labels where Index is 0-based internally.
/// </summary>
public sealed class OneBasedConverter : IValueConverter
{
    public static readonly OneBasedConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i ? (i + 1).ToString(CultureInfo.InvariantCulture) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
