# Roadmap

**[Русская версия](ROADMAP.md)**

## Vision

Lucky Dragon Print aims to be a print utility for **all thermal label printer languages**, not just TSPL. The architecture is designed from the start as a set of interchangeable handlers for different languages, unified under one UI/UX and a shared preview pipeline.

TSPL is the starting point — chosen because TSC and Honeywell PC dominate Russia's "Honest Sign" marking ecosystem. Other industrial standards will be added as the project matures.

## Supported languages

### Category A — industrial label printers (primary scope)

| Format | Manufacturers | Market share | Version |
|---|---|---|---|
| **TSPL / TSPL2** | TSC Auto ID, Honeywell PC, GoDEX, Postek, Argox | ~30% RU marking | v1.0 ✅ / v1.1 |
| **ZPL II** | Zebra ZT, ZD, GK, GX, ZQ series | ~40% global market | v1.2 |
| **EPL2** | older Zebra LP / TLP (Eltron legacy) | legacy, but widespread | v1.3 |
| **DPL** | Datamax-O'Neil, Honeywell PD / PM | enterprise | v1.3 |
| **CPCL** | Zebra QLn, ZQ mobile / battery | handheld terminals | v2.x |
| **IPL** | Intermec PB / PM, Honeywell PB | enterprise legacy | v2.x |
| **SPL** | STAR Micronics TSP series | nice-to-have | v2.x |
| **CLP** | Citizen CL / CLP series | nice-to-have | v2.x |

### Category B — receipt printers (separate module, under consideration)

Receipt printers (fiscal and kitchen printing) are a different class of work with different paper. Possibly a separate utility on top of the same base:

- **ESC/POS** — Epson TM, Bixolon SRP, Star TSP, Citizen CT (the most widespread)
- **SBPL** — Sato (gray zone between label and receipt)

## Versions

### v1.0 ✅ Released — May 2026

Base pipeline for TSPL:

- Print via Windows Spooler RAW
- Visual preview (15 BARCODE formats, DataMatrix, QR, BITMAP, text)
- Page selection with reactive filtering
- Light / Dark / Auto themes
- `.tspl` file association
- RU / EN UI
- Inno Setup installer

### v1.1 — Extending TSPL family + UX polish

- **TSPL2** — extended commands (multiple BITMAP modes, advanced fonts, GRF download, RFID)
- Drag-and-drop files onto the application window
- CLI mode: `LDPrint.exe --printer "TSC TX200" --pages "1-3" "file.tspl"`
- Recent files menu
- Additional UI localizations (`zh`, `tr`, `kk` — for CIS + China markets)
- Batch print (queue multiple files at once)

### v1.2 — Zebra entry (TSC's main competitor)

- **ZPL II** full support — parser + renderer + print pipeline
- Auto-format detection — language identified from first ~200 bytes of file
- Mixed-format print queue — TSPL and ZPL files in the same queue

### v1.3 — Datamax + Eltron

- **EPL2** (legacy Zebra LP / TLP)
- **DPL** (Datamax-O'Neil / Honeywell PD / PM)
- Cross-language converter where mathematically possible: TSPL → ZPL for TSC-to-Zebra migration

### v2.0 — Cross-platform + automation

- **Avalonia UI rewrite** — Linux and macOS support (WPF is Windows-only)
- **Watch folder mode** — file appears in folder → automatic print (for 1C / ERP integration)
- **HTTP webhook server** — REST API `POST /print` with JSON job description
- **ARM64 native build** — Windows ARM + Apple Silicon

### v2.x — Beyond

- **CPCL** (Zebra mobile)
- **IPL** (Intermec)
- **SPL** (STAR Micronics)
- **CLP** (Citizen)
- **ESC/POS** (separate module for receipt printers)
- **Plugin SDK** — third-party developers can add their own language without core modification

## Architectural evolution

### Today (v1.0)

A single pipeline for TSPL:

```
TsplParser → TsplDocument → TsplRenderer → BitmapSource (for UI)
                                        → byte[]        (for printing)
RawPrinter → byte stream → Windows Spooler → Printer
```

### v1.2 — multi-format refactor

Introduce the `IPrintLanguageHandler` abstraction:

```csharp
public interface IPrintLanguageHandler {
    string LanguageName { get; }                              // "TSPL", "ZPL", "EPL2"
    bool CanParse(ReadOnlySpan<byte> header);                  // sniff first ~200 bytes
    IPrintDocument Parse(byte[] content);
    BitmapSource Render(IPrintDocument doc, int pageIndex);
    byte[] BuildStream(IPrintDocument doc, int[] pageIndices);
}
```

Implementations: `TsplHandler`, `Tspl2Handler`, `ZplHandler`, `EplHandler`, `DplHandler`, ...

`PrintLanguageDetector` looks at the first ~200 bytes of the opened file and picks the appropriate handler automatically. The UI remains unified — preview, page selection, themes work identically across all languages.

### v2.0 — Avalonia + automation

Moving the UI from WPF to Avalonia gives Linux and macOS support without rewriting business logic (Services / Models / ViewModels run on any .NET runtime). Watch folder and HTTP server are separate modules, opt-in via CLI flags.

## Not in scope

- **Label editor** — that's a different product (Lucky Dragon Flow). This utility only prints existing files.
- **PDF conversion** — RAW TSPL has no visual correspondence to PDF (it's printer commands). TSPL → PDF conversion is only possible via render-to-bitmap → PDF, which already happens in preview and has no practical use as a standalone workflow.
- **Marking / GS1 code generation** — that's the job of Lucky Dragon Flow and its tools. Lucky Dragon Print only reproduces already-generated files.
- **Retail fiscal reporting** — out of scope (different class of software).

## How to help

Pull requests welcome for any task on this roadmap. Especially valuable:

- Real printer test files (anonymized) — for parser and renderer verification
- Compatibility reports for specific printer models
- UI localization into additional languages
- Handler implementations for other print languages
