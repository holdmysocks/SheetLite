using SheetLite;

namespace SheetLite.Tests;

internal sealed class GridTypesTests
{
    [Test] public void CellAddress_is_zero_based_and_prints_A1_style()
    {
        var a1 = new CellAddress(0, 0);
        var b3 = new CellAddress(2, 1);
        Assert.Equal("A1", a1.ToString());
        Assert.Equal("B3", b3.ToString());
    }

    [Test] public void CellAddress_offset_and_ordering()
    {
        Assert.Equal(new CellAddress(5, 7), new CellAddress(4, 6).Offset(1, 1));
        Assert.True(new CellAddress(0, 9) < new CellAddress(1, 0));
        Assert.True(new CellAddress(1, 0) > new CellAddress(0, 9));
        Assert.False(new CellAddress(2, 2) < new CellAddress(2, 2));
    }

    [Test] public void ColumnName_handles_single_double_and_triple_letters()
    {
        Assert.Equal("A", CellAddress.ColumnName(0));
        Assert.Equal("Z", CellAddress.ColumnName(25));
        Assert.Equal("AA", CellAddress.ColumnName(26));
        Assert.Equal("AZ", CellAddress.ColumnName(51));
        Assert.Equal("BA", CellAddress.ColumnName(52));
    }

    [Test] public void CellRange_normalizes_swapped_corners()
    {
        var range = new CellRange(5, 4, 1, 2);
        Assert.Equal(1, range.Left);
        Assert.Equal(2, range.Top);
        Assert.Equal(5, range.Right);
        Assert.Equal(4, range.Bottom);
        Assert.Equal(5, range.Width);
        Assert.Equal(3, range.Height);
    }

    [Test] public void CellRange_contains_and_intersects()
    {
        var range = CellRange.FromSize(topRow: 1, leftColumn: 1, height: 2, width: 2);
        Assert.True(range.Contains(new CellAddress(1, 1)));
        Assert.True(range.Contains(2, 2));
        Assert.False(range.Contains(new CellAddress(3, 2)));
        Assert.True(range.Intersects(range.Offset(1, 1)));
        Assert.False(range.Intersects(range.Offset(3, 0)));
    }

    [Test] public void CellEdit_setValue_applies_value_without_touching_formatting()
    {
        var cell = new CellModel { Value = "old", BackColor = Color.Red, Bold = true };
        CellEdit.SetValue("new").ApplyTo(cell);
        Assert.Equal("new", cell.Value);
        Assert.Equal(Color.Red, cell.BackColor);
        Assert.True(cell.Bold);
    }

    [Test] public void CellEdit_formatting_overloads_are_selective()
    {
        var cell = new CellModel { Value = "v" };
        CellEdit.Format(bold: true).ApplyTo(cell);
        Assert.True(cell.Bold);
        Assert.Null(cell.BackColor);

        CellEdit.Format(backColor: Color.Blue).ApplyTo(cell);
        Assert.Equal(Color.Blue, cell.BackColor);
        Assert.True(cell.Bold);

        CellEdit.ResetFormatting().ApplyTo(cell);
        Assert.Null(cell.BackColor);
        Assert.False(cell.Bold);
        Assert.Equal("v", cell.Value);
    }

    [Test] public void CellEdit_clearValue_empties_only_the_value()
    {
        var cell = new CellModel { Value = "keep-format", ForeColor = Color.Green };
        CellEdit.ClearValue().ApplyTo(cell);
        Assert.Equal("", cell.Value);
        Assert.Equal(Color.Green, cell.ForeColor);
    }
}
