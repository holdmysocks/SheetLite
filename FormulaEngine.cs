using System.Runtime.CompilerServices;

namespace SheetLite;

/// <summary>
/// Compatibility facade over the shared parsed dependency graph for a worksheet.
/// Existing callers keep receiving formatted <see cref="FormulaResult"/> values.
/// </summary>
internal static class FormulaEngine
{
    private static readonly ConditionalWeakTable<SheetModel, FormulaDependencyGraph> Graphs = new();

    public static FormulaResult Evaluate(SheetModel sheet, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return GetGraph(sheet).GetResult(new FormulaCellAddress(row, column));
    }

    public static FormulaEvaluationContext CreateContext(SheetModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return new FormulaEvaluationContext(GetGraph(sheet));
    }

    /// <summary>Returns the one graph/cache shared by every formula reader for this model.</summary>
    public static FormulaDependencyGraph GetGraph(SheetModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        EnsureFormulaReadsAllowed(sheet);
        return Graphs.GetValue(sheet, static model => new FormulaDependencyGraph(model));
    }

    /// <summary>
    /// Looks up an already-created graph without creating one. Model mutation paths use this
    /// so bulk loaders that never evaluate formulas do not pay graph construction costs.
    /// </summary>
    public static bool TryGetGraph(SheetModel sheet, out FormulaDependencyGraph? graph)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return Graphs.TryGetValue(sheet, out graph);
    }

    /// <summary>Applies post-write raw-value notifications to an existing shared graph.</summary>
    public static FormulaChangeSet NotifyCellsChanged(
        SheetModel sheet,
        IEnumerable<FormulaCellUpdate> updates,
        WorksheetMutationToken token)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(updates);
        return Graphs.TryGetValue(sheet, out var graph)
            ? graph.ApplyMutation(updates, token)
            : new FormulaChangeSet();
    }

    /// <summary>Keeps an existing graph version-aligned after formatting-only changes.</summary>
    public static FormulaChangeSet NotifyVersionChanged(SheetModel sheet, WorksheetMutationToken token)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return Graphs.TryGetValue(sheet, out var graph)
            ? graph.SynchronizeVersion(token)
            : new FormulaChangeSet();
    }

    /// <summary>Rebuilds an existing graph after row/column storage or address mappings change.</summary>
    public static FormulaChangeSet NotifyStructureChanged(SheetModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return Graphs.TryGetValue(sheet, out var graph)
            ? graph.Rebuild()
            : new FormulaChangeSet();
    }

    public static FormulaResult EvaluateExpression(SheetModel sheet, string expression)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        EnsureFormulaReadsAllowed(sheet);
        var parsed = FormulaParser.Parse(expression);
        if (parsed.Error is not null) return ToLegacyResult(parsed.Error);
        return ToLegacyResult(GetGraph(sheet).Evaluate(parsed.Expression!));
    }

    internal static FormulaResult ToLegacyResult(FormulaValue value) => value switch
    {
        ErrorValue error => new FormulaResult(false, "", FormulaValueFormatter.LegacyError(error)),
        _ => new FormulaResult(true, FormulaValueFormatter.Format(value), null)
    };

    internal static void EnsureFormulaReadsAllowed(SheetModel sheet)
    {
        if (sheet.HasPendingRawMutations)
            throw new InvalidOperationException(
                "Formula values cannot be read while an update batch has pending raw-value changes.");
    }

    internal sealed class FormulaEvaluationContext(FormulaDependencyGraph graph)
    {
        public FormulaDependencyGraph Graph { get; } = graph;

        public FormulaResult Evaluate(int row, int column) =>
            Graph.GetResult(new FormulaCellAddress(row, column));

        public FormulaValue EvaluateTyped(int row, int column) =>
            Graph.GetValue(new FormulaCellAddress(row, column));
    }
}

internal readonly record struct FormulaResult(bool Success, string Value, string? Error);
