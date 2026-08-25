namespace SheetLite;

/// <summary>
/// Grid-independent read/write view of one worksheet. A grid pane (primary, secondary,
/// future virtual-mode host) binds to this instead of holding a second copy of state.
/// </summary>
internal interface IWorksheetDataSource
{
    event EventHandler<WorksheetChangeSet>? Changed;
    int RowCount { get; }
    int ColumnCount { get; }
    /// <summary>Bumped on every mutation so panes can invalidate only what changed.</summary>
    int Version { get; }
    CellModel GetCell(CellAddress address);
    /// <summary>Evaluated display text: formulas are calculated once per data version.</summary>
    string GetEvaluatedText(CellAddress address);
    CellDisplayValue GetDisplayValue(CellAddress address);
    void SetCell(CellAddress address, in CellEdit edit);
}

/// <summary>An <see cref="IWorksheetDataSource"/> backed by a live <see cref="SheetModel"/>.
/// The provider indirection lets panes survive undo/redo and worksheet switches, which replace
/// the underlying <see cref="SheetModel"/> instance.</summary>
internal sealed class SheetModelDataSource : IWorksheetDataSource, IDisposable
{
    private readonly Func<SheetModel> sheetProvider;
    private int cachedVersion = -1;
    private FormulaEngine.FormulaEvaluationContext? evaluationContext;
    private SheetModel? cachedSheet;
    private bool disposed;

    public SheetModelDataSource(Func<SheetModel> sheetProvider)
    {
        this.sheetProvider = sheetProvider ?? throw new ArgumentNullException(nameof(sheetProvider));
        RefreshBinding();
    }

    public event EventHandler<WorksheetChangeSet>? Changed;

    public SheetModel Sheet => RefreshBinding();
    public int RowCount => Sheet.RowCount;
    public int ColumnCount => Sheet.ColumnCount;
    public int Version => Sheet.Version;

    public CellModel GetCell(CellAddress address) => Sheet.GetCell(address);

    public string GetEvaluatedText(CellAddress address)
    {
        SheetModel sheet = Sheet;
        return sheet.EvaluatedValue(address.Row, address.Column, ContextFor(sheet));
    }

    public CellDisplayValue GetDisplayValue(CellAddress address)
    {
        CellModel cell = Sheet.GetCell(address);
        Color background = cell.BackColor ?? Theme.CellBackground;
        Color foreground = cell.ForeColor ?? Theme.AdaptiveCellText(background);
        return new(GetEvaluatedText(address), background, foreground, cell.FontSize ?? CellModel.DefaultFontSize,
            cell.Bold, cell.Italic, cell.Underline, cell.HorizontalAlignment ?? CellHorizontalAlignment.Left,
            cell.VerticalAlignment ?? CellVerticalAlignment.Middle);
    }

    public void SetCell(CellAddress address, in CellEdit edit) => Sheet.SetCell(address, edit);

    /// <summary>
    /// Rebinds change forwarding after the provider starts returning another sheet. Calling this is
    /// harmless when the sheet has not changed and ensures the previous model cannot publish ghost updates.
    /// </summary>
    public SheetModel RefreshBinding()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SheetModel sheet = sheetProvider() ?? throw new InvalidOperationException("The worksheet provider returned null.");
        if (ReferenceEquals(cachedSheet, sheet)) return sheet;
        if (cachedSheet is not null) cachedSheet.Changed -= OnSheetChanged;
        cachedSheet = sheet;
        cachedVersion = -1;
        evaluationContext = null;
        sheet.Changed += OnSheetChanged;
        return sheet;
    }

    private void OnSheetChanged(object? sender, WorksheetChangeSet changes)
    {
        // A provider may be switched before its host has rendered the new model. Suppress a late
        // notification from the old model and move the subscription as soon as either model speaks.
        SheetModel current = sheetProvider();
        if (!ReferenceEquals(sender, current)) { RefreshBinding(); return; }
        Changed?.Invoke(this, changes);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (cachedSheet is not null) cachedSheet.Changed -= OnSheetChanged;
        cachedSheet = null;
        evaluationContext = null;
        Changed = null;
    }

    /// <summary>Memoized evaluation context, valid for one data version (one recalculation batch).</summary>
    public FormulaEngine.FormulaEvaluationContext ContextFor(SheetModel sheet)
    {
        if (evaluationContext is null || cachedSheet != sheet || cachedVersion != sheet.Version) { evaluationContext = FormulaEngine.CreateContext(sheet); cachedSheet = sheet; cachedVersion = sheet.Version; }
        return evaluationContext;
    }
}

/// <summary>
/// Pane-specific mapping between display order and physical worksheet order.
/// Filtering hides rows here instead of setting thousands of row-visible flags;
/// sort previews permute the map without mutating worksheet storage. The physical
/// sheet is never reordered through this type.
/// </summary>
internal sealed class WorksheetView
{
    private List<int> visibleRows = [];
    private List<int> visibleColumns = [];
    private Dictionary<int, int> displayRowForModelRow = [];

    public IReadOnlyList<int> VisibleRows => visibleRows;
    public IReadOnlyList<int> VisibleColumns => visibleColumns;
    public int DisplayRowCount => visibleRows.Count;
    public int DisplayColumnCount => visibleColumns.Count;

    public static WorksheetView Identity(int rowCount, int columnCount)
    {
        var view = new WorksheetView();
        view.Reset(rowCount, columnCount);
        return view;
    }

    /// <summary>Rebuilds an identity mapping for the given logical dimensions, clearing any filter or order.</summary>
    public void Reset(int rowCount, int columnCount)
    {
        if (rowCount < 0) throw new ArgumentOutOfRangeException(nameof(rowCount));
        if (columnCount < 0) throw new ArgumentOutOfRangeException(nameof(columnCount));
        visibleRows = Enumerable.Range(0, rowCount).ToList();
        visibleColumns = Enumerable.Range(0, columnCount).ToList();
        RebuildRowIndex();
    }

    /// <summary>Hides every model row for which <paramref name="predicate"/> returns true.</summary>
    public void HideRows(Func<int, bool> predicate)
    {
        visibleRows.RemoveAll(new Predicate<int>(predicate));
        RebuildRowIndex();
    }

    /// <summary>
    /// Reorders displayed rows. <paramref name="modelRowsInDisplayOrder"/> must be a permutation of the
    /// currently visible model rows; hidden rows stay hidden and storage is untouched.
    /// </summary>
    public void SetRowOrder(IReadOnlyList<int> modelRowsInDisplayOrder)
    {
        if (modelRowsInDisplayOrder.Count != visibleRows.Count) throw new ArgumentException("Order must cover exactly the currently visible rows.", nameof(modelRowsInDisplayOrder));
        var kept = visibleRows.ToHashSet();
        if (modelRowsInDisplayOrder.Distinct().Count() != modelRowsInDisplayOrder.Count || modelRowsInDisplayOrder.Any(row => !kept.Contains(row)))
            throw new ArgumentException("Order must be a permutation of the currently visible rows.", nameof(modelRowsInDisplayOrder));
        visibleRows = [.. modelRowsInDisplayOrder];
        RebuildRowIndex();
    }

    public int ModelRowForDisplayRow(int displayRow)
    {
        if (displayRow < 0 || displayRow >= visibleRows.Count) throw new ArgumentOutOfRangeException(nameof(displayRow));
        return visibleRows[displayRow];
    }

    public int ModelColumnForDisplayColumn(int displayColumn)
    {
        if (displayColumn < 0 || displayColumn >= visibleColumns.Count) throw new ArgumentOutOfRangeException(nameof(displayColumn));
        return visibleColumns[displayColumn];
    }

    /// <summary>Display position of a model row, or -1 when filtered out.</summary>
    public int DisplayRowForModelRow(int modelRow) => displayRowForModelRow.GetValueOrDefault(modelRow, -1);

    public int DisplayColumnForModelColumn(int modelColumn) => visibleColumns.IndexOf(modelColumn);

    public bool IsRowVisible(int modelRow) => DisplayRowForModelRow(modelRow) >= 0;

    private void RebuildRowIndex()
    {
        displayRowForModelRow = new Dictionary<int, int>(visibleRows.Count);
        for (int display = 0; display < visibleRows.Count; display++) displayRowForModelRow[visibleRows[display]] = display;
    }
}
