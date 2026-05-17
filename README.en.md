# Lucky Dragon Print

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2011-blue.svg)](#)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](#)

TSPL file print utility for Windows 11 thermal label printers. Visual label preview, page selection, Light / Dark themes, RU / EN UI.

**[Русский README](README.md) · [Roadmap](ROADMAP.en.md) · [Changelog](CHANGELOG.md)**

## Why this exists

Many thermal label printers (TSC, Honeywell PC, GoDEX, Postek, Argox) speak **TSPL**. Marking systems (such as Russia's "Honest Sign") generate `.tspl` / `.prn` files, sometimes with multiple labels in one file. Windows offers no preview for these files and no convenient way to print selected pages only.

Lucky Dragon Print solves both problems:

- Visually renders each page with real barcodes, DataMatrix, and QR codes
- Sends the byte stream to the printer via Windows Spooler with `RAW` datatype — byte-for-byte as in the file, no driver modifications
- Allows printing any subset of pages: `1,3,5-7`

## Features

- **Visual preview** — barcodes (15+ formats via ZXing), DataMatrix, QR, BITMAP (raster blocks from the file), text with TSPL fonts 0–5 and rotations 0° / 90° / 180° / 270°
- **Page selection** — `1,3,5-7` / `All` / `Current` with reactive list filtering
- **Auto-DPI** — detects 203 / 300 / 600 dpi from drawing command coordinates
- **Label metadata** — size mm × mm, density, speed, direction, gap, codepage
- **Themes** — Light / Dark / Auto (follows Windows 11 system setting)
- **Dark title bar** — DWM `DWMWA_USE_IMMERSIVE_DARK_MODE` on Windows 11
- **i18n** — Russian (default) and English, switchable in UI
- **`.tspl` file association** — double-click in Explorer opens the file (optional, per-user HKCU)
- **Wheel navigation** — mouse wheel switches between pages like a PDF viewer
- **Printer settings** — ⚙ button opens the standard Windows print preferences dialog

## Install

Download from [Releases](https://github.com/estasi/lucky-dragon-print/releases):

- **Installer** (recommended) — `LuckyDragonPrint-Setup-X.Y.Z.exe`. Standard wizard in Russian or English, registers `.tspl` association (optional), creates Start menu shortcut. ~64 MB.
- **Portable** — `LDPrint.exe`. Self-contained single-file, no .NET install required. Runs from any folder. ~68 MB.

System requirements: **Windows 11 x64** or Windows 10 x64. .NET runtime is NOT required (bundled in exe). On Windows 11 ARM64 runs via x64 emulation (native ARM64 build planned for v2.0).

## Usage

1. **Open file** — "📁 Open file…" button or double-click `.tspl` in Explorer (if association is installed)
2. **Select printer** from dropdown — list of installed Windows printers. ⚠ Microsoft Print to PDF and similar virtual PDF printers **do not work** (see below)
3. **Specify pages** — empty = all, `1,3,5` = specific, `5-9` = range, `1,3,5-7` = mixed
4. **Click 🖨 Print** — byte stream is sent via Windows Spooler in RAW mode

The preview panel on the right shows a graphical render of the selected page. Mouse wheel switches pages, "All" / "Current" buttons fill the corresponding range.

## Supported printers

Any thermal label printer with **TSPL** or **TSPL2** support:

- **TSC Auto ID** — TX, TTP, TE, MH, DA, TDP series
- **Honeywell** — PC42, PC43, PC23, PD43 (in TSPL mode)
- **GoDEX** — G500, ZX1200i, EZ series
- **Postek** — most of the lineup
- **Argox** — OS, CP, X series (TSPL-compatible)
- Any TSC OEM or rebrand

## What does NOT work

**Virtual PDF printers** (Microsoft Print to PDF, Adobe PDF, doPDF, etc.) — fundamental incompatibility. TSPL is a command language for physical thermal printers. A RAW byte stream of TSPL commands makes no sense to a PDF renderer; the virtual printer will either write garbage to the `.pdf` file or refuse the job.

To test TSPL output without hardware, use an offline TSPL viewer or a driver emulator, or inspect the byte stream via diagnostic output.

## Build from source

Requirements: **.NET 8 SDK**, **Inno Setup 6** (for the installer).

```powershell
# Self-contained single-file exe
dotnet publish LdPrint/LdPrint.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish

# Installer (requires iscc in PATH)
iscc installer\LuckyDragonPrint.iss
# → installer\Output\LuckyDragonPrint-Setup-1.0.0.exe
```

## Architecture

```
LdPrint/
├── Models/                          — TsplDocument, TsplPage
├── Services/
│   ├── TsplParser.cs                — BITMAP-aware byte-level walker
│   ├── TsplRenderer.cs              — BitmapSource render with auto-DPI
│   ├── BarcodeRenderer.cs           — ZXing wrapper for barcodes
│   ├── RawPrinter.cs                — P/Invoke winspool.drv (RAW datatype)
│   ├── PageRangeParser.cs           — "1,3,5-7" → int[]
│   ├── ThemeService.cs              — swap MergedDictionaries
│   ├── WindowChrome.cs              — DWM dark title bar
│   ├── LocalizationService.cs      — i18n from JSON resources
│   ├── FileAssociationService.cs   — HKCU\Software\Classes
│   └── SettingsService.cs           — %APPDATA%\LDPrint\settings.json
├── ViewModels/MainViewModel.cs      — MVVM (CommunityToolkit.Mvvm)
├── MainWindow.xaml(.cs)
└── Assets/
    ├── AppIcon.ico
    ├── i18n/{ru,en}.json
    └── Themes/{Light,Dark,Controls}.xaml
```

## Roadmap

In the long term, support for other thermal printer languages: **TSPL2**, **ZPL II** (Zebra), **EPL2**, **DPL** (Datamax / Honeywell), **CPCL**, and others. Full plan in [ROADMAP.en.md](ROADMAP.en.md).

## License

[MIT](LICENSE) © 2026 Lucky Dragon LLC

## Contact

- Issues, bug reports, feature requests — [GitHub Issues](https://github.com/estasi/lucky-dragon-print/issues)
- A product by [Lucky Dragon Flow](https://flow.lucky-dragon.ru) — marking platform for Russia's "Honest Sign" system
