using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using LdPrint.Models;

namespace LdPrint.Services;

/// <summary>
/// Renders a TSPL page to a Windows <see cref="BitmapSource"/> for preview.
/// Walks the page bytes the same way as <see cref="TsplParser"/> (so BITMAP
/// binary payloads are skipped, not treated as text), and translates each
/// drawing command into GDI+ calls plus ZXing barcodes.
///
/// Goal: be readable, not pixel-identical to a TSC printer. Antialiased
/// text and bitmap-scaling differ from thermal output by definition.
/// </summary>
public static class TsplRenderer
{
    /// <summary>
    /// Render the page and store it in <see cref="TsplPage.PreviewImage"/>.
    /// If already rendered, no-op. Errors fall through quietly — the page
    /// keeps its null PreviewImage and the UI falls back to text preview.
    /// </summary>
    public static void EnsureRendered(TsplDocument doc, TsplPage page)
    {
        if (page.PreviewImage is not null) return;
        try
        {
            page.PreviewImage = Render(doc, page);
        }
        catch
        {
            // best-effort: leave PreviewImage null on failure
        }
    }

    public static BitmapSource Render(TsplDocument doc, TsplPage page)
    {
        // Auto-detect DPI: scan the page to find the max coordinate referenced
        // by any drawing command, then pick the smallest standard DPI such
        // that the SIZE-mm fits all coordinates. This handles the common case
        // where a file is generated for a 300dpi printer but the header gives
        // no DPI hint and our default of 203 would clip half the content.
        var dpi = ResolveEffectiveDpi(doc, page);
        var (widthPx, heightPx) = ComputeCanvasSize(doc, dpi);

        using var bmp = new Bitmap(widthPx, heightPx, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.White);

            RenderCommands(g, doc, page, widthPx, heightPx);
        }

        // DIRECTION in TSPL describes how the label exits the printer; it
        // does NOT affect how a human reads the finished label. For preview
        // we always show the readable orientation, so no 180° flip even when
        // DIRECTION=1. The value is still exposed in metadata.
        return BitmapToBitmapSource(bmp);
    }

    /// <summary>
    /// Pick the smallest standard DPI from {203, 300, 600} such that the
    /// label's SIZE-mm contains every coordinate the page references. If
    /// the file has no SIZE or no overflowing drawings, returns the
    /// document's declared DPI (or 203 default).
    /// </summary>
    public static int ResolveEffectiveDpi(TsplDocument doc, TsplPage page)
    {
        if (doc.WidthMm <= 0 || doc.HeightMm <= 0) return doc.Dpi;

        var (maxX, maxY) = ScanMaxCoords(page.Content);
        if (maxX <= 0 && maxY <= 0) return doc.Dpi;

        // Required DPI: maxX * 25.4 / WidthMm  (dots / mm / inch unit).
        var reqX = maxX * 25.4 / doc.WidthMm;
        var reqY = maxY * 25.4 / doc.HeightMm;
        var required = Math.Max(reqX, reqY);

        // Add a small safety margin so an element on the very edge doesn't
        // tip us into a higher tier when 203 would already fit comfortably.
        const double margin = 1.02;
        required *= margin;

        if (required <= doc.Dpi) return doc.Dpi;
        if (required <= 203) return 203;
        if (required <= 300) return 300;
        return 600;
    }

    private static (int widthPx, int heightPx) ComputeCanvasSize(TsplDocument doc, int dpi)
    {
        var dotsPerMm = dpi / 25.4;
        var w = doc.WidthMm > 0 ? (int)Math.Round(doc.WidthMm * dotsPerMm) : 480;
        var h = doc.HeightMm > 0 ? (int)Math.Round(doc.HeightMm * dotsPerMm) : 320;
        w = Math.Clamp(w, 80, 8000);
        h = Math.Clamp(h, 60, 8000);
        return (w, h);
    }

    /// <summary>
    /// Walk the page once and find the right/bottom-most pixel coordinate any
    /// drawing command would touch. Uses BITMAP/DOWNLOAD payload-skip logic
    /// just like the parser. Bounding boxes are heuristic — we don't need
    /// exact text metrics to pick a DPI tier.
    /// </summary>
    private static (int maxX, int maxY) ScanMaxCoords(byte[] bytes)
    {
        var maxX = 0; var maxY = 0;
        var pos = 0;
        while (pos < bytes.Length)
        {
            pos = SkipHorizontalWhitespace(bytes, pos);
            if (pos >= bytes.Length) break;
            if (bytes[pos] == 0x0A || bytes[pos] == 0x0D) { pos++; continue; }
            var lineEnd = FindEol(bytes, pos);

            if (MatchKeyword(bytes, pos, "BITMAP"))
            {
                if (TryParseBitmapHeader(bytes, pos + 6, lineEnd,
                    out var x, out var y, out var wBytes, out var hRows, out var binStart))
                {
                    UpdateMax(ref maxX, ref maxY, x + wBytes * 8, y + hRows);
                    pos = Math.Min(binStart + wBytes * hRows, bytes.Length);
                    if (pos < bytes.Length && bytes[pos] == 0x0D) pos++;
                    if (pos < bytes.Length && bytes[pos] == 0x0A) pos++;
                    continue;
                }
            }
            if (MatchKeyword(bytes, pos, "DOWNLOAD"))
            {
                if (TryParseDownloadHeader(bytes, pos + 8, lineEnd, out var size, out var binStart))
                {
                    pos = Math.Min(binStart + size, bytes.Length);
                    if (pos < bytes.Length && bytes[pos] == 0x0D) pos++;
                    if (pos < bytes.Length && bytes[pos] == 0x0A) pos++;
                    continue;
                }
            }

            var line = System.Text.Encoding.ASCII.GetString(bytes, pos,
                Math.Max(0, lineEnd - pos)).TrimEnd('\r');
            EstimateCommandBounds(line, ref maxX, ref maxY);
            pos = lineEnd >= bytes.Length ? bytes.Length : lineEnd + 1;
        }
        return (maxX, maxY);
    }

    private static void UpdateMax(ref int maxX, ref int maxY, int x, int y)
    {
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }

    private static bool TryParseBitmapHeader(byte[] b, int from, int lineEnd,
        out int x, out int y, out int wBytes, out int hRows, out int binStart)
    {
        x = y = wBytes = hRows = 0; binStart = -1;
        var i = from;
        var vals = new int[5];
        var lastComma = -1;
        for (var p = 0; p < 5; p++)
        {
            if (!TryReadIntLocal(b, ref i, lineEnd, out vals[p])) return false;
            i = SkipHorizontalWhitespace(b, i);
            if (i >= lineEnd || b[i] != ',') return false;
            lastComma = i;
            i++;
            i = SkipHorizontalWhitespace(b, i);
        }
        x = vals[0]; y = vals[1]; wBytes = vals[2]; hRows = vals[3];
        binStart = lastComma + 1;
        return wBytes > 0 && hRows > 0;
    }

    private static bool TryParseDownloadHeader(byte[] b, int from, int lineEnd,
        out int size, out int binStart)
    {
        size = 0; binStart = -1;
        var i = SkipHorizontalWhitespace(b, from);
        if (i < lineEnd && b[i] == '"')
        {
            i++;
            while (i < lineEnd && b[i] != '"') i++;
            if (i >= lineEnd) return false;
            i++;
            i = SkipHorizontalWhitespace(b, i);
            if (i >= lineEnd || b[i] != ',') return false;
            i++;
        }
        i = SkipHorizontalWhitespace(b, i);
        if (!TryReadIntLocal(b, ref i, lineEnd, out size)) return false;
        i = SkipHorizontalWhitespace(b, i);
        if (i >= lineEnd || b[i] != ',') return false;
        binStart = i + 1;
        return size > 0;
    }

    private static bool TryReadIntLocal(byte[] b, ref int i, int lineEnd, out int value)
    {
        value = 0;
        i = SkipHorizontalWhitespace(b, i);
        var sign = 1;
        if (i < lineEnd && b[i] == '-') { sign = -1; i++; }
        if (i >= lineEnd || b[i] < '0' || b[i] > '9') return false;
        var n = 0;
        while (i < lineEnd && b[i] >= '0' && b[i] <= '9')
        {
            n = n * 10 + (b[i] - '0');
            i++;
        }
        value = sign * n;
        return true;
    }

    /// <summary>
    /// For non-binary commands: parse the first 2 (x,y) or 4 (xs,ys,xe,ye)
    /// integers and estimate a bounding-box right/bottom edge. Best-effort —
    /// we don't try to be exact, we just need to know if 203 DPI canvas is
    /// large enough.
    /// </summary>
    private static void EstimateCommandBounds(string line, ref int maxX, ref int maxY)
    {
        var upper = line.AsSpan().TrimStart();
        if (upper.IsEmpty) return;

        // TEXT/BARCODE/DMATRIX/QRCODE/BAR/BOX/ELLIPSE/REVERSE/ERASE all have
        // 'cmd x,y,...' format. We grab x,y plus an optional w,h or xe,ye.
        var ints = ParseLeadingInts(line, 4);
        if (ints.Length < 2) return;

        var x = ints[0]; var y = ints[1];
        int right = x, bottom = y;

        if (upper.StartsWith("BITMAP", StringComparison.OrdinalIgnoreCase))
        {
            // Handled via TryParseBitmapHeader path; only here as a fallback.
            if (ints.Length >= 4)
            { right = x + ints[2] * 8; bottom = y + ints[3]; }
        }
        else if (upper.StartsWith("BAR ", StringComparison.OrdinalIgnoreCase)
              || upper.StartsWith("BAR\t", StringComparison.OrdinalIgnoreCase))
        {
            if (ints.Length >= 4) { right = x + ints[2]; bottom = y + ints[3]; }
        }
        else if (upper.StartsWith("BOX", StringComparison.OrdinalIgnoreCase))
        {
            if (ints.Length >= 4) { right = Math.Max(x, ints[2]); bottom = Math.Max(y, ints[3]); }
        }
        else if (upper.StartsWith("DMATRIX", StringComparison.OrdinalIgnoreCase)
              || upper.StartsWith("ELLIPSE", StringComparison.OrdinalIgnoreCase))
        {
            if (ints.Length >= 4) { right = x + ints[2]; bottom = y + ints[3]; }
        }
        else if (upper.StartsWith("BARCODE", StringComparison.OrdinalIgnoreCase))
        {
            // Format: BARCODE x,y,"type",h,readable,rot,narrow,wide,"content"
            // Use format-aware module count consistent with RenderBarcode.
            var quotes = FindAllQuotedStrings(line);
            var h = ints.Length >= 4 ? ints[3] : 100;
            var narrow = ints.Length >= 4 ? Math.Max(1, ints[3]) : 2;
            // Note: ints[3] in BARCODE order is height; narrow is later. We
            // don't bother extracting precise narrow here for DPI estimation —
            // narrow=3 worst case is consistent with most TSC labels.
            var bcType = quotes.Count >= 1 ? quotes[0] : "128";
            var contentLen = quotes.Count >= 2 ? quotes[^1].Length : 12;
            var modules = EstimateBarcodeModuleCount(bcType, contentLen);
            right = x + modules * 3; // assume narrow=3 worst case
            bottom = y + h;
        }
        else if (upper.StartsWith("QRCODE", StringComparison.OrdinalIgnoreCase))
        {
            // Format: QRCODE x,y,ECC,cellWidth,...,"content"
            var cell = ints.Length >= 3 ? ints[2] : 5; // cell sits at the 4th token but ECC eats slot 3
            var size = Math.Max(60, cell * 30);
            right = x + size;
            bottom = y + size;
        }
        else if (upper.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase))
        {
            // Rough: 14px/char × xMul, height ~28 × yMul.
            var quotes = FindAllQuotedStrings(line);
            var content = quotes.Count > 0 ? quotes[^1] : "";
            var xMul = ints.Length >= 4 ? ints[3] : 1;
            var yMul = ints.Length >= 4 ? ints[3] : 1;  // approximation
            right = x + Math.Max(40, content.Length * 14 * Math.Max(1, xMul));
            bottom = y + 30 * Math.Max(1, yMul);
        }
        // Other commands — just register x,y.
        UpdateMax(ref maxX, ref maxY, right, bottom);
    }

    private static int[] ParseLeadingInts(string line, int max)
    {
        var result = new List<int>(max);
        var i = 0;
        // Skip the command keyword (alphabetic).
        while (i < line.Length && (char.IsLetter(line[i]) || line[i] == '_')) i++;
        while (i < line.Length && result.Count < max)
        {
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t' || line[i] == ',')) i++;
            if (i >= line.Length) break;
            // Allow negatives; stop at non-numeric.
            var start = i;
            if (line[i] == '-') i++;
            if (i >= line.Length || !char.IsDigit(line[i])) break;
            while (i < line.Length && char.IsDigit(line[i])) i++;
            if (int.TryParse(line.AsSpan(start, i - start), out var v))
                result.Add(v);
            else break;
            // Skip until next separator or quote (we don't parse quoted strings here).
            if (i < line.Length && line[i] == '"') break;
        }
        return result.ToArray();
    }

    private static List<string> FindAllQuotedStrings(string line)
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

    private static int GetIntAtPositionByIndex(string line, int startIdx, int defaultValue)
    {
        // Crude helper — used as fallback if regex would be overkill.
        return defaultValue;
    }

    /// <summary>
    /// Returns the natural module count (1×-width units) for a 1-D barcode
    /// of the given type and content length. Used to derive pixel width as
    /// modules × narrow. EAN/UPC are fixed-width; CODE-128/39/ITF scale
    /// linearly with content length plus start/stop overhead.
    /// </summary>
    private static int EstimateBarcodeModuleCount(string type, int contentLen)
    {
        // Module counts cover only the encoded symbol (no quiet zones). TSC
        // printers do not auto-add quiet-zone modules; the user is expected
        // to leave whitespace by positioning. Including quiet zones here
        // would inflate the width estimate and trip our DPI auto-detector
        // into picking a higher tier than the file targets.
        return type.ToUpperInvariant() switch
        {
            "EAN13" or "UPCA" => 95,
            "EAN8" => 67,
            "UPCE" => 51,
            "39" or "CODE39" => Math.Max(30, (contentLen + 2) * 13),
            "93" or "CODE93" => Math.Max(30, contentLen * 9 + 9),
            "128" or "128M" or "CODE128" => Math.Max(40, contentLen * 11 + 24),
            "I2OF5" or "ITF" or "ITF14" => Math.Max(30, contentLen * 9 + 5),
            "CODA" or "CODABAR" or "NW7" => Math.Max(30, (contentLen + 2) * 13),
            _ => Math.Max(60, contentLen * 11),
        };
    }

    // ────────────────── command walker ──────────────────────────────────────

    private static void RenderCommands(Graphics g, TsplDocument doc, TsplPage page,
        int canvasW, int canvasH)
    {
        var bytes = page.Content;
        var pos = 0;

        while (pos < bytes.Length)
        {
            pos = SkipHorizontalWhitespace(bytes, pos);
            if (pos >= bytes.Length) break;

            if (bytes[pos] == 0x0A || bytes[pos] == 0x0D) { pos++; continue; }

            var lineEnd = FindEol(bytes, pos);

            // BITMAP — has binary payload; render the raster then skip bytes.
            if (MatchKeyword(bytes, pos, "BITMAP"))
            {
                if (TryRenderBitmap(g, bytes, pos, lineEnd, out var payloadEnd))
                {
                    pos = payloadEnd;
                    if (pos < bytes.Length && bytes[pos] == 0x0D) pos++;
                    if (pos < bytes.Length && bytes[pos] == 0x0A) pos++;
                    continue;
                }
            }
            if (MatchKeyword(bytes, pos, "DOWNLOAD"))
            {
                // We don't render downloaded font/image payloads — just skip.
                if (SkipDownloadBlock(bytes, pos + 8, lineEnd, out var payloadEnd))
                {
                    pos = payloadEnd;
                    if (pos < bytes.Length && bytes[pos] == 0x0D) pos++;
                    if (pos < bytes.Length && bytes[pos] == 0x0A) pos++;
                    continue;
                }
            }

            // Decode line as text for everything else.
            var line = DecodeLine(bytes, pos, lineEnd);
            DispatchTextCommand(g, line);
            pos = lineEnd >= bytes.Length ? bytes.Length : lineEnd + 1;
        }
    }

    private static void DispatchTextCommand(Graphics g, string line)
    {
        if (line.Length == 0) return;
        var upper = line.AsSpan().TrimStart();
        if (upper.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase)) { RenderText(g, line); return; }
        if (upper.StartsWith("BARCODE", StringComparison.OrdinalIgnoreCase)) { RenderBarcode(g, line); return; }
        if (upper.StartsWith("DMATRIX", StringComparison.OrdinalIgnoreCase)) { RenderDmatrix(g, line); return; }
        if (upper.StartsWith("QRCODE", StringComparison.OrdinalIgnoreCase)) { RenderQrCode(g, line); return; }
        if (upper.StartsWith("BAR ", StringComparison.OrdinalIgnoreCase)
            || upper.StartsWith("BAR\t", StringComparison.OrdinalIgnoreCase))
        { RenderBar(g, line); return; }
        if (upper.StartsWith("BOX", StringComparison.OrdinalIgnoreCase)) { RenderBox(g, line); return; }
        // Other commands (CLS, SIZE, PRINT, SET, DENSITY, REFERENCE, ...) — no visual.
    }

    // ────────────────── BITMAP raster ───────────────────────────────────────

    private static bool TryRenderBitmap(Graphics g, byte[] bytes, int lineStart, int lineEnd,
        out int payloadEnd)
    {
        payloadEnd = lineEnd;
        // Header: "BITMAP x,y,wBytes,h,mode,"
        var headerStart = lineStart + 6;
        var i = SkipHorizontalWhitespace(bytes, headerStart);
        var vals = new int[5];
        for (var p = 0; p < 5; p++)
        {
            if (!TryReadInt(bytes, ref i, lineEnd, out vals[p])) return false;
            i = SkipHorizontalWhitespace(bytes, i);
            if (p < 4)
            {
                if (i >= lineEnd || bytes[i] != ',') return false;
                i++;
                i = SkipHorizontalWhitespace(bytes, i);
            }
            else
            {
                if (i >= lineEnd || bytes[i] != ',') return false;
                i++;
            }
        }
        var x = vals[0]; var y = vals[1];
        var wBytes = vals[2]; var hRows = vals[3];
        // mode = vals[4] — we treat all modes as OVERWRITE for preview.
        var payloadStart = i;
        var payloadSize = checked(wBytes * hRows);
        payloadEnd = Math.Min(payloadStart + payloadSize, bytes.Length);

        if (wBytes <= 0 || hRows <= 0) return true; // skip silently

        // Decode 1bpp: each byte = 8 horizontal pixels, MSB first.
        // For TSPL, bit value 1 = black dot.
        var pxW = wBytes * 8;
        var pxH = hRows;
        using var raster = new Bitmap(pxW, pxH, PixelFormat.Format24bppRgb);
        var data = raster.LockBits(new Rectangle(0, 0, pxW, pxH), ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var ptr = data.Scan0;
            var rowBuf = new byte[stride];
            for (var row = 0; row < pxH; row++)
            {
                Array.Clear(rowBuf, 0, rowBuf.Length);
                for (var col = 0; col < pxW; col++)
                {
                    var byteIdx = payloadStart + row * wBytes + (col >> 3);
                    if (byteIdx >= bytes.Length) break;
                    var bit = (bytes[byteIdx] >> (7 - (col & 7))) & 1;
                    // Most TSPL generators use bit=0 → black (printed),
                    // bit=1 → white. Try the convention that matches typical
                    // TSC docs first; if it looks inverted on user files we
                    // can flip via a setting later.
                    if (bit == 0)
                    {
                        var pix = col * 3;
                        rowBuf[pix + 0] = 0;
                        rowBuf[pix + 1] = 0;
                        rowBuf[pix + 2] = 0;
                    }
                    else
                    {
                        var pix = col * 3;
                        rowBuf[pix + 0] = 0xFF;
                        rowBuf[pix + 1] = 0xFF;
                        rowBuf[pix + 2] = 0xFF;
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(rowBuf, 0, ptr, stride);
                ptr = IntPtr.Add(ptr, stride);
            }
        }
        finally
        {
            raster.UnlockBits(data);
        }

        g.DrawImageUnscaled(raster, x, y);
        return true;
    }

    private static bool SkipDownloadBlock(byte[] bytes, int from, int lineEnd, out int payloadEnd)
    {
        payloadEnd = lineEnd;
        var i = SkipHorizontalWhitespace(bytes, from);
        if (i < lineEnd && bytes[i] == '"')
        {
            i++;
            while (i < lineEnd && bytes[i] != '"') i++;
            if (i >= lineEnd) return false;
            i++;
            i = SkipHorizontalWhitespace(bytes, i);
            if (i >= lineEnd || bytes[i] != ',') return false;
            i++;
        }
        i = SkipHorizontalWhitespace(bytes, i);
        if (!TryReadInt(bytes, ref i, lineEnd, out var size)) return false;
        i = SkipHorizontalWhitespace(bytes, i);
        if (i >= lineEnd || bytes[i] != ',') return false;
        payloadEnd = Math.Min(i + 1 + size, bytes.Length);
        return true;
    }

    // ────────────────── TEXT ────────────────────────────────────────────────

    // TEXT x,y,"font",rotation,xMul,yMul,"content"
    private static readonly Regex TextRegex = new(
        @"^\s*TEXT\s+(-?\d+)\s*,\s*(-?\d+)\s*,\s*""([^""]*)""\s*,\s*(-?\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*""(.*)""\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static void RenderText(Graphics g, string line)
    {
        var m = TextRegex.Match(line);
        if (!m.Success) return;
        var x = int.Parse(m.Groups[1].Value);
        var y = int.Parse(m.Groups[2].Value);
        var fontName = m.Groups[3].Value;
        var rotation = int.Parse(m.Groups[4].Value);
        var xMul = Math.Max(1, int.Parse(m.Groups[5].Value));
        var yMul = Math.Max(1, int.Parse(m.Groups[6].Value));
        var content = m.Groups[7].Value;

        var (familyName, baseSizePx, bold) = MapTsplFont(fontName);
        var sizePx = baseSizePx * Math.Max(xMul, yMul);

        using var family = SafeFontFamily(familyName);
        using var font = new Font(family, sizePx, bold ? FontStyle.Bold : FontStyle.Regular,
            GraphicsUnit.Pixel);

        var saved = g.Save();
        try
        {
            g.TranslateTransform(x, y);
            if (rotation != 0) g.RotateTransform(rotation);
            g.DrawString(content, font, Brushes.Black, 0, 0);
        }
        finally
        {
            g.Restore(saved);
        }
    }

    /// <summary>
    /// Map TSPL font selector to a Windows font family and base pixel size.
    /// TSPL fonts "0".."8" are bitmap fonts with specific dimensions. We use
    /// monospace for the small ones and proportional for big numbers, with
    /// pixel sizes that approximate the dot-grid sizes.
    /// </summary>
    private static (string family, float sizePx, bool bold) MapTsplFont(string name)
    {
        var n = name.ToUpperInvariant();
        return n switch
        {
            "0" => ("Consolas", 10, false),
            "1" => ("Consolas", 14, false),
            "2" => ("Consolas", 18, false),
            "3" => ("Consolas", 22, true),
            "4" => ("Consolas", 30, true),
            "5" => ("Consolas", 12, false),
            "6" => ("Consolas", 14, false),
            "7" => ("Consolas", 20, false),
            "8" => ("Consolas", 16, false),
            "ROMAN.TTF" => ("Times New Roman", 16, false),
            "BLOCK.TTF" => ("Arial Black", 16, true),
            _ => ("Segoe UI", 14, false),
        };
    }

    private static FontFamily SafeFontFamily(string name)
    {
        try { return new FontFamily(name); }
        catch { return new FontFamily("Segoe UI"); }
    }

    // ────────────────── BARCODE / DMATRIX / QRCODE ──────────────────────────

    // BARCODE x,y,"type",height,human_readable,rotation,narrow,wide,"content"
    private static readonly Regex BarcodeRegex = new(
        @"^\s*BARCODE\s+(-?\d+)\s*,\s*(-?\d+)\s*,\s*""([^""]+)""\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*""(.*)""\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static void RenderBarcode(Graphics g, string line)
    {
        var m = BarcodeRegex.Match(line);
        if (!m.Success) return;
        var x = int.Parse(m.Groups[1].Value);
        var y = int.Parse(m.Groups[2].Value);
        var type = m.Groups[3].Value;
        var heightDots = int.Parse(m.Groups[4].Value);
        var hri = int.Parse(m.Groups[5].Value); // 0=no, 1=yes, 2=yes-below
        var rotation = int.Parse(m.Groups[6].Value);
        var narrow = Math.Max(1, int.Parse(m.Groups[7].Value));
        var content = m.Groups[9].Value;

        // Estimate width in dots using format-specific module counts so the
        // barcode footprint matches what a real TSC printer would emit at
        // the given narrow-module width. A generic `len * narrow * 11`
        // heuristic over-estimates EAN-13 by ~50% and clips the right edge
        // of the canvas at 300 DPI.
        var moduleCount = EstimateBarcodeModuleCount(type, content.Length);
        var widthDots = Math.Max(80, moduleCount * narrow);

        using var bmp = BarcodeRenderer.Render1D(type, content, widthDots, heightDots, hri != 0);
        if (bmp is null) return;

        var saved = g.Save();
        try
        {
            g.TranslateTransform(x, y);
            if (rotation != 0) g.RotateTransform(rotation);
            g.DrawImageUnscaled(bmp, 0, 0);
        }
        finally { g.Restore(saved); }
    }

    // DMATRIX x,y,width,height,[option_args],"content"
    // Common shape: DMATRIX x,y,wDots,hDots,"content"
    // Option args (c, x, r, a, h) sometimes appear before the content quoted string.
    private static readonly Regex DmatrixRegex = new(
        @"^\s*DMATRIX\s+(-?\d+)\s*,\s*(-?\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,[^""]*)?,\s*""(.*)""\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static void RenderDmatrix(Graphics g, string line)
    {
        var m = DmatrixRegex.Match(line);
        if (!m.Success) return;
        var x = int.Parse(m.Groups[1].Value);
        var y = int.Parse(m.Groups[2].Value);
        var w = int.Parse(m.Groups[3].Value);
        var h = int.Parse(m.Groups[4].Value);
        var content = m.Groups[5].Value;

        // TSPL escapes GS separators as backslash-something in some templates;
        // ZXing accepts a raw string for DataMatrix encoding so we don't try
        // to be clever about FNC1.
        using var bmp = BarcodeRenderer.RenderDataMatrix(content, w, h);
        if (bmp is null) return;
        g.DrawImageUnscaled(bmp, x, y);
    }

    // QRCODE x,y,ECC level,cell width,mode,rotation,[model],"content"
    private static readonly Regex QrRegex = new(
        @"^\s*QRCODE\s+(-?\d+)\s*,\s*(-?\d+)\s*,\s*([HMQL])\s*,\s*(\d+)\s*,\s*([AM])\s*,\s*(\d+)\s*(?:,[^,""]*)*,\s*""(.*)""\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static void RenderQrCode(Graphics g, string line)
    {
        var m = QrRegex.Match(line);
        if (!m.Success) return;
        var x = int.Parse(m.Groups[1].Value);
        var y = int.Parse(m.Groups[2].Value);
        var cellWidth = int.Parse(m.Groups[4].Value);
        var content = m.Groups[7].Value;

        // QR module count varies by content; assume ~25 modules for preview
        // pre-scaling and let ZXing layout.
        var size = Math.Max(40, cellWidth * 25);
        using var bmp = BarcodeRenderer.RenderQrCode(content, size, size);
        if (bmp is null) return;
        g.DrawImageUnscaled(bmp, x, y);
    }

    // ────────────────── BAR / BOX ───────────────────────────────────────────

    private static readonly Regex BarRegex = new(
        @"^\s*BAR\s+(-?\d+)\s*,\s*(-?\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void RenderBar(Graphics g, string line)
    {
        var m = BarRegex.Match(line);
        if (!m.Success) return;
        var x = int.Parse(m.Groups[1].Value);
        var y = int.Parse(m.Groups[2].Value);
        var w = int.Parse(m.Groups[3].Value);
        var h = int.Parse(m.Groups[4].Value);
        g.FillRectangle(Brushes.Black, x, y, w, h);
    }

    private static readonly Regex BoxRegex = new(
        @"^\s*BOX\s+(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(\d+)\s*(?:,\s*(\d+))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void RenderBox(Graphics g, string line)
    {
        var m = BoxRegex.Match(line);
        if (!m.Success) return;
        var xs = int.Parse(m.Groups[1].Value);
        var ys = int.Parse(m.Groups[2].Value);
        var xe = int.Parse(m.Groups[3].Value);
        var ye = int.Parse(m.Groups[4].Value);
        var thickness = Math.Max(1, int.Parse(m.Groups[5].Value));
        using var pen = new Pen(Color.Black, thickness);
        var x = Math.Min(xs, xe);
        var y = Math.Min(ys, ye);
        var w = Math.Abs(xe - xs);
        var h = Math.Abs(ye - ys);
        g.DrawRectangle(pen, x, y, w, h);
    }

    // ────────────────── helpers ─────────────────────────────────────────────

    private static int SkipHorizontalWhitespace(byte[] b, int from)
    {
        var i = from;
        while (i < b.Length && (b[i] == 0x20 || b[i] == 0x09)) i++;
        return i;
    }

    private static int FindEol(byte[] b, int from)
    {
        var i = from;
        while (i < b.Length && b[i] != 0x0A) i++;
        return i;
    }

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

    private static bool TryReadInt(byte[] b, ref int i, int lineEnd, out int value)
    {
        value = 0;
        var sign = 1;
        i = SkipHorizontalWhitespace(b, i);
        if (i < lineEnd && b[i] == '-') { sign = -1; i++; }
        if (i >= lineEnd || b[i] < '0' || b[i] > '9') return false;
        var n = 0;
        while (i < lineEnd && b[i] >= '0' && b[i] <= '9')
        {
            n = n * 10 + (b[i] - '0');
            i++;
        }
        value = sign * n;
        return true;
    }

    private static string DecodeLine(byte[] b, int from, int to)
    {
        var len = to - from;
        if (len <= 0) return string.Empty;
        try
        {
            var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return utf8.GetString(b, from, len).TrimEnd('\r');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(866, EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback).GetString(b, from, len).TrimEnd('\r');
        }
    }

    // ────────────────── WPF interop ─────────────────────────────────────────

    private static BitmapSource BitmapToBitmapSource(Bitmap bmp)
    {
        // Save the bitmap to a memory stream then decode as PNG into a WPF
        // BitmapSource. Cheaper than HBITMAP marshaling and avoids GDI handle
        // leaks. Result is freezable so it crosses thread boundaries safely.
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var decoder = new PngBitmapDecoder(ms,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
