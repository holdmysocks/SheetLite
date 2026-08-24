namespace SheetLite;

/// <summary>
/// One published worksheet mutation. Addresses are model coordinates and include both
/// directly edited cells and formula cells whose evaluated values changed.
/// </summary>
internal sealed class WorksheetChangeSet : EventArgs
{
    public WorksheetChangeSet(IEnumerable<CellAddress> changedAddresses, bool structureChanged, bool requiresFullRefresh = false)
    {
        ChangedAddresses = changedAddresses.Distinct().Order().ToArray();
        StructureChanged = structureChanged;
        RequiresFullRefresh = structureChanged || requiresFullRefresh;
    }

    public IReadOnlyCollection<CellAddress> ChangedAddresses { get; }
    public bool StructureChanged { get; }
    public bool RequiresFullRefresh { get; }
}

/// <summary>Proof that one complete, model-owned mutation moved between adjacent versions.</summary>
internal readonly record struct WorksheetMutationToken(int PreviousVersion, int CurrentVersion);

internal sealed partial class SheetModel
{
    private readonly HashSet<CellAddress> pendingChangedAddresses = [];
    private readonly HashSet<FormulaCellAddress> pendingRawValueAddresses = [];
    private int updateDepth;
    private bool pendingMutation;
    private bool pendingStructureChanged;

    /// <summary>Raised once per mutation, or once when the outermost update batch completes.</summary>
    public event EventHandler<WorksheetChangeSet>? Changed;

    /// <summary>
    /// Defers versioning, dependency recalculation, and event publication until the outermost
    /// scope is disposed. Scopes are nestable; callers should use them around paste/fill/replace loops.
    /// </summary>
    public IDisposable BeginUpdate()
    {
        updateDepth++;
        return new UpdateScope(this);
    }

    private sealed class UpdateScope(SheetModel sheet) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            sheet.EndUpdate();
        }
    }

    private void EndUpdate()
    {
        if (updateDepth <= 0) throw new InvalidOperationException("Update scopes were disposed out of balance.");
        updateDepth--;
        if (updateDepth == 0) FlushPendingChanges();
    }

    private void CommitCellMutation(CellAddress address, bool rawValueChanged)
    {
        pendingMutation = true;
        pendingChangedAddresses.Add(address);
        if (rawValueChanged) pendingRawValueAddresses.Add(new FormulaCellAddress(address.Row, address.Column));
        if (updateDepth == 0) FlushPendingChanges();
    }

    private void CommitCellMutations(IEnumerable<CellAddress> addresses, bool rawValuesChanged)
    {
        bool any = false;
        foreach (var address in addresses)
        {
            any = true;
            pendingChangedAddresses.Add(address);
            if (rawValuesChanged) pendingRawValueAddresses.Add(new FormulaCellAddress(address.Row, address.Column));
        }
        if (!any) return;
        pendingMutation = true;
        if (updateDepth == 0) FlushPendingChanges();
    }

    private void CommitStructureMutation()
    {
        pendingMutation = true;
        pendingStructureChanged = true;
        if (updateDepth == 0) FlushPendingChanges();
    }

    private void FlushPendingChanges()
    {
        if (!pendingMutation) return;

        int previousVersion = Version;
        Version++;
        var token = new WorksheetMutationToken(previousVersion, Version);
        FormulaChangeSet formulaChanges;
        if (pendingStructureChanged)
        {
            formulaChanges = FormulaEngine.NotifyStructureChanged(this);
        }
        else if (pendingRawValueAddresses.Count > 0)
        {
            formulaChanges = FormulaEngine.NotifyCellsChanged(this,
                pendingRawValueAddresses.Select(address => new FormulaCellUpdate(address, GetRawValue(address.Row, address.Column))),
                token);
        }
        else
        {
            formulaChanges = FormulaEngine.NotifyVersionChanged(this, token);
        }

        foreach (var address in formulaChanges.ChangedAddresses)
            pendingChangedAddresses.Add(new CellAddress(address.Row, address.Column));

        var changeSet = new WorksheetChangeSet(
            pendingChangedAddresses,
            pendingStructureChanged,
            formulaChanges.RequiresFullRefresh);
        pendingChangedAddresses.Clear();
        pendingRawValueAddresses.Clear();
        pendingMutation = false;
        pendingStructureChanged = false;
        Changed?.Invoke(this, changeSet);
    }
}
