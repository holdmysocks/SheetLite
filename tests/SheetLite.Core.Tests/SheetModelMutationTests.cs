using SheetLite;

namespace SheetLite.Tests;

internal sealed class SheetModelMutationTests
{
    private static SheetModel NewSheet(int rows = 5, int columns = 4)
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(rows, columns);
        return sheet;
    }

    [Test] public void SetCellValue_grows_storage_and_bumps_version()
    {
        var sheet = new SheetModel();
        int before = sheet.Version;
        sheet.SetCellValue(2, 1, "hello");
        Assert.Equal("hello", sheet.Rows[2][1].Value);
        Assert.True(sheet.Version > before);
    }

    [Test] public void SetCellValue_with_same_value_does_not_bump_version()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "x");
        int version = sheet.Version;
        sheet.SetCellValue(0, 0, "x");
        Assert.Equal(version, sheet.Version);
    }

    [Test] public void SetCell_applies_value_and_formatting_together()
    {
        var sheet = NewSheet();
        sheet.SetCell(new CellAddress(1, 1), new CellEdit { Value = "=A1", Bold = true, BackColor = Color.Purple });
        Assert.Equal("=A1", sheet.GetCell(1, 1).Value);
        Assert.True(sheet.GetCell(1, 1).Bold);
        Assert.Equal(Color.Purple, sheet.GetCell(1, 1).BackColor);
    }

    [Test] public void GetRawValue_outside_dimensions_is_empty_without_allocating()
    {
        var sheet = NewSheet();
        Assert.Equal("", sheet.GetRawValue(100, 100));
        Assert.Equal(5, sheet.RowCount);
    }

    [Test] public void EvaluatedValue_calculates_formulas_and_flags_errors()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "2");
        sheet.SetCellValue(1, 0, "3");
        sheet.SetCellValue(2, 0, "=A1+A2");
        Assert.Equal("5", sheet.EvaluatedValue(2, 0));
        sheet.SetCellValue(3, 0, "=A1+)");
        Assert.Equal("#ERROR!", sheet.EvaluatedValue(3, 0));
        Assert.True(sheet.IsFormula(2, 0));
        Assert.False(sheet.IsFormula(0, 0));
    }

    [Test] public void ReplaceCell_swaps_whole_cell_object()
    {
        var sheet = NewSheet();
        var replacement = new CellModel { Value = "replaced", Bold = true };
        sheet.ReplaceCell(0, 3, replacement);
        Assert.Same(replacement, sheet.Rows[0][3]);
    }

    [Test] public void ClearRange_empties_values_but_keeps_formatting()
    {
        var sheet = NewSheet();
        sheet.SetCell(new CellAddress(0, 0), new CellEdit { Value = "keep-style", Bold = true });
        sheet.SetCellValue(1, 1, "gone");
        sheet.ClearRange(new CellRange(0, 0, 1, 1));
        Assert.Equal("", sheet.GetRawValue(0, 0));
        Assert.Equal("", sheet.GetRawValue(1, 1));
        Assert.True(sheet.GetCell(0, 0).Bold);
    }

    [Test] public void InsertRows_shifts_rows_and_keeps_references_above_intact()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "top");
        sheet.SetCellValue(3, 0, "=A1");   // points above the insert index: unchanged
        sheet.SetCellValue(0, 1, "=B4");   // points at old row 3: shifts with it
        sheet.InsertRows(1);
        Assert.Equal("top", sheet.GetRawValue(0, 0));
        Assert.Equal("", sheet.GetRawValue(1, 0));
        Assert.Equal(6, sheet.RowCount);
        Assert.Equal("=A1", sheet.GetRawValue(4, 0));
        Assert.Equal("top", sheet.EvaluatedValue(4, 0));
        Assert.Equal("=B5", sheet.GetRawValue(0, 1));
    }

    [Test] public void InsertColumns_handles_jagged_rows()
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(1, 1);
        sheet.Rows.Add([new CellModel { Value = "short" }]);
        sheet.InsertColumns(1, 2);
        Assert.Equal(3, sheet.ColumnCount);
        Assert.Equal("", sheet.GetRawValue(0, 1));
        Assert.Equal("", sheet.GetRawValue(1, 1));
        Assert.Equal("short", sheet.GetRawValue(1, 0));
    }

    [Test] public void DeleteRows_rewrites_references_of_surviving_rows()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "a");
        sheet.SetCellValue(1, 0, "b");
        sheet.SetCellValue(2, 0, "=SUM(A1:A3)");
        sheet.DeleteRows([1]);
        Assert.Equal("a", sheet.GetRawValue(0, 0));
        Assert.Equal("=SUM(A1:A2)", sheet.GetRawValue(1, 0));
    }

    [Test] public void DeleteRows_referencing_deleted_cell_yields_ref_error_marker()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(1, 0, "victim");
        sheet.SetCellValue(0, 0, "=A2");   // A2 -> model row 1, which gets deleted
        sheet.DeleteRows([1]);
        Assert.Equal("=#REF!", sheet.GetRawValue(0, 0));
    }

    [Test] public void DeleteRows_never_empties_the_sheet()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "last");
        sheet.DeleteRows([0, 1, 2, 3, 4]); // deleting everything still leaves one row
        Assert.Equal(1, sheet.RowCount);
        Assert.Equal("last", sheet.GetRawValue(0, 0));
    }

    [Test] public void DeleteColumns_keeps_at_least_one_column_per_row()
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(2, 1);
        sheet.DeleteColumns([0]);
        Assert.Single(sheet.Rows[0]);
        Assert.Single(sheet.Rows[1]);

        var wide = new SheetModel();
        wide.EnsureSize(1, 4);
        wide.DeleteColumns([1, 3]);
        Assert.Equal(2, wide.Rows[0].Count);
    }

    [Test] public void SwapRows_moves_data_and_follows_references()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "first");
        sheet.SetCellValue(1, 0, "second");
        sheet.SetCellValue(2, 0, "=A1");
        sheet.SwapRows(0, 1);
        Assert.Equal("second", sheet.GetRawValue(0, 0));
        Assert.Equal("first", sheet.GetRawValue(1, 0));
        Assert.Equal("=A2", sheet.GetRawValue(2, 0));
    }

    [Test] public void SwapColumns_moves_data_in_every_row()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "left");
        sheet.SetCellValue(0, 1, "right");
        sheet.SwapColumns(0, 1);
        Assert.Equal("right", sheet.GetRawValue(0, 0));
        Assert.Equal("left", sheet.GetRawValue(0, 1));
    }

    [Test] public void ReorderRows_physically_permutes_rows_and_rewrites_references()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "h");
        sheet.SetCellValue(1, 0, "b");
        sheet.SetCellValue(2, 0, "c");
        sheet.SetCellValue(3, 0, "=A2");   // A2 -> model row 1 ("b")
        sheet.ReorderRows([0, 2, 1, 3, 4]);
        Assert.Equal("h", sheet.GetRawValue(0, 0));
        Assert.Equal("c", sheet.GetRawValue(1, 0));
        Assert.Equal("b", sheet.GetRawValue(2, 0));
        // "b" now lives in row 2 (A3); the formula followed the data.
        Assert.Equal("=A3", sheet.GetRawValue(3, 0));
        Assert.Equal("b", sheet.EvaluatedValue(3, 0));
    }

    [Test] public void ReorderRows_rejects_incomplete_or_repeated_orders()
    {
        var sheet = NewSheet(3, 1);
        Assert.Throws<ArgumentException>(() => sheet.ReorderRows([0, 1]));
        Assert.Throws<ArgumentException>(() => sheet.ReorderRows([0, 1, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.ReorderRows([0, 1, 7]));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.ReorderRows([-1, 1, 2]));
    }

    [Test] public void ReorderRows_with_identity_order_is_a_no_op()
    {
        var sheet = NewSheet();
        int version = sheet.Version;
        sheet.ReorderRows([0, 1, 2, 3, 4]);
        Assert.Equal(version, sheet.Version);
    }

    [Test] public void Structural_operations_reject_out_of_range_inserts()
    {
        var sheet = NewSheet();
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.InsertRows(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.InsertRows(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.InsertColumns(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.SwapRows(0, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.SwapColumns(0, 9));
    }
}
