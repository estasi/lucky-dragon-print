using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LdPrint.Models;

/// <summary>
/// One TSPL "page" — bytes from the CLS that begins it through the PRINT
/// that ends it (inclusive of both). Preserved as raw bytes so the original
/// encoding of TEXT payloads (CP866 / Win-1251 / GB2312 / UTF-8) is intact.
///
/// The graphical preview <see cref="PreviewImage"/> is filled in lazily by
/// the renderer on first access from the ViewModel.
/// </summary>
public sealed partial class TsplPage : ObservableObject
{
    public int Index { get; init; }
    public byte[] Content { get; init; } = [];
    public string PreviewText { get; init; } = string.Empty;
    public int CopiesN { get; init; } = 1;
    public int CopiesM { get; init; } = 1;

    /// <summary>
    /// Rendered visual preview. Null until <c>TsplRenderer.EnsureRendered</c>
    /// fills it in. WPF <c>Image</c> binds to this; ObservableObject ensures
    /// the binding refreshes when the renderer completes.
    /// </summary>
    [ObservableProperty]
    private BitmapSource? _previewImage;

    public int SizeBytes => Content.Length;
    public string DisplayLabel => $"Page {Index + 1}";
}
