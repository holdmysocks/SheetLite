# Refactor Option: Virtualized Spreadsheet Grid

Status: Proposed  
Priority: High for very large CSV/XLSX files  
Scope: Worksheet storage, grid binding, sort/filter views, split view, clipboard, and undo/redo

## Objective

Replace the current fully materialized `DataGridView` with a model-backed virtual grid. The UI should request only the values and styles it needs to display instead of creating and synchronizing a `DataGridViewCell` for every worksheet cell.

The completed refactor should make large worksheets practical without changing normal spreadsheet behavior or the current Dracula presentation.

## Current design and limitation

`MainForm.Render()` currently:

1. Expands `SheetModel` to at least 100 rows by 26 columns.
2. Clears and recreates all grid columns and rows.
3. Copies every model value and style into a `DataGridViewCell`.
4. Evaluates and displays every formula.

`SyncAll()` later scans every grid cell and copies its value back into `SheetModel`. Undo snapshots clone the entire workbook.

This design is straightforward and works well for small and medium files. Its cost grows with total worksheet dimensions rather than visible viewport size. Large files therefore incur:

- many WinForms cell objects;
- full-sheet render and synchronization loops;
- high memory consumption;
- slow sheet switches and split-view refreshes;
- expensive workbook-snapshot undo states.

Relevant current code:

- `CellModel.cs`: dense `List<List<CellModel>>` storage and `EnsureSize()`.
- `MainForm.cs`: `Render()`, `SyncAll()`, `EnsureGrid()`, and primary-grid editing.
- `MainForm.DockedTools.cs`: `RenderSecondaryModel()` and secondary-grid synchronization.

## Proposed architecture

### 1. Make the worksheet model authoritative

Grid controls must stop being a second copy of worksheet state. Every edit should be committed directly through a model-facing operation:

```csharp
worksheet.SetCell(address, edit);
worksheet.InsertRows(index, count);
worksheet.SetFormatting(range, formatting);
```

Saving, formulas, SQL, sorting, filtering, and undo should read from the worksheet model, never from `DataGridViewCell.Value`.

### 2. Add stable coordinate and range types

Introduce grid-independent primitives:

```csharp
internal readonly record struct CellAddress(int Row, int Column);
internal readonly record struct CellRange(CellAddress Start, CellAddress End);
```

Selection, drag-fill, clipboard, formatting, and formula invalidation should use these types rather than retaining `DataGridViewCell` objects.

### 3. Add a worksheet data-source interface

Create an interface that can serve either grid pane:

```csharp
internal interface IWorksheetDataSource
{
    int RowCount { get; }
    int ColumnCount { get; }
    CellDisplayValue GetDisplayValue(CellAddress address);
    CellModel GetCell(CellAddress address);
    void SetCell(CellAddress address, CellEdit edit);
}
```

Both split panes can bind to the same data source while maintaining independent scroll position, current cell, selection, sort/filter view, and frozen panes.

### 4. Enable `DataGridView.VirtualMode`

The first implementation can retain WinForms `DataGridView` and use its virtual-mode events:

- `CellValueNeeded`: retrieve raw or calculated display value.
- `CellValuePushed`: commit an edit directly to the worksheet.
- `CellFormatting`: supply Dracula colors, font weight, and custom formatting.
- `NewRowNeeded` or explicit row-growth commands: extend logical dimensions.
- `RowCount`: expose the logical or filtered row count without calling `Rows.Add()` for every record.

Custom selection outlines, fill handles, header menus, and drag/drop should remain paint overlays based on model coordinates.

### 5. Separate worksheet order from displayed order

Introduce a pane-specific view map:

```csharp
internal sealed class WorksheetView
{
    public IReadOnlyList<int> VisibleRows { get; }
    public IReadOnlyList<int> VisibleColumns { get; }
    public int ModelRowForDisplayRow(int displayRow);
}
```

Filtering changes `VisibleRows` rather than setting thousands of `DataGridViewRow.Visible` flags. Sort preview can reorder the view map without immediately mutating worksheet storage. Saving a sort commits the mapped order; reverting discards the view map.

### 6. Move toward sparse worksheet storage

Virtual mode removes duplicated UI values, but dense `CellModel` allocation would remain expensive. After model-first editing is stable, replace or supplement `List<List<CellModel>>` with sparse row storage:

```csharp
SortedDictionary<int, SheetRow> rows;

internal sealed class SheetRow
{
    public Dictionary<int, CellModel> Cells { get; } = [];
}
```

Blank, unformatted cells should be implicit. Logical row and column counts must be stored separately so trailing blank areas and worksheet dimensions remain representable.

### 7. Replace snapshot undo with change sets

Full `WorkbookModel.Clone()` snapshots undermine the memory savings of sparse storage. Introduce reversible commands such as:

- `CellEditChange`;
- `RangePasteChange`;
- `InsertRowsChange` / `DeleteRowsChange`;
- `InsertColumnsChange` / `DeleteColumnsChange`;
- `MoveRowsChange` / `MoveColumnsChange`;
- `FormattingChange`;
- `WorksheetChange`.

Large pasted ranges may store compressed before/after blocks. Undo stacks should have both an operation limit and a configurable memory budget.

## Migration plan

### Phase 1: Model-first editing

- [ ] Add `CellAddress`, `CellRange`, and worksheet mutation APIs.
- [ ] Route cell edits, paste, clear, fill, formatting, and row/column commands through the model.
- [ ] Remove correctness dependence on `SyncAll()`.
- [ ] Add tests proving save and undo use model state even when the grid has not rendered a cell.

### Phase 2: Primary-grid virtual mode

- [ ] Introduce `IWorksheetDataSource` and `WorksheetView`.
- [ ] Enable `VirtualMode` on the primary grid.
- [ ] Implement value, edit, and formatting callbacks.
- [ ] Preserve selection, fill handle, frozen panes, headers, resizing, and context menus.
- [ ] Replace primary filtering and sorting with view maps.

### Phase 3: Split view

- [ ] Bind the secondary grid to the same data-source abstraction.
- [ ] Maintain independent pane view state.
- [ ] Notify both panes of model and formula changes.
- [ ] Verify edits in a shared document repaint both panes without a full render.

### Phase 4: Sparse storage and differential undo

- [ ] Introduce sparse row/cell storage behind the worksheet interface.
- [ ] Preserve XLSX formatting and CSV dimensions during import/export.
- [ ] Replace workbook snapshots with reversible change sets.
- [ ] Add memory-pressure tests for large paste, delete, and undo sequences.

### Phase 5: Remove legacy synchronization

- [ ] Remove `SyncAll()` and full-grid render loops.
- [ ] Remove code that treats grid cells as canonical data.
- [ ] Consolidate duplicated primary/secondary grid handlers into a reusable pane controller.

## Compatibility requirements

The refactor must preserve:

- CSV and XLSX round-tripping;
- cell background color, text color, and bold formatting;
- simultaneous frozen rows and columns;
- row/column header selection and context menus;
- sorting, filtering, search, replace, and SQL;
- multi-cell copy/paste and drag-fill behavior;
- split-view live editing;
- worksheet and document drag/drop;
- custom selection border and fill-handle painting;
- existing keyboard shortcuts and accessibility names.

## Testing and acceptance criteria

Correctness:

- Existing core and UI-probe tests remain green.
- Saving never requires a full grid-to-model synchronization pass.
- The primary and secondary panes show identical model changes live.
- Sort/filter mappings do not corrupt physical worksheet order.
- Undo restores values, formatting, dimensions, and worksheet structure.

Performance targets should be measured on a representative Windows x64 machine and finalized before implementation. Recommended initial acceptance scenarios:

- Open and scroll a 500,000-row by 20-column CSV without allocating a UI cell per worksheet cell.
- Switch worksheets and open split view without a full-sheet repaint.
- Edit one cell in a large file with latency determined by affected formulas and visible cells, not total cell count.
- Keep idle memory materially below the current fully materialized grid for the same file.
- Paste and undo a large rectangular range without cloning the entire workbook.

## Risks and mitigations

- **DataGridView virtual-mode limitations:** prototype selection, frozen panes, row headers, and custom painting before migrating all commands.
- **Coordinate translation bugs:** centralize display-to-model mapping and test filtered/sorted selections thoroughly.
- **Sparse-storage codec regressions:** preserve codec-facing iteration helpers and add XLSX/CSV round-trip fixtures.
- **Undo complexity:** implement typed changes incrementally, retaining snapshot fallback for unsupported operations during transition.
- **Large selections:** represent selections as ranges rather than enumerating every `DataGridViewCell` wherever practical.

## Non-goals for the first implementation

- Replacing WinForms with another UI framework.
- Implementing Excel's complete worksheet-size and formatting model.
- Loading XLSX parts lazily from disk.
- Paging data from a database or remote service.

The first goal is to remove total-cell-count dependence from the UI while preserving SheetLite's current behavior.
