using System.Drawing;
using System.Drawing.Drawing2D;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;
using ZXing.Datamatrix.Encoder;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace LdPrint.Services;

/// <summary>
/// Wraps ZXing.Net writers and paints the resulting <see cref="BitMatrix"/>
/// onto a <see cref="Bitmap"/> ourselves. We don't pull in the system-drawing
/// rendering binding from ZXing because BitMatrix walking is ~10 lines and
/// avoids version conflicts on net8.0-windows.
///
/// All produced bitmaps are 1bpp logical (black-on-white) but allocated as
/// 24bpp so they can be composited into the page bitmap by the TSPL renderer
/// without colour-key tricks.
/// </summary>
public static class BarcodeRenderer
{
    /// <summary>
    /// Render an EAN-13 / EAN-8 / CODE128 / CODE39 / ITF style 1-D barcode.
    /// Width/height are pixel sizes of the final image; ZXing chooses its
    /// own module width to fit and we render at that size.
    /// </summary>
    public static Bitmap? Render1D(string type, string content, int width, int height,
        bool drawHri)
    {
        if (width <= 0 || height <= 0) return null;

        var format = MapBarcodeType(type);
        if (format is null) return null;

        // For check-digit formats (EAN-13, EAN-8, UPC-A) some generators emit
        // the digits without recomputing the check digit. ZXing rejects that
        // as invalid. We normalize by dropping the supplied check digit and
        // letting ZXing recompute, so previews of "wrong-checksum" labels
        // still show something the user recognizes. The printer doesn't care.
        var normalizedContent = NormalizeForFormat(format.Value, content);
        var hri = drawHri ? content : null; // keep ORIGINAL string for HRI label

        try
        {
            return TryEncode(format.Value, normalizedContent, width, height, hri);
        }
        catch
        {
            // Fallback: if the specialized format fails (length wrong, etc.)
            // emit a CODE-128 barcode with the raw content. Better some
            // visual representation than empty space.
            try
            {
                return TryEncode(BarcodeFormat.CODE_128, content, width, height, hri);
            }
            catch
            {
                return null;
            }
        }
    }

    private static Bitmap TryEncode(BarcodeFormat format, string content,
        int width, int height, string? hri)
    {
        var writer = new MultiFormatWriter();
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 0,
        };
        var matrix = writer.encode(content, format, width, height, hints);
        return PaintMatrix(matrix, width, height, hri);
    }

    private static string NormalizeForFormat(BarcodeFormat format, string content)
    {
        switch (format)
        {
            case BarcodeFormat.EAN_13:
                if (content.Length == 13 && content.All(char.IsDigit))
                    return content[..12]; // drop last digit; ZXing recomputes
                return content;
            case BarcodeFormat.EAN_8:
                if (content.Length == 8 && content.All(char.IsDigit))
                    return content[..7];
                return content;
            case BarcodeFormat.UPC_A:
                if (content.Length == 12 && content.All(char.IsDigit))
                    return content[..11];
                return content;
            default:
                return content;
        }
    }

    public static Bitmap? RenderDataMatrix(string content, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        try
        {
            var writer = new MultiFormatWriter();
            var hints = new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.MARGIN] = 0,
                [EncodeHintType.DATA_MATRIX_SHAPE] = SymbolShapeHint.FORCE_SQUARE,
            };
            var matrix = writer.encode(content, BarcodeFormat.DATA_MATRIX, width, height, hints);
            return PaintMatrix(matrix, width, height, null);
        }
        catch
        {
            return null;
        }
    }

    public static Bitmap? RenderQrCode(string content, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        try
        {
            var writer = new MultiFormatWriter();
            var hints = new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.MARGIN] = 0,
                [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
            };
            var matrix = writer.encode(content, BarcodeFormat.QR_CODE, width, height, hints);
            return PaintMatrix(matrix, width, height, null);
        }
        catch
        {
            return null;
        }
    }

    private static BarcodeFormat? MapBarcodeType(string tsplType)
    {
        // TSPL accepts type names like "128", "128M", "EAN13", "EAN8",
        // "39", "I2OF5", "UPCA", "CODA", "POST" etc. We map the most common.
        return tsplType.ToUpperInvariant() switch
        {
            "128" or "128M" or "CODE128" => BarcodeFormat.CODE_128,
            "EAN13" => BarcodeFormat.EAN_13,
            "EAN8" => BarcodeFormat.EAN_8,
            "39" or "CODE39" => BarcodeFormat.CODE_39,
            "93" or "CODE93" => BarcodeFormat.CODE_93,
            "I2OF5" or "ITF" or "ITF14" => BarcodeFormat.ITF,
            "UPCA" => BarcodeFormat.UPC_A,
            "UPCE" => BarcodeFormat.UPC_E,
            "CODA" or "CODABAR" or "NW7" => BarcodeFormat.CODABAR,
            _ => null,
        };
    }

    private static Bitmap PaintMatrix(BitMatrix matrix, int requestedW, int requestedH,
        string? humanReadableBelow)
    {
        var moduleW = matrix.Width;
        var moduleH = matrix.Height;

        // For barcodes with HRI, reserve ~14% of height for the text strip.
        var hriHeight = humanReadableBelow is null ? 0 : Math.Max(12, requestedH / 7);
        var barcodeH = requestedH - hriHeight;

        var bmp = new Bitmap(requestedW, requestedH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.Clear(Color.White);

        // ZXing already scales the matrix to requestedW × requestedH (or
        // requested-w × barcode-h after subtracting HRI in the caller). We
        // walk the matrix and paint black pixels.
        var scaleX = requestedW / (double)moduleW;
        var scaleY = barcodeH / (double)moduleH;

        if (scaleX < 1 || scaleY < 1)
        {
            // Pixel-by-pixel for high-resolution matrices.
            for (var y = 0; y < moduleH; y++)
                for (var x = 0; x < moduleW; x++)
                    if (matrix[x, y])
                    {
                        var px = (int)(x * scaleX);
                        var py = (int)(y * scaleY);
                        bmp.SetPixel(px, py, Color.Black);
                    }
        }
        else
        {
            // Module-rect fill for crispness when scale ≥ 1.
            using var brush = new SolidBrush(Color.Black);
            for (var y = 0; y < moduleH; y++)
                for (var x = 0; x < moduleW; x++)
                    if (matrix[x, y])
                    {
                        var rx = (int)Math.Round(x * scaleX);
                        var ry = (int)Math.Round(y * scaleY);
                        var rw = Math.Max(1, (int)Math.Round((x + 1) * scaleX) - rx);
                        var rh = Math.Max(1, (int)Math.Round((y + 1) * scaleY) - ry);
                        g.FillRectangle(brush, rx, ry, rw, rh);
                    }
        }

        if (humanReadableBelow is not null && hriHeight > 0)
        {
            using var font = new Font(FontFamily.GenericMonospace,
                Math.Max(6, hriHeight * 0.55f), FontStyle.Regular, GraphicsUnit.Pixel);
            var size = g.MeasureString(humanReadableBelow, font);
            var x = (requestedW - size.Width) / 2;
            g.DrawString(humanReadableBelow, font, Brushes.Black, x, barcodeH);
        }

        return bmp;
    }
}
