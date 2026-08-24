namespace SheetLite;

internal sealed partial class MainForm
{
    private readonly TableLayoutPanel commandHost = new();
    private readonly Panel workspace = new(), findBar = new(), filterBar = new(), sortPanel = new(), infoPanel = new(), sqlPanel = new(), helpPage = new();
    private readonly TableLayoutPanel workspaceLayout = new();
    private readonly SplitContainer splitView = new(), helpSplitView = new();
    private readonly TableLayoutPanel primaryPaneLayout = new(), secondaryPaneLayout = new();
    private readonly Panel primaryFileHeader = new(), secondaryFileHeader = new();
    private DocumentTab primaryDocumentTab = new(), secondaryDocumentTab = new();
    private readonly FlowLayoutPanel primaryDocumentTabs = new(), secondaryDocumentTabs = new();
    private readonly FlowLayoutPanel primarySheetTabs = new(), secondarySheetTabs = new();
    private readonly DataGridView secondaryGrid = new SmoothDataGridView();
    private readonly TextBox findBox = ToolTextBox(), replaceBox = ToolTextBox(), filterValue = ToolTextBox(), filterValue2 = ToolTextBox(), sqlEditor = ToolTextBox(true), helpSearch = ToolTextBox();
    private readonly Label findStatus = ToolLabel("No results"), sqlStatus = ToolLabel("Ready"), helpStatus = ToolLabel("");
    private readonly ListBox helpNavigation = new(), sqlSources = new();
    private readonly RichTextBox helpDocument = new();
    private readonly ToolTip sqlToolTips = new() { AutoPopDelay = 7000, InitialDelay = 350, ReshowDelay = 100 };
    private readonly System.Windows.Forms.Timer findDebounce = new() { Interval = 250 };
    private readonly CheckBox sqlFirstRowHeader = new() { Text = "Row 1 is header", Checked = true, AutoSize = true, ForeColor = Theme.Comment, BackColor = Theme.Surface };
    private readonly Label primaryPaneTitle = ToolLabel("Untitled"), secondaryPaneTitle = ToolLabel("Second view");
    private readonly ComboBox filterColumn = ToolCombo(), filterOperator = ToolCombo(), filterJoin = ToolCombo(), filterColumn2 = ToolCombo(), filterOperator2 = ToolCombo();
    private readonly ComboBox sortTarget = ToolCombo(), sortColumn1 = ToolCombo(), sortDirection1 = ToolCombo(), sortBlanks1 = ToolCombo(), sortColumn2 = ToolCombo(), sortDirection2 = ToolCombo(), sortBlanks2 = ToolCombo();
    private readonly Button replaceToggle = ToolButton("⌄"), addFilterCondition = ToolButton("＋ Add condition"), addSortColumn = ToolButton("＋ Add sort column");
    private readonly Button filterEdit = ToolButton("✎ Conditions"), filterApply = ToolButton("Apply filter"), filterClear = ToolButton("Clear"), filterClose = ToolButton("×");
    private readonly Button sortApplyButton = ToolButton("Sort"), sortSaveButton = ToolButton("Save sort"), sortRevertButton = ToolButton("Revert");
    private readonly Label filterWhereLabel = ToolLabel("WHERE"), filterAdditionalLabel = ToolLabel("Additional condition");
    private bool filterBuilderExpanded, secondFilterVisible, secondSortVisible, helpReturnToWelcome, secondaryPaneActive;
    private bool secondaryDirty, secondarySharesPrimary, secondaryLoading;
    private bool secondaryFillDragging;
    private CellRange secondaryFillSource, secondaryFillPreview;
    private int secondaryFillPrimaryRow, secondaryFillPrimaryColumn;
    private WorkbookModel? sortBaselineWorkbook;
    private bool sortBaselineDirty, sortPreviewApplied;
    private List<int> sortSelectedRows = [];
    private WorkbookModel? secondaryWorkbook;
    private SheetModel? secondaryModel;
    private string? secondaryPath;

    private static TextBox ToolTextBox(bool multiline = false) => new()
    {
        BackColor = Theme.Inset, ForeColor = Theme.Foreground, BorderStyle = BorderStyle.FixedSingle,
        Font = new Font(multiline ? "Consolas" : "Segoe UI", multiline ? 10F : 9F), Multiline = multiline
    };
    private static Label ToolLabel(string text) => new() { Text = text, ForeColor = Theme.Comment, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private static ComboBox ToolCombo() => new() { BackColor = Theme.Inset, ForeColor = Theme.Foreground, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList };
    private static Button ToolButton(string text) { var b = new Button { Text = text, BackColor = Theme.CurrentLine, ForeColor = Theme.Foreground, FlatStyle = FlatStyle.Flat, Height = 26, Cursor = Cursors.Hand }; b.FlatAppearance.BorderColor = Theme.Comment; b.FlatAppearance.MouseOverBackColor = Theme.Comment; return b; }

    private void BuildDockedWorkspace()
    {
        commandHost.Dock = DockStyle.Fill; commandHost.AutoSize = true; commandHost.AutoSizeMode = AutoSizeMode.GrowAndShrink; commandHost.ColumnCount = 1; commandHost.RowCount = 4; commandHost.Margin = Padding.Empty; commandHost.Padding = Padding.Empty; commandHost.BackColor = Theme.Surface;
        commandHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandHost.RowStyles.Add(new RowStyle(SizeType.AutoSize)); commandHost.RowStyles.Add(new RowStyle(SizeType.AutoSize)); commandHost.RowStyles.Add(new RowStyle(SizeType.AutoSize)); commandHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        BuildFindBar(); BuildFilterBar(); BuildSortPanel(); BuildInfoPanel();
        commandHost.Controls.Add(findBar, 0, 0); commandHost.Controls.Add(filterBar, 0, 1); commandHost.Controls.Add(sortPanel, 0, 2); commandHost.Controls.Add(infoPanel, 0, 3);

        workspace.Dock = DockStyle.Fill; workspace.BackColor = Theme.Background; workspace.Margin = Padding.Empty; workspace.Padding = Padding.Empty;
        workspaceLayout.Dock = DockStyle.Fill; workspaceLayout.ColumnCount = 1; workspaceLayout.RowCount = 2; workspaceLayout.Margin = Padding.Empty; workspaceLayout.Padding = Padding.Empty;
        workspaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); workspaceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); workspaceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        splitView.Dock = DockStyle.Fill; splitView.Margin = Padding.Empty; splitView.Padding = Padding.Empty; splitView.Orientation = Orientation.Vertical; splitView.BackColor = Theme.CurrentLine; splitView.SplitterWidth = 5; splitView.Panel2Collapsed = true;
        grid.Dock = DockStyle.Fill; splitView.Panel1.Controls.Add(BuildPane(primaryPaneTitle, grid, primary: true)); ConfigureSecondaryGrid(); splitView.Panel2.Controls.Add(BuildPane(secondaryPaneTitle, secondaryGrid, primary: false));
        BuildSqlPanel(); workspaceLayout.Controls.Add(splitView, 0, 0); workspaceLayout.Controls.Add(sqlPanel, 0, 1); workspace.Controls.Add(workspaceLayout);
        BuildHelpPage(); workspace.Controls.Add(helpPage);
        RefreshPrimarySheetTabs(); RefreshDocumentTabs(); UpdateSplitChrome();
    }

    private void BuildFindBar()
    {
        findBar.Dock = DockStyle.Fill; findBar.Height = 38; findBar.Visible = false; findBar.BackColor = Theme.Surface; findBar.Margin = Padding.Empty; findBar.BorderStyle = BorderStyle.FixedSingle;
        replaceToggle.SetBounds(5, 5, 27, 27); replaceToggle.Click += (_, _) => SetReplaceVisible(!replaceBox.Visible);
        findBox.SetBounds(36, 5, 500, 27); findBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; findBox.PlaceholderText = "Find"; findDebounce.Tick += (_, _) => { findDebounce.Stop(); UpdateFindStatus(); }; findBox.TextChanged += (_, _) => { findDebounce.Stop(); findDebounce.Start(); }; findBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { findDebounce.Stop(); UpdateFindStatus(); FindNext(e.Shift); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) CloseFindBar(); };
        findStatus.SetBounds(545, 9, 100, 22); findStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Button previous = ToolButton("↑"), next = ToolButton("↓"), close = ToolButton("×"); previous.SetBounds(650, 5, 30, 27); next.SetBounds(682, 5, 30, 27); close.SetBounds(714, 5, 30, 27); previous.Anchor = next.Anchor = close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        previous.Click += (_, _) => FindNext(true); next.Click += (_, _) => FindNext(false); close.Click += (_, _) => CloseFindBar();
        replaceBox.SetBounds(36, 37, 500, 27); replaceBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; replaceBox.PlaceholderText = "Replace"; replaceBox.Visible = false;
        Button replaceOne = ToolButton("Replace"), replaceAll = ToolButton("Replace all"); replaceOne.SetBounds(545, 37, 88, 27); replaceAll.SetBounds(638, 37, 106, 27); replaceOne.Anchor = replaceAll.Anchor = AnchorStyles.Top | AnchorStyles.Right; replaceOne.Visible = replaceAll.Visible = false; replaceOne.Tag = "replace"; replaceAll.Tag = "replace";
        replaceOne.Click += (_, _) => ReplaceCurrent(); replaceAll.Click += (_, _) => ReplaceAllDocked();
        findBar.Controls.AddRange([replaceToggle, findBox, findStatus, previous, next, close, replaceBox, replaceOne, replaceAll]);
        findBar.Resize += (_, _) => LayoutFindBar();
    }

    private void LayoutFindBar()
    {
        int right = findBar.ClientSize.Width - 6; var tagged = findBar.Controls.Cast<Control>().Where(c => Equals(c.Tag, "replace")).ToList();
        findBox.Width = Math.Max(160, right - 405); findStatus.Left = right - 196; findStatus.Width = 90;
        var topButtons = findBar.Controls.OfType<Button>().Where(b => b != replaceToggle && b.Tag is null).Take(3).ToList();
        if (topButtons.Count == 3) { topButtons[0].Left = right - 96; topButtons[1].Left = right - 64; topButtons[2].Left = right - 32; }
        replaceBox.Width = findBox.Width; if (tagged.Count == 2) { tagged[0].Left = right - 199; tagged[1].Left = right - 106; }
    }

    private void BuildFilterBar()
    {
        filterBar.Dock = DockStyle.Fill; filterBar.Height = 40; filterBar.Visible = false; filterBar.BackColor = Theme.Surface; filterBar.Margin = Padding.Empty; filterBar.BorderStyle = BorderStyle.FixedSingle;
        filterOperator.Items.AddRange(["contains", "equals", "not equals", "starts with", "ends with", ">", ">=", "<", "<=", "is blank", "is not blank"]); filterOperator.SelectedIndex = 0;
        filterEdit.Click += (_, _) => ToggleFilterBuilder(); filterApply.Click += (_, _) => ApplyDockedFilter(); filterClear.Click += (_, _) => { ClearFilter(); filterValue.Clear(); filterValue2.Clear(); }; filterClose.Click += (_, _) => { filterBar.Visible = false; RefreshCommandHost(); };
        filterAdditionalLabel.Tag = "builder";
        filterJoin.Items.AddRange(["AND", "OR"]); filterJoin.SelectedIndex = 0; filterJoin.Tag = "builder";
        filterColumn2.Tag = "builder"; filterOperator2.Items.AddRange(filterOperator.Items.Cast<object>().ToArray()); filterOperator2.SelectedIndex = 0; filterOperator2.Tag = "builder";
        filterValue2.Tag = "builder"; addFilterCondition.Tag = "builder"; addFilterCondition.Click += (_, _) => { secondFilterVisible = !secondFilterVisible; UpdateFilterBuilderControls(); LayoutFilterBar(); };
        filterBar.Controls.AddRange([filterWhereLabel, filterColumn, filterOperator, filterValue, filterEdit, filterApply, filterClear, filterClose, filterAdditionalLabel, filterJoin, filterColumn2, filterOperator2, filterValue2, addFilterCondition]);
        filterBar.Resize += (_, _) => LayoutFilterBar();
        UpdateFilterBuilderControls();
        LayoutFilterBar();
    }

    private void LayoutFilterBar()
    {
        int width = Math.Max(1, filterBar.ClientSize.Width), gap = 6;
        bool compact = width < 900;
        filterWhereLabel.SetBounds(8, 11, 48, 20);
        filterColumn.SetBounds(62, 6, compact ? 132 : 150, 27);
        filterOperator.SetBounds(filterColumn.Right + gap, 6, compact ? 104 : 120, 27);
        filterClose.SetBounds(width - 38, compact ? (filterBuilderExpanded ? 76 : 40) : 6, 32, 27);
        filterClear.SetBounds(filterClose.Left - 70, filterClose.Top, 64, 27);
        filterApply.SetBounds(filterClear.Left - 102, filterClose.Top, 96, 27);
        filterEdit.SetBounds(compact ? 8 : filterApply.Left - 110, filterClose.Top, 104, 27);
        int valueRight = compact ? width - 8 : filterEdit.Left - gap;
        filterValue.SetBounds(filterOperator.Right + gap, 6, Math.Max(72, valueRight - filterOperator.Right - gap), 27);

        int builderTop = compact ? 40 : 44;
        filterAdditionalLabel.SetBounds(compact ? 8 : 35, builderTop + 3, compact ? 0 : 125, 25);
        filterJoin.SetBounds(compact ? 8 : 165, builderTop, 65, 27);
        filterColumn2.SetBounds(filterJoin.Right + gap, builderTop, compact ? 132 : 150, 27);
        filterOperator2.SetBounds(filterColumn2.Right + gap, builderTop, compact ? 104 : 120, 27);
        addFilterCondition.SetBounds(compact ? width - 140 : filterValue2.Right + gap, builderTop, 132, 27);
        int secondValueRight = compact ? addFilterCondition.Left - gap : Math.Min(width - 140, 738);
        filterValue2.SetBounds(filterOperator2.Right + gap, builderTop, Math.Max(72, secondValueRight - filterOperator2.Right - gap), 27);

        filterAdditionalLabel.Visible = !compact && filterBuilderExpanded && secondFilterVisible;
        filterBar.Height = compact ? (filterBuilderExpanded ? 110 : 74) : (filterBuilderExpanded ? 82 : 40);
    }

    private void BuildSortPanel()
    {
        sortPanel.Dock = DockStyle.Fill; sortPanel.Height = 78; sortPanel.Visible = false; sortPanel.BackColor = Theme.Surface; sortPanel.Margin = Padding.Empty; sortPanel.BorderStyle = BorderStyle.FixedSingle;
        var title = new Label { Text = "Sort", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Theme.Foreground, AutoSize = true, Location = new(10, 10) };
        var targetLabel = ToolLabel("Sort target:"); targetLabel.SetBounds(82, 11, 72, 20); sortTarget.SetBounds(156, 7, 150, 27); sortTarget.Items.AddRange(["All rows", "Selection"]); sortTarget.SelectedIndex = 0;
        var close = ToolButton("×"); close.SetBounds(738, 7, 32, 27); close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.Click += (_, _) => RevertSortPreview();
        sortColumn1.SetBounds(20, 44, 190, 27); sortDirection1.SetBounds(216, 44, 120, 27); sortDirection1.Items.AddRange(["Ascending", "Descending"]); sortDirection1.SelectedIndex = 0; sortBlanks1.SetBounds(342, 44, 110, 27); sortBlanks1.Items.AddRange(["Blanks last", "Blanks first"]); sortBlanks1.SelectedIndex = 0;
        sortColumn2.SetBounds(20, 76, 190, 27); sortDirection2.SetBounds(216, 76, 120, 27); sortDirection2.Items.AddRange(["Ascending", "Descending"]); sortDirection2.SelectedIndex = 0; sortBlanks2.SetBounds(342, 76, 110, 27); sortBlanks2.Items.AddRange(["Blanks last", "Blanks first"]); sortBlanks2.SelectedIndex = 0; sortColumn2.Visible = sortDirection2.Visible = sortBlanks2.Visible = false;
        addSortColumn.SetBounds(464, 44, 130, 27);
        sortApplyButton.BackColor = Theme.Selection; sortApplyButton.ForeColor = Theme.Purple; sortApplyButton.Click += (_, _) => ApplyDockedSort(); sortSaveButton.Click += (_, _) => SaveSortPreview(); sortRevertButton.Click += (_, _) => RevertSortPreview(); sortSaveButton.Enabled = sortRevertButton.Enabled = false;
        void LayoutSort()
        {
            int width = sortPanel.ClientSize.Width, rowY = 43; bool compact = width < 900; int x = 20;
            sortColumn1.SetBounds(x, rowY, compact ? 130 : 190, 27); x = sortColumn1.Right + 6; sortDirection1.SetBounds(x, rowY, compact ? 100 : 120, 27); x = sortDirection1.Right + 6; sortBlanks1.SetBounds(x, rowY, compact ? 100 : 110, 27); x = sortBlanks1.Right + 6; addSortColumn.SetBounds(x, rowY, compact ? 110 : 130, 27);
            sortColumn2.SetBounds(20, 75, sortColumn1.Width, 27); sortDirection2.SetBounds(sortColumn2.Right + 6, 75, sortDirection1.Width, 27); sortBlanks2.SetBounds(sortDirection2.Right + 6, 75, sortBlanks1.Width, 27);
            close.Left = width - 38; int buttonWidth = compact ? 72 : 82, gap = 5; sortRevertButton.SetBounds(width - buttonWidth - 7, rowY, buttonWidth, 27); sortSaveButton.SetBounds(sortRevertButton.Left - buttonWidth - gap, rowY, buttonWidth, 27); sortApplyButton.SetBounds(sortSaveButton.Left - buttonWidth - gap, rowY, buttonWidth, 27);
            sortPanel.Height = secondSortVisible ? 108 : 76;
        }
        addSortColumn.Click += (_, _) => { secondSortVisible = !secondSortVisible; sortColumn2.Visible = sortDirection2.Visible = sortBlanks2.Visible = secondSortVisible; addSortColumn.Text = secondSortVisible ? "− Remove column" : "＋ Add sort column"; LayoutSort(); RefreshCommandHost(); };
        sortPanel.Controls.AddRange([title, targetLabel, sortTarget, close, sortColumn1, sortDirection1, sortBlanks1, sortColumn2, sortDirection2, sortBlanks2, addSortColumn, sortApplyButton, sortSaveButton, sortRevertButton]);
        sortPanel.Resize += (_, _) => LayoutSort(); LayoutSort();
    }

    private void BuildSqlPanel()
    {
        sqlPanel.Dock = DockStyle.Fill; sqlPanel.Visible = false; sqlPanel.BackColor = Theme.Inset; sqlPanel.BorderStyle = BorderStyle.FixedSingle;
        var header = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Theme.Surface, Padding = Padding.Empty };
        var title = ToolLabel("SQL Console"); title.Font = new Font("Segoe UI", 9F, FontStyle.Bold); title.ForeColor = Theme.Foreground; title.SetBounds(10, 9, 92, 20);
        Button IconButton(UiIcon icon, string accessibleName)
        {
            var button = ToolButton(""); button.Image = UiIcons.Draw(icon, icon == UiIcon.Run ? Theme.Purple : Theme.Foreground); button.AccessibleName = accessibleName; button.FlatAppearance.BorderSize = 0; button.Size = new Size(32, 30); return button;
        }
        var run = IconButton(UiIcon.Run, "Run SQL query (F5 or Ctrl+Enter)"); run.SetBounds(104, 3, 32, 30);
        var clear = IconButton(UiIcon.Clear, "Clear SQL editor"); clear.SetBounds(138, 3, 32, 30);
        var close = ToolButton("×"); close.SetBounds(748, 4, 32, 27); close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.FlatAppearance.BorderSize = 0;
        run.Click += (_, _) => RunSql(); clear.Click += (_, _) => sqlEditor.Clear(); close.Click += (_, _) => ToggleSqlConsole(false);
        sqlFirstRowHeader.SetBounds(180, 9, 118, 20); sqlStatus.SetBounds(310, 9, 420, 20); sqlStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        sqlToolTips.SetToolTip(run, run.AccessibleName); sqlToolTips.SetToolTip(clear, clear.AccessibleName); sqlToolTips.SetToolTip(close, "Close SQL console");
        header.Controls.AddRange([title, run, clear, sqlFirstRowHeader, sqlStatus, close]);

        var body = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 210, SplitterWidth = 4, BackColor = Theme.CurrentLine };
        var sourcesHeader = new Label { Text = "TABLES & COLUMNS", Dock = DockStyle.Top, Height = 28, Padding = new Padding(9, 7, 0, 0), ForeColor = Theme.Comment, BackColor = Theme.Inset, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
        sqlSources.Dock = DockStyle.Fill; sqlSources.BorderStyle = BorderStyle.None; sqlSources.BackColor = Theme.Inset; sqlSources.ForeColor = Theme.Foreground; sqlSources.Font = new Font("Consolas", 9F); sqlSources.IntegralHeight = false;
        sqlSources.DoubleClick += (_, _) => InsertSqlSourceSelection();
        body.Panel1.Controls.Add(sqlSources); body.Panel1.Controls.Add(sourcesHeader);

        var editorHeader = new Label { Text = "QUERY   •   F5 or Ctrl+Enter to run", Dock = DockStyle.Top, Height = 28, Padding = new Padding(9, 7, 0, 0), ForeColor = Theme.Comment, BackColor = Theme.Inset, Font = new Font("Segoe UI", 8F, FontStyle.Bold) };
        sqlEditor.Dock = DockStyle.Fill; sqlEditor.AcceptsReturn = true; sqlEditor.AcceptsTab = true; sqlEditor.ScrollBars = ScrollBars.Both; sqlEditor.WordWrap = false; sqlEditor.Text = "SELECT *\r\nFROM current\r\nLIMIT 100;";
        sqlEditor.KeyDown += (_, e) => { if (e.KeyCode == Keys.F5 || e.Control && e.KeyCode == Keys.Enter) { RunSql(); e.SuppressKeyPress = true; } };
        body.Panel2.Controls.Add(sqlEditor); body.Panel2.Controls.Add(editorHeader);
        sqlPanel.Controls.Add(body); sqlPanel.Controls.Add(header);
    }

    private void RefreshSqlSources()
    {
        sqlSources.BeginUpdate(); sqlSources.Items.Clear();
        string fileName = path is null ? "Untitled" : Path.GetFileName(path);
        sqlSources.Items.Add($"▾ {fileName}");
        foreach (var worksheet in workbook.Sheets)
        {
            sqlSources.Items.Add($"  ▦ {worksheet.Name}");
            var sheet = worksheet.Sheet;
            int usedColumns = Math.Max(1, sheet.Rows.SelectMany(row => row.Select((cell, index) => (cell, index))).Where(item => !string.IsNullOrWhiteSpace(item.cell.Value)).Select(item => item.index + 1).DefaultIfEmpty(1).Max());
            for (int column = 0; column < usedColumns; column++)
            {
                string header = sheet.Rows.Count > 0 && column < sheet.Rows[0].Count && sheet.Rows[0][column].Value.Length > 0 ? sheet.Rows[0][column].Value : $"c{column + 1}";
                sqlSources.Items.Add($"    {ColumnName(column),-4}{header}");
            }
        }
        sqlSources.EndUpdate();
    }

    private void InsertSqlSourceSelection()
    {
        if (sqlSources.SelectedItem is not string selected) return;
        string trimmed = selected.TrimStart();
        if (trimmed.StartsWith('▦')) { sqlEditor.SelectedText = "current"; sqlEditor.Focus(); return; }
        if (selected.Length < 8 || !selected.StartsWith("    ", StringComparison.Ordinal)) return;
        string name = selected[8..].Trim();
        sqlEditor.SelectedText = name.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_') ? $"\"{name.Replace("\"", "\"\"")}\"" : name;
        sqlEditor.Focus();
    }

    private void BuildInfoPanel()
    {
        infoPanel.Dock = DockStyle.Fill; infoPanel.Height = 88; infoPanel.Visible = false; infoPanel.BackColor = Theme.Surface; infoPanel.BorderStyle = BorderStyle.FixedSingle;
        var title = new Label { Name = "InfoTitle", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Theme.Purple, AutoSize = true, Location = new(12, 9) };
        var body = new Label { Name = "InfoBody", ForeColor = Theme.Foreground, AutoEllipsis = true, Location = new(12, 34), Size = new(720, 44), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
        var close = ToolButton("×"); close.SetBounds(748, 7, 32, 27); close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.Click += (_, _) => { infoPanel.Visible = false; RefreshCommandHost(); };
        infoPanel.Controls.AddRange([title, body, close]);
    }

    private void BuildHelpPage()
    {
        helpPage.Dock = DockStyle.Fill; helpPage.Visible = false; helpPage.BackColor = Theme.Inset; helpPage.Margin = Padding.Empty;
        var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Surface };
        var title = new Label { Text = "SheetLite Help", ForeColor = Theme.Purple, Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = true, Location = new(18, 14) };
        helpSearch.PlaceholderText = "Search features, formulas, SQL, shortcuts…"; helpSearch.SetBounds(350, 14, 350, 28); helpSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        helpStatus.SetBounds(708, 20, 120, 22); helpStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var close = ToolButton("×"); close.SetBounds(840, 14, 34, 28); close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.Click += (_, _) => CloseHelpPage();
        header.Controls.AddRange([title, helpSearch, helpStatus, close]);

        helpSplitView.Dock = DockStyle.Fill; helpSplitView.FixedPanel = FixedPanel.Panel1; helpSplitView.SplitterWidth = 4; helpSplitView.BackColor = Theme.CurrentLine;
        helpSplitView.Panel1.BackColor = Theme.Surface; helpSplitView.Panel2.BackColor = Theme.Background;
        helpNavigation.Dock = DockStyle.Fill; helpNavigation.BackColor = Theme.Surface; helpNavigation.ForeColor = Theme.Foreground; helpNavigation.BorderStyle = BorderStyle.None; helpNavigation.Font = new Font("Segoe UI", 10F); helpNavigation.IntegralHeight = false;
        helpDocument.Dock = DockStyle.Fill; helpDocument.BackColor = Theme.Background; helpDocument.ForeColor = Theme.Foreground; helpDocument.BorderStyle = BorderStyle.None; helpDocument.Font = new Font("Segoe UI", 10F); helpDocument.ReadOnly = true; helpDocument.DetectUrls = false; helpDocument.ScrollBars = RichTextBoxScrollBars.Vertical; helpDocument.WordWrap = true; helpDocument.Padding = new Padding(18);
        helpSplitView.Panel1.Padding = new Padding(8, 12, 6, 8); helpSplitView.Panel2.Padding = new Padding(20, 14, 20, 14); helpSplitView.Panel1.Controls.Add(helpNavigation); helpSplitView.Panel2.Controls.Add(helpDocument);
        helpPage.Controls.Add(helpSplitView); helpPage.Controls.Add(header);

        helpNavigation.SelectedIndexChanged += (_, _) => { if (helpNavigation.SelectedItem is HelpSection section) RenderHelpSection(section); };
        helpSearch.TextChanged += (_, _) => UpdateHelpSearch();
        helpSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { CloseHelpPage(); e.SuppressKeyPress = true; } };
        helpPage.Resize += (_, _) =>
        {
            int available = Math.Max(180, helpPage.ClientSize.Width - 560);
            helpSearch.Width = Math.Min(420, available); helpSearch.Left = helpPage.ClientSize.Width - helpSearch.Width - 180;
            helpStatus.Left = helpPage.ClientSize.Width - 170; close.Left = helpPage.ClientSize.Width - 46;
            LayoutHelpNavigation();
        };
        UpdateHelpSearch();
    }

    private void ShowHelpPage(string? sectionTitle = null)
    {
        if (helpPage.Visible) { helpSearch.Focus(); return; }
        helpReturnToWelcome = welcome.Visible;
        if (helpReturnToWelcome) ShowEditor();
        findBar.Visible = filterBar.Visible = sortPanel.Visible = infoPanel.Visible = false; RefreshCommandHost();
        helpPage.Visible = true; helpPage.BringToFront(); LayoutHelpNavigation(); helpSearch.Clear(); UpdateHelpSearch();
        if (!string.IsNullOrWhiteSpace(sectionTitle))
        {
            for (int index = 0; index < helpNavigation.Items.Count; index++)
                if (helpNavigation.Items[index] is HelpSection section && section.Title.Equals(sectionTitle, StringComparison.OrdinalIgnoreCase)) { helpNavigation.SelectedIndex = index; break; }
        }
        helpSearch.Focus();
    }

    private void LayoutHelpNavigation()
    {
        const int minimumNavigationWidth = 170, minimumDocumentWidth = 300;
        if (helpSplitView.ClientSize.Width < minimumNavigationWidth + minimumDocumentWidth + helpSplitView.SplitterWidth) return;
        int desired = helpPage.ClientSize.Width < 820 ? 190 : 230;
        int maximum = helpSplitView.ClientSize.Width - minimumDocumentWidth - helpSplitView.SplitterWidth;
        helpSplitView.SplitterDistance = Math.Max(minimumNavigationWidth, Math.Min(desired, maximum));
    }

    private void CloseHelpPage()
    {
        if (!helpPage.Visible) return;
        helpPage.Visible = false;
        if (helpReturnToWelcome) ShowWelcome();
        else grid.Focus();
        helpReturnToWelcome = false;
    }

    private void UpdateHelpSearch()
    {
        string query = helpSearch.Text.Trim();
        var sections = HelpContent.All.Where(section => query.Length == 0 || section.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || section.Body.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        helpNavigation.BeginUpdate(); helpNavigation.Items.Clear(); foreach (var section in sections) helpNavigation.Items.Add(section); helpNavigation.EndUpdate();
        helpStatus.Text = query.Length == 0 ? $"{sections.Count} topics" : $"{sections.Count} result(s)";
        if (sections.Count > 0) helpNavigation.SelectedIndex = 0;
        else { helpDocument.Clear(); helpDocument.SelectionColor = Theme.Comment; helpDocument.AppendText("No help topics match your search."); }
    }

    private void RenderHelpSection(HelpSection section)
    {
        helpDocument.Clear();
        using var headingFont = new Font("Segoe UI", 16F, FontStyle.Bold); using var bodyFont = new Font("Segoe UI", 10F);
        helpDocument.SelectionColor = Theme.Purple; helpDocument.SelectionFont = headingFont; helpDocument.AppendText(section.Title + "\r\n\r\n");
        helpDocument.SelectionColor = Theme.Foreground; helpDocument.SelectionFont = bodyFont; int bodyStart = helpDocument.TextLength; helpDocument.AppendText(section.Body.Replace("\n", "\r\n"));
        string query = helpSearch.Text.Trim();
        if (query.Length > 0)
        {
            int index = bodyStart;
            while ((index = helpDocument.Text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                helpDocument.Select(index, query.Length); helpDocument.SelectionBackColor = Theme.Purple; helpDocument.SelectionColor = Theme.Background; index += query.Length;
            }
        }
        helpDocument.Select(bodyStart, 0); helpDocument.ScrollToCaret(); helpDocument.Select(0, 0);
    }

    private void ShowInfoPanel(string title, string text)
    {
        if (infoPanel.Controls["InfoTitle"] is Label heading) heading.Text = title; if (infoPanel.Controls["InfoBody"] is Label body) body.Text = text;
        infoPanel.Visible = true; RefreshCommandHost();
    }

    private void ShowWelcomeInfo(string title, string text)
    {
        if (welcome.Controls["WelcomeInfo"] is Control old) welcome.Controls.Remove(old);
        var card = new Panel { Name = "WelcomeInfo", Width = 720, Height = 105, BackColor = Theme.Hover, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.None };
        var heading = new Label { Text = title, ForeColor = Theme.Purple, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Location = new(12, 10) };
        var body = new Label { Text = text, ForeColor = Theme.Foreground, AutoEllipsis = true, Location = new(12, 38), Size = new(650, 52) };
        var close = ToolButton("×"); close.SetBounds(678, 7, 32, 27); close.Click += (_, _) => welcome.Controls.Remove(card); card.Controls.AddRange([heading, body, close]);
        card.Location = new(Math.Max(10, (welcome.ClientSize.Width - card.Width) / 2), Math.Max(10, welcome.ClientSize.Height - card.Height - 28)); welcome.Controls.Add(card); card.BringToFront();
    }

    private Control BuildPane(Label title, Control content, bool primary)
    {
        var layout = primary ? primaryPaneLayout : secondaryPaneLayout; var header = primary ? primaryFileHeader : secondaryFileHeader; var tabs = primary ? primarySheetTabs : secondarySheetTabs; var documentTabs = primary ? primaryDocumentTabs : secondaryDocumentTabs;
        layout.Dock = DockStyle.Fill; layout.ColumnCount = 1; layout.RowCount = 3; layout.Margin = Padding.Empty; layout.Padding = Padding.Empty; layout.BackColor = Theme.Background;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        header.Dock = DockStyle.Fill; header.BackColor = Theme.Surface; header.Margin = Padding.Empty;
        documentTabs.Dock = DockStyle.Fill; documentTabs.FlowDirection = FlowDirection.LeftToRight; documentTabs.WrapContents = false; documentTabs.AutoScroll = true; documentTabs.Margin = Padding.Empty; documentTabs.Padding = Padding.Empty; documentTabs.BackColor = Theme.Surface;
        var headerDivider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.CurrentLine, Enabled = false };
        header.Controls.Add(documentTabs); if (primary) header.Controls.Add(toolbar); header.Controls.Add(headerDivider); headerDivider.BringToFront(); header.Resize += (_, _) => UpdateFileTabChrome();

        tabs.Dock = DockStyle.Fill; tabs.FlowDirection = FlowDirection.LeftToRight; tabs.WrapContents = false; tabs.AutoScroll = true; tabs.BackColor = Theme.Surface; tabs.Padding = new Padding(0, 3, 0, 2); tabs.Margin = Padding.Empty;
        tabs.Paint += (_, e) => { using var divider = new Pen(Theme.CurrentLine); e.Graphics.DrawLine(divider, 0, 0, tabs.ClientSize.Width, 0); };
        content.Dock = DockStyle.Fill; layout.Controls.Add(header, 0, 0); layout.Controls.Add(content, 0, 1); layout.Controls.Add(tabs, 0, 2); return layout;
    }

    private void ConfigureSecondaryGrid()
    {
        secondaryGrid.Dock = DockStyle.Fill; secondaryGrid.BackgroundColor = Theme.CellBackground; secondaryGrid.GridColor = Theme.CurrentLine; secondaryGrid.BorderStyle = BorderStyle.None; secondaryGrid.EnableHeadersVisualStyles = false;
        secondaryGrid.DefaultCellStyle = new() { BackColor = Theme.CellBackground, ForeColor = Theme.Foreground, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Foreground, NullValue = "", Font = Font };
        secondaryGrid.ColumnHeadersDefaultCellStyle = new() { BackColor = Theme.HeaderBackground, ForeColor = Theme.Foreground, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Purple, Alignment = DataGridViewContentAlignment.MiddleCenter }; secondaryGrid.RowHeadersDefaultCellStyle = new() { BackColor = Theme.HeaderBackground, ForeColor = Theme.Comment, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Purple };
        secondaryGrid.AllowUserToAddRows = false; secondaryGrid.ReadOnly = false; secondaryGrid.RowTemplate.Height = 23; secondaryGrid.RowHeadersWidth = 50; secondaryGrid.RowHeadersVisible = true; secondaryGrid.ColumnHeadersHeight = 25; secondaryGrid.SelectionMode = DataGridViewSelectionMode.CellSelect; secondaryGrid.MultiSelect = true;
        secondaryGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        secondaryScrollCorner.BackColor = Theme.Hover; secondaryScrollCorner.Size = new(SystemInformation.VerticalScrollBarWidth, SystemInformation.HorizontalScrollBarHeight); secondaryScrollCorner.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; secondaryScrollCorner.Enabled = false;
        secondaryGrid.Controls.Add(secondaryScrollCorner); secondaryGrid.Resize += (_, _) => PositionScrollCorner(secondaryGrid, secondaryScrollCorner); secondaryGrid.HandleCreated += (_, _) => PositionScrollCorner(secondaryGrid, secondaryScrollCorner);
        secondaryGrid.CellPainting += PaintSecondaryHeader;
        secondaryGrid.CellPainting += PaintSecondarySelection;
        secondaryGrid.CellPainting += PaintPaneDimmer;
        secondaryGrid.Enter += (_, _) => SetActivePane(true); secondaryGrid.MouseDown += (_, _) => SetActivePane(true);
        secondaryGrid.MouseDown += BeginSecondaryFillDrag; secondaryGrid.MouseMove += ContinueSecondaryFillDrag; secondaryGrid.MouseUp += EndSecondaryFillDrag; secondaryGrid.Paint += PaintSecondaryFillPreview;
        secondaryGrid.SelectionChanged += (_, _) => { if (secondaryPaneActive) UpdateStatus(); }; secondaryGrid.CurrentCellChanged += (_, _) => { if (secondaryPaneActive) UpdateStatus(); };
        secondaryGrid.CellParsing += (_, e) => { if (e.Value is not null) { e.Value = e.Value.ToString(); e.ParsingApplied = true; } };
        secondaryGrid.EditingControlShowing += (_, e) => { if (e.Control is TextBox box) { box.BorderStyle = BorderStyle.None; box.BackColor = Theme.Selection; box.ForeColor = Theme.Foreground; } };
    }

    private void PaintSecondaryHeader(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if ((e.RowIndex >= 0 && e.ColumnIndex >= 0) || e.Graphics is null) return;
        bool selected = e.State.HasFlag(DataGridViewElementStates.Selected); using var background = new SolidBrush(selected ? Theme.Selection : Theme.HeaderBackground); e.Graphics.FillRectangle(background, e.CellBounds);
        using var border = new Pen(Theme.CurrentLine); e.Graphics.DrawLine(border, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom); e.Graphics.DrawLine(border, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
        string text = e.RowIndex == -1 && e.ColumnIndex >= 0 ? secondaryGrid.Columns[e.ColumnIndex].HeaderText : e.ColumnIndex == -1 && e.RowIndex >= 0 ? (SecondaryModelRow(e.RowIndex) + 1).ToString() : "";
        Color color = selected ? Theme.Purple : e.RowIndex == -1 ? Theme.Foreground : Theme.Comment; var flags = TextFormatFlags.VerticalCenter | (e.ColumnIndex == -1 ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter) | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(e.Graphics, text, e.CellStyle?.Font ?? Font, Rectangle.Inflate(e.CellBounds, -6, 0), color, flags); e.Handled = true;
    }

    private void PaintSecondarySelection(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || !secondaryGrid[e.ColumnIndex, e.RowIndex].Selected || e.Graphics is null) return;
        e.Paint(e.CellBounds, e.PaintParts);
        bool primary = secondaryGrid.CurrentCell?.RowIndex == e.RowIndex && secondaryGrid.CurrentCell.ColumnIndex == e.ColumnIndex;
        if (primary && !secondaryGrid.IsCurrentCellInEditMode)
        {
            var bounds = Rectangle.Inflate(e.CellBounds, -1, -1);
            using var pen = new Pen(Theme.Purple, 2F); e.Graphics.DrawRectangle(pen, bounds);
        }
        e.Handled = true;
    }

    private Rectangle SecondaryFillHandleGrabArea()
    {
        if (!secondaryPaneActive || !TryGetSecondaryFillRange(out var range)) return Rectangle.Empty;
        Rectangle cell = secondaryGrid.GetCellDisplayRectangle(range.Right, range.Bottom, false);
        return new Rectangle(cell.Right - 11, cell.Bottom - 11, 18, 18);
    }

    private void BeginSecondaryFillDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !SecondaryFillHandleGrabArea().Contains(e.Location) || !TryGetSecondaryFillRange(out secondaryFillSource)) return;
        secondaryFillPrimaryRow = secondaryGrid.CurrentCell?.RowIndex ?? secondaryFillSource.Top; secondaryFillPrimaryColumn = secondaryGrid.CurrentCell?.ColumnIndex ?? secondaryFillSource.Left;
        secondaryFillPreview = secondaryFillSource; secondaryFillDragging = true; secondaryGrid.Capture = true; secondaryGrid.Cursor = Cursors.Cross; secondaryGrid.Invalidate();
    }

    private void ContinueSecondaryFillDrag(object? sender, MouseEventArgs e)
    {
        if (!secondaryFillDragging) { secondaryGrid.Cursor = SecondaryFillHandleGrabArea().Contains(e.Location) ? Cursors.Cross : Cursors.Default; return; }
        RestoreSecondaryFillSourceSelection();
        var hit = secondaryGrid.HitTest(e.X, e.Y); if (hit.RowIndex < 0 || hit.ColumnIndex < 0) return;
        int verticalDistance = hit.RowIndex < secondaryFillSource.Top ? secondaryFillSource.Top - hit.RowIndex : Math.Max(0, hit.RowIndex - secondaryFillSource.Bottom);
        int horizontalDistance = hit.ColumnIndex < secondaryFillSource.Left ? secondaryFillSource.Left - hit.ColumnIndex : Math.Max(0, hit.ColumnIndex - secondaryFillSource.Right);
        secondaryFillPreview = verticalDistance >= horizontalDistance
            ? new CellRange(secondaryFillSource.Left, Math.Min(secondaryFillSource.Top, hit.RowIndex), secondaryFillSource.Right, Math.Max(secondaryFillSource.Bottom, hit.RowIndex))
            : new CellRange(Math.Min(secondaryFillSource.Left, hit.ColumnIndex), secondaryFillSource.Top, Math.Max(secondaryFillSource.Right, hit.ColumnIndex), secondaryFillSource.Bottom);
        secondaryGrid.Invalidate();
    }

    private void EndSecondaryFillDrag(object? sender, MouseEventArgs e)
    {
        if (!secondaryFillDragging) return; secondaryFillDragging = false; secondaryGrid.Capture = false; secondaryGrid.Cursor = Cursors.Default;
        if (secondaryFillPreview != secondaryFillSource) ApplySecondaryFill(secondaryFillSource, secondaryFillPreview); secondaryGrid.Invalidate();
    }

    private void PaintSecondaryFillPreview(object? sender, PaintEventArgs e)
    {
        if (secondaryPaneActive && !secondaryGrid.IsCurrentCellInEditMode && TryGetSecondaryFillRange(out var selection))
        {
            Rectangle selectionBounds = SecondaryCellRangeDisplayRectangle(selection);
            if (secondaryGrid.SelectedCells.Count > 1 && !selectionBounds.IsEmpty)
            {
                using var selectionPen = new Pen(Theme.Purple, 1.5F) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                e.Graphics.DrawRectangle(selectionPen, Rectangle.Inflate(selectionBounds, -1, -1));
            }
            Rectangle cell = secondaryGrid.GetCellDisplayRectangle(selection.Right, selection.Bottom, false);
            Rectangle handle = new(cell.Right - 5, cell.Bottom - 5, 9, 9);
            using var handleFill = new SolidBrush(Theme.Purple); using var handleOutline = new Pen(Theme.CellBackground, 2);
            e.Graphics.FillEllipse(handleFill, handle); e.Graphics.DrawEllipse(handleOutline, handle);
        }
        if (!secondaryFillDragging) return; Rectangle bounds = SecondaryCellRangeDisplayRectangle(secondaryFillPreview); if (bounds.IsEmpty) return;
        using var pen = new Pen(Theme.Purple, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }; e.Graphics.DrawRectangle(pen, Rectangle.Inflate(bounds, -1, -1));
        using var font = new Font("Segoe UI", 12, FontStyle.Bold); TextRenderer.DrawText(e.Graphics, "+", font, new Rectangle(bounds.Right - 22, bounds.Bottom - 24, 20, 20), Theme.Purple, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private CellRange SecondarySelectionRange()
    {
        if (secondaryGrid.SelectedCells.Count == 0 && secondaryGrid.CurrentCell is not null) return new(secondaryGrid.CurrentCell.ColumnIndex, secondaryGrid.CurrentCell.RowIndex, secondaryGrid.CurrentCell.ColumnIndex, secondaryGrid.CurrentCell.RowIndex);
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        foreach (DataGridViewCell cell in secondaryGrid.SelectedCells) { left = Math.Min(left, cell.ColumnIndex); top = Math.Min(top, cell.RowIndex); right = Math.Max(right, cell.ColumnIndex); bottom = Math.Max(bottom, cell.RowIndex); }
        return right < 0 ? default : new(left, top, right, bottom);
    }

    private bool TryGetSecondaryFillRange(out CellRange range)
    {
        range = default; if (secondaryGrid.CurrentCell is null || secondaryGrid.SelectedCells.Count == 0) return false; range = SecondarySelectionRange();
        bool rectangular = secondaryGrid.SelectedCells.Count == range.Width * range.Height, wholeHeaderRange = range.Width == secondaryGrid.ColumnCount || range.Height == secondaryGrid.RowCount;
        return rectangular && !wholeHeaderRange;
    }

    private void RestoreSecondaryFillSourceSelection()
    {
        bool exact = secondaryGrid.SelectedCells.Count == secondaryFillSource.Width * secondaryFillSource.Height;
        if (exact) foreach (DataGridViewCell cell in secondaryGrid.SelectedCells) if (cell.RowIndex < secondaryFillSource.Top || cell.RowIndex > secondaryFillSource.Bottom || cell.ColumnIndex < secondaryFillSource.Left || cell.ColumnIndex > secondaryFillSource.Right) { exact = false; break; }
        if (exact) return; secondaryGrid.ClearSelection();
        for (int row = secondaryFillSource.Top; row <= secondaryFillSource.Bottom; row++) for (int column = secondaryFillSource.Left; column <= secondaryFillSource.Right; column++) secondaryGrid[column, row].Selected = true;
        secondaryGrid.CurrentCell = secondaryGrid[secondaryFillPrimaryColumn, secondaryFillPrimaryRow];
    }

    private Rectangle SecondaryCellRangeDisplayRectangle(CellRange range)
    {
        Rectangle first = secondaryGrid.GetCellDisplayRectangle(range.Left, range.Top, false), last = secondaryGrid.GetCellDisplayRectangle(range.Right, range.Bottom, false);
        return first.IsEmpty || last.IsEmpty ? Rectangle.Empty : Rectangle.FromLTRB(first.Left, first.Top, last.Right, last.Bottom);
    }

    private void ApplySecondaryFill(CellRange source, CellRange target)
    {
        if (secondaryModel is null) return; if (secondarySharesPrimary) PushUndo(); else PushSecondaryUndo(); secondaryLoading = true;
        source = new CellRange(source.Left, SecondaryModelRow(source.Top), source.Right, SecondaryModelRow(source.Bottom)); target = new CellRange(target.Left, SecondaryModelRow(target.Top), target.Right, SecondaryModelRow(target.Bottom));
        if (target.Bottom > source.Bottom)
            for (int column = source.Left; column <= source.Right; column++) { var pattern = Enumerable.Range(source.Top, source.Height).Select(row => secondaryModel.Rows[row][column].Clone()).ToList(); for (int row = source.Bottom + 1; row <= target.Bottom; row++) { int offset = row - source.Bottom - 1; SetSecondaryFilledCell(row, column, pattern, offset, source.Top + offset % source.Height, column); } }
        else if (target.Top < source.Top)
            for (int column = source.Left; column <= source.Right; column++) { var pattern = Enumerable.Range(source.Top, source.Height).Select(row => secondaryModel.Rows[row][column].Clone()).ToList(); for (int row = source.Top - 1; row >= target.Top; row--) { int offset = row - source.Top, patternIndex = Mod(offset, source.Height); SetSecondaryFilledCell(row, column, pattern, offset, source.Top + patternIndex, column); } }
        else if (target.Right > source.Right)
            for (int row = source.Top; row <= source.Bottom; row++) { var pattern = Enumerable.Range(source.Left, source.Width).Select(column => secondaryModel.Rows[row][column].Clone()).ToList(); for (int column = source.Right + 1; column <= target.Right; column++) { int offset = column - source.Right - 1; SetSecondaryFilledCell(row, column, pattern, offset, row, source.Left + offset % source.Width); } }
        else if (target.Left < source.Left)
            for (int row = source.Top; row <= source.Bottom; row++) { var pattern = Enumerable.Range(source.Left, source.Width).Select(column => secondaryModel.Rows[row][column].Clone()).ToList(); for (int column = source.Left - 1; column >= target.Left; column--) { int offset = column - source.Left, patternIndex = Mod(offset, source.Width); SetSecondaryFilledCell(row, column, pattern, offset, row, source.Left + patternIndex); } }
        secondaryLoading = false; RecalculateSecondaryFormulaCells(); secondaryGrid.ClearSelection(); secondaryGrid.Invalidate();
        for (int row = target.Top; row <= target.Bottom; row++) for (int column = target.Left; column <= target.Right; column++) secondaryGrid[column, row].Selected = true;
        if (secondarySharesPrimary)
        {
            model = secondaryModel; RecalculateFormulaCells(); SetDirty(); SetActivePane(true);
        }
        else SetSecondaryDirty();
    }

    private void SetSecondaryFilledCell(int row, int column, List<CellModel> pattern, int offset, int sourceRow, int sourceColumn)
    {
        if (secondaryModel is null) return; secondaryModel.ReplaceCell(row, column, CreateFilledCell(pattern, offset, row, column, sourceRow, sourceColumn)); int displayRow = secondaryPane.DisplayRow(row); if (displayRow >= 0) secondaryGrid.InvalidateCell(column, displayRow);
    }

    private void ShowFindBar(bool showReplace)
    {
        findBar.Visible = true; SetReplaceVisible(showReplace); RefreshCommandHost(); findBox.Focus(); findBox.SelectAll(); UpdateFindStatus();
    }
    private void SetReplaceVisible(bool show) { replaceBox.Visible = show; foreach (Control c in findBar.Controls) if (Equals(c.Tag, "replace")) c.Visible = show; findBar.Height = show ? 70 : 38; replaceToggle.Text = show ? "⌃" : "⌄"; RefreshCommandHost(); }
    private void CloseFindBar() { findBar.Visible = false; RefreshCommandHost(); grid.Focus(); }
    private void RefreshCommandHost() { commandHost.PerformLayout(); editorRoot.PerformLayout(); }

    private void UpdateFindStatus()
    {
        if (string.IsNullOrEmpty(findBox.Text)) { findStatus.Text = "No results"; return; }
        var context = FormulaEngine.CreateContext(model);
        int count = 0;
        foreach (int row in primaryPane.View.VisibleRows)
            for (int c = 0; c < grid.ColumnCount && row < model.Rows.Count && c < model.Rows[row].Count; c++)
                if (model.EvaluatedValue(row, c, context).Contains(findBox.Text, StringComparison.CurrentCultureIgnoreCase)) count++;
        findStatus.Text = count == 0 ? "No results" : $"{count:N0} result{(count == 1 ? "" : "s")}";
    }
    private void FindNext(bool backwards)
    {
        string term = findBox.Text; if (term.Length == 0 || grid.ColumnCount == 0 || primaryPane.View.DisplayRowCount == 0) return;
        var context = FormulaEngine.CreateContext(model);
        var visible = primaryPane.View.VisibleRows; int columns = grid.ColumnCount, total = visible.Count * columns;
        int currentFlat = Math.Clamp(grid.CurrentCell?.RowIndex ?? 0, 0, visible.Count - 1) * columns + Math.Max(0, grid.CurrentCell?.ColumnIndex ?? 0);
        for (int n = 1; n <= total; n++)
        {
            int p = (currentFlat + (backwards ? -n : n) + total * 2) % total, displayRow = p / columns, c = p % columns, modelRow = visible[displayRow];
            if (modelRow < model.Rows.Count && c < model.Rows[modelRow].Count && EvaluatedCellValue(modelRow, c, context).Contains(term, StringComparison.CurrentCultureIgnoreCase))
            { grid.CurrentCell = grid[c, displayRow]; grid.ClearSelection(); grid[c, displayRow].Selected = true; grid.FirstDisplayedScrollingRowIndex = Math.Max(0, displayRow - 2); return; }
        }
    }
    private void ReplaceCurrent() { if (grid.CurrentCell is null || findBox.Text.Length == 0) return; int row = ModelRow(grid.CurrentCell.RowIndex), column = grid.CurrentCell.ColumnIndex; if (row >= model.Rows.Count || column >= model.Rows[row].Count) { FindNext(false); return; } string value = MatchableCellValue(row, column); if (!value.Contains(findBox.Text, StringComparison.CurrentCultureIgnoreCase)) { FindNext(false); return; } PushUndo(); string replaced = ReplaceInsensitive(value, findBox.Text, replaceBox.Text); model.SetCellValue(row, column, replaced); grid.InvalidateCell(column, row); ReapplyDockedFilterIfActive(); SetDirtyCell(row, column); UpdateFindStatus(); }
    private void ReplaceAllDocked() { if (findBox.Text.Length == 0) return; var context = FormulaEngine.CreateContext(model); var matches = new List<(int Row, int Column)>(); for (int r = 0; r < model.Rows.Count; r++) for (int c = 0; c < model.Rows[r].Count; c++) if (MatchableCellValue(r, c, context).Contains(findBox.Text, StringComparison.CurrentCultureIgnoreCase)) matches.Add((r, c)); if (matches.Count == 0) { findStatus.Text = "No results"; return; } PushUndo(); var replacements = matches.Select(match => (match.Row, match.Column, Value: ReplaceInsensitive(MatchableCellValue(match.Row, match.Column, context), findBox.Text, replaceBox.Text))).ToList(); loading = true; foreach (var (row, column, value) in replacements) model.SetCellValue(row, column, value); loading = false; grid.Invalidate(); ReapplyDockedFilterIfActive(); SetDirty(); findStatus.Text = $"Replaced {replacements.Count:N0}"; }

    private void ShowFilterBar() { PopulateColumnTools(); filterBar.Visible = true; RefreshCommandHost(); filterValue.Focus(); }
    private void ToggleFilterBuilder() { filterBuilderExpanded = !filterBuilderExpanded; UpdateFilterBuilderControls(); LayoutFilterBar(); RefreshCommandHost(); }
    private void UpdateFilterBuilderControls() { foreach (Control c in filterBar.Controls) if (Equals(c.Tag, "builder")) c.Visible = filterBuilderExpanded && (c == addFilterCondition || secondFilterVisible); addFilterCondition.Visible = filterBuilderExpanded; addFilterCondition.Text = secondFilterVisible ? "− Remove condition" : "＋ Add condition"; }
    private void ApplyDockedFilter()
    {
        headerFilterColumn = -1; headerFilterValues = null; headerFilterOperator = headerFilterConditionValue = null;
        if (filterColumn.SelectedIndex < 0) return; int firstColumn = filterColumn.SelectedIndex; string firstOperator = filterOperator.Text, firstValue = filterValue.Text;
        if (grid.CurrentCell is not null && grid.CurrentCell.RowIndex > 0) grid.CurrentCell = grid[0, 0];
        primaryPane.ResetViewToSheet();
        var context = FormulaEngine.CreateContext(model);
        var keep = new HashSet<int>();
        for (int r = 1; r < model.Rows.Count; r++)
        {
            bool match = FilterMatch(EvaluatedCellValue(r, firstColumn, context), firstOperator, firstValue);
            if (secondFilterVisible && filterColumn2.SelectedIndex >= 0)
            {
                bool other = FilterMatch(EvaluatedCellValue(r, filterColumn2.SelectedIndex, context), filterOperator2.Text, filterValue2.Text);
                match = filterJoin.Text == "OR" ? match || other : match && other;
            }
            if (match) keep.Add(r);
        }
        primaryPane.View.HideRows(row => row > 0 && !keep.Contains(row));
        int visible = primaryPane.View.DisplayRowCount - 1;
        primaryPane.ApplyView();
        filter = firstValue; MirrorPrimaryRowVisibilityToSharedSecondary(); countLabel.Text = $"{visible:N0} visible rows × {grid.ColumnCount:N0} columns"; UpdateFindStatus();
    }
    private void ReapplyDockedFilterIfActive() { if (headerFilterColumn >= 0 && headerFilterValues is not null) ApplyHeaderValueFilter(headerFilterColumn, headerFilterValues); else if (headerFilterColumn >= 0 && headerFilterOperator is not null) ApplyHeaderConditionFilter(headerFilterColumn, headerFilterOperator, headerFilterConditionValue ?? ""); else if (filter is not null && filterColumn.SelectedIndex >= 0 && grid.RowCount > 0) ApplyDockedFilter(); }
    private static bool FilterMatch(string cell, string op, string value)
    {
        int comparison = CompareCells(cell, value); return op switch { "equals" => string.Equals(cell, value, StringComparison.CurrentCultureIgnoreCase), "not equals" => !string.Equals(cell, value, StringComparison.CurrentCultureIgnoreCase), "starts with" => cell.StartsWith(value, StringComparison.CurrentCultureIgnoreCase), "ends with" => cell.EndsWith(value, StringComparison.CurrentCultureIgnoreCase), ">" => comparison > 0, ">=" => comparison >= 0, "<" => comparison < 0, "<=" => comparison <= 0, "is blank" => string.IsNullOrWhiteSpace(cell), "is not blank" => !string.IsNullOrWhiteSpace(cell), _ => cell.Contains(value, StringComparison.CurrentCultureIgnoreCase) };
    }

    private void ShowSortPanel()
    {
        if (!sortPanel.Visible) { FlushPendingEdits(grid); workbook.ActiveSheet.Sheet = model; sortBaselineWorkbook = workbook.Clone(); sortBaselineDirty = dirty; sortSelectedRows = grid.SelectedCells.Cast<DataGridViewCell>().Select(cell => ModelRow(cell.RowIndex)).Where(row => row > 0 && row < model.Rows.Count).Distinct().Order().ToList(); sortPreviewApplied = false; sortSaveButton.Enabled = sortRevertButton.Enabled = false; }
        PopulateColumnTools(); sortPanel.Visible = true; RefreshCommandHost();
    }
    private void ApplyDockedSort()
    {
        if (sortColumn1.SelectedIndex < 0) return;
        if (sortBaselineWorkbook is null) { ShowSortPanel(); if (sortBaselineWorkbook is null) return; }
        workbook = sortBaselineWorkbook.Clone(); model = workbook.ActiveSheet.Sheet; filter = null; RefreshPrimarySheetTabs(); Render();
        int c1 = sortColumn1.SelectedIndex, factor1 = sortDirection1.Text == "Descending" ? -1 : 1, c2 = sortColumn2.SelectedIndex, factor2 = sortDirection2.Text == "Descending" ? -1 : 1;
        var valueCache = new Dictionary<(int Row, int Column), string>();
        string ValueAt(int row, int column)
        {
            var key = (row, column);
            if (!valueCache.TryGetValue(key, out string? value)) valueCache[key] = value = EvaluatedCellValue(row, column);
            return value;
        }
        int CompareRows(int first, int second)
        {
            int compared = CompareWithBlankOrder(ValueAt(first, c1), ValueAt(second, c1), sortBlanks1.Text == "Blanks first", factor1);
            if (compared == 0 && secondSortVisible && c2 >= 0) compared = CompareWithBlankOrder(ValueAt(first, c2), ValueAt(second, c2), sortBlanks2.Text == "Blanks first", factor2);
            return compared != 0 ? compared : first.CompareTo(second);
        }

        if (sortTarget.Text == "Selection")
        {
            var indices = sortSelectedRows.Where(row => row < model.Rows.Count).ToList();
            if (indices.Count < 2) { countLabel.Text = "Select at least two data rows to sort"; return; }
            var sortedOldIndices = indices.ToList(); sortedOldIndices.Sort(CompareRows);
            var rowOrder = Enumerable.Range(0, model.Rows.Count).ToArray();
            for (int i = 0; i < indices.Count; i++) rowOrder[indices[i]] = sortedOldIndices[i];
            bool changed = !rowOrder.SequenceEqual(Enumerable.Range(0, model.Rows.Count));
            if (changed) ApplyRowOrder(rowOrder);
            FinishSortPreview(indices[0], c1, changed); return;
        }

        var body = Enumerable.Range(1, model.Rows.Count - 1).Where(index => model.Rows[index].Any(cell => cell.Value.Length > 0)).ToList(); var blanks = Enumerable.Range(1, model.Rows.Count - 1).Where(index => model.Rows[index].All(cell => cell.Value.Length == 0)).ToList();
        body.Sort(CompareRows); int[] rowOrderAll = [0, .. body, .. blanks]; bool allChanged = !rowOrderAll.SequenceEqual(Enumerable.Range(0, model.Rows.Count));
        if (allChanged) ApplyRowOrder(rowOrderAll); FinishSortPreview(1, c1, allChanged);
    }
    private void FinishSortPreview(int row, int column, bool changed)
    {
        Render(); if (grid.RowCount > 0 && grid.ColumnCount > 0) grid.CurrentCell = grid[Math.Clamp(column, 0, grid.ColumnCount - 1), Math.Clamp(row, 0, grid.RowCount - 1)]; dirty = changed || sortBaselineDirty; sortPreviewApplied = changed; sortSaveButton.Enabled = changed; sortRevertButton.Enabled = true; UpdateTitle(); UpdateStatus(); countLabel.Text = changed ? "Sort preview — Save sort or Revert" : "Already in the requested order";
    }
    private void SaveSortPreview()
    {
        if (sortBaselineWorkbook is not null && sortPreviewApplied)
        {
            undo.Push(new WorkbookSnapshotStep(sortBaselineWorkbook.Clone())); if (undo.Count > 40) { var keep = undo.Take(40).Reverse().ToArray(); undo.Clear(); foreach (var snapshot in keep) undo.Push(snapshot); } redo.Clear(); dirty = true; UpdateTitle(); UpdateStatus();
        }
        sortBaselineWorkbook = null; sortSelectedRows.Clear(); sortPreviewApplied = false; sortPanel.Visible = false; RefreshCommandHost();
    }
    private void RevertSortPreview()
    {
        if (sortBaselineWorkbook is not null) { workbook = sortBaselineWorkbook.Clone(); model = workbook.ActiveSheet.Sheet; dirty = sortBaselineDirty; filter = null; RefreshPrimarySheetTabs(); Render(); UpdateTitle(); UpdateStatus(); }
        sortBaselineWorkbook = null; sortSelectedRows.Clear(); sortPreviewApplied = false; sortPanel.Visible = false; RefreshCommandHost();
    }
    private static int CompareWithBlankOrder(string a, string b, bool blanksFirst, int direction) { bool ae = string.IsNullOrWhiteSpace(a), be = string.IsNullOrWhiteSpace(b); if (ae != be) return ae == blanksFirst ? -1 : 1; return CompareCells(a, b) * direction; }

    private void PopulateColumnTools()
    {
        var names = Enumerable.Range(0, grid.ColumnCount).Select(c => model.Rows.Count > 0 && c < model.Rows[0].Count && model.Rows[0][c].Value.Length > 0 ? $"{ColumnName(c)} — {model.Rows[0][c].Value}" : ColumnName(c)).Cast<object>().ToArray();
        foreach (var combo in new[] { filterColumn, filterColumn2, sortColumn1, sortColumn2 })
        {
            string? old = combo.SelectedItem?.ToString();
            string? header = old?.Contains(" — ", StringComparison.Ordinal) == true ? old[(old.IndexOf(" — ", StringComparison.Ordinal) + 3)..] : null;
            combo.Items.Clear(); combo.Items.AddRange(names); if (combo.Items.Count == 0) continue;
            int semanticIndex = header is null ? -1 : Array.FindIndex(names, item => item.ToString()?.EndsWith(" — " + header, StringComparison.CurrentCultureIgnoreCase) == true);
            combo.SelectedIndex = semanticIndex >= 0 ? semanticIndex : Math.Clamp(grid.CurrentCell?.ColumnIndex ?? 0, 0, combo.Items.Count - 1);
        }
    }

    private void RefreshPrimarySheetTabs()
    {
        if (primarySheetTabs.IsDisposed) return;
        NativeRedraw.Run(primarySheetTabs, () =>
        {
            primarySheetTabs.Controls.Clear();
            for (int index = 0; index < workbook.Sheets.Count; index++) primarySheetTabs.Controls.Add(BuildSheetTab(workbook.Sheets[index].Name, index, workbook.ActiveSheetIndex == index, primary: true));
            var add = ToolButton("＋"); add.AccessibleName = "Add worksheet"; add.Width = 31; add.Height = 25; add.Margin = new Padding(2, 0, 0, 0); add.FlatAppearance.BorderSize = 0; add.BackColor = Theme.Surface; add.Click += (_, _) => AddPrimarySheet(); ConfigureWorksheetBarDrop(add, primary: true); AttachExternalDropTarget(add, FileDropZone.PrimarySheetBar); primarySheetTabs.Controls.Add(add);
        });
    }

    private void RefreshSecondarySheetTabs()
    {
        if (secondarySheetTabs.IsDisposed) return;
        NativeRedraw.Run(secondarySheetTabs, () =>
        {
            secondarySheetTabs.Controls.Clear();
            if (secondaryWorkbook is not null) for (int index = 0; index < secondaryWorkbook.Sheets.Count; index++) secondarySheetTabs.Controls.Add(BuildSheetTab(secondaryWorkbook.Sheets[index].Name, index, secondaryWorkbook.ActiveSheetIndex == index, primary: false));
            if (secondaryWorkbook is not null)
            {
                var add = ToolButton("＋"); add.AccessibleName = "Add worksheet to right pane"; add.Width = 31; add.Height = 25; add.Margin = new Padding(2, 0, 0, 0); add.FlatAppearance.BorderSize = 0; add.BackColor = Theme.Surface; add.Click += (_, _) => AddSecondarySheet(); ConfigureWorksheetBarDrop(add, primary: false); AttachExternalDropTarget(add, FileDropZone.SecondarySheetBar); secondarySheetTabs.Controls.Add(add);
            }
        });
    }

    private Control BuildSheetTab(string name, int index, bool active, bool primary)
    {
        int textWidth = Math.Clamp(TextRenderer.MeasureText(name, Font).Width + 18, 64, 170); const int closeWidth = 24;
        var tab = new Panel { Width = textWidth + closeWidth, Height = 25, Margin = Padding.Empty, BackColor = active ? Theme.Inset : Theme.Surface, Cursor = Cursors.Hand };
        var label = new Label { Text = name, Dock = DockStyle.Fill, ForeColor = active ? Theme.Purple : Theme.Foreground, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true, Padding = new Padding(6, 0, closeWidth + 2, 0) };
        label.Click += (_, _) => { SetActivePane(!primary); if (primary) SwitchPrimarySheet(index); else SwitchSecondarySheet(index); }; if (primary) label.DoubleClick += (_, _) => BeginRenameSheet(index, tab, label);
        tab.Controls.Add(label);
        var close = ToolButton("×"); close.SetBounds(tab.Width - closeWidth - 1, 2, closeWidth, tab.Height - 3); close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.FlatAppearance.BorderSize = 0; close.BackColor = tab.BackColor;
        close.Click += (_, _) =>
        {
            if (primary) { if (workbook.Sheets.Count > 1) { PushWorkbookStructureUndo(); DeletePrimarySheet(index); } else ClosePrimaryDocumentAt(primaryDocumentIndex); }
            else if (secondaryWorkbook?.Sheets.Count > 1) DeleteSecondarySheet(index);
            else CloseSecondaryDocumentAt(secondaryDocumentIndex);
        };
        tab.Controls.Add(close); close.BringToFront();
        tab.Paint += (_, e) => { using var divider = new Pen(Theme.CurrentLine); e.Graphics.DrawLine(divider, tab.ClientSize.Width - 1, 0, tab.ClientSize.Width - 1, tab.ClientSize.Height); };
        if (active) { var accent = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Theme.Purple, Enabled = false }; tab.Controls.Add(accent); accent.BringToFront(); }
        ConfigureSheetTabDrag(tab, label, index, primary);
        return tab;
    }

    private void AddPrimarySheet()
    {
        PushUndo(); PushWorkbookStructureUndo(); string name = workbook.NextSheetName(); var sheet = new SheetModel(); sheet.EnsureSize(100, 26); workbook.Sheets.Add(new(name, sheet)); workbook.ActiveSheetIndex = workbook.Sheets.Count - 1; model = sheet; filter = null; RefreshPrimarySheetTabs(); Render(); SetDirty();
    }

    private void DeletePrimarySheet(int index)
    {
        if (workbook.Sheets.Count <= 1 || index < 0 || index >= workbook.Sheets.Count) return; PushUndo(); PushWorkbookStructureUndo();
        workbook.Sheets.RemoveAt(index); if (index < workbook.ActiveSheetIndex) workbook.ActiveSheetIndex--; else if (index == workbook.ActiveSheetIndex) workbook.ActiveSheetIndex = Math.Min(index, workbook.Sheets.Count - 1);
        model = workbook.ActiveSheet.Sheet; filter = null; RefreshPrimarySheetTabs(); Render(); SetDirty(); countLabel.Text = "Worksheet removed — Undo to restore";
    }

    private void AddSecondarySheet()
    {
        if (secondaryWorkbook is null) return;
        if (secondarySharesPrimary) { AddPrimarySheet(); SetActivePane(true); return; }
        PushSecondaryUndo(); if (ActiveSecondarySession is not null && !secondarySharesPrimary) { ClosePendingUndoStep(ActiveSecondarySession.Undo, secondaryModel!); ActiveSecondarySession.Undo.Push(new WorkbookSnapshotStep(secondaryWorkbook.Clone())); } string name = secondaryWorkbook.NextSheetName(); var sheet = new SheetModel(); sheet.EnsureSize(100, 26); secondaryWorkbook.Sheets.Add(new(name, sheet)); secondaryWorkbook.ActiveSheetIndex = secondaryWorkbook.Sheets.Count - 1; secondaryModel = sheet; RenderSecondaryModel(); RefreshSecondarySheetTabs(); SetSecondaryDirty(); SetActivePane(true);
    }

    private void DeleteSecondarySheet(int index)
    {
        if (secondaryWorkbook is null || secondaryWorkbook.Sheets.Count <= 1 || index < 0 || index >= secondaryWorkbook.Sheets.Count) return;
        if (secondarySharesPrimary) { DeletePrimarySheet(index); SetActivePane(true); return; }
        PushSecondaryUndo(); if (ActiveSecondarySession is not null && !secondarySharesPrimary) { ClosePendingUndoStep(ActiveSecondarySession.Undo, secondaryModel!); ActiveSecondarySession.Undo.Push(new WorkbookSnapshotStep(secondaryWorkbook.Clone())); } secondaryWorkbook.Sheets.RemoveAt(index); if (index < secondaryWorkbook.ActiveSheetIndex) secondaryWorkbook.ActiveSheetIndex--; else if (index == secondaryWorkbook.ActiveSheetIndex) secondaryWorkbook.ActiveSheetIndex = Math.Min(index, secondaryWorkbook.Sheets.Count - 1); secondaryModel = secondaryWorkbook.ActiveSheet.Sheet; RenderSecondaryModel(); RefreshSecondarySheetTabs(); SetSecondaryDirty(); SetActivePane(true);
    }

    private void SwitchPrimarySheet(int index)
    {
        SetActivePane(false); if (index < 0 || index >= workbook.Sheets.Count || index == workbook.ActiveSheetIndex) { UpdateStatus(); return; } if (sortBaselineWorkbook is not null) SaveSortPreview(); FlushPendingEdits(grid); workbook.ActiveSheet.Sheet = model; workbook.ActiveSheetIndex = index; model = workbook.ActiveSheet.Sheet; filter = null; RefreshPrimarySheetTabs(); Render(); UpdateTitle(); UpdateStatus();
    }

    private void SwitchSecondarySheet(int index)
    {
        SetActivePane(true); if (secondaryWorkbook is null || index < 0 || index >= secondaryWorkbook.Sheets.Count) return;
        if (secondarySharesPrimary) { SwitchPrimarySheet(index); SetActivePane(true); return; }
        FlushPendingEdits(secondaryGrid); secondaryWorkbook.ActiveSheet.Sheet = secondaryModel!; secondaryWorkbook.ActiveSheetIndex = index; secondaryModel = secondaryWorkbook.ActiveSheet.Sheet; RefreshSecondarySheetTabs(); RenderSecondaryModel(); UpdateStatus();
    }

    private void BeginRenameSheet(int index, Control tab, Control label)
    {
        if (index < 0 || index >= workbook.Sheets.Count) return; label.Visible = false; var editor = ToolTextBox(); editor.Text = workbook.Sheets[index].Name; editor.Dock = DockStyle.Fill; editor.Margin = Padding.Empty; tab.Controls.Add(editor); editor.BringToFront(); editor.Focus(); editor.SelectAll(); bool completed = false;
        void Finish(bool save)
        {
            if (completed) return; completed = true;
            if (save && editor.Text.Trim().Length > 0) { PushUndo(); PushWorkbookStructureUndo(); workbook.Sheets[index].Name = workbook.UniqueSheetName(editor.Text, index); SetDirty(); }
            RefreshPrimarySheetTabs();
        }
        editor.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Finish(true); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) { Finish(false); e.SuppressKeyPress = true; } };
        editor.LostFocus += (_, _) => Finish(true);
    }

    private void UpdateSplitChrome()
    {
        primaryPaneLayout.RowStyles[0].Height = 35; secondaryPaneLayout.RowStyles[0].Height = 35; primaryFileHeader.Visible = true; secondaryFileHeader.Visible = !splitView.Panel2Collapsed; if (splitView.Panel2Collapsed) secondaryPaneActive = false; SetActivePane(secondaryPaneActive); UpdateFileTabChrome();
    }

    private void UpdateFileTabChrome()
    {
        primaryDocumentTab.IsDirty = dirty; secondaryDocumentTab.IsDirty = secondarySharesPrimary ? dirty : secondaryDirty;
        if (toolbar.Parent is Panel activeHeader)
            toolbar.Width = Math.Min(276, Math.Max(164, activeHeader.ClientSize.Width - 108));
        foreach (var pair in new[] { (Header: primaryFileHeader, Tabs: primaryDocumentTabs), (Header: secondaryFileHeader, Tabs: secondaryDocumentTabs) })
        {
            int toolbarWidth = toolbar.Parent == pair.Header ? toolbar.Width : 0; int available = Math.Max(88, pair.Header.ClientSize.Width - toolbarWidth);
            pair.Tabs.Width = available; foreach (DocumentTab tab in pair.Tabs.Controls.OfType<DocumentTab>()) tab.Height = pair.Header.ClientSize.Height;
        }
    }

    private void SetActivePane(bool secondary)
    {
        secondaryPaneActive = secondary && !splitView.Panel2Collapsed;
        primaryDocumentTab.IsActive = !secondaryPaneActive; secondaryDocumentTab.IsActive = secondaryPaneActive;
        var targetHeader = secondaryPaneActive ? secondaryFileHeader : primaryFileHeader;
        if (toolbar.Parent != targetHeader) { toolbar.Parent?.Controls.Remove(toolbar); targetHeader.Controls.Add(toolbar); toolbar.Dock = DockStyle.Right; toolbar.BringToFront(); }
        grid.Invalidate(); secondaryGrid.Invalidate(); primaryFileHeader.Invalidate(true); secondaryFileHeader.Invalidate(true);
        UpdateFileTabChrome(); UpdateStatus();
    }

    private void ClosePrimaryDocument()
    {
        ResolveSortPreviewBeforePrimaryDocumentChange(); if (!ConfirmLoseChanges() || !CloseSplitView()) return; path = null; dirty = false; filter = null; undo = new(); redo = new(); workbook = WorkbookModel.CreateBlank(); model = workbook.ActiveSheet.Sheet; InitializeDocumentSessions(); RefreshPrimarySheetTabs(); RefreshDocumentTabs(); UpdateTitle(); ShowWelcome();
    }

    private bool CloseSplitView()
    {
        if (!ConfirmAllSecondaryDocumentsClose()) return false;
        SetActivePane(false); splitView.Panel2Collapsed = true; secondaryWorkbook = null; secondaryModel = null; secondaryPath = null; secondaryDirty = secondarySharesPrimary = false; secondaryDocuments.Clear(); secondaryDocumentIndex = 0; secondaryGrid.Columns.Clear(); RefreshSecondarySheetTabs(); RefreshDocumentTabs(); UpdateSplitChrome(); return true;
    }

    private void ToggleSqlConsole(bool? visible = null) { bool show = visible ?? !sqlPanel.Visible; sqlPanel.Visible = show; workspaceLayout.RowStyles[1].Height = show ? 300 : 0; if (show) { RefreshSqlSources(); sqlEditor.Focus(); } workspaceLayout.PerformLayout(); }

    private void OpenSharedSplit()
    {
        FlushPendingEdits(grid); workbook.ActiveSheet.Sheet = model; secondaryDocuments.Clear(); secondaryDocumentIndex = 0; secondaryWorkbook = workbook; secondaryModel = model; secondaryPath = path; secondaryDirty = false; secondarySharesPrimary = true;
        UpdateSecondaryTitle(); RenderSecondaryModel(); RefreshSecondarySheetTabs(); ShowSecondaryPane();
    }

    private void ToggleSplitView() { if (splitView.Panel2Collapsed) OpenSharedSplit(); else CloseSplitView(); }

    private void ShowSecondaryPane()
    {
        splitView.Panel2Collapsed = false; splitView.SplitterDistance = Math.Max(250, splitView.Width / 2); RefreshDocumentTabs(); UpdateSplitChrome(); SetActivePane(true);
    }

    private void OpenFileInSplitPane(bool primary)
    {
        using var dialog = new OpenFileDialog { Filter = SpreadsheetOpenFilter }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (primary)
            {
                if (!ConfirmPrimarySaveBeforeReplace()) return;
                if (splitView.Panel2Collapsed) OpenSharedSplit();
                if (secondarySharesPrimary)
                {
                    FlushPendingEdits(grid); workbook.ActiveSheet.Sheet = model; secondaryWorkbook = workbook; secondaryModel = model; secondaryPath = path; secondaryDirty = dirty; secondarySharesPrimary = false; secondaryDocuments.Clear(); secondaryDocuments.Add(new(secondaryWorkbook, secondaryPath, secondaryDirty)); secondaryDocumentIndex = 0; UpdateSecondaryTitle();
                }
                OpenFile(dialog.FileName); SetActivePane(false);
            }
            else
            {
                if (!splitView.Panel2Collapsed && !ConfirmSecondaryClose()) return;
                if (PathsEqual(dialog.FileName, path)) { OpenSharedSplit(); return; }
                secondaryWorkbook = LoadWorkbook(dialog.FileName); secondaryModel = secondaryWorkbook.ActiveSheet.Sheet; secondaryPath = dialog.FileName; secondaryDirty = secondarySharesPrimary = false; ReplaceActiveSecondaryDocument();
                UpdateSecondaryTitle(); RenderSecondaryModel(); RefreshSecondarySheetTabs(); ShowSecondaryPane();
            }
        }
        catch (Exception ex) { ShowNotice("Split open failed", "Could not open the selected file. " + ex.Message); }
    }

    private static WorkbookModel LoadWorkbook(string file) => Path.GetExtension(file).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? XlsxCodec.LoadWorkbook(file) : WorkbookModel.FromSheet(CsvCodec.Load(file), "Sheet1");

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try { return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(first, second, StringComparison.OrdinalIgnoreCase); }
    }

    private void RenderSecondaryModel(bool preserveViewport = false)
    {
        if (secondaryModel is null) return;
        int firstRow = preserveViewport && secondaryGrid.RowCount > 0 ? Math.Max(0, secondaryGrid.FirstDisplayedScrollingRowIndex) : 0;
        int firstColumn = preserveViewport && secondaryGrid.ColumnCount > 0 ? Math.Max(0, secondaryGrid.FirstDisplayedScrollingColumnIndex) : 0;
        int currentRow = preserveViewport ? secondaryGrid.CurrentCell?.RowIndex ?? 0 : 0, currentColumn = preserveViewport ? secondaryGrid.CurrentCell?.ColumnIndex ?? 0 : 0;
        FlushPendingEdits(secondaryGrid);
        secondaryLoading = true; secondaryPane.RenderSheet(secondaryModel);
        ApplySecondaryFreeze(); secondaryLoading = false;
        if (secondaryGrid.RowCount > 0 && secondaryGrid.ColumnCount > 0)
        {
            secondaryGrid.CurrentCell = secondaryGrid[Math.Clamp(currentColumn, 0, secondaryGrid.ColumnCount - 1), Math.Clamp(currentRow, 0, secondaryGrid.RowCount - 1)];
            try { secondaryGrid.FirstDisplayedScrollingRowIndex = Math.Clamp(firstRow, 0, secondaryGrid.RowCount - 1); secondaryGrid.FirstDisplayedScrollingColumnIndex = Math.Clamp(firstColumn, 0, secondaryGrid.ColumnCount - 1); } catch { }
        }
    }

    /// <summary>Repaint pass: display values are recomputed lazily through the version-keyed data-source context.</summary>
    private void RecalculateSecondaryFormulaCells() => secondaryGrid.Invalidate();

    private bool OnSecondaryEditStarting(int row, int column)
    {
        if (secondaryModel is null || secondaryLoading) return false;
        if (secondarySharesPrimary) PushUndo(); else PushSecondaryUndo();
        return true;
    }

    private void OnSecondaryEditFinished(int row, int column) => UpdateStatus();

    private void OnSecondaryCellCommitted(int row, int column)
    {
        if (secondaryModel is null) return;
        if (secondaryModel.IsFormula(row, column)) { var result = FormulaEngine.Evaluate(secondaryModel, row, column); typeLabel.Text = result.Success ? "Formula" : result.Error ?? "Formula error"; }
        RecalculateSecondaryFormulaCells();
        if (secondarySharesPrimary) { grid.Invalidate(); SetDirtyCell(row, column); }
        else SetSecondaryDirty();
    }

    private void ApplySecondaryFreeze()
    {
        if (secondaryModel is null) return; foreach (DataGridViewColumn column in secondaryGrid.Columns) column.Frozen = column.Index < secondaryModel.FrozenColumns; foreach (DataGridViewRow row in secondaryGrid.Rows) row.Frozen = row.Index < secondaryModel.FrozenRows;
    }

    private void RefreshSharedSecondaryFromModel()
    {
        if (!secondarySharesPrimary || splitView.Panel2Collapsed || secondaryLoading) return;
        secondaryWorkbook = workbook; secondaryModel = model; secondaryPath = path;
        int rows = Math.Max(100, model.RowCount), columns = Math.Max(26, model.ColumnCount);
        if (secondaryGrid.RowCount != rows || secondaryGrid.ColumnCount != columns) RenderSecondaryModel(preserveViewport: true);
        else { ApplySecondaryFreeze(); MirrorPrimaryRowVisibilityToSharedSecondary(); }
        RefreshSecondarySheetTabs(); UpdateSecondaryTitle();
    }

    private void RefreshSharedSecondaryCell(int row, int column)
    {
        if (!secondarySharesPrimary || splitView.Panel2Collapsed || secondaryLoading) return;
        secondaryWorkbook = workbook; secondaryModel = model; secondaryPath = path;
        if (secondaryGrid.RowCount != Math.Max(100, model.RowCount) || secondaryGrid.ColumnCount != Math.Max(26, model.ColumnCount)) { RenderSecondaryModel(preserveViewport: true); return; }
        MirrorPrimaryRowVisibilityToSharedSecondary();
    }

    private void MirrorPrimaryRowVisibilityToSharedSecondary()
    {
        if (!secondarySharesPrimary || splitView.Panel2Collapsed || secondaryGrid.RowCount == 0 || secondaryGrid.ColumnCount == 0) return;
        // The shared pane mirrors the primary view: same sheet, same filtered subset.
        var hidden = primaryPane.View.VisibleRows.ToHashSet();
        secondaryPane.View.Reset(Math.Max(100, model.RowCount), Math.Max(26, model.ColumnCount));
        secondaryPane.View.HideRows(row => !hidden.Contains(row));
        secondaryPane.ApplyView();
        if (secondaryGrid.CurrentCell is not null && !primaryPane.View.IsRowVisible(primaryPane.ModelRow(Math.Min(secondaryGrid.CurrentCell.RowIndex, primaryPane.View.DisplayRowCount - 1))))
            secondaryGrid.CurrentCell = secondaryGrid[0, 0];
        secondaryGrid.Invalidate();
    }

    private void RebindSharedSecondary()
    {
        if (!secondarySharesPrimary) return; secondaryWorkbook = workbook; secondaryModel = model; secondaryPath = path; secondaryDirty = false; UpdateSecondaryTitle();
    }

    private void SetSecondaryDirty() { secondaryDirty = true; CaptureSecondaryDocument(); RefreshDocumentTabs(); UpdateFileTabChrome(); UpdateStatus(); }

    private void MirrorSecondarySelectionToPrimary()
    {
        if (!secondarySharesPrimary || secondaryGrid.CurrentCell is null) return; grid.ClearSelection();
        foreach (DataGridViewCell cell in secondaryGrid.SelectedCells) if (cell.RowIndex < grid.RowCount && cell.ColumnIndex < grid.ColumnCount) grid[cell.ColumnIndex, cell.RowIndex].Selected = true;
        grid.CurrentCell = grid[Math.Min(secondaryGrid.CurrentCell.ColumnIndex, grid.ColumnCount - 1), Math.Min(secondaryGrid.CurrentCell.RowIndex, grid.RowCount - 1)];
    }

    private void SecondaryCopy() { if (secondaryGrid.GetCellCount(DataGridViewElementStates.Selected) > 0 && secondaryGrid.GetClipboardContent() is DataObject data) Clipboard.SetDataObject(data); }
    private void SecondaryCut() { SecondaryCopy(); SecondaryDeleteContents(); }
    private void SecondaryPaste()
    {
        if (secondaryModel is null || secondaryGrid.CurrentCell is null || !Clipboard.ContainsText()) return; if (secondarySharesPrimary) PushUndo(); else PushSecondaryUndo();
        string[][] rows = Clipboard.GetText().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(line => line.Split('\t')).ToArray(); int startRow = secondaryGrid.CurrentCell.RowIndex, startColumn = secondaryGrid.CurrentCell.ColumnIndex;
        secondaryModel.EnsureSize(startRow + rows.Length, startColumn + rows.Max(row => row.Length)); if (secondaryGrid.RowCount < secondaryModel.Rows.Count || secondaryGrid.ColumnCount < secondaryModel.ColumnCount) RenderSecondaryModel(preserveViewport: true);
        secondaryLoading = true; for (int r = 0; r < rows.Length; r++) for (int c = 0; c < rows[r].Length; c++) secondaryModel.SetCellValue(SecondaryModelRow(startRow) + r, startColumn + c, rows[r][c]); secondaryLoading = false; RecalculateSecondaryFormulaCells();
        if (secondarySharesPrimary) { model = secondaryModel; Render(); SetDirty(); SetActivePane(true); } else SetSecondaryDirty();
    }
    private void SecondaryDeleteContents()
    {
        if (secondaryModel is null || secondaryGrid.SelectedCells.Count == 0) return; if (secondarySharesPrimary) PushUndo(); else PushSecondaryUndo(); secondaryLoading = true;
        foreach (DataGridViewCell cell in secondaryGrid.SelectedCells) secondaryModel.SetCellValue(SecondaryModelRow(cell.RowIndex), cell.ColumnIndex, ""); secondaryGrid.Invalidate(); RecalculateSecondaryFormulaCells(); secondaryLoading = false;
        if (secondarySharesPrimary) { model = secondaryModel; grid.Invalidate(); RecalculateFormulaCells(); SetDirty(); } else SetSecondaryDirty();
    }

    private void SaveSecondary() { if (secondaryPath is null || !File.Exists(secondaryPath)) SaveSecondaryAs(); else TrySaveSecondaryTo(secondaryPath); }
    private bool SaveSecondaryAs()
    {
        using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV (UTF-8) (*.csv)|*.csv", DefaultExt = "xlsx", AddExtension = true, FileName = secondaryPath is null ? "Untitled.xlsx" : Path.GetFileName(secondaryPath) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false; return TrySaveSecondaryTo(dialog.FileName);
    }
    private bool TrySaveSecondaryTo(string target)
    {
        if (secondaryWorkbook is null || secondaryModel is null) return false;
        try { FlushPendingEdits(secondaryGrid); secondaryWorkbook.ActiveSheet.Sheet = secondaryModel; UseWaitCursor = true; if (Path.GetExtension(target).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) XlsxCodec.SaveWorkbook(target, secondaryWorkbook); else CsvCodec.Save(target, secondaryModel); secondaryPath = target; secondaryDirty = false; UpdateSecondaryTitle(); UpdateFileTabChrome(); UpdateStatus(); return true; }
        catch (Exception ex) { ShowNotice("Save failed", "Could not save the right-pane file. " + ex.Message); return false; }
        finally { UseWaitCursor = false; }
    }

    private bool ConfirmSecondaryClose()
    {
        if (splitView.Panel2Collapsed || secondarySharesPrimary || !secondaryDirty) return true; string name = secondaryPath is null ? "the right-pane document" : Path.GetFileName(secondaryPath);
        DialogResult result = MessageBox.Show(this, $"Save changes to {name} before closing the split?", "SheetLite", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return result == DialogResult.No || result == DialogResult.Yes && (secondaryPath is not null && File.Exists(secondaryPath) ? TrySaveSecondaryTo(secondaryPath) : SaveSecondaryAs());
    }

    private bool ConfirmPrimarySaveBeforeReplace()
    {
        return ConfirmLoseChanges();
    }

    private void UpdateSecondaryTitle()
    {
        string name = secondaryPath is null ? (secondarySharesPrimary ? (path is null ? "Untitled" : Path.GetFileName(path)) : "Untitled") : Path.GetFileName(secondaryPath); secondaryPaneTitle.Text = name; CaptureSecondaryDocument(); RefreshDocumentTabs(); secondaryDocumentTab.DocumentTitle = name; secondaryDocumentTab.IsDirty = secondarySharesPrimary ? dirty : secondaryDirty;
    }

    private void RunSql()
    {
        FlushPendingEdits(grid); var result = SqlQueryEngine.Execute(model, sqlEditor.Text, new SqlQueryOptions { FirstRowIsHeader = sqlFirstRowHeader.Checked });
        if (!result.Success) { sqlStatus.Text = "Error: " + result.Error; return; }
        if (!splitView.Panel2Collapsed && !secondarySharesPrimary) { CaptureSecondaryDocument(); } else secondaryDocuments.Clear();
        secondaryModel = result.ToSheetModel(); secondaryWorkbook = WorkbookModel.FromSheet(secondaryModel, "Results"); secondaryPath = null; secondaryDirty = secondarySharesPrimary = false; secondaryDocuments.Add(new(secondaryWorkbook)); secondaryDocumentIndex = secondaryDocuments.Count - 1; secondaryPaneTitle.Text = "SQL result"; RenderSecondaryModel(); RefreshSecondarySheetTabs(); RefreshDocumentTabs(); ShowSecondaryPane(); sqlStatus.Text = $"Returned {result.Rows.Count:N0} row(s)";
    }
}
