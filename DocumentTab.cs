using System.ComponentModel;

namespace SheetLite;

internal sealed class DocumentTab : Control
{
    private bool active, dirty, closeHover, dropTarget;
    private string documentTitle = "Untitled";

    public event EventHandler? Activated;
    public event EventHandler? CloseRequested;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string DocumentTitle { get => documentTitle; set { documentTitle = value; AccessibleName = value; Invalidate(); } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IsActive { get => active; set { if (active == value) return; active = value; Invalidate(); } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IsDirty { get => dirty; set { if (dirty == value) return; dirty = value; Invalidate(); } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IsDropTarget { get => dropTarget; set { if (dropTarget == value) return; dropTarget = value; Invalidate(); } }

    public DocumentTab()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Height = 34; Width = 128; Cursor = Cursors.Hand; TabStop = false; AccessibleRole = AccessibleRole.PageTab; BackColor = Theme.Surface; ForeColor = Theme.Foreground;
    }

    private Rectangle CloseBounds
    {
        get { int size = Math.Max(22, (int)Math.Round(24 * DeviceDpi / 96F)); return new Rectangle(Width - size - 3, 3, size, Height - 6); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(active ? Theme.Inset : Theme.Surface);
        using var divider = new Pen(Theme.CurrentLine); e.Graphics.DrawLine(divider, Width - 1, 0, Width - 1, Height);
        if (active) using (var accent = new SolidBrush(Theme.Purple)) e.Graphics.FillRectangle(accent, 0, 0, Width, Math.Max(2, (int)Math.Round(2 * DeviceDpi / 96F)));
        if (dropTarget) { using var wash = new SolidBrush(Color.FromArgb(70, Theme.Purple)); e.Graphics.FillRectangle(wash, ClientRectangle); using var targetBorder = new Pen(Theme.Purple, 2F); e.Graphics.DrawRectangle(targetBorder, 1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3)); }

        var textBounds = new Rectangle(7, 2, Math.Max(1, Width - 14), Height - 3);
        TextRenderer.DrawText(e.Graphics, documentTitle, Font, textBounds, active ? Theme.Purple : Theme.Foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        var indicator = CloseBounds;
        if (closeHover)
            TextRenderer.DrawText(e.Graphics, "×", Font, indicator, Theme.Purple, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        else if (dirty)
        {
            int diameter = Math.Max(6, (int)Math.Round(6 * DeviceDpi / 96F));
            using var dot = new SolidBrush(Theme.Purple); e.Graphics.FillEllipse(dot, indicator.Left + (indicator.Width - diameter) / 2, indicator.Top + (indicator.Height - diameter) / 2, diameter, diameter);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); bool next = CloseBounds.Contains(e.Location); if (next != closeHover) { closeHover = next; Invalidate(CloseBounds); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (closeHover) { closeHover = false; Invalidate(CloseBounds); } }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; if (CloseBounds.Contains(e.Location)) CloseRequested?.Invoke(this, EventArgs.Empty); else Activated?.Invoke(this, EventArgs.Empty); }
}
