namespace SheetLite;

/// <summary>
/// Owns the virtual-mode plumbing for one grid pane: value/edit/format callbacks,
/// sheet rendering, and the pane's display↔model <see cref="WorksheetView"/> map.
/// Filtering populates the view instead of toggling row-visible flags, so a filtered
/// pane simply displays fewer rows while every command keeps working in model
/// coordinates through <see cref="ModelRow"/>/<see cref="DisplayRow"/>.
/// </summary>
internal sealed class WorksheetPaneController : IDisposable
{
    private const int TargetedInvalidationThreshold = 512;
    public readonly DataGridView Grid;
    public readonly SheetModelDataSource Source;
    public readonly WorksheetView View = WorksheetView.Identity(100, 26);
    private Font regularFont = SystemFonts.MessageBoxFont!;
    private Font boldFont = SystemFonts.MessageBoxFont!;
    private readonly Dictionary<(float Size, FontStyle Style), Font> cellFonts = [];
    public Font RegularFont { get => regularFont; set { regularFont = value; ClearFontCache(); } }
    public Font BoldFont { get => boldFont; set { boldFont = value; ClearFontCache(); } }

    private CellAddress? editAddress;
    private readonly Func<bool>? mayFlush;
    private readonly ContextMenuStrip? columnHeaderMenu, rowHeaderMenu;
    private bool disposed;

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
        source.Changed += OnSourceChanged;
        grid.Disposed += OnGridDisposed;
    }

    public SheetModel Model => Source.Sheet;

    // ----- coordinate mapping -----

    /// <summary>Model row displayed at <paramref name="displayRow"/> (grid coordinates are always display coordinates).</summary>
    public int ModelRow(int displayRow) => View.ModelRowForDisplayRow(displayRow);
    public int DisplayRow(int modelRow) => View.DisplayRowForModelRow(modelRow);
    public bool IsRowVisible(int modelRow) => View.IsRowVisible(modelRow);

    internal IReadOnlyList<(int Column, int Row)> MapChangedCells(WorksheetChangeSet changes)
    {
        if (changes.RequiresFullRefresh) return [];
        var cells = new List<(int Column, int Row)>();
        foreach (CellAddress address in changes.ChangedAddresses)
        {
            int row = View.DisplayRowForModelRow(address.Row);
            int column = View.DisplayColumnForModelColumn(address.Column);
            if (row < 0 || column < 0 || row >= Grid.RowCount || column >= Grid.ColumnCount) continue;
            if (!Grid.Columns[column].Visible || !Grid.Rows[row].Visible) continue;
            cells.Add((column, row));
        }
        return cells;
    }

    // ----- rendering -----

    /// <summary>Rebuilds columns for the sheet and sizes the grid from the view map. O(columns); no per-cell UI objects.</summary>
    public void RenderSheet(SheetModel sheet)
    {
        FlushPendingEdits();
        Source.RefreshBinding();
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
        RefreshRowHeights(Enumerable.Range(0, View.DisplayRowCount).Where(displayRow =>
        {
            int modelRow = View.ModelRowForDisplayRow(displayRow);
            return modelRow < sheet.Rows.Count && sheet.Rows[modelRow].Any(cell => cell.FontSize is not null);
        }));
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

    public void InvalidateCell(int displayColumn, int displayRow)
    {
        // Virtual cells cache their formatted style separately from their painted pixels.
        // UpdateCellValue forces CellFormatting to run again before the repaint so a style-only
        // edit (especially switching vertical alignment) cannot display the previous style.
        Grid.UpdateCellValue(displayColumn, displayRow);
        Grid.InvalidateCell(displayColumn, displayRow);
    }

    public void RefreshRowHeights(IEnumerable<int> displayRows)
    {
        foreach (int displayRow in displayRows.Distinct())
        {
            if (displayRow < 0 || displayRow >= View.DisplayRowCount || displayRow >= Grid.RowCount) continue;
            int modelRow = View.ModelRowForDisplayRow(displayRow);
            float largestSize = modelRow < Model.Rows.Count && Model.Rows[modelRow].Count > 0
                ? Model.Rows[modelRow].Max(cell => cell.FontSize ?? CellModel.DefaultFontSize)
                : CellModel.DefaultFontSize;
            Grid.Rows[displayRow].Height = RowHeightFor(largestSize);
        }
    }

    private int RowHeightFor(float fontSize) => Math.Max(Grid.RowTemplate.Height, (int)Math.Ceiling(fontSize * Grid.DeviceDpi / 72F) + 7);

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
        int row = View.ModelRowForDisplayRow(e.RowIndex), column = View.ModelColumnForDisplayColumn(e.ColumnIndex);
        if (row >= sheet.RowCount || column >= sheet.Rows[row].Count) { e.Value = ""; return; }
        var address = new CellAddress(row, column);
        // While a cell is being edited the editor must show the raw source (formula text), not the evaluated result.
        e.Value = editAddress is { } editing && editing == address ? sheet.GetRawValue(address.Row, address.Column) : Source.GetEvaluatedText(address);
    }

    private void OnValuePushed(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= View.DisplayRowCount || e.ColumnIndex < 0) return;
        int row = View.ModelRowForDisplayRow(e.RowIndex), column = View.ModelColumnForDisplayColumn(e.ColumnIndex);
        Source.SetCell(new CellAddress(row, column), CellEdit.SetValue(e.Value?.ToString() ?? ""));
        CellCommitted?.Invoke(row, column);
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        var sheet = Model;
        if (e.RowIndex < 0 || e.RowIndex >= View.DisplayRowCount || e.ColumnIndex < 0) return;
        int row = View.ModelRowForDisplayRow(e.RowIndex), column = View.ModelColumnForDisplayColumn(e.ColumnIndex);
        if (row >= sheet.RowCount || column >= sheet.Rows[row].Count) return;
        CellModel cell = sheet.Rows[row][column];
        CellDisplayValue display = Source.GetDisplayValue(new(row, column));
        e.CellStyle!.BackColor = display.BackColor; e.CellStyle.ForeColor = display.ForeColor;
        e.CellStyle.SelectionForeColor = cell.ForeColor ?? Theme.AdaptiveCellText(e.CellStyle.SelectionBackColor);
        e.CellStyle.Font = FontFor(display);
        e.CellStyle.Alignment = AlignmentFor(display.HorizontalAlignment, display.VerticalAlignment);
    }

    private Font FontFor(CellDisplayValue display)
    {
        FontStyle style = FontStyle.Regular;
        if (display.Bold) style |= FontStyle.Bold;
        if (display.Italic) style |= FontStyle.Italic;
        if (display.Underline) style |= FontStyle.Underline;
        if (Math.Abs(display.FontSize - RegularFont.Size) < 0.01F)
        {
            if (style == FontStyle.Regular) return RegularFont;
            if (style == FontStyle.Bold) return BoldFont;
        }
        var key = (display.FontSize, style);
        if (!cellFonts.TryGetValue(key, out Font? font))
        {
            font = new Font(RegularFont.FontFamily, display.FontSize, style, GraphicsUnit.Point);
            cellFonts[key] = font;
        }
        return font;
    }

    private static DataGridViewContentAlignment AlignmentFor(CellHorizontalAlignment horizontal, CellVerticalAlignment vertical) =>
        (horizontal, vertical) switch
        {
            (CellHorizontalAlignment.Left, CellVerticalAlignment.Top) => DataGridViewContentAlignment.TopLeft,
            (CellHorizontalAlignment.Center, CellVerticalAlignment.Top) => DataGridViewContentAlignment.TopCenter,
            (CellHorizontalAlignment.Right, CellVerticalAlignment.Top) => DataGridViewContentAlignment.TopRight,
            (CellHorizontalAlignment.Left, CellVerticalAlignment.Middle) => DataGridViewContentAlignment.MiddleLeft,
            (CellHorizontalAlignment.Center, CellVerticalAlignment.Middle) => DataGridViewContentAlignment.MiddleCenter,
            (CellHorizontalAlignment.Right, CellVerticalAlignment.Middle) => DataGridViewContentAlignment.MiddleRight,
            (CellHorizontalAlignment.Left, CellVerticalAlignment.Bottom) => DataGridViewContentAlignment.BottomLeft,
            (CellHorizontalAlignment.Center, CellVerticalAlignment.Bottom) => DataGridViewContentAlignment.BottomCenter,
            _ => DataGridViewContentAlignment.BottomRight
        };

    private void ClearFontCache()
    {
        foreach (Font font in cellFonts.Values) font.Dispose();
        cellFonts.Clear();
    }

    private void OnBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= View.DisplayRowCount || e.ColumnIndex < 0) return;
        var address = new CellAddress(View.ModelRowForDisplayRow(e.RowIndex), View.ModelColumnForDisplayColumn(e.ColumnIndex));
        if (EditStarting is null || !EditStarting(address.Row, address.Column)) { e.Cancel = EditStarting is not null; return; }
        editAddress = address;
    }

    private void OnEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (editAddress is { } address) EditFinished?.Invoke(address.Row, address.Column);
        editAddress = null;
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0) Grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
    }

    private void OnSourceChanged(object? sender, WorksheetChangeSet changes)
    {
        if (disposed || Grid.IsDisposed) return;
        if (changes.RequiresFullRefresh) { Grid.Invalidate(); return; }
        IReadOnlyList<(int Column, int Row)> cells = MapChangedCells(changes);
        if (cells.Count > TargetedInvalidationThreshold) { Grid.Invalidate(); return; }
        foreach (var (column, row) in cells) InvalidateCell(column, row);
    }

    private void OnGridDisposed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Source.Changed -= OnSourceChanged;
        Grid.Disposed -= OnGridDisposed;
        ClearFontCache();
        Source.Dispose();
    }
}
