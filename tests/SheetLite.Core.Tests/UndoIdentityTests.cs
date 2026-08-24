using SheetLite;

namespace SheetLite.Tests;

internal sealed class UndoIdentityTests
{
    [Test] public void Workbook_clone_preserves_worksheet_ids_but_not_sheet_or_graph_instances()
    {
        var workbook = WorkbookModel.CreateBlank();
        workbook.Sheets.Add(new WorksheetModel("Second", NewSheet()));
        FormulaDependencyGraph originalGraph = FormulaEngine.GetGraph(workbook.Sheets[0].Sheet);

        WorkbookModel clone = workbook.Clone();

        Assert.True(workbook.Sheets.Select(sheet => sheet.Id).SequenceEqual(clone.Sheets.Select(sheet => sheet.Id)));
        Assert.False(ReferenceEquals(workbook.Sheets[0].Sheet, clone.Sheets[0].Sheet));
        Assert.False(ReferenceEquals(originalGraph, FormulaEngine.GetGraph(clone.Sheets[0].Sheet)));
    }

    [Test] public void Blank_import_and_copy_wrappers_get_fresh_worksheet_ids()
    {
        WorkbookModel firstBlank = WorkbookModel.CreateBlank();
        WorkbookModel secondBlank = WorkbookModel.CreateBlank();
        var importedSource = NewSheet();
        WorkbookModel firstImport = WorkbookModel.FromSheet(importedSource, "Imported");
        WorkbookModel secondImport = WorkbookModel.FromSheet(importedSource.Clone(), "Imported copy");
        var copiedWrapper = new WorksheetModel("Copied", firstImport.ActiveSheet.Sheet.Clone());

        Assert.False(firstBlank.ActiveSheet.Id == secondBlank.ActiveSheet.Id);
        Assert.False(firstImport.ActiveSheet.Id == secondImport.ActiveSheet.Id);
        Assert.False(firstImport.ActiveSheet.Id == copiedWrapper.Id);
    }

    [Test] public void Cell_edit_undo_and_redo_restore_formula_edges_on_the_resolved_sheet()
    {
        WorkbookModel workbook = WorkbookModel.CreateBlank();
        WorksheetModel worksheet = workbook.ActiveSheet;
        SheetModel sheet = worksheet.Sheet;
        sheet.SetCellValue(0, 0, "3");
        sheet.SetCellValue(0, 1, "8");
        sheet.SetCellValue(0, 2, "=A1");
        sheet.TakeUndoSegment(worksheet.Id);

        sheet.SetCellValue(0, 2, "=B1");
        IUndoStep step = sheet.TakeUndoSegment(worksheet.Id)!;
        Assert.Equal("8", sheet.EvaluatedValue(0, 2));

        step.Undo(workbook.FindWorksheet(step.WorksheetId)!.Sheet);
        Assert.Equal("=A1", sheet.GetRawValue(0, 2));
        Assert.Equal("3", sheet.EvaluatedValue(0, 2));
        sheet.SetCellValue(0, 0, "4");
        Assert.Equal("4", sheet.EvaluatedValue(0, 2));
        sheet.TakeUndoSegment(worksheet.Id);

        step.Redo(workbook.FindWorksheet(step.WorksheetId)!.Sheet);
        Assert.Equal("=B1", sheet.GetRawValue(0, 2));
        Assert.Equal("8", sheet.EvaluatedValue(0, 2));
        sheet.SetCellValue(0, 1, "9");
        Assert.Equal("9", sheet.EvaluatedValue(0, 2));
    }

    [Test] public void Structural_undo_and_redo_restore_formula_references_and_graph_values()
    {
        WorkbookModel workbook = WorkbookModel.FromSheet(NewSheet(1, 2));
        WorksheetModel worksheet = workbook.ActiveSheet;
        SheetModel sheet = worksheet.Sheet;
        sheet.SetCellValue(0, 0, "5");
        sheet.SetCellValue(0, 1, "=A1");
        sheet.TakeUndoSegment(worksheet.Id);

        sheet.InsertColumns(0);
        IUndoStep step = sheet.TakeUndoSegment(worksheet.Id)!;
        Assert.Equal("=B1", sheet.GetRawValue(0, 2));
        Assert.Equal("5", sheet.EvaluatedValue(0, 2));

        step.Undo(sheet);
        Assert.Equal(2, sheet.ColumnCount);
        Assert.Equal("=A1", sheet.GetRawValue(0, 1));
        Assert.Equal("5", sheet.EvaluatedValue(0, 1));

        step.Redo(sheet);
        Assert.Equal(3, sheet.ColumnCount);
        Assert.Equal("=B1", sheet.GetRawValue(0, 2));
        Assert.Equal("5", sheet.EvaluatedValue(0, 2));
    }

    [Test] public void Older_cell_step_after_snapshot_restore_updates_live_clone_and_its_graph()
    {
        WorkbookModel workbook = WorkbookModel.FromSheet(NewSheet(1, 2));
        WorksheetModel worksheet = workbook.ActiveSheet;
        SheetModel detached = worksheet.Sheet;
        detached.SetCellValue(0, 0, "5");
        detached.SetCellValue(0, 1, "=A1");
        detached.TakeUndoSegment(worksheet.Id);
        detached.SetCellValue(0, 0, "9");
        IUndoStep olderStep = detached.TakeUndoSegment(worksheet.Id)!;

        WorkbookModel restoredSnapshot = workbook.Clone();
        SheetModel live = restoredSnapshot.FindWorksheet(olderStep.WorksheetId)!.Sheet;
        Assert.False(ReferenceEquals(detached, live));
        Assert.Equal("9", live.EvaluatedValue(0, 1));

        olderStep.Undo(live);

        Assert.Equal("5", live.GetRawValue(0, 0));
        Assert.Equal("5", live.EvaluatedValue(0, 1));
        Assert.Equal("9", detached.GetRawValue(0, 0));
    }

    [Test] public void Older_structure_step_after_snapshot_restore_updates_live_clone_and_its_graph()
    {
        WorkbookModel workbook = WorkbookModel.FromSheet(NewSheet(1, 2));
        WorksheetModel worksheet = workbook.ActiveSheet;
        SheetModel detached = worksheet.Sheet;
        detached.SetCellValue(0, 0, "6");
        detached.SetCellValue(0, 1, "=A1");
        detached.TakeUndoSegment(worksheet.Id);
        detached.InsertColumns(0);
        IUndoStep olderStep = detached.TakeUndoSegment(worksheet.Id)!;

        WorkbookModel restoredSnapshot = workbook.Clone();
        SheetModel live = restoredSnapshot.FindWorksheet(olderStep.WorksheetId)!.Sheet;
        Assert.False(ReferenceEquals(detached, live));
        Assert.Equal("6", live.EvaluatedValue(0, 2));

        olderStep.Undo(live);

        Assert.Equal(2, live.ColumnCount);
        Assert.Equal("=A1", live.GetRawValue(0, 1));
        Assert.Equal("6", live.EvaluatedValue(0, 1));
        Assert.Equal(3, detached.ColumnCount);

        olderStep.Redo(live);
        Assert.Equal(3, live.ColumnCount);
        Assert.Equal("=B1", live.GetRawValue(0, 2));
        Assert.Equal("6", live.EvaluatedValue(0, 2));
    }

    [Test] public void Missing_worksheet_identity_is_not_resolved_to_an_unrelated_sheet()
    {
        WorkbookModel workbook = WorkbookModel.CreateBlank();

        Assert.Null(workbook.FindWorksheet(Guid.NewGuid()));
    }

    [Test] public void Large_non_structural_segment_remains_an_invertible_cell_edit_step()
    {
        const int editCount = 100_001;
        WorkbookModel workbook = WorkbookModel.FromSheet(NewSheet(1, editCount));
        WorksheetModel worksheet = workbook.ActiveSheet;
        SheetModel sheet = worksheet.Sheet;
        using (sheet.BeginUpdate())
            for (int column = 0; column < editCount; column++) sheet.SetCellValue(0, column, "x");

        IUndoStep step = sheet.TakeUndoSegment(worksheet.Id)!;

        Assert.True(step is CellEditsStep);
        step.Undo(sheet);
        Assert.Equal("", sheet.GetRawValue(0, 0));
        Assert.Equal("", sheet.GetRawValue(0, editCount - 1));
        step.Redo(sheet);
        Assert.Equal("x", sheet.GetRawValue(0, 0));
        Assert.Equal("x", sheet.GetRawValue(0, editCount - 1));
    }

    private static SheetModel NewSheet(int rows = 2, int columns = 3)
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(rows, columns);
        return sheet;
    }
}
