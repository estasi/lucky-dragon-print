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

- **Avalonia UI rewrite** — Linux и macOS поддержка (WPF только Windows)
- **Watch folder mode** — файл появляется в папке → автоматическая печать (для интеграции с 1С / ERP)
- **HTTP webhook server** — REST API `POST /print` с JSON-описанием задания
- **ARM64 native build** — Windows ARM + Apple Silicon

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
