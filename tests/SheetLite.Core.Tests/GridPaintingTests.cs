using SheetLite;

namespace SheetLite.Tests;

internal sealed class GridPaintingTests
{
    [Test] public void Header_corner_is_repainted_after_column_header_content()
    {
        using var grid = new SmoothDataGridView
        {
            Size = new Size(240, 100),
            RowHeadersWidth = 58,
            ColumnHeadersHeight = 25,
            RowHeadersVisible = true,
            ColumnHeadersVisible = true
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "A", Width = 110 });
        grid.Rows.Add();
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex != -1 || e.ColumnIndex != 0 || e.Graphics is null) return;
            using var bleed = new SolidBrush(Color.Magenta);
            e.Graphics.FillRectangle(bleed, new Rectangle(0, 0, grid.RowHeadersWidth + 20, grid.ColumnHeadersHeight));
            e.Handled = true;
        };

        grid.CreateControl();
        using var bitmap = new Bitmap(grid.ClientSize.Width, grid.ClientSize.Height);
        grid.DrawToBitmap(bitmap, grid.ClientRectangle);

        Assert.Equal(Theme.HeaderBackground.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(grid.RowHeadersWidth + 10, 10).ToArgb());
    }
}
