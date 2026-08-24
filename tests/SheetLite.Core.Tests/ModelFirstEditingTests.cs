using SheetLite;

namespace SheetLite.Tests;

/// <summary>
/// Phase 1 acceptance criteria from refactor-options/VIRTUALIZED_GRID.md:
/// save and undo must use model state even when no grid cell was ever rendered,
/// and structural edits must keep formulas consistent.
/// </summary>
internal sealed class ModelFirstEditingTests : IDisposable
{
    private readonly string directory;

    public ModelFirstEditingTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "sheetlite-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private string TempPath(string extension) => Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);

    [Test] public void Saving_uses_model_state_without_any_grid_rendering()
    {
        WorkbookModel workbook = WorkbookModel.CreateBlank();
        SheetModel model = workbook.ActiveSheet.Sheet;
        model.SetCellValue(0, 0, "name");
        model.SetCellValue(0, 1, "amount");
        model.SetCellValue(1, 0, "widget");
        model.SetCellValue(1, 1, "42");
        model.SetCellValue(2, 1, "=B2*2");

        string file = TempPath(".csv");
        CsvCodec.Save(file, model);
        var reloaded = CsvCodec.Load(file);

        Assert.Equal("name", reloaded.GetRawValue(0, 0));
        Assert.Equal("42", reloaded.GetRawValue(1, 1));
        Assert.Equal("=B2*2", reloaded.GetRawValue(2, 1));
    }

    [Test] public void Edits_outside_rendered_dimensions_are_saved()
    {
        // Simulates a virtual grid that only materialized the viewport (rows 0..9):
        // a write to row 500 must still reach the saved file because the model is authoritative.
        WorkbookModel workbook = WorkbookModel.CreateBlank();
        SheetModel model = workbook.ActiveSheet.Sheet;
        model.SetCellValue(500, 19, "deep value");

        string file = TempPath(".csv");
        CsvCodec.Save(file, model);
        var reloaded = CsvCodec.Load(file);

        Assert.Equal("deep value", reloaded.GetRawValue(500, 19));
    }

    [Test] public void Undo_snapshot_restores_values_formatting_and_structure_from_the_model()
    {
        WorkbookModel workbook = WorkbookModel.CreateBlank();
        SheetModel model = workbook.ActiveSheet.Sheet;
        model.SetCell(new CellAddress(0, 0), new CellEdit { Value = "original", Bold = true });
        int rowsBeforeEdit = model.RowCount;

        object snapshot = workbook.Clone();          // what PushUndo stores
        model.SetCellValue(0, 0, "edited");
        model.InsertRows(1, 3);
        model.SetCellValue(4, 4, "post-undo noise");

        var restored = (WorkbookModel)snapshot;
        SheetModel sheet = restored.ActiveSheet.Sheet;
        Assert.Equal("original", sheet.GetRawValue(0, 0));
        Assert.True(sheet.GetCell(0, 0).Bold);
        Assert.Equal(rowsBeforeEdit, sheet.RowCount);
        Assert.Equal("", sheet.GetRawValue(4, 4));
    }

    [Test] public void Structural_edits_keep_formulas_evaluable_without_a_grid()
    {
        var model = new SheetModel();
        model.EnsureSize(4, 2);
        model.SetCellValue(0, 1, "10");
        model.SetCellValue(1, 1, "20");
        model.SetCellValue(3, 1, "=SUM(B1:B2)");   // total row under the data
        Assert.Equal("30", model.EvaluatedValue(3, 1));

        model.InsertColumns(0);                     // values and formula shift right to column C
        Assert.Equal("=SUM(C1:C2)", model.GetRawValue(3, 2));
        Assert.Equal("30", model.EvaluatedValue(3, 2));

        model.ReorderRows([0, 2, 1, 3]);            // swap the two data rows, like a sort
        Assert.True(model.IsFormula(3, 2));
        Assert.Equal("=SUM(C1:C3)", model.GetRawValue(3, 2));   // reference followed the moved data
        Assert.Equal("30", model.EvaluatedValue(3, 2));
    }

    [Test] public void Formula_display_updates_when_referenced_cell_changes_through_model()
    {
        var model = new SheetModel();
        model.EnsureSize(2, 1);
        model.SetCellValue(0, 0, "5");
        model.SetCellValue(1, 0, "=A1*2");
        Assert.Equal("10", model.EvaluatedValue(1, 0));
        model.SetCellValue(0, 0, "7");
        Assert.Equal("14", model.EvaluatedValue(1, 0));
    }

    [Test] public void Xlsx_roundtrip_preserves_model_formatting_set_via_api()
    {
        WorkbookModel workbook = WorkbookModel.CreateBlank();
        SheetModel model = workbook.ActiveSheet.Sheet;
        model.SetCell(new CellAddress(0, 0), new CellEdit { Value = "styled", BackColor = Color.Purple, ForeColor = Color.White, Bold = true });

        string file = TempPath(".xlsx");
        XlsxCodec.SaveWorkbook(file, workbook);
        var loaded = XlsxCodec.LoadWorkbook(file);
        CellModel cell = loaded.ActiveSheet.Sheet.GetCell(0, 0);

        Assert.Equal("styled", cell.Value);
        Assert.True(cell.Bold);
        Assert.NotNull(cell.BackColor);
        Assert.NotNull(cell.ForeColor);
    }
}
