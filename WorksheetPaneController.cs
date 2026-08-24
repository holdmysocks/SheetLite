namespace SheetLite;

/// <summary>
/// Owns the virtual-mode plumbing for one grid pane: value/edit/format callbacks,
/// sheet rendering, and the pane's display↔model <see cref="WorksheetView"/> map.
/// Filtering populates the view instead of toggling row-visible flags, so a filtered
/// pane simply displays fewer rows while every command keeps working in model
/// coordinates through <see cref="ModelRow"/>/<see cref="DisplayRow"/>.
/// </summary>
internal sealed class WorksheetPaneController
{
    public readonly DataGridView Grid;
    public readonly SheetModelDataSource Source;
    public readonly WorksheetView View = WorksheetView.Identity(100, 26);
    public Font RegularFont { get; set; } = SystemFonts.MessageBoxFont;
    public Font BoldFont { get; set; } = SystemFonts.MessageBoxFont;

    private CellAddress? editAddress;
    private readonly Func<bool>? mayFlush;
    private readonly ContextMenuStrip? columnHeaderMenu, rowHeaderMenu;

    /// <summary>Raised with model coordinates after a committed cell edit (CellValuePushed).</summary>
    public event Action<int, int>? CellCommitted;
    /// <summary>
    /// Called when an editor opens; return false to refuse the edit. Receives model coordinates.
    /// The host uses this for undo checkpoints and to block edits while a render is in flight.
    /// </summary>
    public Func<int, int, bool>? EditStarting;
    /// <summary>Raised when an editor closes, whether committed or cancelled.</summary>
    public event Action<int, int>? EditFinished;

    public WorksheetPaneController(DataGridView grid, SheetModelDataSource source,
        ContextMenuStrip? columnHeaderMenu = null, ContextMenuStrip? rowHeaderMenu = null,
        Func<bool>? mayFlush = null)
    {
        Grid = grid; Source = source; this.columnHeaderMenu = columnHeaderMenu; this.rowHeaderMenu = rowHeaderMenu; this.mayFlush = mayFlush;
        grid.VirtualMode = true;
        grid.CellValueNeeded += OnValueNeeded;
        grid.CellValuePushed += OnValuePushed;
        grid.CellFormatting += OnCellFormatting;
        grid.CellBeginEdit += OnBeginEdit;
        grid.CellEndEdit += OnEndEdit;
    }

    public SheetModel Model => Source.Sheet;

    // ----- coordinate mapping -----

    /// <summary>Model row displayed at <paramref name="displayRow"/> (grid coordinates are always display coordinates).</summary>
    public int ModelRow(int displayRow) => View.ModelRowForDisplayRow(displayRow);
    public int DisplayRow(int modelRow) => View.DisplayRowForModelRow(modelRow);
    public bool IsRowVisible(int modelRow) => View.IsRowVisible(modelRow);

    // ----- rendering -----

    /// <summary>Rebuilds columns for the sheet and sizes the grid from the view map. O(columns); no per-cell UI objects.</summary>
    public void RenderSheet(SheetModel sheet)
    {
        FlushPendingEdits();
        int columns = Math.Max(26, sheet.ColumnCount), rows = Math.Max(100, sheet.RowCount);
        sheet.EnsureSize(rows, columns);
        View.Reset(rows, columns);
        Grid.Columns.Clear();
        for (int c = 0; c < columns; c++)
        {
            var column = new DataGridViewTextBoxColumn { Name = CellAddress.ColumnName(c), HeaderText = CellAddress.ColumnName(c), SortMode = DataGridViewColumnSortMode.NotSortable, Width = 110 };
            if (columnHeaderMenu is not null) column.HeaderCell.ContextMenuStrip = columnHeaderMenu;
            Grid.Columns.Add(column);
        }
        if (rowHeaderMenu is not null) Grid.RowTemplate.HeaderCell.ContextMenuStrip = rowHeaderMenu;
        Grid.RowCount = View.DisplayRowCount;
        Grid.Invalidate();
    }

    /// <summary>Applies the view map: the pane shows exactly <see cref="WorksheetView.DisplayRowCount"/> rows.</summary>
    public void ApplyView()
    {
        if (Grid.Columns.Count == 0) return;
        if (Grid.RowCount != View.DisplayRowCount) Grid.RowCount = View.DisplayRowCount;
        else Grid.Invalidate();
    }

    /// <summary>Resets an unfiltered identity view over the current sheet dimensions.</summary>
    public void ResetViewToSheet()
    {
        var sheet = Model;
        View.Reset(Math.Max(100, sheet.RowCount), Math.Max(26, sheet.ColumnCount));
        ApplyView();
    }

    public void InvalidateCell(int displayColumn, int displayRow) => Grid.InvalidateCell(displayColumn, displayRow);

    /// <summary>Commits an open editor before a command snapshots or renders state. No-op when the host blocks it.</summary>
    public void FlushPendingEdits()
    {
        if (mayFlush is null || mayFlush()) Grid.EndEdit();
    }

    // ----- virtual-mode events -----

    private void OnValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        var sheet = Model;
        if (e.RowIndex < 0 || e.RowIndex >= View.DisplayRowCount || e.ColumnIndex < 0) { e.Value = ""; return; }
        int row = View.ModelRowForDisplayRow(e.RowIndex);
        if (row >= sheet.RowCount || e.ColumnIndex >= sheet.Rows.Count || e.ColumnIndex >= sheet.Rows[row].Count) { e.Value = ""; return; }
        var address = new CellAddress(row, e.ColumnIndex);
        // While a cell is being edited the editor must show the raw source (formula text), not the evaluated result.
        e.Value = editAddress is { } editing && editing == address ? sheet.GetRawValue(address.Row, address.Column) : Source.GetEvaluatedText(address);
    }

    private void OnValuePushed(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= View.DisplayRowCount || e.ColumnIndex < 0) return;
        int row = View.ModelRowForDisplayRow(e.RowIndex);
        Source.SetCell(new CellAddress(row, e.ColumnIndex), CellEdit.SetValue(e.Value?.ToString() ?? ""));
        CellCommitted?.Invoke(row, e.ColumnIndex);
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        var sheet = Model;
        if (e.RowIndex < 0 || e.RowIndex >= View.DisplayRowCount || e.ColumnIndex < 0) return;
        int row = View.ModelRowForDisplayRow(e.RowIndex);
        if (row >= sheet.RowCount || e.ColumnIndex >= sheet.Rows.Count || e.ColumnIndex >= sheet.Rows[row].Count) return;
        CellDisplayValue display = Source.GetDisplayValue(new(row, e.ColumnIndex));
        e.CellStyle!.BackColor = display.BackColor; e.CellStyle.ForeColor = display.ForeColor; e.CellStyle.Font = display.Bold ? BoldFont : RegularFont;
    }

    private void OnBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= View.DisplayRowCount || e.ColumnIndex < 0) return;
        var address = new CellAddress(View.ModelRowForDisplayRow(e.RowIndex), e.ColumnIndex);
        if (EditStarting is null || !EditStarting(address.Row, address.Column)) { e.Cancel = EditStarting is not null; return; }
        editAddress = address;
    }

    private void OnEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (editAddress is { } address) EditFinished?.Invoke(address.Row, address.Column);
        editAddress = null;
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0) Grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
        Grid.Invalidate();
    }
}
