namespace SheetLite;

internal sealed record WorksheetTabDrag(bool Primary, int Index);
internal sealed record DocumentPaneDrag(bool Primary, int Index);
internal enum FileDropZone { Welcome, PrimaryDocument, SecondaryDocument, PrimarySheetBar, SecondarySheetBar }

internal sealed class WelcomeDropOverlay : Control
{
    public WelcomeDropOverlay()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Selection; Dock = DockStyle.Fill; Visible = false; AllowDrop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(Theme.Selection);
        using var border = new Pen(Theme.Purple, 3F); e.Graphics.DrawRectangle(border, 5, 5, Math.Max(0, Width - 11), Math.Max(0, Height - 11));
        using var titleFont = new Font("Segoe UI", 24F, FontStyle.Regular);
        using var detailFont = new Font("Segoe UI", 10F);
        TextRenderer.DrawText(e.Graphics, "Drop to open", titleFont, ClientRectangle, Theme.Purple, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        var detail = new Rectangle(0, Height / 2 + 34, Width, 30);
        TextRenderer.DrawText(e.Graphics, "CSV, TSV, TXT, or XLSX", detailFont, detail, Theme.Foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);
    }
}

internal sealed partial class MainForm
{
    private readonly WelcomeDropOverlay welcomeDropOverlay = new();
    private readonly HashSet<Control> externalDropTargets = [];
    private DataGridView? fileDropHighlightGrid;
    private FlowLayoutPanel? fileDropHighlightSheetBar;

    private static readonly HashSet<string> DroppableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".csv", ".tsv", ".txt", ".xlsx" };

    private void ConfigureDragAndDrop()
    {
        AllowDrop = true;
        welcome.Controls.Add(welcomeDropOverlay); welcomeDropOverlay.BringToFront();
        AttachExternalDropTree(welcome, FileDropZone.Welcome); AttachExternalDropTarget(welcomeDropOverlay, FileDropZone.Welcome);
        AttachExternalDropTarget(primarySheetTabs, FileDropZone.PrimarySheetBar); AttachExternalDropTarget(secondarySheetTabs, FileDropZone.SecondarySheetBar);
        AttachPaneDropTargets(primaryPaneLayout, secondary: false); AttachPaneDropTargets(secondaryPaneLayout, secondary: true);
        grid.Paint += PaintFileDropHighlight; secondaryGrid.Paint += PaintFileDropHighlight;
        primarySheetTabs.Paint += PaintSheetBarDropHighlight; secondarySheetTabs.Paint += PaintSheetBarDropHighlight;
        ConfigureWorksheetBarDrop(primarySheetTabs, primary: true); ConfigureWorksheetBarDrop(secondarySheetTabs, primary: false);
        ConfigureDocumentBarDrop(primaryDocumentTabs, primary: true); ConfigureDocumentBarDrop(secondaryDocumentTabs, primary: false);
        DragEnter += (_, e) => HandleExternalDrag(e, welcome.Visible ? FileDropZone.Welcome : PaneAtScreenPoint(new Point(e.X, e.Y)) ? FileDropZone.SecondaryDocument : FileDropZone.PrimaryDocument);
        DragOver += (_, e) => HandleExternalDrag(e, welcome.Visible ? FileDropZone.Welcome : PaneAtScreenPoint(new Point(e.X, e.Y)) ? FileDropZone.SecondaryDocument : FileDropZone.PrimaryDocument);
        DragLeave += (_, _) => ClearFileDropVisuals();
        DragDrop += (_, e) => HandleExternalDrop(e, welcome.Visible ? FileDropZone.Welcome : PaneAtScreenPoint(new Point(e.X, e.Y)) ? FileDropZone.SecondaryDocument : FileDropZone.PrimaryDocument);
    }

    private void AttachPaneDropTargets(Control root, bool secondary)
    {
        AttachExternalDropTarget(root, secondary ? FileDropZone.SecondaryDocument : FileDropZone.PrimaryDocument);
        foreach (Control child in root.Controls) AttachPaneDropTargets(child, secondary);
    }

    private void AttachExternalDropTree(Control root, FileDropZone zone)
    {
        AttachExternalDropTarget(root, zone);
        foreach (Control child in root.Controls) AttachExternalDropTree(child, zone);
    }

    private void AttachExternalDropTarget(Control control, FileDropZone zone)
    {
        if (!externalDropTargets.Add(control)) return; control.AllowDrop = true;
        control.Disposed += (_, _) => externalDropTargets.Remove(control);
        control.DragEnter += (_, e) => HandleExternalDrag(e, zone);
        control.DragOver += (_, e) => HandleExternalDrag(e, zone);
        control.DragLeave += (_, _) =>
        {
            Control region = zone switch { FileDropZone.Welcome => welcome, FileDropZone.PrimarySheetBar => primarySheetTabs, FileDropZone.SecondarySheetBar => secondarySheetTabs, FileDropZone.SecondaryDocument => secondaryPaneLayout, _ => primaryPaneLayout };
            if (!region.RectangleToScreen(region.ClientRectangle).Contains(Cursor.Position)) ClearFileDropVisuals();
        };
        control.DragDrop += (_, e) => HandleExternalDrop(e, zone);
    }

    private bool PaneAtScreenPoint(Point point) => !splitView.Panel2Collapsed && secondaryPaneLayout.RectangleToScreen(secondaryPaneLayout.ClientRectangle).Contains(point);

    private static string[] DroppedFiles(IDataObject? data)
    {
        if (data?.GetDataPresent(DataFormats.FileDrop) != true || data.GetData(DataFormats.FileDrop) is not string[] files) return [];
        return files.Where(File.Exists).Where(file => DroppableExtensions.Contains(Path.GetExtension(file))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void HandleExternalDrag(DragEventArgs e, FileDropZone zone)
    {
        if (e.Data?.GetDataPresent(typeof(WorksheetTabDrag)) == true || e.Data?.GetDataPresent(typeof(DocumentPaneDrag)) == true) return;
        string[] files = DroppedFiles(e.Data); e.Effect = files.Length == 0 ? DragDropEffects.None : DragDropEffects.Copy;
        if (files.Length == 0) { ClearFileDropVisuals(); return; }
        if (zone == FileDropZone.Welcome)
        {
            fileDropHighlightGrid = null; welcomeDropOverlay.Visible = true; welcomeDropOverlay.BringToFront(); welcomeDropOverlay.Invalidate();
        }
        else
        {
            welcomeDropOverlay.Visible = false;
            if (zone is FileDropZone.PrimarySheetBar or FileDropZone.SecondarySheetBar)
            {
                fileDropHighlightGrid?.Invalidate(); fileDropHighlightGrid = null; FlowLayoutPanel target = zone == FileDropZone.SecondarySheetBar ? secondarySheetTabs : primarySheetTabs;
                if (!ReferenceEquals(fileDropHighlightSheetBar, target)) { if (fileDropHighlightSheetBar is not null) RestoreSheetBarColors(fileDropHighlightSheetBar); fileDropHighlightSheetBar = target; SetDropColor(target, Theme.Selection); target.Invalidate(); }
            }
            else
            {
                if (fileDropHighlightSheetBar is not null) { fileDropHighlightSheetBar.BackColor = Theme.Surface; fileDropHighlightSheetBar.Invalidate(); fileDropHighlightSheetBar = null; }
                DataGridView target = zone == FileDropZone.SecondaryDocument ? secondaryGrid : grid;
                if (!ReferenceEquals(fileDropHighlightGrid, target)) { fileDropHighlightGrid?.Invalidate(); fileDropHighlightGrid = target; target.Invalidate(); }
            }
        }
    }

    private void HandleExternalDrop(DragEventArgs e, FileDropZone zone)
    {
        string[] files = DroppedFiles(e.Data); ClearFileDropVisuals(); if (files.Length == 0) return;
        if (zone == FileDropZone.Welcome)
        {
            OpenFile(files[0]);
            if (files.Length > 1) OpenFilesAsDocuments(files[1..], secondary: false);
        }
        else if (zone is FileDropZone.PrimarySheetBar or FileDropZone.SecondarySheetBar) ImportFilesIntoPane(files, zone == FileDropZone.SecondarySheetBar, focusImported: false);
        else OpenFilesAsDocuments(files, zone == FileDropZone.SecondaryDocument);
    }

    private void ClearFileDropVisuals()
    {
        welcomeDropOverlay.Visible = false; DataGridView? old = fileDropHighlightGrid; fileDropHighlightGrid = null; old?.Invalidate();
        if (fileDropHighlightSheetBar is not null) { FlowLayoutPanel oldBar = fileDropHighlightSheetBar; fileDropHighlightSheetBar = null; RestoreSheetBarColors(oldBar); oldBar.Invalidate(); }
    }

    private static void SetDropColor(Control root, Color color) { root.BackColor = color; foreach (Control child in root.Controls) SetDropColor(child, color); }
    private void RestoreSheetBarColors(FlowLayoutPanel bar) { bar.BackColor = Theme.Surface; if (ReferenceEquals(bar, primarySheetTabs)) RefreshPrimarySheetTabs(); else RefreshSecondarySheetTabs(); }

    private void PaintFileDropHighlight(object? sender, PaintEventArgs e)
    {
        if (sender is not DataGridView target || !ReferenceEquals(fileDropHighlightGrid, target)) return;
        Rectangle bounds = target.ClientRectangle; bounds.Inflate(-3, -3);
        using var wash = new SolidBrush(Color.FromArgb(78, Theme.Purple)); e.Graphics.FillRectangle(wash, bounds);
        using var border = new Pen(Theme.Purple, 3F); e.Graphics.DrawRectangle(border, bounds);
        var labelBounds = new Rectangle(bounds.Left, bounds.Top + Math.Max(0, bounds.Height / 2 - 28), bounds.Width, 56);
        using var font = new Font("Segoe UI", 17F, FontStyle.Regular);
        TextRenderer.DrawText(e.Graphics, "Drop to open as a new workbook", font, labelBounds, Theme.Foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void PaintSheetBarDropHighlight(object? sender, PaintEventArgs e)
    {
        if (sender is not FlowLayoutPanel bar || !ReferenceEquals(fileDropHighlightSheetBar, bar)) return;
        using var wash = new SolidBrush(Color.FromArgb(90, Theme.Purple)); e.Graphics.FillRectangle(wash, bar.ClientRectangle);
        using var border = new Pen(Theme.Purple, 2F); e.Graphics.DrawRectangle(border, 1, 1, Math.Max(0, bar.ClientSize.Width - 3), Math.Max(0, bar.ClientSize.Height - 3));
        Rectangle textBounds = Rectangle.Inflate(bar.ClientRectangle, -8, 0);
        TextRenderer.DrawText(e.Graphics, "Drop to add worksheet", Font, textBounds, Theme.Foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void ImportFilesIntoPane(string[] files, bool secondary, bool focusImported = true)
    {
        var imports = new List<(string File, WorkbookModel Workbook)>();
        foreach (string file in files)
        {
            try { imports.Add((file, LoadWorkbook(file))); }
            catch (Exception ex) { ShowNotice("Import failed", $"Could not import '{Path.GetFileName(file)}'. {ex.Message}"); }
        }
        if (imports.Count == 0) return;

        if (secondary && !splitView.Panel2Collapsed && !secondarySharesPrimary)
        {
            if (secondaryWorkbook is null) return; secondaryWorkbook.ActiveSheet.Sheet = secondaryModel!;
            int previous = secondaryWorkbook.ActiveSheetIndex, first = AppendImportedSheets(secondaryWorkbook, imports); secondaryWorkbook.ActiveSheetIndex = focusImported ? first : previous; secondaryModel = secondaryWorkbook.ActiveSheet.Sheet;
            RenderSecondaryModel(); RefreshSecondarySheetTabs(); SetSecondaryDirty(); SetActivePane(true);
        }
        else
        {
            PushUndo(); int previous = workbook.ActiveSheetIndex, first = AppendImportedSheets(workbook, imports); workbook.ActiveSheetIndex = focusImported ? first : previous; model = workbook.ActiveSheet.Sheet; filter = null;
            RefreshPrimarySheetTabs(); Render(); SetDirty(); SetActivePane(secondary && !splitView.Panel2Collapsed);
        }
    }

    private void OpenAnotherDialog()
    {
        using var dialog = new OpenFileDialog { Filter = SpreadsheetOpenFilter, Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0) return;
        if (welcome.Visible) { OpenFile(dialog.FileNames[0]); if (dialog.FileNames.Length > 1) OpenFilesAsDocuments(dialog.FileNames[1..], secondary: false); }
        else OpenFilesAsDocuments(dialog.FileNames, secondaryPaneActive && !splitView.Panel2Collapsed);
    }

    private static int AppendImportedSheets(WorkbookModel target, List<(string File, WorkbookModel Workbook)> imports)
    {
        int first = target.Sheets.Count;
        foreach (var import in imports)
        {
            string fileName = Path.GetFileNameWithoutExtension(import.File);
            foreach (WorksheetModel source in import.Workbook.Sheets)
            {
                string requested = Path.GetExtension(import.File).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? $"{fileName} - {source.Name}" : fileName;
                target.Sheets.Add(new(target.UniqueSheetName(requested), source.Sheet.Clone()));
            }
        }
        return first;
    }

    private void ConfigureSheetTabDrag(Control tab, Control dragHandle, int index, bool primary)
    {
        Point start = Point.Empty; bool armed = false, dropAfter = false;
        dragHandle.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { start = e.Location; armed = true; } };
        dragHandle.MouseUp += (_, _) => armed = false;
        dragHandle.MouseMove += (_, e) =>
        {
            if (!armed || e.Button != MouseButtons.Left || Math.Abs(e.X - start.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - start.Y) < SystemInformation.DragSize.Height / 2) return;
            armed = false; tab.DoDragDrop(new WorksheetTabDrag(primary, index), DragDropEffects.Move);
        };
        void Enter(object? _, DragEventArgs e) { if (e.Data?.GetDataPresent(typeof(WorksheetTabDrag)) == true) { e.Effect = DragDropEffects.Move; tab.BackColor = Theme.Selection; tab.Invalidate(); } }
        void Over(object? sender, DragEventArgs e) { Enter(sender, e); Point local = tab.PointToClient(new Point(e.X, e.Y)); bool next = local.X >= tab.Width / 2; if (next != dropAfter) { dropAfter = next; tab.Invalidate(); } }
        void Leave(object? _, EventArgs __) { tab.BackColor = primary && workbook.ActiveSheetIndex == index || !primary && secondaryWorkbook?.ActiveSheetIndex == index ? Theme.Inset : Theme.Surface; tab.Invalidate(); }
        void Drop(object? _, DragEventArgs e)
        {
            Leave(null, EventArgs.Empty); if (e.Data?.GetData(typeof(WorksheetTabDrag)) is not WorksheetTabDrag source) return;
            MoveWorksheetBetweenPanes(source.Primary, source.Index, primary, index + (dropAfter ? 1 : 0));
        }
        foreach (Control target in new[] { tab, dragHandle }) { target.AllowDrop = true; target.DragEnter += Enter; target.DragOver += Over; target.DragLeave += Leave; target.DragDrop += Drop; AttachExternalDropTarget(target, primary ? FileDropZone.PrimarySheetBar : FileDropZone.SecondarySheetBar); }
        tab.Paint += (_, e) => { if (tab.BackColor != Theme.Selection) return; using var pen = new Pen(Theme.Purple, 3F); int x = dropAfter ? tab.ClientSize.Width - 2 : 1; e.Graphics.DrawLine(pen, x, 1, x, tab.ClientSize.Height - 2); };
    }

    private void ConfigureWorksheetBarDrop(Control bar, bool primary)
    {
        bar.AllowDrop = true;
        bar.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(typeof(WorksheetTabDrag)) == true) { e.Effect = DragDropEffects.Move; bar.BackColor = Theme.Selection; } };
        bar.DragOver += (_, e) => { if (e.Data?.GetDataPresent(typeof(WorksheetTabDrag)) == true) e.Effect = DragDropEffects.Move; };
        bar.DragLeave += (_, _) => bar.BackColor = Theme.Surface;
        bar.DragDrop += (_, e) => { bar.BackColor = Theme.Surface; if (e.Data?.GetData(typeof(WorksheetTabDrag)) is WorksheetTabDrag source) MoveWorksheetBetweenPanes(source.Primary, source.Index, primary, primary ? workbook.Sheets.Count : secondaryWorkbook?.Sheets.Count ?? 0); };
    }

    private void MoveWorksheetBetweenPanes(bool sourcePrimary, int sourceIndex, bool targetPrimary, int insertionIndex)
    {
        WorkbookModel? source = sourcePrimary ? workbook : secondaryWorkbook, target = targetPrimary ? workbook : secondaryWorkbook;
        if (source is null || target is null || sourceIndex < 0 || sourceIndex >= source.Sheets.Count) return;
        if (secondarySharesPrimary) PushUndo();
        else
        {
            if (sourcePrimary || targetPrimary) PushUndo();
            if (!sourcePrimary || !targetPrimary) PushSecondaryUndo();
        }
        if (!secondarySharesPrimary && secondaryWorkbook is not null && (source == secondaryWorkbook || target == secondaryWorkbook)) { secondaryWorkbook.ActiveSheet.Sheet = secondaryModel!; }

        if (ReferenceEquals(source, target))
        {
            WorksheetModel moved = source.Sheets[sourceIndex]; source.Sheets.RemoveAt(sourceIndex); if (insertionIndex > sourceIndex) insertionIndex--; insertionIndex = Math.Clamp(insertionIndex, 0, source.Sheets.Count); source.Sheets.Insert(insertionIndex, moved); source.ActiveSheetIndex = insertionIndex;
        }
        else
        {
            WorksheetModel moved = source.Sheets[sourceIndex]; string targetName = target.UniqueSheetName(moved.Name); var transferred = new WorksheetModel(targetName, moved.Sheet);
            if (source.Sheets.Count == 1)
            {
                var blank = new SheetModel(); blank.EnsureSize(100, 26); source.Sheets[0] = new(source.UniqueSheetName("Sheet1", 0), blank); source.ActiveSheetIndex = 0;
            }
            else
            {
                source.Sheets.RemoveAt(sourceIndex); source.ActiveSheetIndex = Math.Clamp(source.ActiveSheetIndex > sourceIndex ? source.ActiveSheetIndex - 1 : source.ActiveSheetIndex, 0, source.Sheets.Count - 1);
            }
            insertionIndex = Math.Clamp(insertionIndex, 0, target.Sheets.Count); target.Sheets.Insert(insertionIndex, transferred); target.ActiveSheetIndex = insertionIndex;
        }

        model = workbook.ActiveSheet.Sheet;
        if (secondarySharesPrimary) { secondaryWorkbook = workbook; secondaryModel = model; }
        else if (secondaryWorkbook is not null) secondaryModel = secondaryWorkbook.ActiveSheet.Sheet;
        RefreshPrimarySheetTabs(); Render(); RefreshSecondarySheetTabs(); if (!splitView.Panel2Collapsed && secondaryModel is not null) RenderSecondaryModel();
        if (targetPrimary || secondarySharesPrimary) SetDirty(); else SetSecondaryDirty();
        if (!ReferenceEquals(source, target)) { if (sourcePrimary) SetDirty(); else SetSecondaryDirty(); }
        SetActivePane(!targetPrimary);
    }

    private void ConfigureDocumentSessionDrag(DocumentTab tab, bool primary, int index, bool shared)
    {
        Point start = Point.Empty; bool armed = false, dropAfter = false;
        tab.MouseDown += (_, e) => { if (!shared && e.Button == MouseButtons.Left) { start = e.Location; armed = true; } };
        tab.MouseUp += (_, _) => armed = false;
        tab.MouseMove += (_, e) =>
        {
            if (shared || !armed || e.Button != MouseButtons.Left || Math.Abs(e.X - start.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - start.Y) < SystemInformation.DragSize.Height / 2) return;
            armed = false; tab.DoDragDrop(new DocumentPaneDrag(primary, index), DragDropEffects.Move);
        };
        tab.DragEnter += (_, e) => { if (!shared && e.Data?.GetDataPresent(typeof(DocumentPaneDrag)) == true) { e.Effect = DragDropEffects.Move; tab.IsDropTarget = true; } };
        tab.DragOver += (_, e) => { if (!shared && e.Data?.GetDataPresent(typeof(DocumentPaneDrag)) == true) { e.Effect = DragDropEffects.Move; dropAfter = tab.PointToClient(new Point(e.X, e.Y)).X >= tab.Width / 2; tab.IsDropTarget = true; } };
        tab.DragLeave += (_, _) => tab.IsDropTarget = false;
        tab.DragDrop += (_, e) => { tab.IsDropTarget = false; if (!shared && e.Data?.GetData(typeof(DocumentPaneDrag)) is DocumentPaneDrag source) MoveDocumentBetweenPanes(source.Primary, source.Index, primary, index + (dropAfter ? 1 : 0)); };
    }

    private void ConfigureDocumentBarDrop(Control bar, bool primary)
    {
        bar.AllowDrop = true;
        bar.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(typeof(DocumentPaneDrag)) == true && (!primary || !secondarySharesPrimary)) { e.Effect = DragDropEffects.Move; bar.BackColor = Theme.Selection; } };
        bar.DragOver += (_, e) => { if (e.Data?.GetDataPresent(typeof(DocumentPaneDrag)) == true && (!primary || !secondarySharesPrimary)) e.Effect = DragDropEffects.Move; };
        bar.DragLeave += (_, _) => bar.BackColor = Theme.Surface;
        bar.DragDrop += (_, e) =>
        {
            bar.BackColor = Theme.Surface; if (e.Data?.GetData(typeof(DocumentPaneDrag)) is not DocumentPaneDrag source) return;
            int insertion = primary ? primaryDocuments.Count : secondaryDocuments.Count; MoveDocumentBetweenPanes(source.Primary, source.Index, primary, insertion);
        };
    }

    private void MoveDocumentBetweenPanes(bool sourcePrimary, int sourceIndex, bool targetPrimary, int insertionIndex)
    {
        if (!sourcePrimary && secondarySharesPrimary) return;
        ResolveSortPreviewBeforePrimaryDocumentChange();
        List<PaneDocumentSession> source = sourcePrimary ? primaryDocuments : secondaryDocuments;
        if (sourceIndex < 0 || sourceIndex >= source.Count) return;
        if (sourcePrimary) { CapturePrimaryDocument(); } else { CaptureSecondaryDocument(); }
        if (!targetPrimary && secondarySharesPrimary) { secondarySharesPrimary = false; secondaryDocuments.Clear(); }
        List<PaneDocumentSession> target = targetPrimary ? primaryDocuments : secondaryDocuments;

        if (ReferenceEquals(source, target))
        {
            PaneDocumentSession moved = source[sourceIndex]; source.RemoveAt(sourceIndex); if (insertionIndex > sourceIndex) insertionIndex--; insertionIndex = Math.Clamp(insertionIndex, 0, source.Count); source.Insert(insertionIndex, moved);
            if (sourcePrimary) primaryDocumentIndex = insertionIndex; else secondaryDocumentIndex = insertionIndex;
        }
        else
        {
            PaneDocumentSession moved = source[sourceIndex];
            if (source.Count == 1)
            {
                var blank = WorkbookModel.CreateBlank(); source[0] = new(blank); if (sourcePrimary) primaryDocumentIndex = 0; else secondaryDocumentIndex = 0;
            }
            else
            {
                source.RemoveAt(sourceIndex); if (sourcePrimary) primaryDocumentIndex = Math.Clamp(primaryDocumentIndex > sourceIndex ? primaryDocumentIndex - 1 : primaryDocumentIndex, 0, source.Count - 1); else secondaryDocumentIndex = Math.Clamp(secondaryDocumentIndex > sourceIndex ? secondaryDocumentIndex - 1 : secondaryDocumentIndex, 0, source.Count - 1);
            }
            insertionIndex = Math.Clamp(insertionIndex, 0, target.Count); target.Insert(insertionIndex, moved); if (targetPrimary) primaryDocumentIndex = insertionIndex; else secondaryDocumentIndex = insertionIndex;
        }

        ApplyPrimaryDocumentSession(primaryDocumentIndex);
        if (!secondarySharesPrimary && secondaryDocuments.Count > 0) ApplySecondaryDocumentSession(secondaryDocumentIndex);
        RefreshPrimarySheetTabs(); Render(); RefreshSecondarySheetTabs(); if (!splitView.Panel2Collapsed && secondaryModel is not null) RenderSecondaryModel(); RefreshDocumentTabs(); UpdateTitle(); UpdateSecondaryTitle(); SetActivePane(!targetPrimary);
    }
}
