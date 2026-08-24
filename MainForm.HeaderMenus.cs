using System.Text;
using System.Text.Json;

namespace SheetLite;

internal sealed partial class MainForm
{
    private enum HeaderCopyFormat { Csv, Raw, Markdown, MarkdownNoHeader, Html, HtmlNoHeader, JsonArrays, JsonObjects, SqlInsert }

    private void BuildRowHeaderMenu()
    {
        ConfigureContextMenu(rowMenu);
        rowMenu.Items.Add(HeaderMenuItem("Sort selected rows…", () => { ShowSortPanel(); sortTarget.SelectedItem = "Selection"; }));
        rowMenu.Items.Add(new ToolStripSeparator());
        rowMenu.Items.Add(HeaderMenuItem("Insert row above", InsertRow, "Ctrl+Shift+↑"));
        rowMenu.Items.Add(HeaderMenuItem("Insert row below", InsertRowBelow, "Ctrl+Shift+↓"));
        rowMenu.Items.Add(HeaderMenuItem("Delete selected row(s)", DeleteRows, "Ctrl+−"));
        rowMenu.Items.Add(HeaderMenuItem("Delete duplicate rows", DeleteDuplicateRows));
        rowMenu.Items.Add(new ToolStripSeparator());
        rowMenu.Items.Add(HeaderMenuItem("Move row up", () => MoveRow(-1), "Alt+↑"));
        rowMenu.Items.Add(HeaderMenuItem("Move row down", () => MoveRow(1), "Alt+↓"));
        rowMenu.Items.Add(new ToolStripSeparator());
        AddClipboardHeaderItems(rowMenu);
        rowMenu.Items.Add(new ToolStripSeparator());
        rowMenu.Items.Add(HeaderMenuItem("Hide selected row(s)", HideSelectedRows));
        rowMenu.Items.Add(HeaderMenuItem("Unhide all rows", UnhideAllRows));
        rowMenu.Items.Add(HeaderMenuItem("Delete hidden rows", DeleteHiddenRows));
        rowMenu.Items.Add(new ToolStripSeparator());
        rowMenu.Items.Add(HeaderMenuItem("Freeze through selected row(s)", FreezeSelectedRows));
        rowMenu.Items.Add(HeaderMenuItem("Unfreeze rows", UnfreezeRows));
    }

    // Split-view menus: same operations as the primary pane, bound to the right pane's
    // worksheet. Sorting uses immediate sorts (the advanced sort panel is coupled to the
    // left pane's preview machinery).
    private void BuildSecondaryRowHeaderMenu()
    {
        ConfigureContextMenu(secondaryRowMenu);
        secondaryRowMenu.Items.Add(HeaderMenuItem("Insert row above", InsertSecondaryRow, "Ctrl+Shift+↑"));
        secondaryRowMenu.Items.Add(HeaderMenuItem("Insert row below", InsertSecondaryRowBelow, "Ctrl+Shift+↓"));
        secondaryRowMenu.Items.Add(HeaderMenuItem("Delete selected row(s)", DeleteSecondaryRows, "Ctrl+−"));
        secondaryRowMenu.Items.Add(HeaderMenuItem("Delete duplicate rows", DeleteSecondaryDuplicateRows));
        secondaryRowMenu.Items.Add(new ToolStripSeparator());
        secondaryRowMenu.Items.Add(HeaderMenuItem("Move row up", () => MoveSecondaryRow(-1), "Alt+↑"));
        secondaryRowMenu.Items.Add(HeaderMenuItem("Move row down", () => MoveSecondaryRow(1), "Alt+↓"));
        secondaryRowMenu.Items.Add(new ToolStripSeparator());
        AddSecondaryClipboardHeaderItems(secondaryRowMenu);
        secondaryRowMenu.Items.Add(new ToolStripSeparator());
        secondaryRowMenu.Items.Add(HeaderMenuItem("Hide selected row(s)", HideSecondarySelectedRows));
        secondaryRowMenu.Items.Add(HeaderMenuItem("Unhide all rows", UnhideAllSecondaryRows));
        secondaryRowMenu.Items.Add(HeaderMenuItem("Delete hidden rows", DeleteSecondaryHiddenRows));
        secondaryRowMenu.Items.Add(new ToolStripSeparator());
        secondaryRowMenu.Items.Add(HeaderMenuItem("Freeze through selected row(s)", FreezeSecondaryRows));
        secondaryRowMenu.Items.Add(HeaderMenuItem("Unfreeze rows", UnfreezeSecondaryRows));
    }

    private void BuildSecondaryColumnHeaderMenu()
    {
        ConfigureContextMenu(secondaryColumnMenu);
        var sort = HeaderSubmenu("Sort");
        sort.DropDownItems.Add(HeaderMenuItem("Sort ascending", () => SortSecondary(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort descending", () => SortSecondary(false)));
        sort.DropDownItems.Add(new ToolStripSeparator());
        sort.DropDownItems.Add(HeaderMenuItem("Sort A to Z", () => SortSecondary(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort Z to A", () => SortSecondary(false)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort smallest to largest", () => SortSecondary(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort largest to smallest", () => SortSecondary(false)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort oldest to newest", () => SortSecondary(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort newest to oldest", () => SortSecondary(false)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort shortest to longest", () => SortSecondaryByTextLength(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort longest to shortest", () => SortSecondaryByTextLength(false)));
        ConfigureDropDown(sort); secondaryColumnMenu.Items.Add(sort);

        secondaryColumnMenu.Items.Add(HeaderMenuItem("Insert column left", InsertSecondaryColumn, "Ctrl+Shift+←"));
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Insert column right", InsertSecondaryColumnRight, "Ctrl+Shift+→"));
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Delete selected column(s)", DeleteSecondaryColumns, "Ctrl+Shift+−"));
        secondaryColumnMenu.Items.Add(new ToolStripSeparator());
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Move column left", () => MoveSecondaryColumn(-1), "Alt+←"));
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Move column right", () => MoveSecondaryColumn(1), "Alt+→"));
        secondaryColumnMenu.Items.Add(new ToolStripSeparator());
        AddSecondaryClipboardHeaderItems(secondaryColumnMenu);
        secondaryColumnMenu.Items.Add(new ToolStripSeparator());
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Freeze through selected column(s)", FreezeSecondaryColumns));
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Unfreeze columns", UnfreezeSecondaryColumns));
        secondaryColumnMenu.Items.Add(new ToolStripSeparator());
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Hide selected column(s)", HideSecondarySelectedColumns));
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Unhide all columns", UnhideAllSecondaryColumns));
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Delete hidden columns", DeleteSecondaryHiddenColumns));
        secondaryColumnMenu.Items.Add(new ToolStripSeparator());
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Auto-fit selected column widths", AutoSizeSecondaryColumns, "Ctrl+Alt+A"));
        secondaryColumnMenu.Items.Add(HeaderMenuItem("Filter this column…", () => ShowSecondaryColumnFilterMenu(secondaryGrid.CurrentCell?.ColumnIndex ?? 0)));
    }

    private void AddSecondaryClipboardHeaderItems(ContextMenuStrip menu)
    {
        menu.Items.Add(HeaderMenuItem("Cut", SecondaryCut, "Ctrl+X"));
        menu.Items.Add(HeaderMenuItem("Copy", SecondaryCopy, "Ctrl+C"));
        menu.Items.Add(HeaderMenuItem("Paste", SecondaryPaste, "Ctrl+V"));

        var copyAs = HeaderSubmenu("Copy As");
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy using CSV format", () => CopySelectionAsSecondary(HeaderCopyFormat.Csv)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as raw values", () => CopySelectionAsSecondary(HeaderCopyFormat.Raw)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as formatted Markdown table", () => CopySelectionAsSecondary(HeaderCopyFormat.Markdown)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as Markdown table (no header)", () => CopySelectionAsSecondary(HeaderCopyFormat.MarkdownNoHeader)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as HTML table", () => CopySelectionAsSecondary(HeaderCopyFormat.Html)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as HTML table (no header)", () => CopySelectionAsSecondary(HeaderCopyFormat.HtmlNoHeader)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as JSON (array of arrays)", () => CopySelectionAsSecondary(HeaderCopyFormat.JsonArrays)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as JSON (array of objects)", () => CopySelectionAsSecondary(HeaderCopyFormat.JsonObjects)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as SQL INSERT statement", () => CopySelectionAsSecondary(HeaderCopyFormat.SqlInsert)));
        ConfigureDropDown(copyAs); menu.Items.Add(copyAs);

        var pasteAs = HeaderSubmenu("Paste As");
        pasteAs.DropDownItems.Add(HeaderMenuItem("Paste using CSV format", () => PasteSpecialSecondary(ParseDelimitedText(Clipboard.GetText()))));
        pasteAs.DropDownItems.Add(HeaderMenuItem("Paste as Markdown table", () => PasteSpecialSecondary(ParseMarkdownTable(Clipboard.GetText()))));
        pasteAs.DropDownItems.Add(HeaderMenuItem("Paste as JSON (arrays or objects)", () => PasteSpecialSecondary(ParseJsonTable(Clipboard.GetText()))));
        pasteAs.DropDownItems.Add(HeaderMenuItem("Paste transpose", PasteTransposeSecondary));
        ConfigureDropDown(pasteAs); menu.Items.Add(pasteAs);
    }

    private void BuildColumnHeaderMenu()
    {
        ConfigureContextMenu(columnMenu);
        var sort = HeaderSubmenu("Sort");
        sort.DropDownItems.Add(HeaderMenuItem("Sort ascending", () => Sort(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort descending", () => Sort(false)));
        sort.DropDownItems.Add(new ToolStripSeparator());
        sort.DropDownItems.Add(HeaderMenuItem("Sort A to Z", () => Sort(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort Z to A", () => Sort(false)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort smallest to largest", () => Sort(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort largest to smallest", () => Sort(false)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort oldest to newest", () => Sort(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort newest to oldest", () => Sort(false)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort shortest to longest", () => SortByTextLength(true)));
        sort.DropDownItems.Add(HeaderMenuItem("Sort longest to shortest", () => SortByTextLength(false)));
        sort.DropDownItems.Add(new ToolStripSeparator());
        sort.DropDownItems.Add(HeaderMenuItem("Advanced sort…", ShowSortPanel));
        ConfigureDropDown(sort); columnMenu.Items.Add(sort);

        columnMenu.Items.Add(HeaderMenuItem("Insert column left", InsertColumn, "Ctrl+Shift+←"));
        columnMenu.Items.Add(HeaderMenuItem("Insert column right", InsertColumnRight, "Ctrl+Shift+→"));
        columnMenu.Items.Add(HeaderMenuItem("Delete selected column(s)", DeleteColumns, "Ctrl+Shift+−"));
        columnMenu.Items.Add(new ToolStripSeparator());
        columnMenu.Items.Add(HeaderMenuItem("Move column left", () => MoveColumn(-1), "Alt+←"));
        columnMenu.Items.Add(HeaderMenuItem("Move column right", () => MoveColumn(1), "Alt+→"));
        columnMenu.Items.Add(new ToolStripSeparator());
        AddClipboardHeaderItems(columnMenu);
        columnMenu.Items.Add(new ToolStripSeparator());
        columnMenu.Items.Add(HeaderMenuItem("Freeze through selected column(s)", FreezeSelectedColumns));
        columnMenu.Items.Add(HeaderMenuItem("Unfreeze columns", UnfreezeColumns));
        columnMenu.Items.Add(new ToolStripSeparator());
        columnMenu.Items.Add(HeaderMenuItem("Hide selected column(s)", HideSelectedColumns));
        columnMenu.Items.Add(HeaderMenuItem("Unhide all columns", UnhideAllColumns));
        columnMenu.Items.Add(HeaderMenuItem("Delete hidden columns", DeleteHiddenColumns));
        columnMenu.Items.Add(new ToolStripSeparator());
        columnMenu.Items.Add(HeaderMenuItem("Auto-fit selected column widths", AutoSizeColumns, "Ctrl+Alt+A"));
        columnMenu.Items.Add(HeaderMenuItem("Filter this column…", () => ShowColumnFilterMenu(grid.CurrentCell?.ColumnIndex ?? 0)));
    }

    private void AddClipboardHeaderItems(ContextMenuStrip menu)
    {
        menu.Items.Add(HeaderMenuItem("Cut", Cut, "Ctrl+X"));
        menu.Items.Add(HeaderMenuItem("Copy", Copy, "Ctrl+C"));
        menu.Items.Add(HeaderMenuItem("Paste", Paste, "Ctrl+V"));

        var copyAs = HeaderSubmenu("Copy As");
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy using CSV format", () => CopySelectionAs(HeaderCopyFormat.Csv)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as raw values", () => CopySelectionAs(HeaderCopyFormat.Raw)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as formatted Markdown table", () => CopySelectionAs(HeaderCopyFormat.Markdown)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as Markdown table (no header)", () => CopySelectionAs(HeaderCopyFormat.MarkdownNoHeader)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as HTML table", () => CopySelectionAs(HeaderCopyFormat.Html)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as HTML table (no header)", () => CopySelectionAs(HeaderCopyFormat.HtmlNoHeader)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as JSON (array of arrays)", () => CopySelectionAs(HeaderCopyFormat.JsonArrays)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as JSON (array of objects)", () => CopySelectionAs(HeaderCopyFormat.JsonObjects)));
        copyAs.DropDownItems.Add(HeaderMenuItem("Copy as SQL INSERT statement", () => CopySelectionAs(HeaderCopyFormat.SqlInsert)));
        ConfigureDropDown(copyAs); menu.Items.Add(copyAs);

        var pasteAs = HeaderSubmenu("Paste As");
        pasteAs.DropDownItems.Add(HeaderMenuItem("Paste using CSV format", () => PasteSpecial(ParseDelimitedText(Clipboard.GetText()))));
        pasteAs.DropDownItems.Add(HeaderMenuItem("Paste as Markdown table", () => PasteSpecial(ParseMarkdownTable(Clipboard.GetText()))));
        pasteAs.DropDownItems.Add(HeaderMenuItem("Paste as JSON (arrays or objects)", () => PasteSpecial(ParseJsonTable(Clipboard.GetText()))));
        pasteAs.DropDownItems.Add(HeaderMenuItem("Paste transpose", PasteTranspose));
        ConfigureDropDown(pasteAs); menu.Items.Add(pasteAs);
    }

    private static ToolStripMenuItem HeaderMenuItem(string text, Action action, string shortcut = "")
    {
        var item = new ToolStripMenuItem(text) { BackColor = Theme.Background, ForeColor = Theme.Foreground, ShortcutKeyDisplayString = shortcut };
        item.Click += (_, _) => action(); return item;
    }

    private static ToolStripMenuItem HeaderSubmenu(string text) => new(text) { BackColor = Theme.Background, ForeColor = Theme.Foreground };

    private void DeleteDuplicateRows()
    {
        var selected = grid.SelectedCells.Cast<DataGridViewCell>().Select(cell => ModelRow(cell.RowIndex)).Where(row => row > 0 && row < model.Rows.Count).Distinct().Order().ToList();
        if (selected.Count == 0) selected = Enumerable.Range(1, Math.Max(0, model.Rows.Count - 1)).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal); var duplicates = new List<int>();
        foreach (int row in selected) { string key = string.Join('\u001f', model.Rows[row].Select(cell => cell.Value)); if (!seen.Add(key)) duplicates.Add(row); }
        if (duplicates.Count == 0) { countLabel.Text = "No duplicate rows found"; return; }
        PushUndo(); model.DeleteRows(duplicates); RenderSelect(Math.Min(duplicates.Min(), model.Rows.Count - 1), 0); countLabel.Text = $"Removed {duplicates.Count:N0} duplicate row(s)";
    }

    private void HideSelectedRows()
    {
        var rows = grid.SelectedCells.Cast<DataGridViewCell>().Select(cell => cell.RowIndex).Distinct().Where(row => row >= 0).ToList(); if (rows.Count == 0 || rows.Count >= primaryPane.View.DisplayRowCount) return;
        var hiddenModel = rows.Select(ModelRow).ToHashSet();
        primaryPane.View.HideRows(hiddenModel.Contains);
        int next = Enumerable.Range(0, primaryPane.View.DisplayRowCount).FirstOrDefault(row => !hiddenModel.Contains(primaryPane.ModelRow(row))); if (grid.CurrentCell is not null && hiddenModel.Contains(ModelRow(grid.CurrentCell.RowIndex))) grid.CurrentCell = grid[Math.Min(grid.CurrentCell.ColumnIndex, grid.ColumnCount - 1), next];
        primaryPane.ApplyView(); MirrorPrimaryRowVisibilityToSharedSecondary(); UpdateStatus();
    }

    private void UnhideAllRows() { primaryPane.ResetViewToSheet(); filter = null; headerFilterColumn = -1; headerFilterValues = null; headerFilterOperator = headerFilterConditionValue = null; MirrorPrimaryRowVisibilityToSharedSecondary(); UpdateFindStatus(); UpdateStatus(); }

    private void DeleteHiddenRows()
    {
        var hidden = Enumerable.Range(0, model.Rows.Count).Except(primaryPane.View.VisibleRows).ToList(); if (hidden.Count == 0) return;
        PushUndo(); model.DeleteRows(hidden.OrderDescending().ToList()); filter = null; headerFilterColumn = -1; headerFilterValues = null; headerFilterOperator = headerFilterConditionValue = null; RenderSelect(Math.Min(hidden.Min(), model.Rows.Count - 1), 0);
    }

    private void HideSelectedColumns()
    {
        var columns = grid.SelectedCells.Cast<DataGridViewCell>().Select(cell => cell.ColumnIndex).Distinct().Where(column => column >= 0).ToList(); if (columns.Count == 0 || columns.Count >= grid.ColumnCount) return;
        int next = Enumerable.Range(0, grid.ColumnCount).First(column => !columns.Contains(column) && grid.Columns[column].Visible); if (grid.CurrentCell is not null && columns.Contains(grid.CurrentCell.ColumnIndex)) grid.CurrentCell = grid[next, grid.CurrentCell.RowIndex];
        foreach (int column in columns) grid.Columns[column].Visible = false; UpdateStatus();
    }

    private void UnhideAllColumns() { foreach (DataGridViewColumn column in grid.Columns) column.Visible = true; UpdateStatus(); }

    private void DeleteHiddenColumns()
    {
        var hidden = grid.Columns.Cast<DataGridViewColumn>().Where(column => !column.Visible && column.Index < model.ColumnCount).Select(column => column.Index).OrderDescending().ToList(); if (hidden.Count == 0) return;
        PushUndo(); FormulaReferenceUpdater.DeleteColumns(model, hidden); foreach (var row in model.Rows) foreach (int column in hidden) if (row.Count > 1 && column < row.Count) row.RemoveAt(column); RenderSelect(0, Math.Min(hidden.Min(), model.ColumnCount - 1));
    }

    private void SortByTextLength(bool ascending)
    {
        int column = grid.CurrentCell?.ColumnIndex ?? -1; if (column < 0 || model.Rows.Count < 2) return; PushUndo();
        var body = Enumerable.Range(1, model.Rows.Count - 1).Where(row => model.Rows[row].Any(cell => cell.Value.Length > 0)).ToList(); var blanks = Enumerable.Range(1, model.Rows.Count - 1).Except(body).ToList(); int direction = ascending ? 1 : -1;
        body.Sort((first, second) => { int compared = EvaluatedCellValue(first, column).Length.CompareTo(EvaluatedCellValue(second, column).Length) * direction; return compared != 0 ? compared : first.CompareTo(second); }); ApplyRowOrder([0, .. body, .. blanks]); RenderSelect(1, column);
    }

    private List<List<string>> SelectedValues()
    {
        var selected = grid.SelectedCells.Cast<DataGridViewCell>().ToList(); if (selected.Count == 0) return [];
        int top = selected.Min(cell => cell.RowIndex), bottom = selected.Max(cell => cell.RowIndex), left = selected.Min(cell => cell.ColumnIndex), right = selected.Max(cell => cell.ColumnIndex);
        var matrix = new List<List<string>>(); for (int row = top; row <= bottom; row++) { var values = new List<string>(); for (int column = left; column <= right; column++) values.Add(grid[column, row].Selected ? EvaluatedCellValue(ModelRow(row), column) : ""); matrix.Add(values); }
        while (matrix.Count > 1 && matrix[^1].All(string.IsNullOrEmpty)) matrix.RemoveAt(matrix.Count - 1);
        while (matrix.Count > 0 && matrix[0].Count > 1 && matrix.All(row => string.IsNullOrEmpty(row[^1]))) foreach (var row in matrix) row.RemoveAt(row.Count - 1); return matrix;
    }

    private void CopySelectionAs(HeaderCopyFormat format) => CopyValuesAs(SelectedValues(), format);

    private void CopyValuesAs(List<List<string>> values, HeaderCopyFormat format)
    {
        if (values.Count == 0) return; string text = format switch
        {
            HeaderCopyFormat.Csv => string.Join(Environment.NewLine, values.Select(row => string.Join(',', row.Select(value => CsvCodec.Escape(value))))),
            HeaderCopyFormat.Raw => string.Join(Environment.NewLine, values.Select(row => string.Join('\t', row))),
            HeaderCopyFormat.Markdown => Markdown(values, true), HeaderCopyFormat.MarkdownNoHeader => Markdown(values, false),
            HeaderCopyFormat.Html => Html(values, true), HeaderCopyFormat.HtmlNoHeader => Html(values, false),
            HeaderCopyFormat.JsonArrays => JsonSerializer.Serialize(values), HeaderCopyFormat.JsonObjects => JsonObjects(values),
            HeaderCopyFormat.SqlInsert => SqlInsert(values), _ => ""
        }; Clipboard.SetText(text);
    }

    private static string Markdown(List<List<string>> rows, bool header)
    {
        string Row(IEnumerable<string> cells) => "| " + string.Join(" | ", cells.Select(cell => cell.Replace("|", "\\|"))) + " |";
        var lines = rows.Select(Row).ToList(); if (header && rows.Count > 0) lines.Insert(1, Row(rows[0].Select(_ => "---"))); return string.Join(Environment.NewLine, lines);
    }
    private static string Html(List<List<string>> rows, bool header)
    {
        var text = new StringBuilder("<table>\n"); for (int row = 0; row < rows.Count; row++) { string tag = header && row == 0 ? "th" : "td"; text.Append("  <tr>"); foreach (string value in rows[row]) text.Append('<').Append(tag).Append('>').Append(System.Net.WebUtility.HtmlEncode(value)).Append("</").Append(tag).Append('>'); text.AppendLine("</tr>"); } return text.Append("</table>").ToString();
    }
    private static string JsonObjects(List<List<string>> rows)
    {
        if (rows.Count < 2) return "[]"; var headers = rows[0].Select((value, index) => string.IsNullOrWhiteSpace(value) ? $"Column{index + 1}" : value).ToArray(); var objects = rows.Skip(1).Select(row => headers.Select((header, index) => (header, value: index < row.Count ? row[index] : "")).ToDictionary(item => item.header, item => item.value)); return JsonSerializer.Serialize(objects);
    }
    private static string SqlInsert(List<List<string>> rows)
    {
        if (rows.Count == 0) return ""; var columns = rows[0].Select((value, index) => string.IsNullOrWhiteSpace(value) ? $"Column{index + 1}" : value).Select(value => $"[{value.Replace("]", "]]" )}]"); var body = rows.Skip(1).Select(row => "(" + string.Join(", ", row.Select(value => "'" + value.Replace("'", "''") + "'")) + ")"); return $"INSERT INTO [SheetLiteData] ({string.Join(", ", columns)}) VALUES\n{string.Join(",\n", body)};";
    }

    private void PasteTranspose()
    {
        var rows = Clipboard.GetText().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(row => row.Split('\t').ToList()).ToList(); if (rows.Count == 0) return; int width = rows.Max(row => row.Count); var transposed = Enumerable.Range(0, width).Select(column => rows.Select(row => column < row.Count ? row[column] : "").ToList()).ToList(); PasteSpecial(transposed);
    }

    private void PasteSpecial(List<List<string>> rows)
    {
        if (rows.Count == 0 || grid.CurrentCell is null) return; PushUndo(); int startRow = ModelRow(grid.CurrentCell.RowIndex), startColumn = grid.CurrentCell.ColumnIndex, width = rows.Max(row => row.Count); EnsureGrid(startRow + rows.Count, startColumn + width); loading = true;
        for (int row = 0; row < rows.Count; row++) for (int column = 0; column < rows[row].Count; column++) model.SetCellValue(startRow + row, startColumn + column, rows[row][column]);
        loading = false; grid.Invalidate(); RecalculateFormulaCells(); SetDirty();
    }

    // ----- split-view (right pane) commands -----

    private void PushSecondaryEditUndo() { if (secondarySharesPrimary) PushUndo(); else PushSecondaryUndo(); }
    private void MarkSecondaryEdited() { if (secondarySharesPrimary) SetDirty(); else SetSecondaryDirty(); }

    private void InsertSecondaryRow() { if (secondaryModel is null || secondaryGrid.CurrentCell is null) return; PushSecondaryEditUndo(); secondaryModel.InsertRows(SecondaryModelRow(secondaryGrid.CurrentCell.RowIndex)); AfterSecondaryStructureChange(0); }
    private void InsertSecondaryRowBelow()
    {
        if (secondaryModel is null || secondaryGrid.CurrentCell is null) return;
        int lastDisplay = secondaryGrid.SelectedCells.Count > 0 ? secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Max(c => c.RowIndex) : secondaryGrid.CurrentCell.RowIndex;
        int index = Math.Min(SecondaryModelRow(Math.Min(lastDisplay, secondaryPane.View.DisplayRowCount - 1)) + 1, secondaryModel.Rows.Count);
        PushSecondaryEditUndo(); secondaryModel.InsertRows(index); AfterSecondaryStructureChange(Math.Min(index, secondaryModel.Rows.Count - 1));
    }
    private void DeleteSecondaryRows()
    {
        if (secondaryModel is null) return; var indices = secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Select(c => SecondaryModelRow(c.RowIndex)).Distinct().Where(i => i < secondaryModel.Rows.Count).OrderDescending().ToList(); if (indices.Count == 0) return;
        PushSecondaryEditUndo(); secondaryModel.DeleteRows(indices); AfterSecondaryStructureChange(0);
    }
    private void DeleteSecondaryDuplicateRows()
    {
        if (secondaryModel is null) return;
        var selected = secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Select(cell => SecondaryModelRow(cell.RowIndex)).Where(row => row > 0 && row < secondaryModel.Rows.Count).Distinct().Order().ToList();
        if (selected.Count == 0) selected = Enumerable.Range(1, Math.Max(0, secondaryModel.Rows.Count - 1)).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal); var duplicates = new List<int>();
        foreach (int row in selected) { string key = string.Join('\u001f', secondaryModel.Rows[row].Select(cell => cell.Value)); if (!seen.Add(key)) duplicates.Add(row); }
        if (duplicates.Count == 0) { countLabel.Text = "No duplicate rows found"; return; }
        PushSecondaryEditUndo(); secondaryModel.DeleteRows(duplicates); AfterSecondaryStructureChange(0); countLabel.Text = $"Removed {duplicates.Count:N0} duplicate row(s)";
    }
    private void MoveSecondaryRow(int delta)
    {
        if (secondaryModel is null || secondaryGrid.CurrentCell is null) return; int i = SecondaryModelRow(secondaryGrid.CurrentCell.RowIndex), j = i + delta;
        if (j < 0 || j >= secondaryModel.Rows.Count) return; PushSecondaryEditUndo(); secondaryModel.SwapRows(i, j); AfterSecondaryStructureChange(Math.Clamp(j, 0, secondaryPane.View.DisplayRowCount - 1));
    }

    private void InsertSecondaryColumn() { if (secondaryModel is null || secondaryGrid.CurrentCell is null) return; PushSecondaryEditUndo(); secondaryModel.InsertColumns(secondaryGrid.CurrentCell.ColumnIndex); AfterSecondaryStructureChange(-1); }
    private void InsertSecondaryColumnRight()
    {
        if (secondaryModel is null) return;
        int index = secondaryGrid.SelectedCells.Count > 0 ? secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Max(c => c.ColumnIndex) + 1 : (secondaryGrid.CurrentCell?.ColumnIndex ?? 0) + 1;
        index = Math.Min(index, secondaryModel.ColumnCount); PushSecondaryEditUndo(); secondaryModel.InsertColumns(index); AfterSecondaryStructureChange(-1);
    }
    private void DeleteSecondaryColumns()
    {
        if (secondaryModel is null) return; var indices = secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.ColumnIndex).Distinct().Where(i => i < secondaryModel.ColumnCount).OrderDescending().ToList(); if (indices.Count == 0) return;
        PushSecondaryEditUndo(); secondaryModel.DeleteColumns(indices); AfterSecondaryStructureChange(-1);
    }
    private void MoveSecondaryColumn(int delta)
    {
        if (secondaryModel is null || secondaryGrid.CurrentCell is null) return; int i = secondaryGrid.CurrentCell.ColumnIndex, j = i + delta;
        if (j < 0 || j >= secondaryModel.ColumnCount) return; PushSecondaryEditUndo(); secondaryModel.SwapColumns(i, j); AfterSecondaryStructureChange(-1);
    }

    /// <summary>Re-renders the right pane after a structural edit; <paramref name="focusRow"/> is a display row to keep in range (-1 leaves columns-only changes alone).</summary>
    private void AfterSecondaryStructureChange(int focusRow)
    {
        secondaryHeaderFilterColumn = -1; secondaryHeaderFilterValues = null; secondaryHeaderFilterOperator = secondaryHeaderFilterConditionValue = null;
        RenderSecondaryModel(preserveViewport: true);
        if (focusRow >= 0 && secondaryGrid.RowCount > 0 && secondaryGrid.ColumnCount > 0) secondaryGrid.CurrentCell = secondaryGrid[Math.Min(secondaryGrid.CurrentCell?.ColumnIndex ?? 0, secondaryGrid.ColumnCount - 1), Math.Min(focusRow, secondaryGrid.RowCount - 1)];
        MarkSecondaryEdited(); UpdateStatus();
    }

    private void SortSecondary(bool ascending)
    {
        if (secondaryModel is null || secondaryGrid.CurrentCell is null) return; int column = secondaryGrid.CurrentCell.ColumnIndex; if (secondaryModel.Rows.Count < 2) return; PushSecondaryEditUndo();
        var context = FormulaEngine.CreateContext(secondaryModel);
        var body = Enumerable.Range(1, secondaryModel.Rows.Count - 1).Where(row => secondaryModel.Rows[row].Any(cell => cell.Value.Length > 0)).ToList();
        var blanks = Enumerable.Range(1, secondaryModel.Rows.Count - 1).Except(body).ToList(); int direction = ascending ? 1 : -1;
        body.Sort((first, second) => { int compared = CompareCells(secondaryModel.EvaluatedValue(first, column, context), secondaryModel.EvaluatedValue(second, column, context)) * direction; return compared != 0 ? compared : first.CompareTo(second); });
        secondaryModel.ReorderRows([0, .. body, .. blanks]); AfterSecondaryStructureChange(1);
    }
    private void SortSecondaryByTextLength(bool ascending)
    {
        if (secondaryModel is null || secondaryGrid.CurrentCell is null) return; int column = secondaryGrid.CurrentCell.ColumnIndex; if (secondaryModel.Rows.Count < 2) return; PushSecondaryEditUndo();
        var context = FormulaEngine.CreateContext(secondaryModel);
        var body = Enumerable.Range(1, secondaryModel.Rows.Count - 1).Where(row => secondaryModel.Rows[row].Any(cell => cell.Value.Length > 0)).ToList();
        var blanks = Enumerable.Range(1, secondaryModel.Rows.Count - 1).Except(body).ToList(); int direction = ascending ? 1 : -1;
        body.Sort((first, second) => { int compared = secondaryModel.EvaluatedValue(first, column, context).Length.CompareTo(secondaryModel.EvaluatedValue(second, column, context).Length) * direction; return compared != 0 ? compared : first.CompareTo(second); });
        secondaryModel.ReorderRows([0, .. body, .. blanks]); AfterSecondaryStructureChange(1);
    }

    private void FreezeSecondaryRows()
    {
        if (secondaryModel is null) return; var rows = secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex).Distinct().ToList(); if (rows.Count == 0) return;
        PushSecondaryEditUndo(); secondaryModel.FrozenRows = rows.Max() + 1; ApplySecondaryFreeze(); MarkSecondaryEdited();
    }
    private void UnfreezeSecondaryRows() { if (secondaryModel is null) return; PushSecondaryEditUndo(); secondaryModel.FrozenRows = 0; ApplySecondaryFreeze(); MarkSecondaryEdited(); }
    private void FreezeSecondaryColumns()
    {
        if (secondaryModel is null) return; var columns = secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.ColumnIndex).Distinct().ToList(); if (columns.Count == 0) return;
        PushSecondaryEditUndo(); secondaryModel.FrozenColumns = columns.Max() + 1; ApplySecondaryFreeze(); MarkSecondaryEdited();
    }
    private void UnfreezeSecondaryColumns() { if (secondaryModel is null) return; PushSecondaryEditUndo(); secondaryModel.FrozenColumns = 0; ApplySecondaryFreeze(); MarkSecondaryEdited(); }

    private void HideSecondarySelectedRows()
    {
        var rows = secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Select(cell => cell.RowIndex).Distinct().Where(row => row >= 0).ToList(); if (rows.Count == 0 || rows.Count >= secondaryPane.View.DisplayRowCount) return;
        var hiddenModel = rows.Select(SecondaryModelRow).ToHashSet();
        secondaryPane.View.HideRows(hiddenModel.Contains);
        int next = Enumerable.Range(0, secondaryPane.View.DisplayRowCount).FirstOrDefault(row => !hiddenModel.Contains(secondaryPane.ModelRow(row))); if (secondaryGrid.CurrentCell is not null && hiddenModel.Contains(SecondaryModelRow(secondaryGrid.CurrentCell.RowIndex))) secondaryGrid.CurrentCell = secondaryGrid[Math.Min(secondaryGrid.CurrentCell.ColumnIndex, secondaryGrid.ColumnCount - 1), next];
        secondaryPane.ApplyView(); UpdateStatus();
    }
    private void UnhideAllSecondaryRows()
    {
        secondaryPane.ResetViewToSheet(); secondaryHeaderFilterColumn = -1; secondaryHeaderFilterValues = null; secondaryHeaderFilterOperator = secondaryHeaderFilterConditionValue = null; UpdateStatus();
    }
    private void DeleteSecondaryHiddenRows()
    {
        if (secondaryModel is null) return; var hidden = Enumerable.Range(0, secondaryModel.Rows.Count).Except(secondaryPane.View.VisibleRows).ToList(); if (hidden.Count == 0) return;
        PushSecondaryEditUndo(); secondaryModel.DeleteRows(hidden.OrderDescending().ToList()); secondaryHeaderFilterColumn = -1; secondaryHeaderFilterValues = null; secondaryHeaderFilterOperator = secondaryHeaderFilterConditionValue = null; AfterSecondaryStructureChange(0);
    }

    private void HideSecondarySelectedColumns()
    {
        var columns = secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Select(cell => cell.ColumnIndex).Distinct().Where(column => column >= 0).ToList(); if (columns.Count == 0 || columns.Count >= secondaryGrid.ColumnCount) return;
        int next = Enumerable.Range(0, secondaryGrid.ColumnCount).First(column => !columns.Contains(column) && secondaryGrid.Columns[column].Visible); if (secondaryGrid.CurrentCell is not null && columns.Contains(secondaryGrid.CurrentCell.ColumnIndex)) secondaryGrid.CurrentCell = secondaryGrid[next, secondaryGrid.CurrentCell.RowIndex];
        foreach (int column in columns) secondaryGrid.Columns[column].Visible = false; UpdateStatus();
    }
    private void UnhideAllSecondaryColumns() { foreach (DataGridViewColumn column in secondaryGrid.Columns) column.Visible = true; UpdateStatus(); }
    private void DeleteSecondaryHiddenColumns()
    {
        if (secondaryModel is null) return; var hidden = secondaryGrid.Columns.Cast<DataGridViewColumn>().Where(column => !column.Visible && column.Index < secondaryModel.ColumnCount).Select(column => column.Index).OrderDescending().ToList(); if (hidden.Count == 0) return;
        PushSecondaryEditUndo(); secondaryModel.DeleteColumns(hidden); AfterSecondaryStructureChange(-1);
    }

    private void AutoSizeSecondaryColumns()
    {
        foreach (int c in secondaryGrid.SelectedCells.Cast<DataGridViewCell>().Select(x => x.ColumnIndex).Distinct()) secondaryGrid.Columns[c].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        BeginInvoke(() => { foreach (DataGridViewColumn c in secondaryGrid.Columns) if (c.AutoSizeMode != DataGridViewAutoSizeColumnMode.None) { int w = Math.Min(c.Width, 400); c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None; c.Width = w; } });
    }

    private List<List<string>> SelectedValuesSecondary()
    {
        if (secondaryModel is null) return [];
        var selected = secondaryGrid.SelectedCells.Cast<DataGridViewCell>().ToList(); if (selected.Count == 0) return [];
        int top = selected.Min(cell => cell.RowIndex), bottom = selected.Max(cell => cell.RowIndex), left = selected.Min(cell => cell.ColumnIndex), right = selected.Max(cell => cell.ColumnIndex);
        var matrix = new List<List<string>>(); for (int row = top; row <= bottom; row++) { var values = new List<string>(); for (int column = left; column <= right; column++) values.Add(secondaryGrid[column, row].Selected ? secondaryModel.EvaluatedValue(SecondaryModelRow(row), column) : ""); matrix.Add(values); }
        while (matrix.Count > 1 && matrix[^1].All(string.IsNullOrEmpty)) matrix.RemoveAt(matrix.Count - 1);
        while (matrix.Count > 0 && matrix[0].Count > 1 && matrix.All(row => string.IsNullOrEmpty(row[^1]))) foreach (var row in matrix) row.RemoveAt(row.Count - 1); return matrix;
    }

    private void CopySelectionAsSecondary(HeaderCopyFormat format) => CopyValuesAs(SelectedValuesSecondary(), format);

    private void PasteSpecialSecondary(List<List<string>> rows)
    {
        if (rows.Count == 0 || secondaryModel is null || secondaryGrid.CurrentCell is null) return; PushSecondaryEditUndo();
        int startRow = SecondaryModelRow(secondaryGrid.CurrentCell.RowIndex), startColumn = secondaryGrid.CurrentCell.ColumnIndex, width = rows.Max(row => row.Count);
        secondaryModel.EnsureSize(startRow + rows.Count, startColumn + width);
        if (secondaryGrid.RowCount < secondaryModel.Rows.Count || secondaryGrid.ColumnCount < secondaryModel.ColumnCount) RenderSecondaryModel(preserveViewport: true);
        for (int row = 0; row < rows.Count; row++) for (int column = 0; column < rows[row].Count; column++) secondaryModel.SetCellValue(startRow + row, startColumn + column, rows[row][column]);
        secondaryGrid.Invalidate(); RecalculateSecondaryFormulaCells(); MarkSecondaryEdited();
    }

    private void PasteTransposeSecondary()
    {
        var rows = Clipboard.GetText().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(row => row.Split('\t').ToList()).ToList(); if (rows.Count == 0) return; int width = rows.Max(row => row.Count); var transposed = Enumerable.Range(0, width).Select(column => rows.Select(row => column < row.Count ? row[column] : "").ToList()).ToList(); PasteSpecialSecondary(transposed);
    }

    // ----- split-view column filter dropdown -----

    private int secondaryHeaderFilterColumn = -1;
    private HashSet<string>? secondaryHeaderFilterValues;
    private string? secondaryHeaderFilterOperator, secondaryHeaderFilterConditionValue;
    private ColumnFilterPopup? secondaryColumnFilterPopup;

    private string SecondaryHeaderFilterValue(int row, int column, FormulaEngine.FormulaEvaluationContext? context = null) => secondaryModel is not null && row < secondaryModel.Rows.Count && column < secondaryModel.Rows[row].Count ? secondaryModel.EvaluatedValue(row, column, context) : "";

    private void ReapplySecondaryFilterIfActive()
    {
        if (secondaryModel is null) return;
        if (secondaryHeaderFilterColumn >= 0 && secondaryHeaderFilterValues is not null) ApplySecondaryHeaderValueFilter(secondaryHeaderFilterColumn, secondaryHeaderFilterValues);
        else if (secondaryHeaderFilterColumn >= 0 && secondaryHeaderFilterOperator is not null) ApplySecondaryHeaderConditionFilter(secondaryHeaderFilterColumn, secondaryHeaderFilterOperator, secondaryHeaderFilterConditionValue ?? "");
    }

    private void ShowSecondaryColumnFilterMenu(int column)
    {
        if (secondaryModel is null || column < 0 || column >= secondaryGrid.ColumnCount) return;
        secondaryColumnFilterPopup?.Close();
        int lastRow = Math.Max(1, secondaryModel.Rows.FindLastIndex(row => row.Any(cell => cell.Value.Length > 0)));
        var filterContext = FormulaEngine.CreateContext(secondaryModel);
        var choices = Enumerable.Range(1, lastRow)
            .Select(row => SecondaryHeaderFilterValue(row, column, filterContext))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value.Length == 0 ? 0 : 1)
            .ThenBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .Select(value => new ColumnFilterChoice(value, secondaryHeaderFilterColumn != column || secondaryHeaderFilterValues is null || secondaryHeaderFilterValues.Contains(value)))
            .ToList();

        void RunAndClose(Action action) { secondaryColumnFilterPopup?.Close(); action(); }
        var popup = new ColumnFilterPopup(
            choices,
            ascending => RunAndClose(() => SortSecondary(ascending)),
            ascending => RunAndClose(() => SortSecondaryByTextLength(ascending)),
            () => RunAndClose(() => SortSecondary(true)),
            values => { ApplySecondaryHeaderValueFilter(column, values); secondaryColumnFilterPopup?.Close(); },
            (op, value) => { ApplySecondaryHeaderConditionFilter(column, op, value); secondaryColumnFilterPopup?.Close(); },
            () => { UnhideAllSecondaryRows(); secondaryColumnFilterPopup?.Close(); },
            secondaryHeaderFilterColumn == column ? secondaryHeaderFilterOperator : null,
            secondaryHeaderFilterColumn == column ? secondaryHeaderFilterConditionValue : null);
        secondaryColumnFilterPopup = popup;
        popup.FormClosed += (_, _) => { if (ReferenceEquals(secondaryColumnFilterPopup, popup)) secondaryColumnFilterPopup = null; };
        PositionFilterPopup(popup, secondaryGrid, column);
        popup.Show(this);
    }

    private void ApplySecondaryHeaderValueFilter(int column, HashSet<string> values)
    {
        if (secondaryModel is null || column < 0 || column >= secondaryGrid.ColumnCount) return; secondaryHeaderFilterColumn = column; secondaryHeaderFilterValues = new HashSet<string>(values, StringComparer.CurrentCultureIgnoreCase); secondaryHeaderFilterOperator = secondaryHeaderFilterConditionValue = null;
        var context = FormulaEngine.CreateContext(secondaryModel);
        if (secondaryGrid.CurrentCell is not null && secondaryGrid.CurrentCell.RowIndex > 0) secondaryGrid.CurrentCell = secondaryGrid[column, 0];
        secondaryPane.ResetViewToSheet();
        secondaryPane.View.HideRows(row => row > 0 && !values.Contains(SecondaryHeaderFilterValue(row, column, context)));
        int visible = secondaryPane.View.DisplayRowCount - 1;
        secondaryPane.ApplyView();
        countLabel.Text = $"{visible:N0} visible rows × {secondaryGrid.ColumnCount:N0} columns"; secondaryGrid.Invalidate(); UpdateStatus();
    }

    private void ApplySecondaryHeaderConditionFilter(int column, string op, string value)
    {
        if (secondaryModel is null || column < 0 || column >= secondaryGrid.ColumnCount) return; secondaryHeaderFilterColumn = column; secondaryHeaderFilterValues = null; secondaryHeaderFilterOperator = op; secondaryHeaderFilterConditionValue = value;
        var context = FormulaEngine.CreateContext(secondaryModel);
        if (secondaryGrid.CurrentCell is not null && secondaryGrid.CurrentCell.RowIndex > 0) secondaryGrid.CurrentCell = secondaryGrid[column, 0];
        secondaryPane.ResetViewToSheet();
        secondaryPane.View.HideRows(row => row > 0 && !FilterMatch(SecondaryHeaderFilterValue(row, column, context), op, value));
        int visible = secondaryPane.View.DisplayRowCount - 1;
        secondaryPane.ApplyView();
        countLabel.Text = $"{visible:N0} visible rows × {secondaryGrid.ColumnCount:N0} columns"; secondaryGrid.Invalidate(); UpdateStatus();
    }


    private static List<List<string>> ParseDelimitedText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? [] : CsvCodec.ParseRows(text);
    }

    private static List<List<string>> ParseMarkdownTable(string text)
    {
        var rows = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Replace("\\|", "|")).ToList()).ToList(); if (rows.Count > 1 && rows[1].All(cell => cell.Trim('-', ':', ' ').Length == 0)) rows.RemoveAt(1); return rows;
    }

    private static List<List<string>> ParseJsonTable(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return []; try { using var document = JsonDocument.Parse(text); if (document.RootElement.ValueKind != JsonValueKind.Array) return []; var elements = document.RootElement.EnumerateArray().ToList(); if (elements.Count == 0) return []; if (elements[0].ValueKind == JsonValueKind.Array) return elements.Select(element => element.EnumerateArray().Select(JsonValue).ToList()).ToList(); if (elements[0].ValueKind == JsonValueKind.Object) { var headers = elements.SelectMany(element => element.EnumerateObject().Select(property => property.Name)).Distinct().ToList(); var rows = new List<List<string>> { headers }; rows.AddRange(elements.Select(element => headers.Select(header => element.TryGetProperty(header, out var value) ? JsonValue(value) : "").ToList())); return rows; } } catch { } return [];
    }
    private static string JsonValue(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ValueKind == JsonValueKind.Null ? "" : value.ToString();

    private void TrackHeaderDropDown(object? sender, DataGridViewCellMouseEventArgs e) => SetHeaderDropDownHover(e.RowIndex == -1 && e.ColumnIndex >= 0 && e.X >= grid.Columns[e.ColumnIndex].Width - 24 ? e.ColumnIndex : -1);
    private void SetHeaderDropDownHover(int column) { if (headerDropDownHoverColumn == column) return; int old = headerDropDownHoverColumn; headerDropDownHoverColumn = column; if (old >= 0) grid.InvalidateCell(old, -1); if (column >= 0) grid.InvalidateCell(column, -1); }

    private void PaintHeaderDropDown(Graphics graphics, Rectangle bounds, int column)
    {
        var area = new Rectangle(bounds.Right - 23, bounds.Top + 3, 20, bounds.Height - 6); bool active = column == headerFilterColumn, hover = column == headerDropDownHoverColumn;
        if (!active && !hover && grid.CurrentCell?.ColumnIndex != column) return;
        if (active || hover) { using var background = new SolidBrush(active ? Theme.Selection : Theme.Hover); graphics.FillRectangle(background, area); }
        Color color = active ? Theme.Purple : Theme.Comment; using var brush = new SolidBrush(color); int centerX = area.Left + area.Width / 2, centerY = area.Top + area.Height / 2 + 1; graphics.FillPolygon(brush, [new Point(centerX - 4, centerY - 2), new Point(centerX + 4, centerY - 2), new Point(centerX, centerY + 3)]);
    }

    private void ShowHeaderFilterDropDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.RowIndex != -1 || e.ColumnIndex < 0 || e.X < grid.Columns[e.ColumnIndex].Width - 24) return; grid.CurrentCell = grid[e.ColumnIndex, Math.Max(0, grid.CurrentCell?.RowIndex ?? 0)]; ShowColumnFilterMenu(e.ColumnIndex);
    }

    private void ShowColumnFilterMenu(int column)
    {
        if (column < 0 || column >= grid.ColumnCount) return;
        columnFilterPopup?.Close();
        int lastRow = Math.Max(1, model.Rows.FindLastIndex(row => row.Any(cell => cell.Value.Length > 0)));
        var filterContext = FormulaEngine.CreateContext(model);
        var choices = Enumerable.Range(1, lastRow)
            .Select(row => HeaderFilterValue(row, column, filterContext))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value.Length == 0 ? 0 : 1)
            .ThenBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .Select(value => new ColumnFilterChoice(value, headerFilterColumn != column || headerFilterValues is null || headerFilterValues.Contains(value)))
            .ToList();

        void RunAndClose(Action action) { columnFilterPopup?.Close(); action(); }
        var popup = new ColumnFilterPopup(
            choices,
            ascending => RunAndClose(() => Sort(ascending)),
            ascending => RunAndClose(() => SortByTextLength(ascending)),
            () => RunAndClose(ShowSortPanel),
            values => { ApplyHeaderValueFilter(column, values); columnFilterPopup?.Close(); },
            (op, value) => { ApplyHeaderConditionFilter(column, op, value); columnFilterPopup?.Close(); },
            () => { ClearFilter(); columnFilterPopup?.Close(); },
            headerFilterColumn == column ? headerFilterOperator : null,
            headerFilterColumn == column ? headerFilterConditionValue : null);
        columnFilterPopup = popup;
        popup.FormClosed += (_, _) => { if (ReferenceEquals(columnFilterPopup, popup)) columnFilterPopup = null; };
        PositionFilterPopup(popup, grid, column);
        popup.Show(this);
    }

    /// <summary>Pins a filter popup under a pane's column header, clamped to the working area.</summary>
    private void PositionFilterPopup(ColumnFilterPopup popup, DataGridView pane, int column)
    {
        Rectangle header = pane.GetCellDisplayRectangle(column, -1, true);
        Point headerLeft = pane.PointToScreen(new Point(header.Left, header.Bottom));
        Point headerRight = pane.PointToScreen(new Point(header.Right, header.Bottom));
        Rectangle working = Screen.FromPoint(headerLeft).WorkingArea;
        int gridRight = pane.PointToScreen(new Point(pane.ClientSize.Width - (pane.Controls.OfType<VScrollBar>().Any(scroll => scroll.Visible) ? SystemInformation.VerticalScrollBarWidth : 0), 0)).X;
        int availableRight = Math.Min(working.Right, gridRight);
        int x = headerLeft.X;
        if (x + popup.Width > availableRight) x = headerRight.X - popup.Width;
        x = Math.Max(working.Left, Math.Min(x, availableRight - popup.Width));
        int y = headerLeft.Y;
        if (y + popup.Height > working.Bottom) y = Math.Max(working.Top, working.Bottom - popup.Height);
        popup.Location = new Point(x, y);
    }

    private string HeaderFilterValue(int row, int column, FormulaEngine.FormulaEvaluationContext? context = null) => row < model.Rows.Count && column < model.Rows[row].Count ? EvaluatedCellValue(row, column, context) : "";

    private void ApplyHeaderValueFilter(int column, HashSet<string> values)
    {
        if (column < 0 || column >= grid.ColumnCount) return; headerFilterColumn = column; headerFilterValues = new HashSet<string>(values, StringComparer.CurrentCultureIgnoreCase); headerFilterOperator = headerFilterConditionValue = null; filter = $"Values in {ColumnName(column)}";
        var context = FormulaEngine.CreateContext(model);
        if (grid.CurrentCell is not null && grid.CurrentCell.RowIndex > 0) grid.CurrentCell = grid[column, 0];
        primaryPane.ResetViewToSheet();
        primaryPane.View.HideRows(row => row > 0 && !values.Contains(HeaderFilterValue(row, column, context)));
        int visible = primaryPane.View.DisplayRowCount - 1;
        primaryPane.ApplyView();
        MirrorPrimaryRowVisibilityToSharedSecondary(); countLabel.Text = $"{visible:N0} visible rows × {grid.ColumnCount:N0} columns"; grid.Invalidate(); UpdateFindStatus();
    }

    private void ApplyHeaderConditionFilter(int column, string op, string value)
    {
        if (column < 0 || column >= grid.ColumnCount) return; headerFilterColumn = column; headerFilterValues = null; headerFilterOperator = op; headerFilterConditionValue = value; filter = $"{ColumnName(column)} {op}";
        var context = FormulaEngine.CreateContext(model);
        if (grid.CurrentCell is not null && grid.CurrentCell.RowIndex > 0) grid.CurrentCell = grid[column, 0];
        primaryPane.ResetViewToSheet();
        primaryPane.View.HideRows(row => row > 0 && !FilterMatch(HeaderFilterValue(row, column, context), op, value));
        int visible = primaryPane.View.DisplayRowCount - 1;
        primaryPane.ApplyView();
        MirrorPrimaryRowVisibilityToSharedSecondary(); countLabel.Text = $"{visible:N0} visible rows × {grid.ColumnCount:N0} columns"; grid.Invalidate(); UpdateFindStatus();
    }
}
