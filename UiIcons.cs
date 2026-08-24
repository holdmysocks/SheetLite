using System.Drawing.Drawing2D;

namespace SheetLite;

internal enum UiIcon
{
    Search, Sort, Filter, Database, Split, Freeze, Fill, TextColor, Help, Run, Clear
}

/// <summary>Small code-drawn icons keep the portable build self-contained and avoid copied UI assets.</summary>
internal static class UiIcons
{
    public static Bitmap Draw(UiIcon icon, Color color, int size = 18)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var pen = new Pen(color, 1.65F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        float s = size / 18F;
        PointF P(float x, float y) => new(x * s, y * s);
        RectangleF R(float x, float y, float w, float h) => new(x * s, y * s, w * s, h * s);

        switch (icon)
        {
            case UiIcon.Search:
                g.DrawEllipse(pen, R(2.5F, 2.5F, 9.5F, 9.5F)); g.DrawLine(pen, P(10.3F, 10.3F), P(15.5F, 15.5F));
                break;
            case UiIcon.Sort:
                g.DrawLine(pen, P(4, 2.5F), P(4, 15)); g.DrawLine(pen, P(1.5F, 5), P(4, 2.5F)); g.DrawLine(pen, P(6.5F, 5), P(4, 2.5F));
                g.DrawLine(pen, P(13.5F, 15.5F), P(13.5F, 3)); g.DrawLine(pen, P(11, 13), P(13.5F, 15.5F)); g.DrawLine(pen, P(16, 13), P(13.5F, 15.5F));
                break;
            case UiIcon.Filter:
                using (var path = new GraphicsPath()) { path.AddLines([P(2, 3), P(16, 3), P(10.5F, 9), P(10.5F, 14), P(7.5F, 15.5F), P(7.5F, 9), P(2, 3)]); g.DrawPath(pen, path); }
                break;
            case UiIcon.Database:
                g.DrawEllipse(pen, R(3, 2, 12, 4)); g.DrawArc(pen, R(3, 5, 12, 4), 0, 180); g.DrawArc(pen, R(3, 9, 12, 4), 0, 180); g.DrawLine(pen, P(3, 4), P(3, 14)); g.DrawLine(pen, P(15, 4), P(15, 14)); g.DrawArc(pen, R(3, 12, 12, 4), 0, 180);
                break;
            case UiIcon.Split:
                g.DrawRectangle(pen, R(2, 3, 14, 12)); g.DrawLine(pen, P(9, 3), P(9, 15));
                break;
            case UiIcon.Freeze:
                g.DrawLine(pen, P(9, 1.8F), P(9, 16.2F)); g.DrawLine(pen, P(2.8F, 5.4F), P(15.2F, 12.6F)); g.DrawLine(pen, P(2.8F, 12.6F), P(15.2F, 5.4F));
                g.DrawLine(pen, P(9, 1.8F), P(7.3F, 3.6F)); g.DrawLine(pen, P(9, 1.8F), P(10.7F, 3.6F));
                break;
            case UiIcon.Fill:
                g.RotateTransform(-35, MatrixOrder.Append); g.TranslateTransform(-5, 4, MatrixOrder.Append); g.DrawRectangle(pen, R(5, 4, 8, 8)); g.ResetTransform(); g.DrawLine(pen, P(3, 14.5F), P(15, 14.5F));
                break;
            case UiIcon.TextColor:
                using (var font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString("A", font, brush, P(4.2F, 1)); g.FillRectangle(brush, R(3, 14, 12, 2));
                break;
            case UiIcon.Help:
                using (var font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString("?", font, brush, P(5, 1.5F)); g.DrawEllipse(pen, R(1.5F, 1.5F, 15, 15));
                break;
            case UiIcon.Run:
                g.FillPolygon(brush, [P(4, 2.5F), P(15, 9), P(4, 15.5F)]);
                break;
            case UiIcon.Clear:
                g.DrawLine(pen, P(4, 4), P(14, 14)); g.DrawLine(pen, P(14, 4), P(4, 14));
                break;
        }
        return bitmap;
    }
}
