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

    private void CopySelectionAs(HeaderCopyFormat format)
    {
        var values = SelectedValues(); if (values.Count == 0) return; string text = format switch
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

        Rectangle header = grid.GetCellDisplayRectangle(column, -1, true);
        Point headerLeft = grid.PointToScreen(new Point(header.Left, header.Bottom));
        Point headerRight = grid.PointToScreen(new Point(header.Right, header.Bottom));
        Rectangle working = Screen.FromPoint(headerLeft).WorkingArea;
        int gridRight = grid.PointToScreen(new Point(grid.ClientSize.Width - (grid.Controls.OfType<VScrollBar>().Any(scroll => scroll.Visible) ? SystemInformation.VerticalScrollBarWidth : 0), 0)).X;
        int availableRight = Math.Min(working.Right, gridRight);
        int x = headerLeft.X;
        if (x + popup.Width > availableRight) x = headerRight.X - popup.Width;
        x = Math.Max(working.Left, Math.Min(x, availableRight - popup.Width));
        int y = headerLeft.Y;
        if (y + popup.Height > working.Bottom) y = Math.Max(working.Top, working.Bottom - popup.Height);
        popup.Location = new Point(x, y);
        popup.Show(this);
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
