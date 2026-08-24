using SheetLite;

namespace SheetLite.Tests;

internal sealed class ModelGraphIntegrationTests
{
    private static SheetModel NewSheet(int rows = 4, int columns = 4)
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(rows, columns);
        return sheet;
    }

    [Test] public void Mutations_do_not_eagerly_create_a_formula_graph()
    {
        var sheet = NewSheet();

        sheet.SetCellValue(0, 0, "=1+1");

        Assert.False(FormulaEngine.TryGetGraph(sheet, out _));
        Assert.Equal("2", sheet.EvaluatedValue(0, 0));
        Assert.True(FormulaEngine.TryGetGraph(sheet, out _));
    }

    [Test] public void Nested_batch_updates_graph_and_publishes_once_without_touching_unaffected_formula()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(1, 0, "2");
        sheet.SetCellValue(0, 1, "=A1*2");
        sheet.SetCellValue(1, 1, "=A2*3");
        sheet.SetCellValue(0, 3, "=99+1");
        var graph = FormulaEngine.GetGraph(sheet);
        graph.ResetMetrics();
        int version = sheet.Version;
        int eventCount = 0;
        WorksheetChangeSet? published = null;
        sheet.Changed += (_, changes) => { eventCount++; published = changes; };

        using (sheet.BeginUpdate())
        {
            sheet.SetCellValue(0, 0, "4");
            using (sheet.BeginUpdate()) sheet.SetCellValue(1, 0, "5");
        }

        Assert.Equal(version + 1, sheet.Version);
        Assert.Equal(1, eventCount);
        Assert.NotNull(published);
        Assert.True(published!.ChangedAddresses.Contains(new CellAddress(0, 0)));
        Assert.True(published.ChangedAddresses.Contains(new CellAddress(1, 0)));
        Assert.True(published.ChangedAddresses.Contains(new CellAddress(0, 1)));
        Assert.True(published.ChangedAddresses.Contains(new CellAddress(1, 1)));
        Assert.False(published.ChangedAddresses.Contains(new CellAddress(0, 3)));
        Assert.Equal(2, graph.Metrics.EvaluatedNodeCount);
        Assert.Equal(0, graph.Metrics.ParsedFormulaCount);
        Assert.Equal(new NumberValue(8), graph.GetValue(new FormulaCellAddress(0, 1)));
        Assert.Equal(new NumberValue(15), graph.GetValue(new FormulaCellAddress(1, 1)));
    }

    [Test] public void Formula_reads_are_rejected_while_raw_batch_changes_are_pending()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(0, 1, "=A1+1");
        var graph = FormulaEngine.GetGraph(sheet);

        using (sheet.BeginUpdate())
        {
            sheet.SetCellValue(0, 0, "2");

            Assert.True(sheet.HasPendingRawMutations);
            Assert.Throws<InvalidOperationException>(() => graph.GetValue(new FormulaCellAddress(0, 1)));
            Assert.Throws<InvalidOperationException>(() => FormulaEngine.Evaluate(sheet, 0, 1));
            Assert.Throws<InvalidOperationException>(() => FormulaEngine.EvaluateExpression(sheet, "=A1+1"));
        }
    }

    [Test] public void Formula_reads_are_fresh_after_raw_batch_disposal()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(0, 1, "=A1+1");
        var graph = FormulaEngine.GetGraph(sheet);

        using (sheet.BeginUpdate())
            sheet.SetCellValue(0, 0, "8");

        Assert.False(sheet.HasPendingRawMutations);
        Assert.Equal(new NumberValue(9), graph.GetValue(new FormulaCellAddress(0, 1)));
        Assert.Equal("9", FormulaEngine.Evaluate(sheet, 0, 1).Value);
    }

    [Test] public void Formatting_only_batches_remain_formula_readable()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(0, 1, "=A1+1");
        var graph = FormulaEngine.GetGraph(sheet);

        using (sheet.BeginUpdate())
        {
            sheet.SetCell(new CellAddress(0, 0), CellEdit.Format(bold: true));

            Assert.False(sheet.HasPendingRawMutations);
            Assert.Equal(new NumberValue(2), graph.GetValue(new FormulaCellAddress(0, 1)));
            Assert.Equal("2", FormulaEngine.Evaluate(sheet, 0, 1).Value);
        }
    }

    [Test] public void Formatting_only_change_synchronizes_version_without_reparse()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "=40+2");
        var graph = FormulaEngine.GetGraph(sheet);
        graph.ResetMetrics();
        int eventCount = 0;
        WorksheetChangeSet? published = null;
        sheet.Changed += (_, changes) => { eventCount++; published = changes; };

        sheet.SetCell(new CellAddress(0, 0), CellEdit.Format(bold: true));

        Assert.Equal(sheet.Version, graph.SynchronizedVersion);
        Assert.Equal(0, graph.Metrics.ParsedFormulaCount);
        Assert.Equal(0, graph.Metrics.EvaluatedNodeCount);
        Assert.Equal(1, eventCount);
        Assert.True(published!.ChangedAddresses.Contains(new CellAddress(0, 0)));
        Assert.False(published.RequiresFullRefresh);
    }

    [Test] public void True_no_op_has_no_version_or_event()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "same");
        int version = sheet.Version;
        int eventCount = 0;
        sheet.Changed += (_, _) => eventCount++;

        sheet.SetCellValue(0, 0, "same");
        sheet.SetCell(new CellAddress(0, 0), CellEdit.Format(bold: false));

        Assert.Equal(version, sheet.Version);
        Assert.Equal(0, eventCount);
    }

    [Test] public void Insert_columns_captures_undo_prestate_before_formula_rewrite_and_rebuilds_once()
    {
        var sheet = NewSheet(1, 2);
        sheet.SetCellValue(0, 0, "5");
        sheet.SetCellValue(0, 1, "=A1");
        Guid worksheetId = Guid.NewGuid();
        sheet.TakeUndoSegment(worksheetId);
        var graph = FormulaEngine.GetGraph(sheet);
        graph.ResetMetrics();
        int eventCount = 0;
        WorksheetChangeSet? published = null;
        sheet.Changed += (_, changes) => { eventCount++; published = changes; };

        sheet.InsertColumns(0);

        Assert.Equal("=B1", sheet.GetRawValue(0, 2));
        Assert.Equal(1, graph.Metrics.RebuildCount);
        Assert.Equal(1, eventCount);
        Assert.True(published!.StructureChanged);
        Assert.True(published.RequiresFullRefresh);
        var step = sheet.TakeUndoSegment(worksheetId);
        Assert.NotNull(step);
        graph.ResetMetrics();
        eventCount = 0;

        step!.Undo(sheet);

        Assert.Equal(2, sheet.ColumnCount);
        Assert.Equal("5", sheet.GetRawValue(0, 0));
        Assert.Equal("=A1", sheet.GetRawValue(0, 1));
        Assert.Equal("5", sheet.EvaluatedValue(0, 1));
        Assert.Equal(1, graph.Metrics.RebuildCount);
        Assert.Equal(1, eventCount);
        Assert.True(published!.RequiresFullRefresh);
    }

    [Test] public void Apply_snapshots_batches_explicit_restores_into_one_change()
    {
        var sheet = NewSheet(2, 2);
        sheet.SetCellValue(0, 0, "before-a");
        sheet.SetCellValue(1, 0, "before-b");
        Guid worksheetId = Guid.NewGuid();
        sheet.TakeUndoSegment(worksheetId);
        using (sheet.BeginUpdate())
        {
            sheet.SetCellValue(0, 0, "after-a");
            sheet.SetCellValue(1, 0, "after-b");
        }
        var step = sheet.TakeUndoSegment(worksheetId);
        Assert.NotNull(step);
        int version = sheet.Version;
        int eventCount = 0;
        WorksheetChangeSet? published = null;
        sheet.Changed += (_, changes) => { eventCount++; published = changes; };

        step!.Undo(sheet);

        Assert.Equal(version + 1, sheet.Version);
        Assert.Equal(1, eventCount);
        Assert.Equal(2, published!.ChangedAddresses.Count);
        Assert.Equal("before-a", sheet.GetRawValue(0, 0));
        Assert.Equal("before-b", sheet.GetRawValue(1, 0));
    }

    [Test] public void Version_gap_rebuilds_instead_of_accepting_incomplete_manual_notification()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(0, 1, "=A1");
        sheet.SetCellValue(0, 2, "2");
        sheet.SetCellValue(0, 3, "=C1");
        var detachedGraph = new FormulaDependencyGraph(sheet);
        detachedGraph.ResetMetrics();

        // Both writes share one adjacent version. A detached graph receives no authoritative
        // model notification, so its next read must detect the version gap and rebuild.
        using (sheet.BeginUpdate())
        {
            sheet.SetCellValue(0, 0, "5");
            sheet.SetCellValue(0, 2, "7");
        }

        Assert.Equal(new NumberValue(5), detachedGraph.GetValue(new FormulaCellAddress(0, 1)));
        Assert.Equal(1, detachedGraph.Metrics.RebuildCount);
        Assert.Equal(new NumberValue(7), detachedGraph.GetValue(new FormulaCellAddress(0, 3)));
        Assert.Equal(sheet.Version, detachedGraph.SynchronizedVersion);
    }

    [Test] public void Token_mismatch_fallback_publishes_full_refresh_without_claiming_structure_change()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(0, 1, "=A1+1");
        var graph = FormulaEngine.GetGraph(sheet);
        var versionField = typeof(FormulaDependencyGraph).GetField(
            "synchronizedVersion",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(versionField);
        versionField!.SetValue(graph, sheet.Version - 1); // simulate one omitted prior notification
        WorksheetChangeSet? published = null;
        sheet.Changed += (_, changes) => published = changes;

        sheet.SetCellValue(0, 0, "9");

        Assert.NotNull(published);
        Assert.True(published!.RequiresFullRefresh);
        Assert.False(published.StructureChanged);
        Assert.Equal("10", sheet.EvaluatedValue(0, 1));
    }

    [Test] public void Mixed_batch_replaces_formula_edges_and_recalculates_shared_dependents_once()
    {
        var sheet = NewSheet(1, 6);
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(0, 1, "2");
        sheet.SetCellValue(0, 2, "=A1");
        sheet.SetCellValue(0, 3, "=C1+1");
        sheet.SetCellValue(0, 4, "=B1");
        sheet.SetCellValue(0, 5, "plain");
        var graph = FormulaEngine.GetGraph(sheet);
        graph.ResetMetrics();
        int eventCount = 0;
        sheet.Changed += (_, _) => eventCount++;

        using (sheet.BeginUpdate())
        {
            sheet.SetCellValue(0, 0, "10");
            sheet.SetCellValue(0, 1, "20");
            sheet.SetCellValue(0, 2, "=B1");
            sheet.SetCellValue(0, 4, "literal");
            sheet.SetCellValue(0, 5, "=C1+2");
        }

        Assert.Equal(1, eventCount);
        Assert.Equal(3, graph.Metrics.EvaluatedNodeCount);
        Assert.Equal(new NumberValue(20), graph.GetValue(new FormulaCellAddress(0, 2)));
        Assert.Equal(new NumberValue(21), graph.GetValue(new FormulaCellAddress(0, 3)));
        Assert.Equal(new TextValue("literal"), graph.GetValue(new FormulaCellAddress(0, 4)));
        Assert.Equal(new NumberValue(22), graph.GetValue(new FormulaCellAddress(0, 5)));

        graph.ResetMetrics();
        sheet.SetCellValue(0, 0, "99");
        Assert.Equal(0, graph.Metrics.EvaluatedNodeCount);

        graph.ResetMetrics();
        sheet.SetCellValue(0, 1, "30");
        Assert.Equal(3, graph.Metrics.EvaluatedNodeCount);
        Assert.Equal(new NumberValue(30), graph.GetValue(new FormulaCellAddress(0, 2)));
        Assert.Equal(new NumberValue(31), graph.GetValue(new FormulaCellAddress(0, 3)));
        Assert.Equal(new NumberValue(32), graph.GetValue(new FormulaCellAddress(0, 5)));
    }

    [Test] public void Sql_query_consumes_fresh_cached_formula_results_after_precedent_edit()
    {
        var sheet = NewSheet(3, 2);
        sheet.SetCellValue(0, 0, "Input");
        sheet.SetCellValue(0, 1, "Double");
        sheet.SetCellValue(1, 0, "3");
        sheet.SetCellValue(1, 1, "=A2*2");
        sheet.SetCellValue(2, 0, "8");
        sheet.SetCellValue(2, 1, "=A3*2");
        var graph = FormulaEngine.GetGraph(sheet);

        sheet.SetCellValue(1, 0, "7");
        graph.ResetMetrics();
        var result = SqlQueryEngine.Execute(
            sheet,
            "SELECT Input, Double WHERE Double >= 14 ORDER BY Double DESC",
            new SqlQueryOptions { FirstRowIsHeader = true });

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.Rows.Count);
        Assert.Sequence(["8", "16"], result.Rows[0].Select(cell => cell.Value));
        Assert.Sequence(["7", "14"], result.Rows[1].Select(cell => cell.Value));
        Assert.Equal(0, graph.Metrics.EvaluatedNodeCount);
        Assert.True(graph.Metrics.CacheHitCount > 0);
    }
}
