namespace SheetLite;

internal sealed class PaneDocumentSession(WorkbookModel workbook, string? path = null, bool dirty = false)
{
    public WorkbookModel Workbook { get; set; } = workbook;
    public string? Path { get; set; } = path;
    public bool Dirty { get; set; } = dirty;
    public Stack<WorkbookModel> Undo { get; set; } = new();
    public Stack<WorkbookModel> Redo { get; set; } = new();
    public string Title => Path is null ? "Untitled" : System.IO.Path.GetFileName(Path);
}

internal sealed partial class MainForm
{
    private readonly List<PaneDocumentSession> primaryDocuments = [], secondaryDocuments = [];
    private int primaryDocumentIndex, secondaryDocumentIndex;

    private void InitializeDocumentSessions()
    {
        primaryDocuments.Clear(); primaryDocuments.Add(new(workbook, path, dirty) { Undo = undo, Redo = redo }); primaryDocumentIndex = 0;
    }

    private void CapturePrimaryDocument()
    {
        if (primaryDocuments.Count == 0) return; var session = primaryDocuments[Math.Clamp(primaryDocumentIndex, 0, primaryDocuments.Count - 1)];
        session.Workbook = workbook; session.Path = path; session.Dirty = dirty; session.Undo = undo; session.Redo = redo;
    }

    private void ReplaceActivePrimaryDocument()
    {
        var session = new PaneDocumentSession(workbook, path, dirty) { Undo = undo, Redo = redo };
        if (primaryDocuments.Count == 0) { primaryDocuments.Add(session); primaryDocumentIndex = 0; }
        else primaryDocuments[Math.Clamp(primaryDocumentIndex, 0, primaryDocuments.Count - 1)] = session;
        RefreshDocumentTabs();
    }

    private void CaptureSecondaryDocument()
    {
        if (secondarySharesPrimary || secondaryDocuments.Count == 0 || secondaryWorkbook is null) return; var session = secondaryDocuments[Math.Clamp(secondaryDocumentIndex, 0, secondaryDocuments.Count - 1)];
        session.Workbook = secondaryWorkbook; session.Path = secondaryPath; session.Dirty = secondaryDirty;
    }

    private PaneDocumentSession? ActiveSecondarySession => secondarySharesPrimary || secondaryDocuments.Count == 0
        ? null
        : secondaryDocuments[Math.Clamp(secondaryDocumentIndex, 0, secondaryDocuments.Count - 1)];

    private void PushSecondaryUndo()
    {
        if (secondaryWorkbook is null || secondaryModel is null) return;
        SyncSecondaryAll(); secondaryWorkbook.ActiveSheet.Sheet = secondaryModel;
        PaneDocumentSession? session = ActiveSecondarySession; if (session is null) return;
        session.Undo.Push(secondaryWorkbook.Clone());
        if (session.Undo.Count > 40) { var keep = session.Undo.Take(40).Reverse().ToArray(); session.Undo.Clear(); foreach (var snapshot in keep) session.Undo.Push(snapshot); }
        session.Redo.Clear();
    }

    private void SecondaryUndo()
    {
        if (secondarySharesPrimary) { Undo(); return; }
        PaneDocumentSession? session = ActiveSecondarySession;
        if (session is null || session.Undo.Count == 0 || secondaryWorkbook is null || secondaryModel is null) return;
        SyncSecondaryAll(); secondaryWorkbook.ActiveSheet.Sheet = secondaryModel; session.Redo.Push(secondaryWorkbook.Clone());
        secondaryWorkbook = session.Undo.Pop(); secondaryModel = secondaryWorkbook.ActiveSheet.Sheet; secondaryDirty = session.Dirty = true; session.Workbook = secondaryWorkbook;
        RefreshSecondarySheetTabs(); RenderSecondaryModel(); RefreshDocumentTabs(); UpdateSecondaryTitle(); UpdateStatus();
    }

    private void SecondaryRedo()
    {
        if (secondarySharesPrimary) { Redo(); return; }
        PaneDocumentSession? session = ActiveSecondarySession;
        if (session is null || session.Redo.Count == 0 || secondaryWorkbook is null || secondaryModel is null) return;
        SyncSecondaryAll(); secondaryWorkbook.ActiveSheet.Sheet = secondaryModel; session.Undo.Push(secondaryWorkbook.Clone());
        secondaryWorkbook = session.Redo.Pop(); secondaryModel = secondaryWorkbook.ActiveSheet.Sheet; secondaryDirty = session.Dirty = true; session.Workbook = secondaryWorkbook;
        RefreshSecondarySheetTabs(); RenderSecondaryModel(); RefreshDocumentTabs(); UpdateSecondaryTitle(); UpdateStatus();
    }

    private void ReplaceActiveSecondaryDocument()
    {
        if (secondaryWorkbook is null) return; var session = new PaneDocumentSession(secondaryWorkbook, secondaryPath, secondaryDirty);
        if (secondaryDocuments.Count == 0) { secondaryDocuments.Add(session); secondaryDocumentIndex = 0; }
        else secondaryDocuments[Math.Clamp(secondaryDocumentIndex, 0, secondaryDocuments.Count - 1)] = session;
        RefreshDocumentTabs();
    }

    private void ActivatePrimaryDocument(int index)
    {
        if (index < 0 || index >= primaryDocuments.Count) return; if (index == primaryDocumentIndex) { SetActivePane(false); return; }
        ResolveSortPreviewBeforePrimaryDocumentChange();
        SyncAll(); CapturePrimaryDocument(); primaryDocumentIndex = index; PaneDocumentSession session = primaryDocuments[index];
        workbook = session.Workbook; model = workbook.ActiveSheet.Sheet; path = session.Path; dirty = session.Dirty; undo = session.Undo; redo = session.Redo; filter = null;
        RefreshPrimarySheetTabs(); Render(); RefreshDocumentTabs(); UpdateTitle(); SetActivePane(false);
    }

    private void ApplyPrimaryDocumentSession(int index)
    {
        ResolveSortPreviewBeforePrimaryDocumentChange();
        primaryDocumentIndex = Math.Clamp(index, 0, primaryDocuments.Count - 1); PaneDocumentSession session = primaryDocuments[primaryDocumentIndex];
        workbook = session.Workbook; model = workbook.ActiveSheet.Sheet; path = session.Path; dirty = session.Dirty; undo = session.Undo; redo = session.Redo; filter = null;
    }

    private void ApplySecondaryDocumentSession(int index)
    {
        secondaryDocumentIndex = Math.Clamp(index, 0, secondaryDocuments.Count - 1); PaneDocumentSession session = secondaryDocuments[secondaryDocumentIndex];
        secondaryWorkbook = session.Workbook; secondaryModel = secondaryWorkbook.ActiveSheet.Sheet; secondaryPath = session.Path; secondaryDirty = session.Dirty;
    }

    private void ActivateSecondaryDocument(int index)
    {
        if (secondarySharesPrimary) { SetActivePane(true); return; }
        if (index < 0 || index >= secondaryDocuments.Count) return; if (index == secondaryDocumentIndex) { SetActivePane(true); return; }
        SyncSecondaryAll(); CaptureSecondaryDocument(); secondaryDocumentIndex = index; PaneDocumentSession session = secondaryDocuments[index];
        secondaryWorkbook = session.Workbook; secondaryModel = secondaryWorkbook.ActiveSheet.Sheet; secondaryPath = session.Path; secondaryDirty = session.Dirty;
        RefreshSecondarySheetTabs(); RenderSecondaryModel(); RefreshDocumentTabs(); UpdateSecondaryTitle(); SetActivePane(true);
    }

    private void RefreshDocumentTabs()
    {
        if (primaryDocumentTabs.IsDisposed || secondaryDocumentTabs.IsDisposed) return;
        primaryDocumentTabs.SuspendLayout(); primaryDocumentTabs.Controls.Clear();
        for (int index = 0; index < primaryDocuments.Count; index++)
        {
            PaneDocumentSession session = primaryDocuments[index]; var tab = BuildDocumentSessionTab(session.Title, session.Dirty, index == primaryDocumentIndex, primary: true, index); primaryDocumentTabs.Controls.Add(tab); if (index == primaryDocumentIndex) primaryDocumentTab = tab;
        }
        primaryDocumentTabs.ResumeLayout();

        secondaryDocumentTabs.SuspendLayout(); secondaryDocumentTabs.Controls.Clear();
        if (secondarySharesPrimary && !splitView.Panel2Collapsed)
        {
            var tab = BuildDocumentSessionTab(path is null ? "Untitled" : Path.GetFileName(path), dirty, true, primary: false, 0, shared: true); secondaryDocumentTabs.Controls.Add(tab); secondaryDocumentTab = tab;
        }
        else
        {
            for (int index = 0; index < secondaryDocuments.Count; index++)
            {
                PaneDocumentSession session = secondaryDocuments[index]; var tab = BuildDocumentSessionTab(session.Title, session.Dirty, index == secondaryDocumentIndex, primary: false, index); secondaryDocumentTabs.Controls.Add(tab); if (index == secondaryDocumentIndex) secondaryDocumentTab = tab;
            }
        }
        secondaryDocumentTabs.ResumeLayout(); UpdateFileTabChrome();
    }

    private DocumentTab BuildDocumentSessionTab(string title, bool dirtyState, bool active, bool primary, int index, bool shared = false)
    {
        var tab = new DocumentTab { DocumentTitle = title, IsDirty = dirtyState, IsActive = active && (primary ? !secondaryPaneActive : secondaryPaneActive), Margin = Padding.Empty };
        tab.Width = Math.Clamp(TextRenderer.MeasureText(title, Font).Width + 48, 108, 180); tab.Height = 35;
        tab.Activated += (_, _) => { if (shared) SetActivePane(true); else if (primary) ActivatePrimaryDocument(index); else ActivateSecondaryDocument(index); };
        tab.CloseRequested += (_, _) => { if (shared) CloseSplitView(); else if (primary) ClosePrimaryDocumentAt(index); else CloseSecondaryDocumentAt(index); };
        ConfigureDocumentSessionDrag(tab, primary, index, shared); AttachExternalDropTarget(tab, primary ? FileDropZone.PrimaryDocument : FileDropZone.SecondaryDocument); return tab;
    }

    private void ClosePrimaryDocumentAt(int index)
    {
        if (index < 0 || index >= primaryDocuments.Count) return;
        ResolveSortPreviewBeforePrimaryDocumentChange();
        if (primaryDocuments.Count == 1) { ClosePrimaryDocument(); return; }
        if (index == primaryDocumentIndex) { CapturePrimaryDocument(); if (primaryDocuments[index].Dirty && !ConfirmLoseChanges()) return; }
        else if (!ConfirmInactiveDocumentClose(primaryDocuments[index])) return;
        primaryDocuments.RemoveAt(index); if (index < primaryDocumentIndex) primaryDocumentIndex--; else if (index == primaryDocumentIndex) primaryDocumentIndex = Math.Min(index, primaryDocuments.Count - 1);
        PaneDocumentSession next = primaryDocuments[primaryDocumentIndex]; workbook = next.Workbook; model = workbook.ActiveSheet.Sheet; path = next.Path; dirty = next.Dirty; undo = next.Undo; redo = next.Redo;
        RefreshPrimarySheetTabs(); Render(); RefreshDocumentTabs(); UpdateTitle();
    }

    private void CloseSecondaryDocumentAt(int index)
    {
        if (index < 0 || index >= secondaryDocuments.Count) return;
        if (secondaryDocuments.Count == 1) { CloseSplitView(); return; }
        if (index == secondaryDocumentIndex) { CaptureSecondaryDocument(); if (!ConfirmSecondaryClose()) return; }
        else if (!ConfirmInactiveDocumentClose(secondaryDocuments[index])) return;
        secondaryDocuments.RemoveAt(index); if (index < secondaryDocumentIndex) secondaryDocumentIndex--; else if (index == secondaryDocumentIndex) secondaryDocumentIndex = Math.Min(index, secondaryDocuments.Count - 1);
        PaneDocumentSession next = secondaryDocuments[secondaryDocumentIndex]; secondaryWorkbook = next.Workbook; secondaryModel = secondaryWorkbook.ActiveSheet.Sheet; secondaryPath = next.Path; secondaryDirty = next.Dirty;
        RefreshSecondarySheetTabs(); RenderSecondaryModel(); RefreshDocumentTabs(); UpdateSecondaryTitle();
    }

    private void OpenFilesAsDocuments(string[] files, bool secondary)
    {
        var loaded = new List<PaneDocumentSession>();
        foreach (string file in files)
        {
            try { loaded.Add(new(LoadWorkbook(file), file)); sessionRecent.RemoveAll(item => PathsEqual(item, file)); sessionRecent.Insert(0, file); }
            catch (Exception ex) { ShowNotice("Open failed", $"Could not open '{Path.GetFileName(file)}'. {ex.Message}"); }
        }
        if (loaded.Count == 0) return;
        if (secondary && !splitView.Panel2Collapsed)
        {
            if (!secondarySharesPrimary) { SyncSecondaryAll(); CaptureSecondaryDocument(); }
            else { secondaryDocuments.Clear(); secondarySharesPrimary = false; }
            int first = secondaryDocuments.Count; secondaryDocuments.AddRange(loaded); secondaryDocumentIndex = first; PaneDocumentSession active = secondaryDocuments[first];
            secondaryWorkbook = active.Workbook; secondaryModel = secondaryWorkbook.ActiveSheet.Sheet; secondaryPath = active.Path; secondaryDirty = false; RefreshSecondarySheetTabs(); RenderSecondaryModel(); RefreshDocumentTabs(); UpdateSecondaryTitle(); SetActivePane(true);
        }
        else
        {
            ResolveSortPreviewBeforePrimaryDocumentChange();
            SyncAll(); CapturePrimaryDocument(); int first = primaryDocuments.Count; primaryDocuments.AddRange(loaded); primaryDocumentIndex = first; PaneDocumentSession active = primaryDocuments[first];
            workbook = active.Workbook; model = workbook.ActiveSheet.Sheet; path = active.Path; dirty = false; undo = active.Undo; redo = active.Redo; filter = null; ShowEditor(); RefreshPrimarySheetTabs(); Render(); RefreshDocumentTabs(); UpdateTitle(); SetActivePane(false);
        }
    }

    private void ResolveSortPreviewBeforePrimaryDocumentChange()
    {
        if (sortBaselineWorkbook is not null) SaveSortPreview();
    }

    private bool ConfirmAllDocumentChanges()
    {
        CapturePrimaryDocument();
        if (!ConfirmLoseChanges()) return false;
        for (int index = 0; index < primaryDocuments.Count; index++)
        {
            if (index == primaryDocumentIndex) continue;
            if (!ConfirmInactiveDocumentClose(primaryDocuments[index])) return false;
        }
        return ConfirmAllSecondaryDocumentsClose();
    }

    private bool ConfirmAllSecondaryDocumentsClose()
    {
        if (splitView.Panel2Collapsed || secondarySharesPrimary) return true; CaptureSecondaryDocument();
        if (!ConfirmSecondaryClose()) return false;
        for (int index = 0; index < secondaryDocuments.Count; index++)
        {
            if (index == secondaryDocumentIndex) continue;
            if (!ConfirmInactiveDocumentClose(secondaryDocuments[index])) return false;
        }
        return true;
    }

    private bool ConfirmInactiveDocumentClose(PaneDocumentSession session)
    {
        if (!session.Dirty) return true;
        DialogResult result = MessageBox.Show(this, $"Save changes to {session.Title}?", "SheetLite", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return result == DialogResult.No || result == DialogResult.Yes && TrySaveInactiveDocument(session);
    }

    private bool TrySaveInactiveDocument(PaneDocumentSession session)
    {
        string? target = session.Path;
        if (target is null)
        {
            using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV (UTF-8) (*.csv)|*.csv", DefaultExt = "xlsx", AddExtension = true, FileName = "Untitled.xlsx" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false; target = dialog.FileName;
        }
        try
        {
            UseWaitCursor = true;
            if (Path.GetExtension(target).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) XlsxCodec.SaveWorkbook(target, session.Workbook);
            else CsvCodec.Save(target, session.Workbook.ActiveSheet.Sheet);
            session.Path = target; session.Dirty = false; RefreshDocumentTabs(); return true;
        }
        catch (Exception ex) { ShowNotice("Save failed", "Could not save the file. " + ex.Message); return false; }
        finally { UseWaitCursor = false; }
    }
}
