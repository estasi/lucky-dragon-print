# Changelog

All notable changes to Lucky Dragon Print are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.0.1] — 2026-05-18

### Fixed

- **DataMatrix preview size now consistent across pages.** ZXing's built-in scaling used integer-multiple module sizing (`floor(reqWidth / naturalModules)`), centring the symbol with white padding. Different GS1 payloads (varying `(91)Key` / `(92)Sig` lengths) produced different natural module counts (e.g. 24×24 vs 26×26) → different padding → visually smaller or larger DataMatrix in the same bounding box. The renderer now encodes at natural module size and float-scales to fill the requested rectangle exactly, so every page in a multi-page `.tspl` displays the symbol at the same visual size. Affects: visual preview only — printer output was already correct (printer firmware computes its own module width independently of the preview).

## [1.0.0] — 2026-05-17

Initial public release.

### Added

#### Print pipeline

- TSPL file printing via Windows Spooler `RAW` datatype
- P/Invoke `winspool.drv` for `OpenPrinter` / `StartDocPrinter` / `WritePrinter` / `EndDocPrinter`
- Byte-preserving stream — header + selected page bytes concatenated without modification
- Page range syntax: `1,3,5-7` / `All` / `Все` / `Current` / `Текущая`

#### Visual preview

- BitmapSource renderer using `System.Drawing.Common` + WPF interop
- 15+ barcode formats via ZXing.Net: EAN-13, EAN-8, UPC-A, UPC-E, CODE39, CODE128, ITF, CODABAR, MSI, PLESSEY, Pharmacode, and more
- DataMatrix GS1 (for marking codes)
- QR Code (all error correction levels)
- BITMAP raster — decoded 1bpp binary from TSPL byte stream
- Text with TSPL fonts 0–5 and rotations 0° / 90° / 180° / 270°
- Auto-DPI detection: scans command coordinates, picks the smallest standard DPI (203 / 300 / 600) that fits

#### Parser

- BITMAP-aware byte-level walker — skips `width × height` payload bytes to avoid phantom `PRINT` keywords inside binary data
- Multi-encoding support (CP866 / Win-1251 / UTF-8 / GB2312) — preserves original bytes for printing
- Extracts label metadata: `SIZE`, `GAP`, `DIRECTION`, `DENSITY`, `SPEED`, `CODEPAGE`

#### User interface

- WPF on .NET 8
- MVVM via CommunityToolkit.Mvvm 8.4 source generators
- Light / Dark / Auto themes (follows Windows 11 system setting)
- DWM dark title bar (`DwmSetWindowAttribute` with `DWMWA_USE_IMMERSIVE_DARK_MODE`)
- Custom ControlTemplates for full theming of Button / ComboBox / TextBox / ListBox
- Lucky Dragon Flow design tokens: rose gold #C49A7A accent + neutral charcoal dark + warm beige light
- Golos Text font family (cyrillic-optimized) with Segoe UI fallback
- Russian (default) + English UI via JSON resource files with `{loc:T key}` markup extension
- Persistent settings in `%APPDATA%\LDPrint\settings.json`

#### Convenience features

- Reactive page filter — typing a range hides non-matching pages, list rebuilds live
- Wheel-mouse navigation between pages (PDF-viewer style)
- Reactive preview — typing a page number updates preview immediately
- File association for `.tspl` and `.prn` (per-user HKCU, no UAC required)
- Printer settings dialog via `rundll32 printui.dll PrintUIEntry /e`
- Visible label boundaries — paper-effect with drop shadow on the preview canvas

#### Packaging

- Self-contained single-file executable (~68 MB, no .NET install required on target)
- Inno Setup 6 installer with LZMA2/ultra64 compression (~64 MB)
- Multi-language installer wizard (Russian + English)
- Optional file association during install
- Windows 11 / Windows 10 x64

### Supported printers (tested)

- TSC TX200, TTP-244 Pro, MH series
- Honeywell PC42, PC43 (in TSPL emulation mode)
- Any TSPL-compatible thermal label printer

### Known issues

- Microsoft Print to PDF and other virtual PDF printers do NOT work — RAW TSPL byte stream is fundamentally incompatible with PDF rendering. Use a real thermal printer or test offline.
- ARM64 Windows users — runs via x64 emulation only. Native ARM64 build planned for v2.0.
- macOS and Linux not supported — WPF is Windows-only. Cross-platform Avalonia rewrite planned for v2.0.

[1.0.1]: https://github.com/estasi/lucky-dragon-print/releases/tag/v1.0.1
[1.0.0]: https://github.com/estasi/lucky-dragon-print/releases/tag/v1.0.0
