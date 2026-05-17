namespace LdPrint.Models;

/// <summary>
/// Parsed TSPL file split into a global header and a sequence of pages.
/// Header-extracted label metadata (dimensions, DPI, direction) lives on
/// the document and is consumed by the graphical renderer.
/// </summary>
public sealed class TsplDocument
{
    public string SourcePath { get; init; } = string.Empty;
    public byte[] Header { get; init; } = [];
    public IReadOnlyList<TsplPage> Pages { get; init; } = [];

    /// <summary>Best-effort encoding name used for preview decoding only.</summary>
    public string PreviewEncoding { get; init; } = "ascii";

    /// <summary>Label width in millimeters from the SIZE command. 0 if unknown.</summary>
    public double WidthMm { get; init; }

    /// <summary>Label height in millimeters from the SIZE command. 0 if unknown.</summary>
    public double HeightMm { get; init; }

    /// <summary>
    /// Resolution in dots per inch. Defaults to 203 (the standard for TSC /
    /// Zebra desktop models). Honored if the file's DENSITY/DPI header tells
    /// us otherwise.
    /// </summary>
    public int Dpi { get; init; } = 203;

    /// <summary>DIRECTION command value (0 = head-out, 1 = head-in / 180°). Default 0.</summary>
    public int Direction { get; init; }

    /// <summary>DENSITY (darkness) 0..15. -1 if not specified.</summary>
    public int Density { get; init; } = -1;

    /// <summary>SPEED in inches per second. 0 if not specified.</summary>
    public int Speed { get; init; }

    /// <summary>GAP between labels in millimeters. 0 if not specified.</summary>
    public double GapMm { get; init; }

    /// <summary>CODEPAGE name (e.g. "UTF-8", "1251", "DEFAULT"). Empty if not set.</summary>
    public string Codepage { get; init; } = string.Empty;

    public int PageCount => Pages.Count;
}
