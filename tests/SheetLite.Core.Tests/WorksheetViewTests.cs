using SheetLite;

namespace SheetLite.Tests;

/// <summary>
/// The view map must translate between display and model coordinates without ever
/// reordering physical worksheet storage — filtering and sort previews depend on that.
/// </summary>
internal sealed class WorksheetViewTests
{
    private static List<string> PhysicalOrder(SheetModel sheet)
    {
        var values = new List<string>();
        for (int row = 0; row < sheet.RowCount; row++) values.Add(sheet.GetRawValue(row, 0));
        return values;
    }

    [Test] public void Identity_view_maps_every_row_one_to_one()
    {
        var view = WorksheetView.Identity(rowCount: 5, columnCount: 3);
        Assert.Equal(5, view.DisplayRowCount);
        Assert.Equal(3, view.DisplayColumnCount);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, view.ModelRowForDisplayRow(i));
            Assert.Equal(i, view.DisplayRowForModelRow(i));
            Assert.True(view.IsRowVisible(i));
        }
    }

    [Test] public void HideRows_removes_only_matching_model_rows()
    {
        var view = WorksheetView.Identity(6, 1);
        view.HideRows(row => row % 2 == 1);
        Assert.Sequence([0, 2, 4], view.VisibleRows);
        Assert.Equal(0, view.ModelRowForDisplayRow(0));
        Assert.Equal(2, view.ModelRowForDisplayRow(1));
        Assert.Equal(4, view.ModelRowForDisplayRow(2));
        Assert.Equal(-1, view.DisplayRowForModelRow(1));
        Assert.False(view.IsRowVisible(3));
    }

    [Test] public void Hiding_rows_leaves_columns_untouched()
    {
        var view = WorksheetView.Identity(4, 4);
        view.HideRows(row => row == 1 || row == 2);
        Assert.Equal(4, view.DisplayColumnCount);
        for (int column = 0; column < 4; column++) Assert.Equal(column, view.ModelColumnForDisplayColumn(column));
        Assert.Equal(-1, view.DisplayColumnForModelColumn(9));
        Assert.Equal(2, view.DisplayRowCount);
    }

    [Test] public void SetRowOrder_permutes_display_order_without_touching_storage()
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(4, 1);
        string[] names = ["alpha", "bravo", "charlie", "delta"];
        for (int i = 0; i < names.Length; i++) sheet.SetCellValue(i, 0, names[i]);

        var view = WorksheetView.Identity(4, 1);
        view.HideRows(row => false); // keep everything; exercise the predicate path
        view.SetRowOrder([3, 0, 2, 1]); // a "sorted" display order

        Assert.Equal("delta", sheet.GetRawValue(view.ModelRowForDisplayRow(0), 0));
        Assert.Equal("alpha", sheet.GetRawValue(view.ModelRowForDisplayRow(1), 0));
        Assert.Equal("charlie", sheet.GetRawValue(view.ModelRowForDisplayRow(2), 0));
        Assert.Equal("bravo", sheet.GetRawValue(view.ModelRowForDisplayRow(3), 0));

        // Physical storage is untouched: saving still writes the original order.
        Assert.Sequence(names, PhysicalOrder(sheet));
    }

    [Test] public void SetRowOrder_preserves_hidden_rows_and_validates_input()
    {
        var view = WorksheetView.Identity(5, 1);
        view.HideRows(row => row == 1); // hidden: 1

        Assert.Throws<ArgumentException>(() => view.SetRowOrder([0, 1, 2, 3, 4])); // includes hidden row / wrong size
        Assert.Throws<ArgumentException>(() => view.SetRowOrder([4, 0]));          // incomplete
        Assert.Throws<ArgumentException>(() => view.SetRowOrder([2, 2, 4, 3]));    // repeated

        view.SetRowOrder([4, 3, 2, 0]); // reverse of the visible set only
        Assert.Sequence([4, 3, 2, 0], view.VisibleRows);
        Assert.Equal(-1, view.DisplayRowForModelRow(1));
    }

    [Test] public void Reset_clears_filters_and_orders()
    {
        var view = WorksheetView.Identity(5, 3);
        view.HideRows(row => row > 2);
        view.SetRowOrder([2, 1, 0]);
        view.Reset(7, 2);
        Assert.Equal(7, view.DisplayRowCount);
        Assert.Equal(2, view.DisplayColumnCount);
        for (int i = 0; i < 7; i++) Assert.Equal(i, view.DisplayRowForModelRow(i));
    }

    [Test] public void Out_of_range_lookups_throw_instead_of_returning_garbage()
    {
        var view = WorksheetView.Identity(3, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.ModelRowForDisplayRow(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.ModelRowForDisplayRow(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.ModelColumnForDisplayColumn(2));
    }
}
