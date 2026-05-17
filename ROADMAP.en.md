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

The main goal of v2.0 is to port the utility to Linux and macOS (including Apple Silicon ARM64). Technically Windows 11 ARM64 already runs the current x64 build via emulation, but native ARM64 is preferred for performance.

#### What's Windows-bound in v1.0

| Component | v1.0 implementation | Non-Windows problem |
|---|---|---|
| UI framework | WPF | Windows-only, no port |
| Bitmap rendering | System.Drawing.Common (GDI+) | .NET 6+ deprecated on non-Windows; works on Linux via libgdiplus with font and FPS issues |
| Printing | P/Invoke `winspool.drv` (StartDocPrinter RAW) | Windows-only API |
| Title bar dark | DWM `DwmSetWindowAttribute` | Windows-only |
| File association | HKCU registry + SHChangeNotify | Windows-only |
| Printer settings dialog | `rundll32 printui.dll /e` | Windows-only |
| Theme auto-detect | HKCU\\...\\AppsUseLightTheme | Windows-only |
| Installer | Inno Setup 6 | Windows-only |

#### What's already cross-platform (no changes needed)

- **.NET 8 runtime** — Win/Linux/macOS × x64/arm64
- **CommunityToolkit.Mvvm** — pure .NET source generators
- **ZXing.Net** 0.16.10 — pure .NET, no native bindings
- **`Environment.SpecialFolder.ApplicationData`** — resolves correctly per OS: `%APPDATA%` (Win), `~/.config` (Linux XDG), `~/Library/Application Support` (macOS)
- **JSON resource i18n** — via `Assembly.GetManifestResourceStream`
- **TsplParser / PageRangeParser / BarcodeRenderer internals** — pure C#

This means ~50% of the codebase (business logic) ports without changes. Only the UI + system integration layer needs work.

#### Layer 1 — UI framework: WPF → Avalonia 11

**Why Avalonia, not MAUI:**
- Avalonia 11 = single codebase for Win + Mac + Linux + iOS + Android + WebAssembly
- XAML + MVVM — minimal migration from WPF (close mental model)
- ARM64 native on all platforms
- MAUI Linux is community-only (Uno / GtkSharp), not Microsoft-supported

**Changes:** `MainWindow.xaml`, `Themes/{Light,Dark,Controls}.xaml`, `App.xaml`, custom ControlTemplates.

**Unchanged:** `ViewModels/*` (`CommunityToolkit.Mvvm` compatible 1:1), `Models/*`, pure-C# services.

**Effort:** 1.5–2 weeks.

#### Layer 2 — Rendering: System.Drawing → SkiaSharp

`System.Drawing.Common` is marked `[SupportedOSPlatform("windows")]` since .NET 6. On Linux it goes through libgdiplus — slow with TTF issues.

**Replacement:** SkiaSharp 2.x — Google's Skia engine (same one in Chrome, Flutter, Android). Native binaries for all platforms × x64/arm64. HarfBuzz for TTF/OTF on every OS.

**Changes:**
- `TsplRenderer.cs`: `Graphics` → `SKCanvas`, `Bitmap` → `SKBitmap`/`SKImage`
- `BarcodeRenderer.cs`: switch to `ZXing.Net.Bindings.SkiaSharp`
- `SKBitmap` → Avalonia `IBitmap` conversion for display

**Effort:** 3–5 days.

#### Layer 3 — Printing: IPrinterService abstraction

```csharp
public interface IPrinterService {
    IEnumerable<string> GetInstalledPrinters();
    Task PrintRawAsync(string printerName, byte[] data, string docName);
    Task OpenPrinterPropertiesAsync(string printerName);
}
```

**Implementations:**
- `WindowsPrinterService` — current `RawPrinter` (winspool.drv P/Invoke)
- `CupsPrinterService` (Linux + macOS — same CUPS on both):
  - Enumerate: `lpstat -a` or HTTP `localhost:631/printers`
  - Print RAW: `lp -d <printer> -o raw -t "<docName>" -` ← stdin = bytes
  - Properties: `xdg-open localhost:631/printers/<name>` (CUPS web UI) — unified UX for Linux + macOS

**Critical check:** verify that CUPS `-o raw` actually preserves bytes without transformation. CUPS is known to transform PostScript/PCL by default — for TSPL that breaks the stream. Test on a real TSPL printer + dummy printer before release.

**Recommendation:** shell out initially (simple, reliable, `lp` exists everywhere). P/Invoke `libcups` later if job state control is needed.

**Effort:** 3–5 days + per-OS testing.

#### Layer 4 — Theme + title bar: simplified via Avalonia

Avalonia 11 gives this for free:
- `Application.Current.RequestedThemeVariant = ThemeVariant.Light/Dark/Default`
- `TopLevel.PlatformSettings.ColorValues` — system theme detection
- `Window.ExtendClientAreaToDecorationsHint = true` + custom titlebar for unified dark across all OSes
- DWM dark on Windows, NSAppearance on macOS, GTK theme on Linux — Avalonia applies it itself

`ThemeService.cs` shrinks to ~10 lines. `WindowChrome.cs` removed.

**Effort:** 1 day (simplification).

#### Layer 5 — File association: IFileAssociationService

**Implementations:**
- `WindowsFileAssociationService` — current HKCU + SHChangeNotify
- `LinuxFileAssociationService`:
  - `~/.local/share/applications/lucky-dragon-print.desktop` (XDG Desktop Entry)
  - `~/.local/share/mime/packages/lucky-dragon-print.xml` (MIME type `application/x-tspl`)
  - `update-mime-database` + `xdg-mime default lucky-dragon-print.desktop application/x-tspl`
- `MacFileAssociationService`:
  - macOS is statically wired via `Info.plist` `CFBundleDocumentTypes` at `.app` build time
  - Runtime user-level override via Launch Services API is complex, skip
  - `RegisterAsync` = no-op + info message: "On macOS the association is set via Finder: right-click → Get Info → Open with"

**Effort:** 2–3 days.

#### Layer 6 — Build matrix: 6 RIDs

| RID | Platform | Binary | Size (expected) |
|---|---|---|---|
| `win-x64` | Windows Intel/AMD | `LDPrint.exe` | ~68 MB |
| `win-arm64` | Windows ARM (Surface Pro X, Snapdragon Win11) | `LDPrint.exe` | ~70 MB |
| `linux-x64` | Linux Intel/AMD | `LDPrint` (executable) | ~75 MB |
| `linux-arm64` | Linux ARM (Raspberry Pi 4/5, ARM servers) | `LDPrint` | ~75 MB |
| `osx-x64` | macOS Intel (legacy) | `LDPrint.app/Contents/MacOS/LDPrint` | ~80 MB |
| `osx-arm64` | macOS Apple Silicon (M1/M2/M3/M4) | `LDPrint.app/...` | ~75 MB |

**CI** — matrix strategy with 6 RIDs. GitHub Actions runners:
- `windows-latest` — win-x64 + cross-compile win-arm64
- `ubuntu-latest` — linux-x64 + cross-compile linux-arm64 (via QEMU or native arm64 runner)
- `macos-13` (Intel) → osx-x64
- `macos-14` (Apple Silicon, free for public repos) → osx-arm64

**CI effort:** 1–2 days.

#### Layer 7 — Packaging per OS

**Windows** (v1.0 + additions):
- Keep `LuckyDragonPrint.iss` for win-x64
- Duplicate `.iss` for win-arm64 (`ArchitecturesAllowed=arm64`)
- Or multi-arch installer: one `.iss` with two `[Files]` sections

**Linux:**
- **`.AppImage`** (primary) — universal, single-file, no install required. `linuxdeploy` + GitHub Action `AppImageCrafters/build-appimage`
- `.deb` (secondary) — for Debian/Ubuntu via `dotnet-deb` or `dpkg-deb`
- `.rpm` (later on request) — Fedora/RHEL
- `.tar.gz` (fallback with instructions)

**macOS:**
- **`.app` bundle** — standard structure `LDPrint.app/Contents/{MacOS,Resources,Info.plist}`
- Universal binary (x64+arm64 in one `.app` via `lipo -create`)
- Wrapped in `.dmg` via `create-dmg` or `hdiutil`
- Without signing — Gatekeeper shows a warning, document right-click → Open workaround

**Effort:** 2–3 days for AppImage + DMG, +1 day for .deb.

#### Layer 8 — Code signing (deferred to v2.1)

| Platform | Cost | Without signing |
|---|---|---|
| Windows (DigiCert/Sectigo standard) | $300–500/year | SmartScreen "Unknown publisher" warning for ~30 days until build reputation. EV cert (~$700+) removes warning immediately |
| macOS (Apple Developer ID) | $99/year | Gatekeeper blocks. User does right-click → Open or `xattr -d com.apple.quarantine LDPrint.app` |
| Linux | free | Distro repos sign their packages themselves; AppImage optionally GPG-signed |

**Decision:** v2.0 ships without signing, document workaround in README. Code signing → v2.1 if user complaints arise.

#### v2.0 effort summary

| Layer | Effort |
|---|---|
| WPF → Avalonia 11 (UI rewrite) | 1.5–2 weeks |
| System.Drawing → SkiaSharp (rendering) | 3–5 days |
| winspool → CUPS abstraction (printing) | 3–5 days |
| File association cross-platform | 2–3 days |
| Theme/title bar (simplification) | 1 day |
| CI matrix expansion (6 RIDs) | 1–2 days |
| AppImage + DMG packaging | 2–3 days |
| Per-platform testing | 1–2 days |
| **v2.0 total** | **4–6 weeks solo** |

Code signing v2.1: +2–3 days + $99–700/year.

#### Additional features in v2.0 (after the port)

- **Watch folder mode** — file appears in folder → automatic print (integration with 1C / ERP / Excel export)
- **HTTP webhook server** — REST API `POST /print` with JSON job description (for remote printing from Honest Sign webhooks, EDI systems, warehouse systems)

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
