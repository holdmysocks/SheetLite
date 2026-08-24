namespace SheetLite;

/// <summary>A compact, theme-native cell color picker with palette, hex entry, preview, and per-color reset.</summary>
internal sealed class CellColorDialog : Form
{
    private static readonly Color[] PaletteColors =
    [
        ColorTranslator.FromHtml("#FFFFFF"), ColorTranslator.FromHtml("#D8DEE9"), Theme.Comment,
        ColorTranslator.FromHtml("#555B6E"), Theme.CurrentLine, Theme.CellBackground, Color.Black,
        ColorTranslator.FromHtml("#FF5555"), ColorTranslator.FromHtml("#FFB86C"), ColorTranslator.FromHtml("#F1FA8C"),
        ColorTranslator.FromHtml("#50FA7B"), ColorTranslator.FromHtml("#8BE9FD"), ColorTranslator.FromHtml("#6272A4"),
        Theme.Purple, ColorTranslator.FromHtml("#FF79C6"), ColorTranslator.FromHtml("#8B1E3F"),
        ColorTranslator.FromHtml("#9C5700"), ColorTranslator.FromHtml("#665C00"), ColorTranslator.FromHtml("#166534"),
        ColorTranslator.FromHtml("#155E75"), ColorTranslator.FromHtml("#1E3A8A"), ColorTranslator.FromHtml("#581C87")
    ];

    private readonly bool backgroundMode;
    private readonly FlowLayoutPanel palette = new();
    private readonly TextBox hexValue = new();
    private readonly Label preview = new();
    private readonly Label validation = new();
    private bool changingHex;

    public CellColorDialog(bool background, Color initialColor)
    {
        backgroundMode = background;
        SelectedColor = Color.FromArgb(initialColor.R, initialColor.G, initialColor.B);
        Text = background ? "Cell background color" : "Cell text color";
        AccessibleName = Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Surface;
        ForeColor = Theme.Foreground;
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(432, 354);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(20, 17, 20, 16),
            Margin = Padding.Empty, ColumnCount = 1, RowCount = 6
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        root.Controls.Add(new Label
        {
            Text = background ? "Choose a fill color" : "Choose a text color",
            AutoSize = true, ForeColor = Theme.Foreground, Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        palette.Dock = DockStyle.Fill;
        palette.BackColor = Theme.Inset;
        palette.Padding = new Padding(7);
        palette.Margin = Padding.Empty;
        palette.WrapContents = true;
        palette.AccessibleName = "Color palette";
        foreach (Color color in PaletteColors) palette.Controls.Add(CreateSwatch(color));
        root.Controls.Add(palette, 0, 1);

        var custom = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 9, 0, 0) };
        custom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        custom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        custom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        custom.Controls.Add(new Label { Text = "Hex", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Comment }, 0, 0);
        hexValue.Dock = DockStyle.Fill;
        hexValue.BorderStyle = BorderStyle.FixedSingle;
        hexValue.BackColor = Theme.Inset;
        hexValue.ForeColor = Theme.Foreground;
        hexValue.Font = new Font("Consolas", 10F);
        hexValue.MaxLength = 7;
        hexValue.TextAlign = HorizontalAlignment.Center;
        hexValue.AccessibleName = "Hex color";
        hexValue.TextChanged += (_, _) => UpdateFromHex();
        custom.Controls.Add(hexValue, 1, 0);
        preview.Dock = DockStyle.Fill;
        preview.Margin = new Padding(12, 0, 0, 0);
        preview.Text = background ? "Sample cell" : "Sample text";
        preview.TextAlign = ContentAlignment.MiddleCenter;
        preview.BorderStyle = BorderStyle.FixedSingle;
        custom.Controls.Add(preview, 2, 0);
        root.Controls.Add(custom, 0, 2);

        validation.Dock = DockStyle.Fill;
        validation.ForeColor = ColorTranslator.FromHtml("#FF7777");
        validation.TextAlign = ContentAlignment.MiddleLeft;
        validation.Margin = Padding.Empty;
        root.Controls.Add(validation, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var reset = DialogButton("Reset to default", 126);
        reset.AccessibleName = background ? "Reset background color to default" : "Reset text color to automatic";
        reset.Click += (_, _) => { ResetToDefault = true; DialogResult = DialogResult.OK; Close(); };
        footer.Controls.Add(reset, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Surface, Margin = Padding.Empty };
        var apply = DialogButton("Apply", 76, accent: true);
        var cancel = DialogButton("Cancel", 76);
        apply.Click += (_, _) => ApplySelection();
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(apply);
        actions.Controls.Add(cancel);
        footer.Controls.Add(actions, 1, 0);
        root.Controls.Add(footer, 0, 5);

        Controls.Add(root);
        AcceptButton = apply;
        CancelButton = cancel;
        Shown += (_, _) => { NativeTheme.ApplyDarkWindow(this); hexValue.Focus(); hexValue.SelectAll(); };
        SetColor(SelectedColor);
    }

    public Color SelectedColor { get; private set; }
    public bool ResetToDefault { get; private set; }

    internal static bool TryParseHex(string text, out Color color)
    {
        string value = text.Trim();
        if (value.StartsWith('#')) value = value[1..];
        if (value.Length == 6 && int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
        {
            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }
        color = Color.Empty;
        return false;
    }

    private Button CreateSwatch(Color color)
    {
        var swatch = new Button
        {
            BackColor = color, FlatStyle = FlatStyle.Flat, Size = new Size(31, 31),
            Margin = new Padding(4), Padding = Padding.Empty, Cursor = Cursors.Hand,
            TabStop = true, Tag = color, AccessibleName = $"Color #{color.R:X2}{color.G:X2}{color.B:X2}"
        };
        swatch.FlatAppearance.BorderColor = Theme.Comment;
        swatch.FlatAppearance.BorderSize = 1;
        swatch.FlatAppearance.MouseOverBackColor = color;
        swatch.FlatAppearance.MouseDownBackColor = color;
        swatch.Click += (_, _) => SetColor(color);
        return swatch;
    }

    private static Button DialogButton(string text, int width, bool accent = false)
    {
        var button = new Button
        {
            Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.Flat,
            BackColor = accent ? Theme.Purple : Theme.Hover,
            ForeColor = accent ? Theme.Inset : Theme.Foreground,
            Margin = new Padding(6, 0, 0, 0), Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = accent ? Theme.Purple : Theme.Comment;
        button.FlatAppearance.MouseOverBackColor = accent ? ControlPaint.Light(Theme.Purple, 0.08F) : Theme.Selection;
        button.FlatAppearance.MouseDownBackColor = accent ? ControlPaint.Dark(Theme.Purple, 0.08F) : Theme.CurrentLine;
        return button;
    }

    private void SetColor(Color color)
    {
        SelectedColor = Color.FromArgb(color.R, color.G, color.B);
        changingHex = true;
        hexValue.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
        changingHex = false;
        validation.Text = "";
        hexValue.BackColor = Theme.Inset;
        preview.BackColor = backgroundMode ? SelectedColor : Theme.CellBackground;
        preview.ForeColor = backgroundMode ? Theme.AdaptiveCellText(SelectedColor) : SelectedColor;
        foreach (Button swatch in palette.Controls.OfType<Button>())
            swatch.FlatAppearance.BorderSize = swatch.Tag is Color candidate && candidate.ToArgb() == SelectedColor.ToArgb() ? 3 : 1;
    }

    private void UpdateFromHex()
    {
        if (changingHex) return;
        if (TryParseHex(hexValue.Text, out Color color)) SetColor(color);
        else { validation.Text = "Enter a six-digit color, such as #BD93F9."; hexValue.BackColor = ColorTranslator.FromHtml("#3A2028"); }
    }

    private void ApplySelection()
    {
        if (!TryParseHex(hexValue.Text, out Color color))
        {
            validation.Text = "Enter a six-digit color, such as #BD93F9.";
            hexValue.Focus(); hexValue.SelectAll();
            return;
        }
        SetColor(color);
        ResetToDefault = false;
        DialogResult = DialogResult.OK;
        Close();
    }
}
