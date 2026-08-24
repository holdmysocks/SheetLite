# SheetLite

A fast, portable, Dracula-themed CSV and XLSX workbook editor for Windows, built with WinForms on .NET 9. SheetLite ships as a single self-contained executable — no installer, no runtime download, no network access, no telemetry.

![Version](https://img.shields.io/badge/version-0.5.0-blue) ![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey) ![.NET](https://img.shields.io/badge/.NET-9.0-purple)

## Overview

SheetLite is a lightweight spreadsheet application for quickly viewing and editing delimited text files and Excel workbooks. It focuses on the core editing workflow — open, inspect, edit, formula-calculate, sort, filter, and save — with a custom borderless window and a consistent Dracula dark theme throughout the chrome, menus, grid, dialogs, and docked tools.

Key design traits:

- **Portable**: one `win-x64` self-contained executable (`PublishSingleFile` + `ReadyToRun`), unsigned, no installer.
- **Private by default**: performs no network requests, collects no telemetry, writes no registry keys, and does not persist a recent-files list.
- **Model/UI separation**: workbook/sheet models and the formula/SQL engines are plain C# classes with no UI dependencies; all Windows Forms code lives in the `MainForm*` partials and small controls.
- **Zero external NuGet packages** for the app itself: CSV parsing, XLSX (Open XML) read/write, formula evaluation, and SQL querying are implemented in-repo.

## Features

### Editing

- Grid editing with cell/range selection, cut/copy/paste, clear contents, undo/redo.
- Multiple open workbooks as reorderable document tabs; multiple worksheets per workbook with add/remove/rename/reorder.
- Split view (`Ctrl+Alt+S`) with a second pane: either a shared view of the active workbook or an independently opened file with its own undo/redo stacks.
- Drag and drop files from Explorer onto the welcome screen, the grid, or the worksheet bar to open or import workbooks.
- Find & Replace (docked), row/column insert/delete/move, freeze panes, hide/unhide columns, auto-fit column widths.
- Fill handle: drag to extend number/date series or copy cells; formulas adjust relative references automatically.
- Bold formatting and per-cell background/text colors (XLSX round-trip).

### Formulas

Excel-style formulas entered in cells with `=`:

- Operators: `+ - * / ^`, unary `+/-`, parentheses.
- A1 references with `$` absolute rows/columns; ranges like `A1:B10`.
- Functions including arithmetic, aggregates, text, date/time, and logical helpers (see Help → Formula reference in-app).
- Automatic recalculation after edits, paste, replace, fill, sort, and structural changes; circular-reference detection with typed error display instead of crashes.
- Reference rewriting: formulas follow inserted/deleted/moved rows and columns and reordered sorts.
- XLSX save writes native formulas plus cached values; cross-sheet references are preserved (not recalculated) when opening/saving.

Example: `=SUM(A1:A10) * $B$1`, `=CONCAT(A2, " — ", B2)`.

### SQL console

A deliberately small, read-only SQL dialect over the current workbook (`View → SQL console`, `Ctrl+\``):

```sql
SELECT c1, c2 FROM worksheet WHERE c2 > 100 ORDER BY c1 DESC LIMIT 20
```

- Queries run against calculated formula values.
- Results open as an editable result document in the right pane; use Save As to keep them.
- Double-click worksheets/columns in the sidebar to insert names into the query.

### Sorting and filtering

- Quick sort current column with `Ctrl+Up` / `Ctrl+Down`.
- Advanced multi-key sort with preview: Save keeps it; Revert restores the exact pre-sort workbook, including dirty state.
- Docked filter bar and a per-column quick-filter card supporting filter-by-values (with search) and filter-by-condition operators.
- Column context menus expose sort variants, insert/delete/move, copy/paste-as, freeze, hide, and auto-size commands.

### Clipboard interop

Copy As and Paste As between formats:

- **Copy As:** CSV, raw tabular values, Markdown (± header), HTML (± header), JSON arrays, JSON objects, SQL INSERT statements.
- **Paste As:** CSV/delimited text, Markdown tables, JSON arrays/objects, transposed tabular ranges.

## Supported file formats

| Format | Read | Write | Notes |
|--------|------|-------|-------|
| CSV    | ✅   | ✅    | Delimiter detection (comma, tab, semicolon, pipe); delimiter/BOM reused on save |
| TSV/TXT| ✅   | ✅    | Same delimited pipeline |
| XLSX   | ✅   | ✅    | Multiple sheets, native formulas + cached values, basic colors/bold, frozen panes |

Current limits: no merged cells, charts, conditional formatting, validation, macros, named ranges, or advanced styling; column widths are session-only.

## Getting started

### Prerequisites

- Windows 10/11 x64
- .NET 9 SDK (to build)

### Build and run

```powershell
dotnet build -c Release
dotnet run -c Release
```

### Run the tests

The core model, primitives, data source, and view maps are covered by a dependency-free test runner (no NuGet packages needed):

```powershell
dotnet run --project tests/SheetLite.Core.Tests -c Release
```

The suite exits non-zero when a test fails, so it can be used in scripts or CI.

### Publish a single-file executable

The project is preconfigured for a compressed, self-contained, ReadyToRun single-file build:

```powershell
dotnet publish -c Release
# Output: bin\Release\net9.0-windows\win-x64\publish\SheetLite.exe
```

The executable is unsigned, so SmartScreen may warn on first launch; verify a SHA-256 checksum before trusting builds from third parties.

## Keyboard shortcuts

| Keys | Action |
|------|--------|
| `F1` | Help |
| `Ctrl+N` | New spreadsheet |
| `Ctrl+O` / `Ctrl+Shift+O` | Open file in active pane / Open another workbook tab |
| `Ctrl+S` / `Ctrl+Shift+S` | Save / Save as |
| `Ctrl+Shift+T` | Sort setup |
| `Ctrl+Shift+Arrows` | Insert row above/below, column left/right |
| `Ctrl+Subtract` / `Ctrl+Shift+Subtract` | Delete selected rows/columns |
| `Ctrl+Z` / `Ctrl+Y` | Undo / Redo |
| `Ctrl+F` / `Ctrl+H` | Find / Replace |
| `Ctrl+L` / `Ctrl+Shift+L` | Filter / Clear filter |
| `Ctrl+Up` / `Ctrl+Down` | Quick sort current column |
| `` Ctrl+` `` | SQL console |
| `Ctrl+Alt+S` | Split view |
| `Ctrl+Shift+F` | Freeze at current cell |
| `Ctrl+B` | Toggle bold |
| `Alt+Arrows` | Move selected row/column |

## Project structure

```
SheetLite/
├── SheetLite.csproj              # net9.0-windows WinForms exe; single-file publish config
├── Program.cs                    # Entry point and global exception handlers
├── MainForm.cs                   # Window shell, menus, toolbar, status bar, command routing
├── MainForm.Documents.cs         # Workbook tabs, split panes, open/save orchestration
├── MainForm.DockedTools.cs       # Find/Replace, Filter, Sort, SQL console panels
├── MainForm.HeaderMenus.cs       # Row/column/cell header context menus
├── MainForm.DragDrop.cs          # File drop zones and import targets
├── CellModel.cs                  # Cell/Sheet/Workbook models (no UI dependencies)
├── SheetModelOperations.cs       # Model-first mutation APIs + change versioning for SheetModel
├── GridTypes.cs                  # CellAddress / CellRange / CellEdit / display-value primitives
├── WorksheetDataSource.cs        # IWorksheetDataSource, memoizing SheetModelDataSource, WorksheetView
├── DocumentTab.cs                # Custom workbook tab control
├── ColumnFilterPopup.cs          # Per-column value/condition filter card
├── CsvCodec.cs                   # Delimited-text parse/serialize with delimiter detection
├── XlsxCodec.cs                  # Minimal Open XML reader/writer for XLSX
├── FormulaEngine.cs              # Recursive-descent parser/evaluator with memoized context
├── FormulaReferenceUpdater.cs    # A1 reference rewriting for structural edits
├── SqlQueryEngine.cs             # Read-only SQL dialect over the active sheet
├── Theme.cs / NativeTheme.cs     # Dracula palette, renderers, dark title bar integration
├── UiIcons.cs / HelpContent.cs   # Embedded icons and in-app help text
├── Assets/                       # App icon, titlebar glyphs, embedded resources
├── tests/SheetLite.Core.Tests    # Dependency-free test runner for models, primitives, data source
├── CODE_REVIEW*.md               # Three rounds of full-source code review documents
└── refactor-options/             # Proposed large refactors with detailed designs
```

### Architecture notes

- `WorkbookModel`/`SheetModel` hold immutable-ish cell data; the engines (`FormulaEngine`, `SqlQueryEngine`) operate directly on models so they stay unit-testable and UI-free.
- Editing is model-first: every cell or structural change goes through `SheetModel`'s mutation APIs (`SheetModelOperations.cs`), bumping a version counter; saving, undo snapshots, sorting, filtering, find/replace, and SQL all read from models, never from grid cells. There is no grid-to-model synchronization pass.
- Both grids run in `DataGridView.VirtualMode` through one shared `WorksheetPaneController`: rendering builds columns and assigns `RowCount` (O(columns), no per-cell UI objects), `CellValueNeeded` computes display text through the version-memoized `SheetModelDataSource`, edits commit via `CellValuePushed`, and styles come from model formatting in `CellFormatting`. Filtering populates each pane's `WorksheetView` map instead of toggling row-visibility flags.
- Undo/redo records compact cell change-sets (`UndoSteps.cs`) for edits and before/after sheet states for structural commands, falling back to full snapshots only for workbook-shape commands like sheet renames and sort-preview saves.
- `FormulaEvaluationContext` memoizes cell results within one recalculation batch and detects cycles; render paths share a single context per pass, and `SheetModelDataSource` caches one context per model version for future virtual-mode panes.
- Undo/redo is snapshot-based (`Stack<WorkbookModel>`), with independent secondary stacks for right-pane documents.
- All destructive keyboard commands route through a single router (`ProcessCmdKey`) that respects overlay/focus state.

## Documentation

- [`CODE_REVIEW.md`](CODE_REVIEW.md) — Round 1: full review of ~4.3k LOC grouped by severity with concrete fixes.
- [`CODE_REVIEW_2.md`](CODE_REVIEW_2.md) — Round 2: post-fix verification of Round 1 findings plus new-issue hunt.
- [`CODE_REVIEW_3.md`](CODE_REVIEW_3.md) — Round 3: verification of Round 2 fixes, regressions, and remaining findings.
- [`refactor-options/DEPENDENCY_GRAPH_FORMULA_ENGINE.md`](refactor-options/DEPENDENCY_GRAPH_FORMULA_ENGINE.md) — proposed incremental dependency-graph formula engine (high priority before the formula language grows).
- [`refactor-options/VIRTUALIZED_GRID.md`](refactor-options/VIRTUALIZED_GRID.md) — proposed model-backed virtual grid to make very large files practical.

## Roadmap

1. Dependency-graph formula engine (incremental recalculation, reusable syntax trees, typed errors, cross-sheet references).
2. Virtualized grid for very large CSV/XLSX files — Phases 1–5 are implemented (model-first editing, both panes virtual with view-map filtering, one shared pane controller, change-set undo); sparse cell storage inside `SheetModel` is deferred. See [`refactor-options/VIRTUALIZED_GRID.md`](refactor-options/VIRTUALIZED_GRID.md).
3. Address residual findings from Round 3 of the code reviews (e.g., shortcuts that still target the left pane with two independent files).

## Privacy and portability

No network requests, no telemetry, no registry keys, no persisted recent-files list. Files are only touched when you explicitly open/save. Native Windows file/color dialogs are retained.

## License

All rights reserved until a license is chosen.
