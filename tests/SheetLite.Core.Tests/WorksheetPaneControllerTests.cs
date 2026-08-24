using System.Reflection;
using SheetLite;

namespace SheetLite.Tests;

internal sealed class WorksheetPaneControllerTests
{
    [Test] public void Virtual_callbacks_read_and_format_wide_cells_on_low_row_sheets()
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(2, 10);
        sheet.SetCell(new CellAddress(0, 9), new CellEdit { Value = "=1+1", BackColor = Color.Purple, ForeColor = Color.White });

        using var grid = new DataGridView();
        var pane = new WorksheetPaneController(grid, new SheetModelDataSource(() => sheet));
        pane.View.Reset(2, 10);

        var needed = new DataGridViewCellValueEventArgs(9, 0);
        InvokePrivate(pane, "OnValueNeeded", needed);
        Assert.Equal("2", needed.Value?.ToString());

        var formatting = new DataGridViewCellFormattingEventArgs(9, 0, needed.Value, typeof(string), new DataGridViewCellStyle());
        InvokePrivate(pane, "OnCellFormatting", formatting);
        Assert.Equal(Color.Purple, formatting.CellStyle.BackColor);
        Assert.Equal(Color.White, formatting.CellStyle.ForeColor);
    }

    [Test] public void Change_mapping_uses_the_panes_filtered_row_map_and_skips_hidden_cells()
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(4, 3);
        using var grid = Grid(4, 3);
        using var pane = new WorksheetPaneController(grid, new SheetModelDataSource(() => sheet));
        pane.View.Reset(4, 3);
        pane.View.HideRows(row => row == 1);
        pane.ApplyView();
        grid.Columns[2].Visible = false;

        var changes = new WorksheetChangeSet(
            [new CellAddress(0, 0), new CellAddress(1, 0), new CellAddress(3, 1), new CellAddress(2, 2)],
            structureChanged: false);

        Assert.Sequence([(0, 0), (1, 2)], pane.MapChangedCells(changes));
    }

    [Test] public void Full_refresh_change_set_is_not_mapped_as_targeted_cells()
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(2, 2);
        using var grid = Grid(2, 2);
        using var pane = new WorksheetPaneController(grid, new SheetModelDataSource(() => sheet));
        pane.View.Reset(2, 2);

        var changes = new WorksheetChangeSet([new CellAddress(0, 0)], structureChanged: true);

        Assert.Equal(0, pane.MapChangedCells(changes).Count);
    }

    [Test] public void Two_panes_bound_to_one_model_keep_independent_change_mappings()
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(4, 2);
        using var firstGrid = Grid(4, 2);
        using var secondGrid = Grid(4, 2);
        using var first = new WorksheetPaneController(firstGrid, new SheetModelDataSource(() => sheet));
        using var second = new WorksheetPaneController(secondGrid, new SheetModelDataSource(() => sheet));
        first.View.Reset(4, 2);
        second.View.Reset(4, 2);
        first.View.HideRows(row => row == 1);
        second.View.HideRows(row => row == 2);
        first.ApplyView();
        second.ApplyView();
        var changes = new WorksheetChangeSet([new CellAddress(1, 0), new CellAddress(2, 0)], structureChanged: false);

        Assert.Sequence([(0, 1)], first.MapChangedCells(changes));
        Assert.Sequence([(0, 1)], second.MapChangedCells(changes));
        Assert.Equal(2, first.ModelRow(first.MapChangedCells(changes)[0].Row));
        Assert.Equal(1, second.ModelRow(second.MapChangedCells(changes)[0].Row));
    }

    private static DataGridView Grid(int rows, int columns)
    {
        var grid = new DataGridView { VirtualMode = true, AllowUserToAddRows = false };
        for (int column = 0; column < columns; column++) grid.Columns.Add($"C{column}", $"C{column}");
        grid.RowCount = rows;
        return grid;
    }

    private static void InvokePrivate(WorksheetPaneController pane, string methodName, EventArgs args)
    {
        MethodInfo method = typeof(WorksheetPaneController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing {methodName}.");
        method.Invoke(pane, [null, args]);
    }
}
