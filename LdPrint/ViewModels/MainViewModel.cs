using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using LdPrint.Models;
using LdPrint.Services;

namespace LdPrint.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private TsplDocument? _document;

    [ObservableProperty]
    private ObservableCollection<string> _printers = new();

    [ObservableProperty]
    private string? _selectedPrinter;

    [ObservableProperty]
    private string _pageRangeText = string.Empty;

    [ObservableProperty]
    private TsplPage? _selectedPage;

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AssociationButtonText))]
    private bool _isFileAssociationRegistered;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private ThemeChoice? _selectedTheme;

    public ObservableCollection<TsplPage> Pages { get; } = new();

    /// <summary>
    /// Subset of <see cref="Pages"/> that matches the current PageRangeText
    /// filter. The ListBox binds to this so the user only sees the pages
    /// they're about to print; the full Pages list is still used for the
    /// page indicator and the print pipeline.
    /// </summary>
    public ObservableCollection<TsplPage> VisiblePages { get; } = new();

    public IReadOnlyList<LanguageOption> AvailableLanguages =>
        LocalizationService.Current.AvailableLanguages;

    /// <summary>
    /// Themes wrapped in a localised display object. The wrapper subscribes
    /// to language changes so DisplayName updates live without needing to
    /// rebuild the collection on every language switch.
    /// </summary>
    public IReadOnlyList<ThemeChoice> AvailableThemes { get; } =
        ThemeService.Current.AvailableThemes
            .Select(t => new ThemeChoice(t)).ToArray();

    public string FileLabel
    {
        get
        {
            if (Document is null) return Loc("no_file");
            return $"{Path.GetFileName(FilePath ?? "")}  •  {Document.PageCount} {Loc("pages_count_short")}  •  {FormatBytes(GetTotalBytes())}";
        }
    }

    public string AssociationButtonText =>
        IsFileAssociationRegistered ? Loc("remove_association") : Loc("set_as_default");

    /// <summary>
    /// Multi-piece line describing the loaded label's print metadata: size,
    /// detected DPI, density, speed, direction, gap, codepage. Pieces are
    /// joined with "·" and any unset piece is skipped.
    /// </summary>
    public string LabelMetadataText
    {
        get
        {
            if (Document is null) return string.Empty;
            var parts = new List<string>();
            if (Document.WidthMm > 0 && Document.HeightMm > 0)
                parts.Add(string.Format(Loc("meta_size"),
                    FormatMm(Document.WidthMm), FormatMm(Document.HeightMm)));
            // DPI auto-detected per-page; if we have a selected page show its
            // effective DPI, otherwise the document's declared DPI.
            int dpi = SelectedPage is not null
                ? TsplRenderer.ResolveEffectiveDpi(Document, SelectedPage)
                : Document.Dpi;
            parts.Add(string.Format(Loc("meta_dpi"), dpi));
            if (Document.Density >= 0)
                parts.Add(string.Format(Loc("meta_density"), Document.Density));
            if (Document.Speed > 0)
                parts.Add(string.Format(Loc("meta_speed"), Document.Speed));
            parts.Add(string.Format(Loc("meta_direction"), Document.Direction));
            if (Document.GapMm > 0)
                parts.Add(string.Format(Loc("meta_gap"), FormatMm(Document.GapMm)));
            if (!string.IsNullOrEmpty(Document.Codepage))
                parts.Add(string.Format(Loc("meta_codepage"), Document.Codepage));
            return string.Join("  ·  ", parts);
        }
    }

    public string PageIndicator
    {
        get
        {
            if (Document is null || Document.PageCount == 0) return string.Empty;
            var cur = SelectedPage?.Index + 1 ?? 0;
            return string.Format(Loc("page_indicator"), cur, Document.PageCount);
        }
    }

    private static string FormatMm(double mm)
        => mm % 1 == 0 ? mm.ToString("F0", CultureInfo.InvariantCulture)
                       : mm.ToString("F1", CultureInfo.InvariantCulture);

    public MainViewModel() : this(null) { }

    public MainViewModel(string? initialFile)
    {
        _statusMessage = Loc("status_ready");
        SelectedLanguage = AvailableLanguages.FirstOrDefault(
            l => l.Code == LocalizationService.Current.CurrentLanguage)
            ?? AvailableLanguages[0];
        // Initial value of SelectedTheme matches the theme already applied by
        // App.OnStartup via SettingsService. Setting it here doesn't re-apply
        // because OnSelectedThemeChanged short-circuits when the chosen id
        // equals the current SelectedThemeId.
        SelectedTheme = AvailableThemes.FirstOrDefault(
            t => t.Id == ThemeService.Current.SelectedThemeId)
            ?? AvailableThemes[0];

        // React to language changes for non-bound strings (FileLabel,
        // AssociationButtonText, StatusMessage that already had ru text).
        LocalizationService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocalizationService.CurrentLanguage)
                || e.PropertyName == "Item[]")
            {
                OnPropertyChanged(nameof(FileLabel));
                OnPropertyChanged(nameof(AssociationButtonText));
                OnPropertyChanged(nameof(LabelMetadataText));
                OnPropertyChanged(nameof(PageIndicator));
            }
        };

        RefreshPrinters();
        RefreshAssociationState();

        if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile))
            LoadFile(initialFile);
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void OpenFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc("dialog_open_title"),
            Filter = Loc("dialog_filter_tspl"),
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;
        LoadFile(dlg.FileName);
    }

    private void LoadFile(string path)
    {
        try
        {
            var doc = TsplParser.Parse(path);
            FilePath = path;
            Document = doc;

            Pages.Clear();
            foreach (var p in doc.Pages) Pages.Add(p);
            // Reset filter to "all pages" — VisiblePages starts as a copy
            // of Pages. OnPageRangeTextChanged will resync if the user
            // types a range later.
            VisiblePages.Clear();
            foreach (var p in doc.Pages) VisiblePages.Add(p);

            SelectedPage = Pages.Count > 0 ? Pages[0] : null;
            PageRangeText = string.Empty;
            StatusMessage = string.Format(CultureInfo.InvariantCulture,
                Loc("status_loaded"), doc.PageCount, Path.GetFileName(path));
            OnPropertyChanged(nameof(FileLabel));
            PrintCommand.NotifyCanExecuteChanged();
            SelectAllPagesCommand.NotifyCanExecuteChanged();
            SelectCurrentPageCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc("error_parse_prefix"), ex.Message);
            MessageBox.Show(ex.ToString(), Loc("error_parse_title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RefreshPrinters()
    {
        var current = SelectedPrinter;
        Printers.Clear();
        foreach (var p in RawPrinter.ListInstalledPrinters())
            Printers.Add(p);

        if (current != null && Printers.Contains(current))
            SelectedPrinter = current;
        else
        {
            var def = RawPrinter.DefaultPrinterName();
            SelectedPrinter = Printers.Contains(def) ? def
                : (Printers.Count > 0 ? Printers[0] : null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelectPages))]
    private void SelectAllPages() => PageRangeText = string.Empty;

    /// <summary>
    /// Move the selected page by <paramref name="delta"/> with clamping at
    /// the list boundaries. Called from MainWindow on mouse-wheel events so
    /// the user can flip through labels with the wheel.
    /// </summary>
    public void NavigatePage(int delta)
    {
        if (Document is null || VisiblePages.Count == 0) return;
        // Wheel navigates within the currently-visible subset so the user
        // doesn't jump to hidden pages they've filtered out.
        var current = SelectedPage is null ? 0 : VisiblePages.IndexOf(SelectedPage);
        if (current < 0) current = 0;
        var next = Math.Clamp(current + delta, 0, VisiblePages.Count - 1);
        if (next != current) SelectedPage = VisiblePages[next];
    }

    /// <summary>
    /// Open the Windows print preferences dialog for the selected printer.
    /// Uses rundll32 + printui.dll which is the standard, non-admin path —
    /// same UX as Notepad's File → Page Setup → Properties.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenPrinterSettings))]
    private void OpenPrinterSettings()
    {
        if (string.IsNullOrEmpty(SelectedPrinter)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = $"printui.dll,PrintUIEntry /e /n \"{SelectedPrinter}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private bool CanOpenPrinterSettings() => !string.IsNullOrEmpty(SelectedPrinter);

    [RelayCommand(CanExecute = nameof(CanSelectCurrent))]
    private void SelectCurrentPage()
    {
        if (SelectedPage is null) return;
        PageRangeText = (SelectedPage.Index + 1).ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task PrintAsync()
    {
        if (Document is null || string.IsNullOrEmpty(SelectedPrinter)) return;

        int[] indices;
        try
        {
            indices = PageRangeParser.Parse(PageRangeText, Document.PageCount);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc("error_range_prefix"), ex.Message);
            return;
        }

        IsBusy = true;
        StatusMessage = string.Format(Loc("status_sending"), indices.Length, SelectedPrinter);

        try
        {
            var bytes = TsplParser.BuildPrintStream(Document, indices);
            var docName = $"Lucky Dragon Print: {Path.GetFileName(FilePath ?? "labels.tspl")}";
            var written = await Task.Run(() =>
                RawPrinter.SendBytes(SelectedPrinter, docName, bytes));
            StatusMessage = string.Format(Loc("status_printed"),
                indices.Length, FormatBytes(written), SelectedPrinter);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc("error_print_prefix"), ex.Message);
            MessageBox.Show(ex.ToString(), Loc("error_print_title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleFileAssociation()
    {
        try
        {
            if (IsFileAssociationRegistered)
            {
                FileAssociationService.Unregister();
                StatusMessage = Loc("status_assoc_removed");
            }
            else
            {
                FileAssociationService.Register();
                StatusMessage = Loc("status_assoc_set");
            }
            RefreshAssociationState();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc("error_assoc_prefix"), ex.Message);
            MessageBox.Show(ex.ToString(), Loc("error_assoc_title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;
        LocalizationService.Current.SetLanguage(value.Code);
        App.Settings.Language = value.Code;
        App.Settings.Save();
    }

    partial void OnSelectedThemeChanged(ThemeChoice? value)
    {
        if (value is null) return;
        if (ThemeService.Current.SelectedThemeId == value.Id) return;
        ThemeService.Current.SetTheme(value.Id);
        App.Settings.Theme = value.Id;
        App.Settings.Save();
    }

    // ---- CanExecute helpers ----
    private bool NotBusy => !IsBusy;
    private bool CanPrint() => !IsBusy && Document != null && Document.PageCount > 0 && !string.IsNullOrEmpty(SelectedPrinter);
    private bool CanSelectPages() => Document != null && Document.PageCount > 0;
    private bool CanSelectCurrent() => SelectedPage != null;

    partial void OnDocumentChanged(TsplDocument? value)
    {
        OnPropertyChanged(nameof(FileLabel));
        OnPropertyChanged(nameof(LabelMetadataText));
        OnPropertyChanged(nameof(PageIndicator));
    }

    partial void OnSelectedPrinterChanged(string? value)
    {
        PrintCommand.NotifyCanExecuteChanged();
        OpenPrinterSettingsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Reactive preview: when the user types a page range, select the first
    /// page matched so the preview pane updates as they type. Parse errors
    /// are ignored silently — half-typed input like "5-" shouldn't fight the
    /// user with an error message.
    /// </summary>
    partial void OnPageRangeTextChanged(string value)
    {
        if (Document is null || Pages.Count == 0) return;
        try
        {
            var indices = PageRangeParser.Parse(value, Document.PageCount);
            if (indices.Length == 0) return;
            RebuildVisiblePages(indices);
            var target = Pages[indices[0]];
            if (!ReferenceEquals(target, SelectedPage)) SelectedPage = target;
        }
        catch
        {
            // partial input — leave VisiblePages and selection alone so the
            // user isn't blasted with errors mid-typing.
        }
    }

    /// <summary>
    /// Replace VisiblePages with the subset of Pages identified by the given
    /// 0-based indices. Keeps SelectedPage if it's still visible; otherwise
    /// falls back to the first visible page.
    /// </summary>
    private void RebuildVisiblePages(int[] visibleIndices)
    {
        VisiblePages.Clear();
        foreach (var idx in visibleIndices)
            if (idx >= 0 && idx < Pages.Count)
                VisiblePages.Add(Pages[idx]);

        if (VisiblePages.Count == 0) return;
        if (SelectedPage is null || !VisiblePages.Contains(SelectedPage))
            SelectedPage = VisiblePages[0];
    }

    partial void OnSelectedPageChanged(TsplPage? value)
    {
        SelectCurrentPageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(LabelMetadataText));
        OnPropertyChanged(nameof(PageIndicator));
        if (value is null || Document is null) return;
        if (value.PreviewImage is not null) return;

        // Render on the thread pool — TSPL rendering with bitmap decoding +
        // ZXing barcodes can take 50-200ms per page; we don't want to block
        // the UI thread. The page is an ObservableObject so when PreviewImage
        // is assigned, WPF refreshes automatically.
        var doc = Document;
        var page = value;
        _ = Task.Run(() =>
        {
            TsplRenderer.EnsureRendered(doc, page);
        });
    }

    private void RefreshAssociationState()
    {
        IsFileAssociationRegistered = FileAssociationService.IsRegistered();
    }

    // ---- helpers ----

    private static string Loc(string key) => LocalizationService.Current[key];

    private long GetTotalBytes()
    {
        if (Document is null) return 0;
        long total = Document.Header.LongLength;
        foreach (var p in Document.Pages) total += p.Content.LongLength;
        return total;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}

/// <summary>
/// View-side wrapper for a <see cref="ThemeOption"/> that exposes a
/// localised <see cref="DisplayName"/> for the ComboBox and re-raises
/// PropertyChanged whenever the active language changes so the label
/// flips between «Светлая» and «Light» live.
/// </summary>
public sealed class ThemeChoice : ObservableObject
{
    private readonly ThemeOption _option;

    public ThemeChoice(ThemeOption option)
    {
        _option = option;
        LocalizationService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocalizationService.CurrentLanguage)
                || e.PropertyName == "Item[]")
                OnPropertyChanged(nameof(DisplayName));
        };
    }

    public string Id => _option.Id;
    public string DisplayName => LocalizationService.Current[_option.LocalizationKey];
}
