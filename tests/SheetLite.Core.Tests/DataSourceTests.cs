using SheetLite;

namespace SheetLite.Tests;

internal sealed class DataSourceTests
{
    private static SheetModel NewSheet()
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(3, 3);
        return sheet;
    }

    [Test] public void DataSource_exposes_logical_dimensions_and_version()
    {
        var sheet = NewSheet();
        var source = new SheetModelDataSource(() => sheet);
        Assert.Equal(3, source.RowCount);
        Assert.Equal(3, source.ColumnCount);
        Assert.Equal(sheet.Version, source.Version);
        int before = source.Version;
        source.SetCell(new CellAddress(0, 0), CellEdit.SetValue("x"));
        Assert.True(source.Version > before);
    }

    [Test] public void GetEvaluatedText_returns_raw_text_for_plain_cells()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(1, 2, "plain");
        var source = new SheetModelDataSource(() => sheet);
        Assert.Equal("plain", source.GetEvaluatedText(new CellAddress(1, 2)));
    }

    [Test] public void GetEvaluatedText_calculates_formulas_and_reflects_edits_after_version_bump()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "4");
        sheet.SetCellValue(1, 0, "=A1*10");
        var source = new SheetModelDataSource(() => sheet);

        Assert.Equal("40", source.GetEvaluatedText(new CellAddress(1, 0)));

        // Memoized context must be invalidated when the model changes.
        sheet.SetCellValue(0, 0, "5");
        Assert.Equal("50", source.GetEvaluatedText(new CellAddress(1, 0)));
    }

    [Test] public void GetDisplayValue_resolves_default_theme_styling()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "styled");
        sheet.SetCell(new CellAddress(0, 0), CellEdit.Format(backColor: Color.Purple, bold: true));
        var display = new SheetModelDataSource(() => sheet).GetDisplayValue(new CellAddress(0, 0));

        Assert.Equal("styled", display.Text);
        Assert.Equal(Color.Purple, display.BackColor);
        Assert.Equal(Theme.Foreground, display.ForeColor); // unset ForeColor falls back to theme default
        Assert.True(display.Bold);
    }

    [Test] public void GetEvaluatedText_matches_SheetModel_EvaluatedValue_for_all_cell_kinds()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "7");            // plain number
        sheet.SetCellValue(1, 0, "=A1*3");       // valid formula -> 21
        sheet.SetCellValue(2, 0, "=A1+)");       // broken formula -> #ERROR!
        sheet.SetCellValue(2, 2, "tail");         // plain text
        var source = new SheetModelDataSource(() => sheet);
        foreach (int row in Enumerable.Range(0, 3))
            foreach (int column in Enumerable.Range(0, 3))
                Assert.Equal(sheet.EvaluatedValue(row, column), source.GetEvaluatedText(new CellAddress(row, column)));
    }

    [Test] public void SetCell_through_interface_commits_to_the_sheet()
    {
        var sheet = NewSheet();
        IWorksheetDataSource source = new SheetModelDataSource(() => sheet);
        source.SetCell(new CellAddress(2, 2), new CellEdit { Value = "via interface", ForeColor = Color.Orange });
        Assert.Equal("via interface", sheet.GetRawValue(2, 2));
        Assert.Equal(Color.Orange, sheet.GetCell(2, 2).ForeColor);
    }

    [Test] public void Provider_indirection_follows_worksheet_switches_and_undo()
    {
        var first = WorkbookModel.CreateBlank();
        var second = WorkbookModel.CreateBlank();
        SheetModel? current = first.ActiveSheet.Sheet;
        var source = new SheetModelDataSource(() => current!);

        source.SetCell(new CellAddress(0, 0), CellEdit.SetValue("in first"));
        current = second.ActiveSheet.Sheet;             // e.g. user switched sheets
        Assert.Equal("", source.GetEvaluatedText(new CellAddress(0, 0)));

        current = (SheetModel)((WorkbookModel)first.Clone()).ActiveSheet.Sheet; // e.g. undo restored a snapshot
        Assert.Equal("in first", source.GetEvaluatedText(new CellAddress(0, 0)));
    }

    [Test] public void Evaluation_context_is_reused_within_one_version_and_shared_with_callers()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "=B1");
        sheet.SetCellValue(0, 1, "shared");
        var source = new SheetModelDataSource(() => sheet);

        Assert.Equal("shared", source.GetEvaluatedText(new CellAddress(0, 0)));
        var context = source.ContextFor(sheet);
        Assert.Same(context, source.ContextFor(sheet));   // memoized within the same version

        sheet.SetCellValue(0, 1, "changed");
        Assert.NotSame(context, source.ContextFor(sheet)); // invalidated by version bump
    }
}
