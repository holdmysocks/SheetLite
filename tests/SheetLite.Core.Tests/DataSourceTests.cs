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
        Assert.Equal(Theme.Foreground, display.ForeColor); // unset ForeColor adapts to the dark theme background
        Assert.True(display.Bold);
    }

    [Test] public void Default_text_color_adapts_to_custom_cell_background()
    {
        var sheet = NewSheet();
        sheet.SetCell(new CellAddress(0, 0), CellEdit.Format(backColor: Color.White));
        sheet.SetCell(new CellAddress(0, 1), CellEdit.Format(backColor: Color.Black));
        using var source = new SheetModelDataSource(() => sheet);

        Assert.Equal(Color.Black, source.GetDisplayValue(new CellAddress(0, 0)).ForeColor);
        Assert.Equal(Theme.Foreground, source.GetDisplayValue(new CellAddress(0, 1)).ForeColor);

        sheet.SetCell(new CellAddress(0, 0), CellEdit.Format(foreColor: Color.Red));
        Assert.Equal(Color.Red, source.GetDisplayValue(new CellAddress(0, 0)).ForeColor);
    }

    [Test] public void Themed_color_picker_parses_hex_colors()
    {
        Assert.True(CellColorDialog.TryParseHex("#BD93F9", out Color purple));
        Assert.Equal(Color.FromArgb(0xBD, 0x93, 0xF9), purple);
        Assert.True(CellColorDialog.TryParseHex("000000", out Color black));
        Assert.Equal(Color.Black.ToArgb(), black.ToArgb());
    }

    [Test] public void Themed_color_picker_rejects_invalid_hex_colors()
    {
        Assert.False(CellColorDialog.TryParseHex("", out _));
        Assert.False(CellColorDialog.TryParseHex("#12345", out _));
        Assert.False(CellColorDialog.TryParseHex("#GG0000", out _));
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

    [Test] public void Changed_is_forwarded_only_from_the_current_provider_sheet()
    {
        var first = NewSheet();
        var second = NewSheet();
        SheetModel current = first;
        using var source = new SheetModelDataSource(() => current);
        var changes = new List<(SheetModel Sheet, WorksheetChangeSet Changes)>();
        source.Changed += (_, change) => changes.Add((source.Sheet, change));

        first.SetCellValue(0, 0, "first");
        current = second;
        source.RefreshBinding();
        first.SetCellValue(0, 1, "stale");
        second.SetCellValue(1, 1, "second");

        Assert.Equal(2, changes.Count);
        Assert.Same(first, changes[0].Sheet);
        Assert.Same(second, changes[1].Sheet);
        Assert.True(changes[1].Changes.ChangedAddresses.Contains(new CellAddress(1, 1)));
    }

    [Test] public void Provider_switch_detected_by_old_notification_suppresses_ghost_event_and_rebinds()
    {
        var first = NewSheet();
        var second = NewSheet();
        SheetModel current = first;
        using var source = new SheetModelDataSource(() => current);
        int eventCount = 0;
        source.Changed += (_, _) => eventCount++;

        current = second;
        first.SetCellValue(0, 0, "late old write");
        second.SetCellValue(0, 0, "new write");

        Assert.Equal(1, eventCount);
    }

    [Test] public void Dispose_unsubscribes_from_the_bound_sheet()
    {
        var sheet = NewSheet();
        var source = new SheetModelDataSource(() => sheet);
        int eventCount = 0;
        source.Changed += (_, _) => eventCount++;
        source.Dispose();

        sheet.SetCellValue(0, 0, "after dispose");

        Assert.Equal(0, eventCount);
    }
}
