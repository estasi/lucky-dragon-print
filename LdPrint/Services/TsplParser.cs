using System.IO;
using System.Text;
using LdPrint.Models;

namespace LdPrint.Services;

/// <summary>
/// Byte-level walker for TSPL files. Knows about three classes of commands:
///
/// 1. Boundary commands (CLS, PRINT) — divide the file into header + N pages.
/// 2. Binary-payload commands (BITMAP, DOWNLOAD) — have raw bytes inline after
///    their parameter list. The walker must skip those bytes by computed
///    length, otherwise newline / "PRINT" bytes inside the payload create
///    phantom line starts and corrupt page detection.
/// 3. Plain text commands (TEXT, BARCODE, DMATRIX, …) — single-line, no
///    inline binary. Used for preview generation.
///
/// Encoding of TEXT payloads is never re-encoded; only ASCII keywords are
/// scanned. Preview decoding tries UTF-8 → CP866 → Win-1251 for human
/// display only — the printer always receives the original bytes.
/// </summary>
public static class TsplParser
{
    public static TsplDocument Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is empty.", nameof(path));

        var bytes = File.ReadAllBytes(path);
        var doc = Parse(bytes);

        return new TsplDocument
        {
            SourcePath = path,
            Header = doc.Header,
            Pages = doc.Pages,
            PreviewEncoding = doc.PreviewEncoding,
            WidthMm = doc.WidthMm,
            HeightMm = doc.HeightMm,
            Dpi = doc.Dpi,
            Direction = doc.Direction,
            Density = doc.Density,
            Speed = doc.Speed,
            GapMm = doc.GapMm,
            Codepage = doc.Codepage,
        };
    }

    public static TsplDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        EnsureCodePagesRegistered();

        // Skip UTF-8 BOM if present.
        var pos = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            pos = 3;

        var streamStart = pos;
        var headerEnd = -1;
        var pageStart = -1; // offset of current page's CLS, or -1 if not in a page
        var pages = new List<TsplPage>();

        while (pos < bytes.Length)
        {
            // Identify what this line starts with. We accept whitespace before
            // the keyword on a line (rare in generated TSPL but cheap to handle).
            var lineStart = pos;
            var contentStart = SkipHorizontalWhitespace(bytes, pos);

            if (MatchKeyword(bytes, contentStart, "BITMAP"))
            {
                // BITMAP x,y,width_bytes,height,mode,<binary>
                var lineEnd = FindEol(bytes, contentStart);
                var payloadStart = SkipBitmapParamsToBinary(bytes, contentStart + 6, lineEnd,
                    out var width, out var height);
                if (payloadStart < 0)
                {
                    // Malformed — fall back to plain line skip so we don't loop.
                    pos = AfterEol(bytes, lineEnd);
                    continue;
                }
                var payloadEnd = payloadStart + checked(width * height);
                if (payloadEnd > bytes.Length) payloadEnd = bytes.Length; // defensive
                pos = AfterEol(bytes, payloadEnd - 1);
                if (pos <= payloadEnd) pos = payloadEnd; // ensure forward progress if no \n after payload
                // Also consume an optional trailing CRLF right after the payload.
                if (pos < bytes.Length && bytes[pos] == 0x0D) pos++;
                if (pos < bytes.Length && bytes[pos] == 0x0A) pos++;
                continue;
            }

            if (MatchKeyword(bytes, contentStart, "DOWNLOAD"))
            {
                // DOWNLOAD "name",size,<binary>
                var lineEnd = FindEol(bytes, contentStart);
                var payloadStart = SkipDownloadHeaderToBinary(bytes, contentStart + 8, lineEnd,
                    out var size);
                if (payloadStart < 0 || size <= 0)
                {
                    pos = AfterEol(bytes, lineEnd);
                    continue;
                }
                var payloadEnd = Math.Min(payloadStart + size, bytes.Length);
                pos = payloadEnd;
                if (pos < bytes.Length && bytes[pos] == 0x0D) pos++;
                if (pos < bytes.Length && bytes[pos] == 0x0A) pos++;
                continue;
            }

            if (MatchKeyword(bytes, contentStart, "CLS"))
            {
                if (headerEnd < 0) headerEnd = lineStart;
                pageStart = lineStart;
                pos = AfterEol(bytes, FindEol(bytes, contentStart));
                continue;
            }

            if (MatchKeyword(bytes, contentStart, "PRINT"))
            {
                var lineEnd = FindEol(bytes, contentStart);
                var pageEndExclusive = AfterEol(bytes, lineEnd);
                if (pageStart < 0)
                    pageStart = headerEnd >= 0 ? headerEnd : streamStart;
                var n = ParsePrintCopies(bytes, contentStart + 5, lineEnd, out var m);

                var pageBytes = bytes.AsSpan(pageStart, pageEndExclusive - pageStart).ToArray();
                pages.Add(new TsplPage
                {
                    Index = pages.Count,
                    Content = pageBytes,
                    PreviewText = BuildPreviewText(pageBytes),
                    CopiesN = n,
                    CopiesM = m,
                });

                pageStart = -1;
                pos = pageEndExclusive;
                continue;
            }

            // Default: skip this line.
            pos = AfterEol(bytes, FindEol(bytes, contentStart));
        }

        if (headerEnd < 0) headerEnd = streamStart;
        var header = bytes.AsSpan(streamStart, headerEnd - streamStart).ToArray();

        var meta = ExtractLabelMetadata(header);

        return new TsplDocument
        {
            Header = header,
            Pages = pages,
            PreviewEncoding = "auto",
            WidthMm = meta.WidthMm,
            HeightMm = meta.HeightMm,
            Dpi = meta.Dpi,
            Direction = meta.Direction,
            Density = meta.Density,
            Speed = meta.Speed,
            GapMm = meta.GapMm,
            Codepage = meta.Codepage,
        };
    }

    /// <summary>Bundle of metadata extracted from the TSPL header.</summary>
    internal record HeaderMetadata(
        double WidthMm, double HeightMm, int Dpi, int Direction,
        int Density, int Speed, double GapMm, string Codepage);

    /// <summary>
    /// Scans the header bytes for SIZE / GAP / DIRECTION / DENSITY / SPEED /
    /// CODEPAGE / DPI commands. DPI defaults to 203 (TSC desktop); the rest
    /// default to sentinel values (-1 / 0 / "") meaning "not specified".
    /// </summary>
    private static HeaderMetadata ExtractLabelMetadata(byte[] header)
    {
        var text = System.Text.Encoding.UTF8.GetString(header, 0,
            header.Length).Replace("\r", " ");
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        const System.Text.RegularExpressions.RegexOptions O =
            System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        double w = 0, h = 0, gap = 0;
        var dpi = 203;
        var dir = 0;
        var density = -1;
        var speed = 0;
        var codepage = string.Empty;

        var sizeMatch = System.Text.RegularExpressions.Regex.Match(text,
            @"\bSIZE\s+([\d.]+)\s*(mm|inch|in)?\s*,\s*([\d.]+)\s*(mm|inch|in)?", O);
        if (sizeMatch.Success)
        {
            var wVal = double.Parse(sizeMatch.Groups[1].Value, ci);
            var hVal = double.Parse(sizeMatch.Groups[3].Value, ci);
            var wUnit = sizeMatch.Groups[2].Value.ToLowerInvariant();
            var hUnit = sizeMatch.Groups[4].Value.ToLowerInvariant();
            w = (wUnit is "inch" or "in") ? wVal * 25.4 : wVal;
            h = (hUnit is "inch" or "in") ? hVal * 25.4 : hVal;
        }

        // GAP w[unit], h[unit] — first value is the gap distance between labels.
        var gapMatch = System.Text.RegularExpressions.Regex.Match(text,
            @"\bGAP\s+([\d.]+)\s*(mm|inch|in)?\s*,\s*([\d.]+)\s*(mm|inch|in)?", O);
        if (gapMatch.Success)
        {
            var gVal = double.Parse(gapMatch.Groups[1].Value, ci);
            var gUnit = gapMatch.Groups[2].Value.ToLowerInvariant();
            gap = (gUnit is "inch" or "in") ? gVal * 25.4 : gVal;
        }

        var dirMatch = System.Text.RegularExpressions.Regex.Match(text,
            @"\bDIRECTION\s+(\d+)", O);
        if (dirMatch.Success && int.TryParse(dirMatch.Groups[1].Value, out var d))
            dir = d;

        var densityMatch = System.Text.RegularExpressions.Regex.Match(text,
            @"\bDENSITY\s+(\d+)", O);
        if (densityMatch.Success && int.TryParse(densityMatch.Groups[1].Value, out var den)
            && den is >= 0 and <= 15)
            density = den;

        var speedMatch = System.Text.RegularExpressions.Regex.Match(text,
            @"\bSPEED\s+(\d+)", O);
        if (speedMatch.Success && int.TryParse(speedMatch.Groups[1].Value, out var sp))
            speed = sp;

        var cpMatch = System.Text.RegularExpressions.Regex.Match(text,
            @"\bCODEPAGE\s+([A-Za-z0-9\-]+)", O);
        if (cpMatch.Success) codepage = cpMatch.Groups[1].Value;

        // Optional non-standard DPI hint. DPI is a printer property in TSPL,
        // not a command — only present in proprietary extensions.
        var dpiMatch = System.Text.RegularExpressions.Regex.Match(text,
            @"\bDPI\s+(\d+)", O);
        if (dpiMatch.Success && int.TryParse(dpiMatch.Groups[1].Value, out var dotsPerInch)
            && dotsPerInch is >= 100 and <= 1200)
            dpi = dotsPerInch;

        return new HeaderMetadata(w, h, dpi, dir, density, speed, gap, codepage);
    }

    /// <summary>
    /// Build a print-ready byte stream containing the global header followed
    /// by the byte slices of the selected pages in the given order.
    /// </summary>
    public static byte[] BuildPrintStream(TsplDocument doc, IEnumerable<int> pageIndices)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(pageIndices);

        using var ms = new MemoryStream();
        ms.Write(doc.Header);
        foreach (var idx in pageIndices)
        {
            if (idx < 0 || idx >= doc.Pages.Count)
                throw new ArgumentOutOfRangeException(nameof(pageIndices),
                    $"Page index {idx} out of range (0..{doc.Pages.Count - 1}).");
            ms.Write(doc.Pages[idx].Content);
        }
        return ms.ToArray();
    }

    // ─── Keyword / line scanning ────────────────────────────────────────────

    private static int FindEol(byte[] b, int from)
    {
        var i = from;
        while (i < b.Length && b[i] != 0x0A) i++;
        return i;
    }

    /// <summary>Offset of the first byte after the EOL at position <paramref name="eolIdx"/>.</summary>
    private static int AfterEol(byte[] b, int eolIdx)
    {
        if (eolIdx >= b.Length) return b.Length;
        return b[eolIdx] == 0x0A ? eolIdx + 1 : eolIdx;
    }

    private static int SkipHorizontalWhitespace(byte[] b, int from)
    {
        var i = from;
        while (i < b.Length && (b[i] == 0x20 || b[i] == 0x09)) i++;
        return i;
    }

    /// <summary>
    /// Returns true if bytes[off..] starts with the ASCII keyword (case-
    /// insensitive) followed by a non-word byte or end-of-buffer.
    /// </summary>
    private static bool MatchKeyword(byte[] b, int off, string keyword)
    {
        if (off + keyword.Length > b.Length) return false;
        for (var i = 0; i < keyword.Length; i++)
        {
            var ch = b[off + i];
            if (ch >= 'a' && ch <= 'z') ch = (byte)(ch - 32);
            if (ch != (byte)keyword[i]) return false;
        }
        if (off + keyword.Length == b.Length) return true;
        var next = b[off + keyword.Length];
        var isWord = (next >= 'A' && next <= 'Z') || (next >= 'a' && next <= 'z')
                  || (next >= '0' && next <= '9') || next == '_';
        return !isWord;
    }

    // ─── BITMAP / DOWNLOAD header parsing ───────────────────────────────────

    /// <summary>
    /// Parses 5 integer parameters of a BITMAP command after the "BITMAP"
    /// keyword, returns the offset where the binary payload starts (one past
    /// the last comma) and the width × height pair, or -1 if malformed.
    /// </summary>
    private static int SkipBitmapParamsToBinary(byte[] b, int from, int lineEnd,
        out int width, out int height)
    {
        width = height = 0;
        var i = from;
        var vals = new int[5];
        var lastCommaPos = -1;
        for (var p = 0; p < 5; p++)
        {
            i = SkipHorizontalWhitespace(b, i);
            var sign = 1;
            if (i < lineEnd && b[i] == '-') { sign = -1; i++; }
            if (i >= lineEnd || b[i] < '0' || b[i] > '9') return -1;
            var n = 0;
            while (i < lineEnd && b[i] >= '0' && b[i] <= '9')
            {
                n = n * 10 + (b[i] - '0');
                i++;
            }
            vals[p] = sign * n;
            i = SkipHorizontalWhitespace(b, i);
            if (p < 4)
            {
                if (i >= lineEnd || b[i] != ',') return -1;
                i++; // skip comma
            }
            else
            {
                // 5th value (mode) — the comma after it separates binary.
                if (i >= lineEnd || b[i] != ',') return -1;
                lastCommaPos = i;
                i++;
            }
        }
        width = vals[2];
        height = vals[3];
        if (width <= 0 || height <= 0) return -1;
        return lastCommaPos < 0 ? -1 : lastCommaPos + 1;
    }

    /// <summary>
    /// Parses a DOWNLOAD command header (DOWNLOAD "name",size,) and returns
    /// the offset where the binary payload starts plus the byte size.
    /// </summary>
    private static int SkipDownloadHeaderToBinary(byte[] b, int from, int lineEnd, out int size)
    {
        size = 0;
        var i = SkipHorizontalWhitespace(b, from);
        // Optional quoted name: "name"
        if (i < lineEnd && b[i] == '"')
        {
            i++;
            while (i < lineEnd && b[i] != '"') i++;
            if (i >= lineEnd) return -1;
            i++; // skip closing quote
            i = SkipHorizontalWhitespace(b, i);
            if (i >= lineEnd || b[i] != ',') return -1;
            i++;
        }
        i = SkipHorizontalWhitespace(b, i);
        var n = 0;
        if (i >= lineEnd || b[i] < '0' || b[i] > '9') return -1;
        while (i < lineEnd && b[i] >= '0' && b[i] <= '9')
        {
            n = n * 10 + (b[i] - '0');
            i++;
        }
        size = n;
        i = SkipHorizontalWhitespace(b, i);
        if (i >= lineEnd || b[i] != ',') return -1;
        // Binary starts right after this comma — but it might continue on next
        // line if generator emitted a newline. Most variants put binary
        // straight after the comma on the same line; consume to end of line
        // and start of payload coincides.
        return i + 1;
    }

    private static int ParsePrintCopies(byte[] b, int from, int lineEnd, out int m)
    {
        var i = SkipHorizontalWhitespace(b, from);
        int n = 1;
        m = 1;
        var v = 0; var hasV = false;
        while (i < lineEnd && b[i] >= '0' && b[i] <= '9')
        {
            v = v * 10 + (b[i] - '0');
            hasV = true;
            i++;
        }
        if (hasV) n = Math.Max(1, v);
        i = SkipHorizontalWhitespace(b, i);
        if (i < lineEnd && b[i] == ',')
        {
            i++;
            i = SkipHorizontalWhitespace(b, i);
            v = 0; hasV = false;
            while (i < lineEnd && b[i] >= '0' && b[i] <= '9')
            {
                v = v * 10 + (b[i] - '0');
                hasV = true;
                i++;
            }
            if (hasV) m = Math.Max(1, v);
        }
        return n;
    }

    // ─── Preview generation (human-readable per-page summary) ───────────────

    /// <summary>
    /// Walk a page's bytes the same way as the top-level parser and emit
    /// short human-readable lines for the drawing primitives encountered.
    /// Binary payloads are skipped (only their dimensions reported) and
    /// quoted strings are extracted from TEXT/BARCODE/DMATRIX/QRCODE.
    /// </summary>
    private static string BuildPreviewText(byte[] pageBytes)
    {
        var lines = new List<string>();
        var pos = 0;
        const int maxLines = 8;

        while (pos < pageBytes.Length && lines.Count < maxLines)
        {
            pos = SkipHorizontalWhitespace(pageBytes, pos);
            if (pos >= pageBytes.Length) break;
            if (pageBytes[pos] == 0x0A || pageBytes[pos] == 0x0D) { pos++; continue; }

            var lineEnd = FindEol(pageBytes, pos);

            // BITMAP — skip binary, emit a summary.
            if (MatchKeyword(pageBytes, pos, "BITMAP"))
            {
                var bin = SkipBitmapParamsToBinary(pageBytes, pos + 6, lineEnd, out var w, out var h);
                if (bin > 0)
                {
                    var bytesCount = w * h;
                    // Try to also extract x,y for nicer label.
                    var coords = ExtractFirstTwoInts(pageBytes, pos + 6, lineEnd);
                    var coordStr = coords.HasValue ? $"({coords.Value.x},{coords.Value.y})" : "";
                    lines.Add($"BITMAP at {coordStr}: {w} bytes/row × {h} rows ({bytesCount} bytes)");
                    var binEnd = Math.Min(bin + bytesCount, pageBytes.Length);
                    pos = binEnd;
                    if (pos < pageBytes.Length && pageBytes[pos] == 0x0D) pos++;
                    if (pos < pageBytes.Length && pageBytes[pos] == 0x0A) pos++;
                    continue;
                }
            }

            if (MatchKeyword(pageBytes, pos, "DOWNLOAD"))
            {
                var bin = SkipDownloadHeaderToBinary(pageBytes, pos + 8, lineEnd, out var size);
                if (bin > 0)
                {
                    lines.Add($"DOWNLOAD: {size} bytes payload");
                    pos = Math.Min(bin + size, pageBytes.Length);
                    if (pos < pageBytes.Length && pageBytes[pos] == 0x0D) pos++;
                    if (pos < pageBytes.Length && pageBytes[pos] == 0x0A) pos++;
                    continue;
                }
            }

            // Plain text commands: decode the line and extract content.
            if (TryDescribeCommand(pageBytes, pos, lineEnd, out var description))
            {
                lines.Add(description);
            }
            pos = AfterEol(pageBytes, lineEnd);
        }

        if (lines.Count == 0) return "(no drawing commands)";
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// For a single line, try to recognize a known TSPL drawing command and
    /// emit a short readable description. Decodes the line as text using a
    /// fallback chain (UTF-8 → CP866 → Win-1251) for display only.
    /// </summary>
    private static bool TryDescribeCommand(byte[] b, int lineStart, int lineEnd, out string description)
    {
        description = string.Empty;
        if (lineEnd <= lineStart) return false;

        var line = DecodeLine(b, lineStart, lineEnd);

        if (StartsWithCommand(line, "TEXT"))
        {
            var s = ExtractLastQuotedString(line);
            description = s is null ? Truncate(line, 100)
                                    : $"TEXT: '{Truncate(s, 60)}'";
            return true;
        }
        if (StartsWithCommand(line, "BARCODE"))
        {
            var quotes = ExtractAllQuotedStrings(line);
            if (quotes.Count >= 2)
                description = $"BARCODE {quotes[0]}: {Truncate(quotes[^1], 60)}";
            else if (quotes.Count == 1)
                description = $"BARCODE: {Truncate(quotes[0], 60)}";
            else
                description = Truncate(line, 100);
            return true;
        }
        if (StartsWithCommand(line, "DMATRIX"))
        {
            var s = ExtractLastQuotedString(line);
            description = s is null ? Truncate(line, 100)
                                    : $"DMATRIX: {Truncate(s, 64)}";
            return true;
        }
        if (StartsWithCommand(line, "QRCODE"))
        {
            var s = ExtractLastQuotedString(line);
            description = s is null ? Truncate(line, 100)
                                    : $"QRCODE: {Truncate(s, 60)}";
            return true;
        }
        if (StartsWithCommand(line, "PDF417"))
        {
            var s = ExtractLastQuotedString(line);
            description = s is null ? Truncate(line, 100) : $"PDF417: {Truncate(s, 60)}";
            return true;
        }
        if (StartsWithCommand(line, "BAR") ||
            StartsWithCommand(line, "BOX") ||
            StartsWithCommand(line, "ELLIPSE") ||
            StartsWithCommand(line, "REVERSE") ||
            StartsWithCommand(line, "ERASE"))
        {
            description = Truncate(line, 100);
            return true;
        }
        return false;
    }

    private static bool StartsWithCommand(string line, string kw)
    {
        if (!line.StartsWith(kw, StringComparison.OrdinalIgnoreCase)) return false;
        if (line.Length == kw.Length) return true;
        var ch = line[kw.Length];
        return !char.IsLetterOrDigit(ch) && ch != '_';
    }

    private static string? ExtractLastQuotedString(string line)
    {
        var lastEnd = line.LastIndexOf('"');
        if (lastEnd <= 0) return null;
        var openIdx = line.LastIndexOf('"', lastEnd - 1);
        if (openIdx < 0) return null;
        return line.Substring(openIdx + 1, lastEnd - openIdx - 1);
    }

    private static List<string> ExtractAllQuotedStrings(string line)
    {
        var result = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            var open = line.IndexOf('"', i);
            if (open < 0) break;
            var close = line.IndexOf('"', open + 1);
            if (close < 0) break;
            result.Add(line.Substring(open + 1, close - open - 1));
            i = close + 1;
        }
        return result;
    }

    private static (int x, int y)? ExtractFirstTwoInts(byte[] b, int from, int lineEnd)
    {
        var i = SkipHorizontalWhitespace(b, from);
        if (i >= lineEnd || b[i] < '0' || b[i] > '9') return null;
        var x = 0;
        while (i < lineEnd && b[i] >= '0' && b[i] <= '9') { x = x * 10 + (b[i] - '0'); i++; }
        i = SkipHorizontalWhitespace(b, i);
        if (i >= lineEnd || b[i] != ',') return null;
        i++;
        i = SkipHorizontalWhitespace(b, i);
        if (i >= lineEnd || b[i] < '0' || b[i] > '9') return null;
        var y = 0;
        while (i < lineEnd && b[i] >= '0' && b[i] <= '9') { y = y * 10 + (b[i] - '0'); i++; }
        return (x, y);
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        return s[..max] + "…";
    }

    // ─── Encoding fallback ──────────────────────────────────────────────────

    private static bool _codePagesRegistered;
    private static void EnsureCodePagesRegistered()
    {
        if (_codePagesRegistered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _codePagesRegistered = true;
    }

    private static string DecodeLine(byte[] b, int from, int to)
    {
        var len = to - from;
        if (len <= 0) return string.Empty;
        // Try strict UTF-8; fall back to CP866 then Win-1251.
        try
        {
            var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return utf8.GetString(b, from, len).TrimEnd('\r');
        }
        catch (DecoderFallbackException) { }
        try
        {
            return Encoding.GetEncoding(866, EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback).GetString(b, from, len).TrimEnd('\r');
        }
        catch
        {
            return Encoding.GetEncoding(1251).GetString(b, from, len).TrimEnd('\r');
        }
    }
}
