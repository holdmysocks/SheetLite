namespace SheetLite;

/// <summary>
/// Model-first mutation APIs for <see cref="SheetModel"/>.
/// Every worksheet edit must go through these members so the model is the single
/// source of truth: saving, undo, formulas, sorting, and filtering read from here,
/// never from grid cells. Each mutation bumps <see cref="Version"/> so caches
/// (memoized formula evaluation, virtual-grid repaints) can invalidate cheaply.
/// </summary>
internal sealed partial class SheetModel
{
    /// <summary>Monotonic change counter; incremented by every content or structure mutation.</summary>
    public int Version { get; private set; }
    public int RowCount => Rows.Count;

    // ----- change-set undo recording -----
    // Cell mutations append to the current segment (Before captured on first touch of an
    // address, After refreshed on every write). Structural mutations mark the segment so the
    // host snapshots sheet state instead. TakeUndoSegment hands the finished segment to the
    // undo stack; RestoreRows/ApplySnapshots write without re-recording.

    private const int MaxRecordedChanges = 100_000;
    private readonly List<CellChange> segment = [];
    private readonly Dictionary<CellAddress, CellChange> segmentIndex = [];
    private bool segmentStructureChanged;
    private SheetState? segmentPreState;
    private int suppressRecording;

    /// <summary>Records changes without creating undo entries while restoring history.</summary>
    public IDisposable SuppressRecording() => new RecordingScope(this);

    private sealed class RecordingScope(SheetModel sheet) : IDisposable
    {
        public void Dispose() => sheet.suppressRecording--;
    }

    private CellChange BeginRecord(int row, int column)
    {
        var address = new CellAddress(row, column);
        if (segmentIndex.TryGetValue(address, out var existing)) return existing;
        var change = new CellChange(address, CellSnapshot.Capture(Rows[row][column]));
        segmentIndex[address] = change;
        segment.Add(change);
        return change;
    }

    private void EndRecord(CellChange change, int row, int column) => change.After = CellSnapshot.Capture(Rows[row][column]);

    private void MarkStructural()
    {
        segmentPreState ??= new SheetState(this); // frozen at the first structural mutation of the segment
        segmentStructureChanged = true;
    }

    /// <summary>Closes the current segment: a cell-edit step, a structural step, or null when nothing changed.</summary>
    public IUndoStep? TakeUndoSegment()
    {
        IUndoStep? step;
        if (segmentStructureChanged || segment.Count > MaxRecordedChanges)
        {
            step = new SheetStructureStep(this, segmentPreState ?? new SheetState(this), new SheetState(this));
        }
        else if (segment.Count > 0)
        {
            step = new CellEditsStep(this, segment.ToArray());
        }
        else step = null;
        segment.Clear();
        segmentIndex.Clear();
        segmentStructureChanged = false;
        segmentPreState = null;
        return step;
    }

    /// <summary>Writes recorded states back (undo/redo). Not itself recorded.</summary>
    public void ApplySnapshots(IEnumerable<(CellAddress Address, CellSnapshot State)> restores)
    {
        using var _ = SuppressRecording();
        foreach (var (address, state) in restores)
        {
            CellModel cell = GetCell(address.Row, address.Column);
            cell.Value = state.Value; cell.BackColor = state.BackColor; cell.ForeColor = state.ForeColor; cell.Bold = state.Bold;
        }
        Bump();
    }

    /// <summary>Replaces worksheet storage with a previously captured state (structural undo).</summary>
    public void RestoreRows(List<List<CellModel>> rows, int frozenRows, int frozenColumns)
    {
        using var _ = SuppressRecording();
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row.Select(cell => cell.Clone()).ToList());
        FrozenRows = frozenRows; FrozenColumns = frozenColumns;
        Bump();
    }

    private void Bump() => Version++;

    /// <summary>Returns the cell at the address, growing storage to include it. Blank cells are created on demand.</summary>
    public CellModel GetCell(int row, int column)
    {
        EnsureSize(row + 1, column + 1);
        return Rows[row][column];
    }

    public CellModel GetCell(CellAddress address) => GetCell(address.Row, address.Column);

    /// <summary>Raw (unevaluated) value of a cell; empty string when outside current dimensions.</summary>
    public string GetRawValue(int row, int column)
        => row < 0 || column < 0 || row >= Rows.Count || column >= Rows[row].Count ? "" : Rows[row][column].Value;

    public string GetRawValue(CellAddress address) => GetRawValue(address.Row, address.Column);

    /// <summary>Evaluated display value: formulas are calculated, everything else is returned raw. Errors render as "#ERROR!".</summary>
    public string EvaluatedValue(int row, int column) => EvaluatedValue(row, column, null);

    /// <summary>Single source of truth for evaluated display text; pass a shared <paramref name="context"/> to memoize a whole pass.</summary>
    public string EvaluatedValue(int row, int column, FormulaEngine.FormulaEvaluationContext? context)
    {
        string raw = GetRawValue(row, column);
        if (!raw.TrimStart().StartsWith('=')) return raw;
        FormulaResult result = context is null ? FormulaEngine.Evaluate(this, row, column) : context.Evaluate(row, column);
        return result.Success ? result.Value : "#ERROR!";
    }

    public bool IsFormula(int row, int column) => GetRawValue(row, column).TrimStart().StartsWith('=');

    /// <summary>Applies a value and/or formatting edit to one cell.</summary>
    public void SetCell(int row, int column, in CellEdit edit)
    {
        CellModel cell = GetCell(row, column);
        var change = BeginRecord(row, column);
        edit.ApplyTo(cell);
        EndRecord(change, row, column);
        Bump();
    }

    public void SetCell(CellAddress address, in CellEdit edit) => SetCell(address.Row, address.Column, edit);

    /// <summary>Sets the raw value of one cell, replacing any formula with plain text when <paramref name="value"/> does not start with '='.</summary>
    public void SetCellValue(int row, int column, string value)
    {
        if (value is null) value = "";
        CellModel cell = GetCell(row, column);
        if (cell.Value == value) return;
        var change = BeginRecord(row, column);
        cell.Value = value;
        EndRecord(change, row, column);
        Bump();
    }

    /// <summary>Replaces the whole cell object (used by fill/series operations that clone pattern cells).</summary>
    public void ReplaceCell(int row, int column, CellModel cell)
    {
        EnsureSize(row + 1, column + 1);
        var change = BeginRecord(row, column);
        Rows[row][column] = cell;
        EndRecord(change, row, column);
        Bump();
    }

    /// <summary>Clears values across a range, leaving formatting intact (matches "Clear contents").</summary>
    public void ClearRange(CellRange range)
    {
        bool changed = false;
        foreach (int row in range.Rows)
        {
            if (row >= Rows.Count) break;
            foreach (int column in range.Columns)
            {
                if (column >= Rows[row].Count) break;
                if (Rows[row][column].Value.Length == 0) continue;
                var change = BeginRecord(row, column);
                Rows[row][column].Value = "";
                EndRecord(change, row, column);
                changed = true;
            }
        }
        if (changed) Bump();
    }

    /// <summary>Inserts blank rows at <paramref name="index"/> and shifts formula references at or below it down.</summary>
    public void InsertRows(int index, int count = 1)
    {
        if (count <= 0) return;
        if (index < 0 || index > Rows.Count) throw new ArgumentOutOfRangeException(nameof(index), "Insert index must be within [0, RowCount].");
        FormulaReferenceUpdater.InsertRows(this, index, count);
        int width = Math.Max(ColumnCount, 1);
        for (int i = 0; i < count; i++) Rows.Insert(index, Enumerable.Range(0, width).Select(_ => new CellModel()).ToList());
        MarkStructural();
        Bump();
    }

    /// <summary>Inserts blank columns at <paramref name="index"/> into every existing row and shifts references right.</summary>
    public void InsertColumns(int index, int count = 1)
    {
        if (count <= 0) return;
        if (index < 0 || index > ColumnCount) throw new ArgumentOutOfRangeException(nameof(index), "Insert index must be within [0, ColumnCount].");
        FormulaReferenceUpdater.InsertColumns(this, index, count);
        foreach (var row in Rows)
        {
            while (row.Count < index) row.Add(new());
            for (int i = 0; i < count; i++) row.Insert(index, new());
        }
        Bump();
    }

    /// <summary>Deletes the given rows (duplicates ignored) while always keeping at least one row; affected references collapse to #REF! or shrink.</summary>
    public void DeleteRows(IEnumerable<int> indices)
    {
        var targets = indices.Where(i => i >= 0 && i < Rows.Count).Distinct().OrderDescending().ToList();
        if (targets.Count == 0) return;
        FormulaReferenceUpdater.DeleteRows(this, targets);
        foreach (int i in targets) if (Rows.Count > 1) Rows.RemoveAt(i);
        MarkStructural();
        Bump();
    }

    /// <summary>Deletes the given columns from every row that has them, always keeping at least one column per row.</summary>
    public void DeleteColumns(IEnumerable<int> indices)
    {
        var targets = indices.Where(i => i >= 0).Distinct().OrderDescending().ToList();
        if (targets.Count == 0) return;
        FormulaReferenceUpdater.DeleteColumns(this, targets);
        foreach (var row in Rows) foreach (int i in targets) if (row.Count > 1 && i < row.Count) row.RemoveAt(i);
        MarkStructural();
        Bump();
    }

    /// <summary>Swaps two rows (adjacent moves use this too) and rewrites references to follow the data.</summary>
    public void SwapRows(int first, int second)
    {
        if (first == second) return;
        if (first < 0 || second < 0 || first >= Rows.Count || second >= Rows.Count) throw new ArgumentOutOfRangeException(nameof(first), "Row index out of range.");
        FormulaReferenceUpdater.SwapRows(this, first, second);
        (Rows[first], Rows[second]) = (Rows[second], Rows[first]);
        MarkStructural();
        Bump();
    }

    /// <summary>Swaps two columns in every row and rewrites references to follow the data.</summary>
    public void SwapColumns(int first, int second)
    {
        if (first == second) return;
        int columns = ColumnCount;
        if (first < 0 || second < 0 || first >= columns || second >= columns) throw new ArgumentOutOfRangeException(nameof(first), "Column index out of range.");
        FormulaReferenceUpdater.SwapColumns(this, first, second);
        EnsureSize(Rows.Count, Math.Max(first, second) + 1);
        foreach (var row in Rows) (row[first], row[second]) = (row[second], row[first]);
        MarkStructural();
        Bump();
    }

    /// <summary>
    /// Applies a full ordering permutation to the physical rows (used by sorts).
    /// <paramref name="oldIndicesInNewOrder"/> must contain every row index exactly once.
    /// </summary>
    public void ReorderRows(IReadOnlyList<int> oldIndicesInNewOrder)
    {
        if (oldIndicesInNewOrder.Count != Rows.Count) throw new ArgumentException("Row order must include every row.", nameof(oldIndicesInNewOrder));
        if (oldIndicesInNewOrder.Distinct().Count() != Rows.Count) throw new ArgumentException("Row order must not repeat an index.", nameof(oldIndicesInNewOrder));
        if (oldIndicesInNewOrder.Any(i => i < 0 || i >= Rows.Count)) throw new ArgumentOutOfRangeException(nameof(oldIndicesInNewOrder), "Row order index out of range.");
        if (oldIndicesInNewOrder.SequenceEqual(Enumerable.Range(0, Rows.Count))) return;
        var original = Rows.ToList();
        var oldToNew = oldIndicesInNewOrder.Select((oldIndex, newIndex) => (oldIndex, newIndex)).ToDictionary(item => item.oldIndex, item => item.newIndex);
        FormulaReferenceUpdater.RemapRows(this, oldToNew);
        Rows.Clear();
        foreach (int oldIndex in oldIndicesInNewOrder) Rows.Add(original[oldIndex]);
        MarkStructural();
        Bump();
    }
}
