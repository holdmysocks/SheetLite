using System.Runtime.InteropServices;

namespace SheetLite;

internal static class NativeTheme
{
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    public static void ApplyDarkWindow(Form form)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            ApplyWindowChrome(form);
            ApplyToChildren(form);
        }
        catch { /* Older Windows versions simply retain their system chrome. */ }
    }

    public static void ApplyWindowChrome(Form form)
    {
        if (!OperatingSystem.IsWindows() || !form.IsHandleCreated) return;
        try
        {
            int enabled = 1;
            DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
            int rounded = 2;
            DwmSetWindowAttribute(form.Handle, DwmWindowCornerPreference, ref rounded, sizeof(int));
            int caption = ColorRef(Theme.Background), text = ColorRef(Theme.Foreground), border = DwmColorNone;
            DwmSetWindowAttribute(form.Handle, DwmCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, DwmTextColor, ref text, sizeof(int));
            DwmSetWindowAttribute(form.Handle, DwmBorderColor, ref border, sizeof(int));
        }
        catch { /* Older Windows versions do not expose every DWM attribute. */ }
    }

    private static void ApplyToChildren(Control root)
    {
        foreach (Control child in root.Controls)
        {
            SetWindowTheme(child.Handle, "DarkMode_Explorer", null);
            ApplyToChildren(child);
        }
    }

    private static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;
}

internal sealed class DraculaColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Background;
    public override Color MenuBorder => Theme.Comment;
    public override Color MenuItemBorder => Theme.Purple;
    public override Color MenuItemSelected => Theme.Comment;
    public override Color MenuItemSelectedGradientBegin => Theme.Comment;
    public override Color MenuItemSelectedGradientEnd => Theme.Comment;
    public override Color MenuItemPressedGradientBegin => Theme.CurrentLine;
    public override Color MenuItemPressedGradientMiddle => Theme.CurrentLine;
    public override Color MenuItemPressedGradientEnd => Theme.CurrentLine;
    public override Color ImageMarginGradientBegin => Theme.Background;
    public override Color ImageMarginGradientMiddle => Theme.Background;
    public override Color ImageMarginGradientEnd => Theme.Background;
    public override Color SeparatorDark => Theme.Comment;
    public override Color SeparatorLight => Theme.CurrentLine;
    public override Color ToolStripBorder => Theme.CurrentLine;
    public override Color ToolStripGradientBegin => Theme.Surface;
    public override Color ToolStripGradientMiddle => Theme.Surface;
    public override Color ToolStripGradientEnd => Theme.Surface;
    public override Color ButtonSelectedBorder => Theme.Purple;
    public override Color ButtonSelectedGradientBegin => Theme.Comment;
    public override Color ButtonSelectedGradientMiddle => Theme.Comment;
    public override Color ButtonSelectedGradientEnd => Theme.Comment;
    public override Color ButtonCheckedGradientBegin => Theme.Selection;
    public override Color ButtonCheckedGradientMiddle => Theme.Selection;
    public override Color ButtonCheckedGradientEnd => Theme.Selection;
}

internal sealed class DraculaRenderer : ToolStripProfessionalRenderer
{
    public DraculaRenderer() : base(new DraculaColorTable()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Foreground : Theme.Comment;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Foreground;
        base.OnRenderArrow(e);
    }

    // Menus must never show per-item borders or a light chrome line, regardless of which
    // surface (row header, column header, cell editor) hosts the dropdown. Filling item
    // backgrounds ourselves skips the professional renderer's gradient/border pass entirely.
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        Color fill = e.Item.Selected ? Theme.Selection : e.Item.BackColor;
        if (fill == Color.Empty || fill.A == 0) fill = Theme.Background;
        using var brush = new SolidBrush(fill);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.CurrentLine);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
}

/// <summary>Paint suppression for control rebuilds that would otherwise flash (tab strips, toolbars).</summary>
internal static class NativeRedraw
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SETREDRAW = 0x000B;

    public static void Run(Control control, Action body)
    {
        control.SuspendLayout();
        SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            body();
        }
        finally
        {
            SendMessage(control.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            control.ResumeLayout();
            control.Invalidate(true);
        }
    }
}
