using System.Runtime.InteropServices;

namespace LdPrint.Services;

/// <summary>
/// Raw byte stream printing via the Windows Print Spooler (winspool.drv).
/// Uses datatype "RAW" so the spooler bypasses any GDI processing and the
/// installed printer driver receives the bytes verbatim — exactly what TSPL
/// printers need.
/// </summary>
public static class RawPrinter
{
    public static IReadOnlyList<string> ListInstalledPrinters()
    {
        // PrinterSettings.InstalledPrinters returns a StringCollection of
        // every printer registered in the current user's Windows session,
        // including network printers, "Microsoft Print to PDF", etc.
        var list = new List<string>();
        foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            list.Add(name);
        return list;
    }

    public static string DefaultPrinterName()
    {
        var settings = new System.Drawing.Printing.PrinterSettings();
        return settings.PrinterName;
    }

    /// <summary>
    /// Send a raw byte buffer to the named printer as a single spool job.
    /// </summary>
    /// <returns>The number of bytes written, per WritePrinter().</returns>
    public static int SendBytes(string printerName, string documentName, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new ArgumentException("Printer name is empty.", nameof(printerName));
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) return 0;

        var hPrinter = IntPtr.Zero;

        // PRINTER_DEFAULTS is passed as IntPtr.Zero — we don't need to override
        // datatype or devmode at OpenPrinter level; the datatype is set in the
        // DOC_INFO_1 below.
        if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            throw MakeWin32("OpenPrinter");

        try
        {
            var docInfo = new DOC_INFO_1
            {
                pDocName = string.IsNullOrEmpty(documentName) ? "TsplPrint" : documentName,
                pOutputFile = null,
                pDatatype = "RAW",
            };

            var jobId = StartDocPrinter(hPrinter, 1, ref docInfo);
            if (jobId == 0) throw MakeWin32("StartDocPrinter");

            try
            {
                if (!StartPagePrinter(hPrinter)) throw MakeWin32("StartPagePrinter");

                try
                {
                    var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try
                    {
                        if (!WritePrinter(hPrinter, pinned.AddrOfPinnedObject(),
                                          data.Length, out var written))
                            throw MakeWin32("WritePrinter");

                        return written;
                    }
                    finally
                    {
                        pinned.Free();
                    }
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }

    // ---- P/Invoke ----------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDatatype;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode,
        SetLastError = true, ExactSpelling = true)]
    private static extern bool OpenPrinter(
        [MarshalAs(UnmanagedType.LPWStr)] string pPrinterName,
        out IntPtr hPrinter,
        IntPtr pDefault);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", CharSet = CharSet.Unicode,
        SetLastError = true, ExactSpelling = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode,
        SetLastError = true, ExactSpelling = true)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", CharSet = CharSet.Unicode,
        SetLastError = true, ExactSpelling = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", CharSet = CharSet.Unicode,
        SetLastError = true, ExactSpelling = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", CharSet = CharSet.Unicode,
        SetLastError = true, ExactSpelling = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", CharSet = CharSet.Unicode,
        SetLastError = true, ExactSpelling = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int cbBuf, out int pcWritten);

    private static InvalidOperationException MakeWin32(string fn)
    {
        var err = Marshal.GetLastWin32Error();
        return new InvalidOperationException(
            $"{fn} failed with Win32 error 0x{err:X8} ({err}).");
    }
}
