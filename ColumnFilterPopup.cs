namespace SheetLite;

internal sealed class ColumnFilterChoice(string value, bool isChecked)
{
    public string Value { get; } = value;
    public string Display => Value.Length == 0 ? "(Blanks)" : Value;
    public bool IsChecked { get; set; } = isChecked;
    public override string ToString() => Display;
}

internal sealed class ColumnFilterPopup : Form
{
    private readonly List<ColumnFilterChoice> choices;
    private readonly Action<HashSet<string>> applyValues;
    private readonly Action<string, string> applyCondition;
    private readonly TextBox search = new() { PlaceholderText = "Filter values" };
    private readonly FilterCheckedListBox valueList = new();
    private readonly Label selectionStatus = new();
    private readonly Panel valuePanel = new(), conditionPanel = new();
    private readonly ComboBox conditionOperator = new();
    private readonly TextBox conditionValue = new() { PlaceholderText = "Filter value" };
    private readonly Button valuesTab, conditionTab;
    private bool valueMode = true;

    public ColumnFilterPopup(
        List<ColumnFilterChoice> choices,
        Action<bool> sort,
        Action<bool> sortByLength,
        Action advancedSort,
        Action<HashSet<string>> applyValues,
        Action<string, string> applyCondition,
        Action clearFilter,
        string? initialConditionOperator = null,
        string? initialConditionValue = null)
    {
        this.choices = choices;
        this.applyValues = applyValues;
        this.applyCondition = applyCondition;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.CurrentLine;
        ClientSize = new Size(360, 475);
        ControlBox = false;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9F);
        ForeColor = Theme.Foreground;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = new Padding(1);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;

        var root = new TableLayoutPanel
        {
            BackColor = Theme.Surface,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(8, 7, 8, 7),
            RowCount = 5
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        Controls.Add(root);

        root.Controls.Add(BuildSortSection(sort, sortByLength, advancedSort), 0, 0);
        root.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.CurrentLine, Margin = new Padding(0, 7, 0, 0) }, 0, 1);

        var filterHeader = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.Surface };
        filterHeader.Controls.Add(new Label { Text = "FILTER", AutoSize = true, ForeColor = Theme.Comment, Font = new Font(Font, FontStyle.Bold), Location = new Point(1, 18) });
        conditionTab = MakeButton("Filter by condition", 116, 27, muted: true);
        valuesTab = MakeButton("Filter by values", 98, 27);
        conditionTab.Location = new Point(226, 10); valuesTab.Location = new Point(126, 10);
        valuesTab.Click += (_, _) => SetFilterMode(true);
        conditionTab.Click += (_, _) => SetFilterMode(false);
        filterHeader.Controls.Add(conditionTab); filterHeader.Controls.Add(valuesTab);
        root.Controls.Add(filterHeader, 0, 2);

        BuildValuePanel(); BuildConditionPanel();
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = Padding.Empty };
        body.Controls.Add(conditionPanel); body.Controls.Add(valuePanel);
        root.Controls.Add(body, 0, 3);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Surface, Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 0) };
        var apply = MakeButton("Apply Filter", 82, 28, accent: true);
        var clear = MakeButton("Clear Filter", 76, 28);
        var close = MakeButton("Close", 51, 28);
        apply.Click += (_, _) => ApplyCurrentFilter(); clear.Click += (_, _) => clearFilter(); close.Click += (_, _) => Close();
        footer.Controls.Add(apply); footer.Controls.Add(clear); footer.Controls.Add(close);
        root.Controls.Add(footer, 0, 4);

        Shown += (_, _) => { NativeTheme.ApplyDarkWindow(this); search.Focus(); };
        Deactivate += (_, _) => Close();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); else if (e.KeyCode == Keys.Enter) ApplyCurrentFilter(); };
        if (initialConditionOperator is not null && conditionOperator.Items.Contains(initialConditionOperator)) conditionOperator.SelectedItem = initialConditionOperator;
        conditionValue.Text = initialConditionValue ?? "";
        SetFilterMode(initialConditionOperator is null);
        RebuildValueList();
    }

    private Control BuildSortSection(Action<bool> sort, Action<bool> sortByLength, Action advancedSort)
    {
        var panel = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Top, Margin = Padding.Empty, Padding = Padding.Empty, RowCount = 4 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = "SORT", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Comment, Font = new Font(Font, FontStyle.Bold), Margin = Padding.Empty }, 0, 0);

        var quick = TwoButtonRow();
        Button ascending = MakeButton("▥  Sort Ascending", 0, 27), descending = MakeButton("▥  Sort Descending", 0, 27);
        ascending.Dock = descending.Dock = DockStyle.Fill; ascending.Click += (_, _) => sort(true); descending.Click += (_, _) => sort(false);
        quick.Controls.Add(ascending, 0, 0); quick.Controls.Add(descending, 1, 0); panel.Controls.Add(quick, 0, 1);

        bool moreExpanded = false;
        var more = MakeButton("More options", 128, 27, muted: true); more.Dock = DockStyle.Right; more.TextAlign = ContentAlignment.MiddleCenter;
        more.Paint += (_, e) =>
        {
            var area = new Rectangle(more.ClientSize.Width - 22, 1, 18, more.ClientSize.Height - 2);
            int centerX = area.Left + area.Width / 2, centerY = area.Top + area.Height / 2;
            Point[] points = moreExpanded
                ? [new Point(centerX - 4, centerY + 2), new Point(centerX + 4, centerY + 2), new Point(centerX, centerY - 3)]
                : [new Point(centerX - 4, centerY - 2), new Point(centerX + 4, centerY - 2), new Point(centerX, centerY + 3)];
            using var brush = new SolidBrush(moreExpanded ? Theme.Purple : Theme.Comment); e.Graphics.FillPolygon(brush, points);
        };
        panel.Controls.Add(more, 0, 2);
        var options = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(0, 1, 0, 0), Padding = new Padding(4), RowCount = 5, BackColor = Theme.Inset, Visible = false };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int row = 0; row < 4; row++) options.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        AddSortPair(options, 0, "Sort A to Z", "Sort Z to A", () => sort(true), () => sort(false));
        AddSortPair(options, 1, "Smallest to Largest", "Largest to Smallest", () => sort(true), () => sort(false));
        AddSortPair(options, 2, "Oldest to Newest", "Newest to Oldest", () => sort(true), () => sort(false));
        AddSortPair(options, 3, "Shortest to Longest", "Longest to Shortest", () => sortByLength(true), () => sortByLength(false));
        var advanced = MakeButton("Advanced Sort…", 0, 25); advanced.Dock = DockStyle.Fill; advanced.Click += (_, _) => advancedSort(); options.Controls.Add(advanced, 0, 4); options.SetColumnSpan(advanced, 2);
        panel.Controls.Add(options, 0, 3);
        more.Click += (_, _) => { moreExpanded = !moreExpanded; options.Visible = moreExpanded; more.Invalidate(); };
        return panel;
    }

    private void BuildValuePanel()
    {
        valuePanel.Dock = DockStyle.Fill; valuePanel.BackColor = Theme.Surface; valuePanel.Margin = Padding.Empty;
        StyleTextBox(search); search.TextChanged += (_, _) => RebuildValueList();
        Button selectAll = MakeButton("Select all", 74, 27, muted: true), clearSelection = MakeButton("Clear", 58, 27, muted: true);
        selectAll.Location = new Point(0, 31); clearSelection.Location = new Point(79, 31);
        selectAll.Click += (_, _) => SetVisibleChoices(true); clearSelection.Click += (_, _) => SetVisibleChoices(false);
        selectionStatus.AutoSize = false; selectionStatus.TextAlign = ContentAlignment.MiddleRight; selectionStatus.ForeColor = Theme.Comment;
        valueList.ItemCheck += (_, e) => { if (e.Index >= 0 && valueList.Items[e.Index] is ColumnFilterChoice choice) choice.IsChecked = e.NewValue == CheckState.Checked; if (IsHandleCreated) BeginInvoke((Action)UpdateSelectionStatus); else UpdateSelectionStatus(); };
        valuePanel.Controls.Add(valueList); valuePanel.Controls.Add(selectionStatus); valuePanel.Controls.Add(clearSelection); valuePanel.Controls.Add(selectAll); valuePanel.Controls.Add(search);
        valuePanel.Resize += (_, _) => LayoutValuePanel();
    }

    private void BuildConditionPanel()
    {
        conditionPanel.Dock = DockStyle.Fill; conditionPanel.BackColor = Theme.Surface; conditionPanel.Margin = Padding.Empty; conditionPanel.Visible = false;
        conditionPanel.Controls.Add(new Label { Text = "Show rows where the selected column…", AutoSize = true, ForeColor = Theme.Comment, Location = new Point(0, 5) });
        conditionOperator.DropDownStyle = ComboBoxStyle.DropDownList; conditionOperator.FlatStyle = FlatStyle.Flat; conditionOperator.BackColor = Theme.Inset; conditionOperator.ForeColor = Theme.Foreground;
        conditionOperator.Items.AddRange(["contains", "equals", "not equals", "starts with", "ends with", ">", ">=", "<", "<=", "is blank", "is not blank"]); conditionOperator.SelectedIndex = 0;
        StyleTextBox(conditionValue);
        conditionPanel.Controls.Add(conditionValue); conditionPanel.Controls.Add(conditionOperator);
        conditionPanel.Resize += (_, _) => LayoutConditionPanel();
    }

    private void LayoutValuePanel()
    {
        int width = Math.Max(0, valuePanel.ClientSize.Width);
        search.SetBounds(0, 0, width, 27);
        selectionStatus.SetBounds(Math.Max(108, width - 170), 32, Math.Min(170, Math.Max(0, width - 108)), 25);
        valueList.SetBounds(0, 62, width, Math.Max(0, valuePanel.ClientSize.Height - 62));
    }

    private void LayoutConditionPanel()
    {
        int width = Math.Max(0, conditionPanel.ClientSize.Width);
        conditionOperator.SetBounds(0, 31, width, 28);
        conditionValue.SetBounds(0, 69, width, 27);
    }

    private void SetFilterMode(bool values)
    {
        valueMode = values; valuePanel.Visible = values; conditionPanel.Visible = !values;
        StyleTab(valuesTab, values); StyleTab(conditionTab, !values);
        if (Visible) (values ? search : conditionValue).Focus();
    }

    private void RebuildValueList()
    {
        string query = search.Text.Trim(); valueList.BeginUpdate(); valueList.Items.Clear();
        foreach (var choice in choices.Where(choice => query.Length == 0 || choice.Display.Contains(query, StringComparison.CurrentCultureIgnoreCase))) valueList.Items.Add(choice, choice.IsChecked);
        valueList.EndUpdate(); UpdateSelectionStatus();
    }

    private void SetVisibleChoices(bool isChecked)
    {
        foreach (ColumnFilterChoice choice in valueList.Items) choice.IsChecked = isChecked;
        for (int index = 0; index < valueList.Items.Count; index++) valueList.SetItemChecked(index, isChecked);
        UpdateSelectionStatus();
    }

    private void UpdateSelectionStatus()
    {
        int selected = choices.Count(choice => choice.IsChecked);
        selectionStatus.Text = selected == choices.Count ? "All selected" : $"{selected} of {choices.Count} selected";
    }

    private void ApplyCurrentFilter()
    {
        if (valueMode) applyValues(choices.Where(choice => choice.IsChecked).Select(choice => choice.Value).ToHashSet(StringComparer.CurrentCultureIgnoreCase));
        else applyCondition(conditionOperator.Text, conditionValue.Text);
    }

    private static TableLayoutPanel TwoButtonRow()
    {
        var row = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty, RowCount = 1 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); return row;
    }

    private static void AddSortPair(TableLayoutPanel panel, int row, string leftText, string rightText, Action leftAction, Action rightAction)
    {
        Button left = MakeButton(leftText, 0, 24), right = MakeButton(rightText, 0, 24); left.Dock = right.Dock = DockStyle.Fill;
        left.Click += (_, _) => leftAction(); right.Click += (_, _) => rightAction(); panel.Controls.Add(left, 0, row); panel.Controls.Add(right, 1, row);
    }

    private static Button MakeButton(string text, int width, int height, bool muted = false, bool accent = false)
    {
        var button = new Button { Text = text, Width = width, Height = height, FlatStyle = FlatStyle.Flat, BackColor = accent ? Theme.Purple : muted ? Theme.Surface : Theme.Hover, ForeColor = accent ? Theme.Inset : Theme.Foreground, Margin = new Padding(2), Padding = Padding.Empty, Cursor = Cursors.Hand, TabStop = false };
        button.FlatAppearance.BorderColor = accent ? Theme.Purple : Theme.CurrentLine; button.FlatAppearance.BorderSize = 1; button.FlatAppearance.MouseOverBackColor = accent ? ControlPaint.Light(Theme.Purple, .08f) : Theme.Selection; button.FlatAppearance.MouseDownBackColor = accent ? ControlPaint.Dark(Theme.Purple, .08f) : Theme.CurrentLine; return button;
    }

    private static void StyleTab(Button tab, bool selected)
    {
        tab.BackColor = selected ? Theme.Hover : Theme.Surface; tab.ForeColor = selected ? Theme.Foreground : Theme.Comment; tab.FlatAppearance.BorderColor = selected ? Theme.Comment : Theme.CurrentLine;
    }

    private static void StyleTextBox(TextBox box)
    {
        box.BackColor = Theme.Inset; box.ForeColor = Theme.Foreground; box.BorderStyle = BorderStyle.FixedSingle;
    }
}

internal sealed class FilterCheckedListBox : CheckedListBox
{
    public FilterCheckedListBox()
    {
        BackColor = Theme.Surface;
        BorderStyle = BorderStyle.FixedSingle;
        CheckOnClick = true;
        DrawMode = DrawMode.OwnerDrawFixed;
        ForeColor = Theme.Foreground;
        IntegralHeight = false;
        ItemHeight = Math.Max(22, (int)Math.Round(28 * DeviceDpi / 96F));
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Theme.Hover : e.Index % 2 == 0 ? Theme.Surface : Theme.Background);
        e.Graphics.FillRectangle(background, e.Bounds);
        string text = Items[e.Index]?.ToString() ?? "";
        TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 39), e.Bounds.Height), Theme.Foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var check = new Rectangle(e.Bounds.Right - 25, e.Bounds.Top + (e.Bounds.Height - 15) / 2, 15, 15);
        using var border = new Pen(Theme.Comment); e.Graphics.DrawRectangle(border, check);
        if (GetItemChecked(e.Index))
        {
            using var fill = new SolidBrush(Theme.Purple); e.Graphics.FillRectangle(fill, Rectangle.Inflate(check, -2, -2));
            using var mark = new Pen(Theme.Inset, 2F); e.Graphics.DrawLines(mark, [new Point(check.Left + 3, check.Top + 8), new Point(check.Left + 6, check.Bottom - 3), new Point(check.Right - 3, check.Top + 4)]);
        }
        if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, Theme.Purple, selected ? Theme.Hover : Theme.Surface);
    }
}
