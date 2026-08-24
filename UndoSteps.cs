namespace SheetLite;

/// <summary>An entry on an undo/redo stack. Steps know how to restore both directions.</summary>
internal interface IUndoStep
{
    /// <summary>Stable identity of the logical worksheet this step applies to.</summary>
    Guid WorksheetId { get; }
    void Undo(SheetModel sheet);
    void Redo(SheetModel sheet);
}

/// <summary>Full-workbook fallback step (sort baselines, workbook-structure commands).</summary>
internal sealed class WorkbookSnapshotStep(WorkbookModel workbook) : IUndoStep
{
    public WorkbookModel Workbook { get; } = workbook;
    public Guid WorksheetId => Workbook.ActiveSheet.Id;
    public void Undo(SheetModel sheet) { }
    public void Redo(SheetModel sheet) { }
}

/// <summary>Immutable cell state captured for change-set undo.</summary>
internal sealed record CellSnapshot(string Value, Color? BackColor, Color? ForeColor, bool Bold)
{
    public static CellSnapshot Capture(CellModel cell) => new(cell.Value, cell.BackColor, cell.ForeColor, cell.Bold);
}

internal sealed class CellChange(CellAddress address, CellSnapshot before)
{
    public CellAddress Address { get; init; } = address;
    public CellSnapshot Before { get; init; } = before;
    public CellSnapshot After { get; set; } = before;
}

/// <summary>Inverts a run of cell edits: Undo restores Before states, Redo reapplies After states.</summary>
internal sealed class CellEditsStep(Guid worksheetId, IReadOnlyList<CellChange> changes) : IUndoStep
{
    public Guid WorksheetId { get; } = worksheetId;
    public IReadOnlyList<CellChange> Changes { get; } = changes;

    public void Undo(SheetModel sheet) => sheet.ApplySnapshots(Changes.Select(change => (change.Address, change.Before)));
    public void Redo(SheetModel sheet) => sheet.ApplySnapshots(Changes.Select(change => (change.Address, change.After)));
}

/// <summary>A deep copy of one worksheet's storage, used to invert structural operations.</summary>
internal sealed class SheetState(SheetModel sheet)
{
    public List<List<CellModel>> Rows { get; } = sheet.Rows.Select(row => row.Select(cell => cell.Clone()).ToList()).ToList();
    public int FrozenRows { get; } = sheet.FrozenRows;
    public int FrozenColumns { get; } = sheet.FrozenColumns;

    /// <summary>Copies the captured state back into the live sheet without consuming the capture.</summary>
    public void ApplyTo(SheetModel sheet)
    {
        sheet.RestoreRows(Rows, FrozenRows, FrozenColumns);
    }
}

/// <summary>Inverts a structural operation (insert/delete/move/sort/clear) via before/after sheet states.</summary>
internal sealed class SheetStructureStep(Guid worksheetId, SheetState before, SheetState after) : IUndoStep
{
    public Guid WorksheetId { get; } = worksheetId;
    private readonly SheetState after = after;
    public void Undo(SheetModel sheet) => before.ApplyTo(sheet);
    public void Redo(SheetModel sheet) => after.ApplyTo(sheet);
}
