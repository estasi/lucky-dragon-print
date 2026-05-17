# Lucky Dragon Print

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2011-blue.svg)](#)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](#)

Утилита печати TSPL-файлов на термопринтеры под Windows 11. Графический предпросмотр этикеток, выбор страниц, темы Light / Dark, RU / EN интерфейс.

**[English README](README.en.md) · [Roadmap](ROADMAP.md) · [Changelog](CHANGELOG.md)**

## Зачем это нужно

Многие термопринтеры (TSC, Honeywell PC, GoDEX, Postek, Argox) используют язык **TSPL**. Системы маркировки (например, «Честный знак») генерируют `.tspl` / `.prn` файлы, иногда сразу несколько этикеток в одном файле. Windows не показывает превью таких файлов и не предлагает удобного способа напечатать выборочные страницы.

Lucky Dragon Print решает обе задачи:

- Графически рендерит каждую страницу с реальными штрихкодами, DataMatrix и QR
- Отправляет байтовый поток в принтер через Windows Spooler в RAW-режиме — побайтово как в файле, без модификаций драйвером
- Позволяет печатать любое подмножество страниц: `1,3,5-7`

## Возможности

- **Графический предпросмотр** — BARCODE (15+ форматов через ZXing), DataMatrix, QR, BITMAP (растровые блоки из файла), текст с TSPL fonts 0–5 и поворотами 0°/90°/180°/270°
- **Выбор страниц** — `1,3,5-7` / «Все» / «Текущая» с реактивной фильтрацией списка
- **Авто-DPI** — определяет 203 / 300 / 600 dpi из координат drawing-команд
- **Метаданные этикетки** — размер мм×мм, плотность, скорость, направление, gap, codepage
- **Темы** — Light / Dark / Auto (следует системной настройке Windows 11)
- **Тёмный title bar** — DWM `DWMWA_USE_IMMERSIVE_DARK_MODE` на Windows 11
- **i18n** — русский (по умолчанию) и английский, переключение в UI
- **Ассоциация .tspl** — двойной клик в проводнике открывает файл (опционально, per-user в HKCU)
- **Wheel-навигация** — колесо мыши переключает страницы как в PDF-viewer
- **Настройки принтера** — кнопка ⚙ открывает стандартный диалог свойств печати

## Установка

Скачать с [Releases](https://github.com/estasi/lucky-dragon-print/releases):

- **Installer** (рекомендуется) — `LuckyDragonPrint-Setup-X.Y.Z.exe`. Стандартный wizard на русском или английском, регистрирует ассоциацию `.tspl` (опционально), создаёт ярлык в Пуске. ~64 MB.
- **Portable** — `LDPrint.exe`. Self-contained single-file, не требует установки .NET. Запускается с любой папки. ~68 MB.

Системные требования: **Windows 11 x64** или Windows 10 x64. .NET runtime НЕ нужен (вшит в exe). На Windows 11 ARM64 работает через эмуляцию x64 (нативный ARM64 build запланирован на v2.0).

## Использование

1. **Открыть файл** — кнопка «📁 Открыть файл…» или двойной клик на `.tspl` в проводнике (если установлена ассоциация)
2. **Выбрать принтер** из dropdown — список установленных в Windows. ⚠ Microsoft Print to PDF и подобные виртуальные PDF-принтеры **не работают** (см. ниже)
3. **Указать страницы** — пусто = все, `1,3,5` = конкретные, `5-9` = диапазон, `1,3,5-7` = смесь
4. **Нажать 🖨 Печать** — байтовый поток отправляется через Windows Spooler в RAW-режиме

В preview-панели справа — графический рендер выбранной страницы. Wheel мышью переключает страницы, кнопка «Все» / «Текущая» подставляет соответствующий диапазон.

## Поддерживаемые принтеры

Любой термопринтер этикеток с поддержкой **TSPL** или **TSPL2**:

- **TSC Auto ID** — TX, TTP, TE, MH, DA, TDP серии
- **Honeywell** — PC42, PC43, PC23, PD43 (в TSPL-режиме)
- **GoDEX** — G500, ZX1200i, EZ серии
- **Postek** — большая часть линейки
- **Argox** — OS, CP, X серии (TSPL-совместимы)
- Любой OEM или ребрендинг TSC

## Что НЕ работает

**Виртуальные PDF-принтеры** (Microsoft Print to PDF, Adobe PDF, doPDF и т.п.) — фундаментальная несовместимость. TSPL — это команды для физического термопринтера. RAW байтовый поток с TSPL-командами не имеет смысла для PDF-рендера, и виртуальный принтер либо пишет мусор в `.pdf`, либо отказывается принимать задание.

Если нужно протестировать TSPL-вывод без железа — используйте offline TSPL viewer или эмулятор драйвера, или сравните байтовый поток через диагностический output.

## Сборка из исходников

Требования: **.NET 8 SDK**, **Inno Setup 6** (для installer'а).

```powershell
# Self-contained single-file exe
dotnet publish LdPrint/LdPrint.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish

# Installer (требует iscc в PATH)
iscc installer\LuckyDragonPrint.iss
# → installer\Output\LuckyDragonPrint-Setup-1.0.0.exe
```

## Архитектура

```
LdPrint/
├── Models/                          — TsplDocument, TsplPage
├── Services/
│   ├── TsplParser.cs                — BITMAP-aware byte-level walker
│   ├── TsplRenderer.cs              — BitmapSource рендер с auto-DPI
│   ├── BarcodeRenderer.cs           — ZXing wrapper для штрихкодов
│   ├── RawPrinter.cs                — P/Invoke winspool.drv (RAW datatype)
│   ├── PageRangeParser.cs           — «1,3,5-7» → int[]
│   ├── ThemeService.cs              — swap MergedDictionaries
│   ├── WindowChrome.cs              — DWM dark title bar
│   ├── LocalizationService.cs      — i18n из JSON ресурсов
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

В перспективе — поддержка других языков термопринтеров: **TSPL2**, **ZPL II** (Zebra), **EPL2**, **DPL** (Datamax / Honeywell), **CPCL**, и других. Полный план — [ROADMAP.md](ROADMAP.md).

## Лицензия

[MIT](LICENSE) © 2026 Lucky Dragon LLC

## Связь

- Issues, bug reports, feature requests — [GitHub Issues](https://github.com/estasi/lucky-dragon-print/issues)
- Продукт от [Lucky Dragon Flow](https://flow.lucky-dragon.ru) — платформа маркировки Честного знака
