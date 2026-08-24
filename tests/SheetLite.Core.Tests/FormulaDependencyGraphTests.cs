using SheetLite;

namespace SheetLite.Tests;

internal sealed class FormulaDependencyGraphTests
{
    private static SheetModel NewSheet(int rows = 8, int columns = 8)
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(rows, columns);
        return sheet;
    }

    [Test] public void Parser_builds_reusable_tree_and_extracts_cell_and_range_dependencies()
    {
        var parsed = FormulaParser.Parse(" = SUM($A$1:B2, 'A1') + C3");

        Assert.True(parsed.Success);
        Assert.True(parsed.Expression is BinaryExpression);
        var dependencies = FormulaDependencyExtractor.Extract(parsed.Expression!);
        Assert.Equal(2, dependencies.Count);
        Assert.True(dependencies.Contains(new CellDependency(new FormulaCellAddress(2, 2))));
        Assert.True(dependencies.Contains(new RangeDependency(new FormulaRangeAddress(
            new FormulaCellAddress(0, 0), new FormulaCellAddress(1, 1)))));
    }

    [Test] public void Parser_preserves_absolute_reference_markers()
    {
        var parsed = FormulaParser.Parse("=$BC$12");

        var reference = parsed.Expression as CellReferenceExpression;
        Assert.NotNull(reference);
        Assert.Equal(new FormulaCellAddress(11, 54), reference!.Address);
        Assert.True(reference.AbsoluteRow);
        Assert.True(reference.AbsoluteColumn);
    }

    [Test] public void Typed_values_and_errors_keep_legacy_entry_points_compatible()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "=1/0");
        sheet.SetCellValue(0, 1, "=TRUE");

        var graph = FormulaEngine.GetGraph(sheet);
        Assert.Equal(new ErrorValue(FormulaError.DivisionByZero, "Division by zero."), graph.GetValue(new FormulaCellAddress(0, 0)));
        Assert.Equal(new BooleanValue(true), graph.GetValue(new FormulaCellAddress(0, 1)));
        Assert.False(FormulaEngine.Evaluate(sheet, 0, 0).Success);
        Assert.Equal("Division by zero.", FormulaEngine.Evaluate(sheet, 0, 0).Error);
        Assert.Equal("TRUE", FormulaEngine.Evaluate(sheet, 0, 1).Value);
    }

    [Test] public void Tree_evaluator_preserves_current_operator_and_function_behavior()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "2");
        sheet.SetCellValue(1, 0, "3");
        sheet.SetCellValue(2, 0, "4");

        Assert.Equal("512", FormulaEngine.EvaluateExpression(sheet, "=2^3^2").Value);
        Assert.Equal("10", FormulaEngine.EvaluateExpression(sheet, "=SUM(A1:A3, 1)").Value);
        Assert.Equal("3", FormulaEngine.EvaluateExpression(sheet, "=AVERAGE(A1:A3)").Value);
        Assert.Equal("3", FormulaEngine.EvaluateExpression(sheet, "=COUNT(A1:A3, 'x')").Value);
        Assert.Equal("a'b2", FormulaEngine.EvaluateExpression(sheet, "=CONCAT('a''b', A1)").Value);
        Assert.Equal("7", FormulaEngine.EvaluateExpression(sheet, "=-(1-8)").Value);
    }

    [Test] public void Parse_and_evaluation_failures_have_typed_errors_and_legacy_messages()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "text");

        var badSyntax = FormulaParser.Parse("=1+)");
        Assert.Equal(FormulaError.Value, badSyntax.Error!.Error);
        Assert.True(FormulaEngine.EvaluateExpression(sheet, "=A1+1").Error!.Contains("not numeric", StringComparison.Ordinal));
        Assert.Equal("Cross-sheet references are not supported.", FormulaEngine.EvaluateExpression(sheet, "=Sheet2!A1").Error);
        Assert.True(FormulaEngine.EvaluateExpression(sheet, "=MISSING(1)").Error!.Contains("Unknown function", StringComparison.Ordinal));
    }

    [Test] public void Cross_sheet_detection_is_not_masked_by_a_ref_error_token()
    {
        var sheet = NewSheet();

        Assert.Equal("Invalid cell reference.", FormulaEngine.EvaluateExpression(sheet, "=#REF!").Error);
        Assert.Equal(
            "Cross-sheet references are not supported.",
            FormulaEngine.EvaluateExpression(sheet, "=#REF!+Sheet2!A1").Error);
    }

    [Test] public void Exclamation_marks_inside_quoted_strings_are_not_sheet_references()
    {
        var sheet = NewSheet();

        Assert.Equal("Hello!", FormulaEngine.EvaluateExpression(sheet, "=CONCAT(\"Hello!\")").Value);
        Assert.Equal("It's!", FormulaEngine.EvaluateExpression(sheet, "=CONCAT('It''s!')").Value);
        Assert.Equal(
            "Say \"Hello!\"",
            FormulaEngine.EvaluateExpression(sheet, "=CONCAT(\"Say \"\"Hello!\"\"\")").Value);
        Assert.Equal(
            "Cross-sheet references are not supported.",
            FormulaEngine.EvaluateExpression(sheet, "=Sheet2!A1").Error);
    }

    [Test] public void Enormous_and_reversed_ranges_visit_only_stored_cells()
    {
        var sheet = NewSheet(rows: 3, columns: 3);
        sheet.SetCellValue(0, 0, "2");
        sheet.SetCellValue(1, 0, "3");

        Assert.Equal("5", FormulaEngine.EvaluateExpression(sheet, "=SUM(A2147483647:A1)").Value);
        Assert.Equal("2.5", FormulaEngine.EvaluateExpression(sheet, "=AVERAGE(A1:A2147483647)").Value);
        Assert.Equal("2", FormulaEngine.EvaluateExpression(sheet, "=MIN(A2147483647:A1)").Value);
        Assert.Equal("3", FormulaEngine.EvaluateExpression(sheet, "=MAX(A1:A2147483647)").Value);
        Assert.Equal("2", FormulaEngine.EvaluateExpression(sheet, "=COUNT(A2147483647:A1)").Value);
        Assert.Equal("23.", FormulaEngine.EvaluateExpression(sheet, "=CONCAT(A1:A2147483647, '.')").Value);
    }

    [Test] public void Streaming_range_evaluation_stops_at_the_first_error()
    {
        var sheet = NewSheet(rows: 3, columns: 1);
        sheet.SetCellValue(0, 0, "=1/0");
        sheet.SetCellValue(1, 0, "=40+2");
        var graph = FormulaEngine.GetGraph(sheet);
        var parsed = FormulaParser.Parse("=SUM(A1:A2147483647)");
        graph.ResetMetrics();

        var result = graph.Evaluate(parsed.Expression!);

        Assert.True(result is ErrorValue { Error: FormulaError.DivisionByZero });
        Assert.Equal(1, graph.Metrics.CacheHitCount);
        Assert.Equal(0, graph.Metrics.EvaluatedNodeCount);
    }

    [Test] public void Contexts_share_one_graph_and_clean_reads_hit_its_cache()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "=40+2");
        var first = FormulaEngine.CreateContext(sheet);
        var second = FormulaEngine.CreateContext(sheet);

        Assert.Same(first.Graph, second.Graph);
        first.Graph.ResetMetrics();
        Assert.Equal("42", first.Evaluate(0, 0).Value);
        Assert.Equal("42", second.Evaluate(0, 0).Value);
        Assert.Equal(0, first.Graph.Metrics.EvaluatedNodeCount);
        Assert.Equal(2, first.Graph.Metrics.CacheHitCount);
    }

    [Test] public void Precedent_edit_recalculates_only_transitive_dependents()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(0, 1, "=A1*2");
        sheet.SetCellValue(0, 2, "=B1+10");
        sheet.SetCellValue(0, 3, "=99+1");
        var graph = FormulaEngine.GetGraph(sheet);
        graph.ResetMetrics();
        WorksheetChangeSet? published = null;
        sheet.Changed += (_, changes) => published = changes;

        sheet.SetCellValue(0, 0, "4");

        Assert.Equal(new NumberValue(8), graph.GetValue(new FormulaCellAddress(0, 1)));
        Assert.Equal(new NumberValue(18), graph.GetValue(new FormulaCellAddress(0, 2)));
        Assert.Equal(new NumberValue(100), graph.GetValue(new FormulaCellAddress(0, 3)));
        Assert.True(published!.ChangedAddresses.Contains(new CellAddress(0, 0)));
        Assert.True(published.ChangedAddresses.Contains(new CellAddress(0, 1)));
        Assert.True(published.ChangedAddresses.Contains(new CellAddress(0, 2)));
        Assert.False(published.ChangedAddresses.Contains(new CellAddress(0, 3)));
        Assert.Equal(2, graph.Metrics.EvaluatedNodeCount);
    }

    [Test] public void Long_chain_precedent_edit_evaluates_each_formula_once_without_reparsing()
    {
        const int formulaCount = 400;
        var sheet = NewSheet(rows: formulaCount + 1, columns: 1);
        for (var row = 0; row < formulaCount; row++)
            sheet.SetCellValue(row, 0, $"=A{row + 2}");
        sheet.SetCellValue(formulaCount, 0, "1");
        var graph = FormulaEngine.GetGraph(sheet);
        graph.ResetMetrics();

        sheet.SetCellValue(formulaCount, 0, "2");

        Assert.Equal(new NumberValue(2), graph.GetValue(new FormulaCellAddress(0, 0)));
        Assert.Equal(formulaCount, graph.Metrics.EvaluatedNodeCount);
        Assert.Equal(0, graph.Metrics.ParsedFormulaCount);
        Assert.Equal(0, graph.Metrics.RebuildCount);
    }

    [Test] public void Extreme_dependency_depth_returns_typed_error_instead_of_overflowing_stack()
    {
        const int extraDepth = 16;
        var formulaCount = FormulaDependencyGraph.MaximumEvaluationDepth + extraDepth;
        var sheet = NewSheet(rows: formulaCount + 1, columns: 1);
        for (var row = 0; row < formulaCount; row++)
            sheet.SetCellValue(row, 0, $"=A{row + 2}");
        sheet.SetCellValue(formulaCount, 0, "1");

        var value = FormulaEngine.GetGraph(sheet).GetValue(new FormulaCellAddress(0, 0));

        Assert.True(value is ErrorValue { Error: FormulaError.Number });
        Assert.True(((ErrorValue)value).Detail!.Contains("dependency depth", StringComparison.Ordinal));
    }

    [Test] public void Cycle_beyond_evaluation_depth_marks_every_ring_member_as_circular()
    {
        var ringSize = FormulaDependencyGraph.MaximumEvaluationDepth + 17;
        var sheet = NewSheet(rows: ringSize, columns: 1);
        for (var row = 0; row < ringSize - 1; row++)
            sheet.SetCellValue(row, 0, $"=A{row + 2}");
        sheet.SetCellValue(ringSize - 1, 0, "=A1");
        var graph = FormulaEngine.GetGraph(sheet);

        for (var row = 0; row < ringSize; row++)
            Assert.True(graph.GetValue(new FormulaCellAddress(row, 0)) is ErrorValue
            {
                Error: FormulaError.CircularReference
            });
    }

    [Test] public void Formula_edit_replaces_obsolete_dependency_edges()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(0, 1, "2");
        sheet.SetCellValue(0, 2, "=A1");
        var graph = FormulaEngine.GetGraph(sheet);

        sheet.SetCellValue(0, 2, "=B1");
        graph.ResetMetrics();
        sheet.SetCellValue(0, 0, "7");

        Assert.Equal(0, graph.Metrics.EvaluatedNodeCount);
        Assert.Equal(new NumberValue(2), graph.GetValue(new FormulaCellAddress(0, 2)));
    }

    [Test] public void Circular_references_are_typed_and_recover_when_cycle_breaks()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "=B1+1");
        sheet.SetCellValue(0, 1, "=C1+1");
        sheet.SetCellValue(0, 2, "=A1+1");
        var graph = FormulaEngine.GetGraph(sheet);

        Assert.True(graph.GetValue(new FormulaCellAddress(0, 0)) is ErrorValue { Error: FormulaError.CircularReference });
        Assert.True(graph.GetValue(new FormulaCellAddress(0, 1)) is ErrorValue { Error: FormulaError.CircularReference });
        Assert.True(graph.GetValue(new FormulaCellAddress(0, 2)) is ErrorValue { Error: FormulaError.CircularReference });

        sheet.SetCellValue(0, 2, "2");

        Assert.Equal(new NumberValue(4), graph.GetValue(new FormulaCellAddress(0, 0)));
        Assert.Equal(new NumberValue(3), graph.GetValue(new FormulaCellAddress(0, 1)));
        Assert.Equal(new NumberValue(2), graph.GetValue(new FormulaCellAddress(0, 2)));
    }

    [Test] public void Large_ranges_remain_compact_but_still_invalidate_dependents()
    {
        var sheet = NewSheet(rows: 5000, columns: 2);
        sheet.SetCellValue(0, 1, "=SUM(A1:A5000)");
        var graph = FormulaEngine.GetGraph(sheet);
        Assert.True(graph.TryGetNode(new FormulaCellAddress(0, 1), out var node));
        Assert.Single(node!.Precedents);
        Assert.True(node.Precedents.Single() is RangeDependency);
        graph.ResetMetrics();
        WorksheetChangeSet? published = null;
        sheet.Changed += (_, changes) => published = changes;

        sheet.SetCellValue(4000, 0, "5");

        Assert.Equal(new NumberValue(5), graph.GetValue(new FormulaCellAddress(0, 1)));
        Assert.Equal(1, graph.Metrics.EvaluatedNodeCount);
        Assert.True(graph.Metrics.RangeDependencyQueries > 0);
        Assert.True(published!.ChangedAddresses.Contains(new CellAddress(0, 1)));
    }

    [Test] public void Version_divergence_safely_rebuilds_when_no_mutation_notification_arrives()
    {
        var sheet = NewSheet();
        sheet.SetCellValue(0, 0, "1");
        sheet.SetCellValue(0, 1, "=A1+1");
        var graph = FormulaEngine.GetGraph(sheet);

        sheet.SetCellValue(0, 0, "9");

        Assert.Equal(new NumberValue(10), graph.GetValue(new FormulaCellAddress(0, 1)));
        Assert.Equal(sheet.Version, graph.SynchronizedVersion);
    }
}
