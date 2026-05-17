# Roadmap

**[English version](ROADMAP.en.md)**

## Видение

Lucky Dragon Print — это утилита печати для **всех языков термопринтеров этикеток**, не только TSPL. Архитектура с самого начала проектируется как набор взаимозаменяемых handler'ов для разных языков, объединённых одним UI/UX и общим preview pipeline.

TSPL — стартовая точка, выбрана потому что TSC и Honeywell PC доминируют в РФ маркировке Честного знака. По мере развития будут добавляться остальные индустриальные стандарты.

## Поддерживаемые языки

### Категория A — индустриальные label printers (основной scope)

| Формат | Производители | Доля рынка | Версия |
|---|---|---|---|
| **TSPL / TSPL2** | TSC Auto ID, Honeywell PC, GoDEX, Postek, Argox | ~30% РФ маркировка | v1.0 ✅ / v1.1 |
| **ZPL II** | Zebra ZT, ZD, GK, GX, ZQ серии | ~40% мирового рынка | v1.2 |
| **EPL2** | старые Zebra LP / TLP (Eltron legacy) | legacy, множество в обороте | v1.3 |
| **DPL** | Datamax-O'Neil, Honeywell PD / PM | enterprise | v1.3 |
| **CPCL** | Zebra QLn, ZQ mobile / battery | ТСД, мобильная печать | v2.x |
| **IPL** | Intermec PB / PM, Honeywell PB | enterprise legacy | v2.x |
| **SPL** | STAR Micronics TSP серии | nice-to-have | v2.x |
| **CLP** | Citizen CL / CLP серии | nice-to-have | v2.x |

### Категория B — receipt printers (отдельный модуль, рассмотрение)

Для receipt printers (фискальная и кухонная печать) — другой класс задач, другая бумага. Возможно отдельная утилита поверх той же базы:

- **ESC/POS** — Epson TM, Bixolon SRP, Star TSP, Citizen CT (наиболее распространённый)
- **SBPL** — Sato (gray zone между label и receipt)

## Версии

### v1.0 ✅ Released — май 2026

Базовый pipeline для TSPL:

- Печать через Windows Spooler RAW
- Графический preview (BARCODE 15 форматов, DataMatrix, QR, BITMAP, text)
- Выбор страниц с реактивным фильтром
- Темы Light / Dark / Auto
- Файловая ассоциация `.tspl`
- RU / EN UI
- Inno Setup installer

### v1.1 — Расширение TSPL семейства + UX polish

- **TSPL2** — расширенные команды (multiple BITMAP modes, advanced fonts, GRF download, RFID)
- Drag-and-drop файлов на окно приложения
- CLI режим: `LDPrint.exe --printer "TSC TX200" --pages "1-3" "file.tspl"`
- Recent files menu
- Дополнительные локализации UI (`zh`, `tr`, `kk` — для рынков СНГ + Китай)
- Печать списком (queue несколько файлов сразу)

### v1.2 — Zebra entry (главный конкурент TSC)

- **ZPL II** полная поддержка — parser + renderer + print pipeline
- Auto-format detection — по первым ~200 байтам файла определяется язык
- Print queue mixed-format — TSPL и ZPL файлы в одной очереди

### v1.3 — Datamax + Eltron

- **EPL2** (legacy Zebra LP / TLP)
- **DPL** (Datamax-O'Neil / Honeywell PD / PM)
- Конвертер между языками (где математически возможно): TSPL → ZPL для миграции с TSC на Zebra

### v2.0 — Cross-platform + automation

Главная цель v2.0 — портировать утилиту на Linux и macOS (включая Apple Silicon ARM64). Технически — Windows 11 ARM64 запускает текущий x64 build через эмуляцию, но нативный ARM64 предпочтительнее по производительности.

#### Что в v1.0 завязано на Windows

| Компонент | v1.0 реализация | Проблема для non-Windows |
|---|---|---|
| UI framework | WPF | Только Windows, нет порта |
| Bitmap rendering | System.Drawing.Common (GDI+) | .NET 6+ deprecated на non-Windows; на Linux через libgdiplus с проблемами шрифтов и FPS |
| Printing | P/Invoke `winspool.drv` (StartDocPrinter RAW) | Windows-only API |
| Title bar dark | DWM `DwmSetWindowAttribute` | Windows-only |
| File association | HKCU registry + SHChangeNotify | Windows-only |
| Printer settings dialog | `rundll32 printui.dll /e` | Windows-only |
| Theme auto-detect | HKCU\\...\\AppsUseLightTheme | Windows-only |
| Installer | Inno Setup 6 | Windows-only |

#### Что уже cross-platform (без изменений)

- **.NET 8 runtime** — Win/Linux/macOS × x64/arm64
- **CommunityToolkit.Mvvm** — pure .NET source generators
- **ZXing.Net** 0.16.10 — pure .NET, без native bindings
- **`Environment.SpecialFolder.ApplicationData`** — резолвится корректно на каждой ОС: `%APPDATA%` (Win), `~/.config` (Linux XDG), `~/Library/Application Support` (macOS)
- **JSON resource i18n** — через `Assembly.GetManifestResourceStream`
- **TsplParser / PageRangeParser / BarcodeRenderer internal** — pure C#

Это значит ~50% codebase (бизнес-логика) портируется без изменений. Меняется только UI + system integration.

#### Слой 1 — UI framework: WPF → Avalonia 11

**Выбор Avalonia, не MAUI:**
- Avalonia 11 = single-codebase Win + Mac + Linux + iOS + Android + WebAssembly
- XAML + MVVM — минимальная миграция из WPF (близкий ментальный паттерн)
- ARM64 нативно на всех платформах
- MAUI Linux — community-only (Uno / GtkSharp), не Microsoft-supported

**Меняется:** `MainWindow.xaml`, `Themes/{Light,Dark,Controls}.xaml`, `App.xaml`, custom ControlTemplates.

**Не меняется:** `ViewModels/*` (`CommunityToolkit.Mvvm` совместим 1:1), `Models/*`, pure-C# services.

**Эффорт:** 1.5–2 недели.

#### Слой 2 — Rendering: System.Drawing → SkiaSharp

System.Drawing.Common с .NET 6+ помечен `[SupportedOSPlatform("windows")]`. На Linux работает через libgdiplus — медленный, проблемы с TTF.

**Замена:** SkiaSharp 2.x — Google Skia engine (тот же что в Chrome, Flutter, Android). Native binaries для всех платформ × x64/arm64. HarfBuzz для TTF/OTF.

**Меняется:**
- `TsplRenderer.cs`: `Graphics` → `SKCanvas`, `Bitmap` → `SKBitmap`/`SKImage`
- `BarcodeRenderer.cs`: переключение на `ZXing.Net.Bindings.SkiaSharp`
- Конвертация `SKBitmap` → Avalonia `IBitmap` для отображения

**Эффорт:** 3–5 дней.

#### Слой 3 — Printing: IPrinterService абстракция

```csharp
public interface IPrinterService {
    IEnumerable<string> GetInstalledPrinters();
    Task PrintRawAsync(string printerName, byte[] data, string docName);
    Task OpenPrinterPropertiesAsync(string printerName);
}
```

**Реализации:**
- `WindowsPrinterService` — текущий `RawPrinter` (winspool.drv P/Invoke)
- `CupsPrinterService` (Linux + macOS — CUPS одинаков на обеих):
  - Enumerate: `lpstat -a` или HTTP `localhost:631/printers`
  - Print RAW: `lp -d <printer> -o raw -t "<docName>" -` ← stdin = bytes
  - Properties: `xdg-open localhost:631/printers/<name>` (CUPS web UI) — единый UX для Linux + macOS

**Критичная проверка:** CUPS `-o raw` действительно сохраняет байты без преобразования. CUPS известен трансформацией PostScript/PCL по умолчанию — для TSPL это сломает поток. Тестировать на реальном TSPL принтере + dummy printer перед релизом.

**Рекомендация:** shell-out на старте (просто, надёжно, `lp` есть везде). P/Invoke `libcups` — позже если нужен контроль над job state.

**Эффорт:** 3–5 дней + per-OS тестирование.

#### Слой 4 — Theme + title bar: упрощение через Avalonia

Avalonia 11 даёт это бесплатно:
- `Application.Current.RequestedThemeVariant = ThemeVariant.Light/Dark/Default`
- `TopLevel.PlatformSettings.ColorValues` — детект системной темы
- `Window.ExtendClientAreaToDecorationsHint = true` + custom titlebar для unified dark на всех ОС
- DWM dark на Windows, NSAppearance на macOS, GTK theme на Linux — Avalonia применяет сам

`ThemeService.cs` сжимается до ~10 строк. `WindowChrome.cs` удаляется.

**Эффорт:** 1 день (упрощение).

#### Слой 5 — File association: IFileAssociationService

**Реализации:**
- `WindowsFileAssociationService` — текущий HKCU + SHChangeNotify
- `LinuxFileAssociationService`:
  - `~/.local/share/applications/lucky-dragon-print.desktop` (XDG Desktop Entry)
  - `~/.local/share/mime/packages/lucky-dragon-print.xml` (MIME type `application/x-tspl`)
  - `update-mime-database` + `xdg-mime default lucky-dragon-print.desktop application/x-tspl`
- `MacFileAssociationService`:
  - macOS делается через `Info.plist` `CFBundleDocumentTypes` при сборке `.app` (статически)
  - Runtime user override через Launch Services API — сложно, скипаем
  - `RegisterAsync` = no-op + info-сообщение «На macOS установка через Finder: правый клик → Get Info → Open with»

**Эффорт:** 2–3 дня.

#### Слой 6 — Build matrix: 6 RID

| RID | Платформа | Binary | Размер (ожид.) |
|---|---|---|---|
| `win-x64` | Windows Intel/AMD | `LDPrint.exe` | ~68 MB |
| `win-arm64` | Windows ARM (Surface Pro X, Snapdragon Win11) | `LDPrint.exe` | ~70 MB |
| `linux-x64` | Linux Intel/AMD | `LDPrint` (executable) | ~75 MB |
| `linux-arm64` | Linux ARM (Raspberry Pi 4/5, ARM servers) | `LDPrint` | ~75 MB |
| `osx-x64` | macOS Intel (legacy) | `LDPrint.app/Contents/MacOS/LDPrint` | ~80 MB |
| `osx-arm64` | macOS Apple Silicon (M1/M2/M3/M4) | `LDPrint.app/...` | ~75 MB |

**CI** — matrix strategy с 6 RID. GitHub Actions runners:
- `windows-latest` — win-x64 + cross-compile win-arm64
- `ubuntu-latest` — linux-x64 + cross linux-arm64 (через QEMU или native arm64 runner)
- `macos-13` (Intel) → osx-x64
- `macos-14` (Apple Silicon, бесплатный на public repos) → osx-arm64

**Эффорт CI:** 1–2 дня.

#### Слой 7 — Packaging per OS

**Windows** (v1.0 + добавления):
- Сохраняем `LuckyDragonPrint.iss` для win-x64
- Дублируем `.iss` для win-arm64 (`ArchitecturesAllowed=arm64`)
- Или multi-arch installer: один `.iss` с двумя `[Files]` секциями

**Linux:**
- **`.AppImage`** (primary) — universal, single-file без установки. `linuxdeploy` + GitHub Action `AppImageCrafters/build-appimage`
- `.deb` (secondary) — Debian/Ubuntu через `dotnet-deb` или `dpkg-deb`
- `.rpm` (позже по запросу) — Fedora/RHEL
- `.tar.gz` (fallback с инструкцией)

**macOS:**
- **`.app` bundle** — стандартная структура `LDPrint.app/Contents/{MacOS,Resources,Info.plist}`
- Universal binary (x64+arm64 в одном `.app` через `lipo -create`)
- Wrapper в `.dmg` через `create-dmg` или `hdiutil`
- Без подписи — Gatekeeper показывает warning, документируем right-click → Open

**Эффорт:** 2–3 дня AppImage + DMG, +1 день .deb.

#### Слой 8 — Code signing (отложено до v2.1)

| Платформа | Цена | Без подписи |
|---|---|---|
| Windows (DigiCert/Sectigo standard) | $300–500/год | SmartScreen warning «Unknown publisher» первые ~30 дней до build reputation. EV cert (~$700+) убирает warning сразу |
| macOS (Apple Developer ID) | $99/год | Gatekeeper блокирует. User делает right-click → Open или `xattr -d com.apple.quarantine LDPrint.app` |
| Linux | free | Distro repos подписывают свои пакеты сами; для AppImage опционально GPG-подпись |

**Решение:** в v2.0 без подписи, документируем workaround в README. Code signing → v2.1 при жалобах пользователей.

#### Сводка эффорта v2.0

| Слой | Эффорт |
|---|---|
| WPF → Avalonia 11 (UI rewrite) | 1.5–2 недели |
| System.Drawing → SkiaSharp (rendering) | 3–5 дней |
| winspool → CUPS abstraction (printing) | 3–5 дней |
| File association cross-platform | 2–3 дня |
| Theme/title bar (упрощение) | 1 день |
| CI matrix expansion (6 RID) | 1–2 дня |
| AppImage + DMG packaging | 2–3 дня |
| Testing на каждой платформе | 1–2 дня |
| **Итого v2.0** | **4–6 недель соло** |

Code signing v2.1: +2–3 дня + $99–700/год.

#### Дополнительные функции v2.0 (после порта)

- **Watch folder mode** — файл появляется в папке → автоматическая печать (интеграция с 1С / ERP / Excel экспорт)
- **HTTP webhook server** — REST API `POST /print` с JSON-описанием задания (для удалённой печати из webhooks Честного знака, ЭДО, складских систем)

### v2.x — Beyond

- **CPCL** (Zebra mobile)
- **IPL** (Intermec)
- **SPL** (STAR Micronics)
- **CLP** (Citizen)
- **ESC/POS** (отдельный модуль для receipt printers)
- **Plugin SDK** — third-party разработчики добавляют свой язык без модификации core

## Архитектурная эволюция

### Сейчас (v1.0)

Один pipeline под TSPL:

```
TsplParser → TsplDocument → TsplRenderer → BitmapSource (для UI)
                                        → byte[]        (для печати)
RawPrinter → byte stream → Windows Spooler → Printer
```

### v1.2 — multi-format рефакторинг

Появляется абстракция `IPrintLanguageHandler`:

```csharp
public interface IPrintLanguageHandler {
    string LanguageName { get; }                              // "TSPL", "ZPL", "EPL2"
    bool CanParse(ReadOnlySpan<byte> header);                  // sniff первые ~200 байт
    IPrintDocument Parse(byte[] content);
    BitmapSource Render(IPrintDocument doc, int pageIndex);
    byte[] BuildStream(IPrintDocument doc, int[] pageIndices);
}
```

Реализации: `TsplHandler`, `Tspl2Handler`, `ZplHandler`, `EplHandler`, `DplHandler`, ...

`PrintLanguageDetector` смотрит первые ~200 байт открытого файла и выбирает подходящий handler автоматически. UI остаётся единым — preview, page selection, themes одинаковы для всех языков.

### v2.0 — Avalonia + automation

Перенос UI с WPF на Avalonia даёт Linux и macOS поддержку без переписывания бизнес-логики (Services / Models / ViewModels работают на любом .NET runtime). Watch folder и HTTP server — отдельные модули, опциональны через CLI флаги.

## Не входит в roadmap

- **Редактор этикеток** — это другой продукт (Lucky Dragon Flow). Эта утилита только печатает существующие файлы.
- **Конвертация в PDF** — RAW TSPL не имеет визуального соответствия PDF (это команды принтера). Преобразование TSPL → PDF возможно только через рендер в bitmap → PDF, что уже делается в preview, и не имеет практического смысла как отдельный workflow.
- **Маркировка / GS1 кодогенерация** — это работа Lucky Dragon Flow и его инструментов. Lucky Dragon Print только воспроизводит уже сгенерированные файлы.
- **Розничная фискальная отчётность** — вне scope (другой класс ПО).

## Как помочь

Pull requests welcome для любой задачи из этой roadmap. Особенно ценны:

- Тестовые файлы реальных принтеров (anonymized) — для проверки парсеров и рендеров
- Отчёты о совместимости с конкретными моделями принтеров
- Локализация интерфейса на дополнительные языки
- Реализация handler'ов для других print languages
