using System.Globalization;

namespace SheetLite;

internal sealed class FormulaNode
{
    public required FormulaCellAddress Address { get; init; }
    public required string RawFormula { get; init; }
    public FormulaExpression? Expression { get; init; }
    public ErrorValue? ParseError { get; init; }
    public IReadOnlyCollection<FormulaDependency> Precedents { get; init; } = [];
    public FormulaValue CachedValue { get; set; } = FormulaValue.Blank;
    public bool Dirty { get; set; } = true;
}

internal sealed class FormulaCalculationMetrics
{
    public int ParsedFormulaCount { get; internal set; }
    public int EvaluatedNodeCount { get; internal set; }
    public int CacheHitCount { get; internal set; }
    public int DirtyNodeCount { get; internal set; }
    public int RangeDependencyQueries { get; internal set; }
    public int RebuildCount { get; internal set; }

    internal void Reset()
    {
        ParsedFormulaCount = 0;
        EvaluatedNodeCount = 0;
        CacheHitCount = 0;
        DirtyNodeCount = 0;
        RangeDependencyQueries = 0;
        RebuildCount = 0;
    }
}

internal readonly record struct FormulaCellUpdate(FormulaCellAddress Address, string RawValue);

internal sealed class FormulaChangeSet
{
    private readonly Dictionary<FormulaCellAddress, FormulaValue> changedValues = [];

    public IReadOnlyCollection<FormulaCellAddress> ChangedAddresses => changedValues.Keys;
    public IReadOnlyDictionary<FormulaCellAddress, FormulaValue> ChangedValues => changedValues;
    public bool IsEmpty => changedValues.Count == 0;
    public bool RequiresFullRefresh { get; private set; }

    internal void Add(FormulaCellAddress address, FormulaValue value) => changedValues[address] = value;
    internal void MarkFullRefresh() => RequiresFullRefresh = true;
}

/// <summary>
/// Parsed formula registry, dependency index, and typed value cache for one worksheet.
/// A graph instance is owned by <see cref="FormulaEngine"/> and shared by every reader of the same model.
/// </summary>
internal sealed class FormulaDependencyGraph
{
    public const int DefaultExpandedRangeLimit = 4096;
    public const int MaximumEvaluationDepth = 512;

    private readonly SheetModel sheet;
    private readonly int expandedRangeLimit;
    private readonly Dictionary<FormulaCellAddress, FormulaNode> formulas = [];
    private readonly Dictionary<FormulaCellAddress, HashSet<FormulaCellAddress>> exactDependents = [];
    private readonly List<IndexedRangeDependency> largeRangeDependents = [];
    private readonly HashSet<FormulaCellAddress> dirty = [];
    private int synchronizedVersion = -1;

    public FormulaDependencyGraph(SheetModel sheet, int expandedRangeLimit = DefaultExpandedRangeLimit)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (expandedRangeLimit < 0) throw new ArgumentOutOfRangeException(nameof(expandedRangeLimit));
        this.sheet = sheet;
        this.expandedRangeLimit = expandedRangeLimit;
        Rebuild();
    }

    public FormulaCalculationMetrics Metrics { get; } = new();
    public int FormulaCount { get { EnsureCurrent(); return formulas.Count; } }
    public int SynchronizedVersion => synchronizedVersion;

    public void ResetMetrics() => Metrics.Reset();

    /// <summary>Reparses the worksheet and recreates every graph edge and cached value.</summary>
    public FormulaChangeSet Rebuild()
    {
        FormulaEngine.EnsureFormulaReadsAllowed(sheet);
        Metrics.RebuildCount++;
        var prior = formulas.ToDictionary(item => item.Key, item => item.Value.CachedValue);
        formulas.Clear();
        exactDependents.Clear();
        largeRangeDependents.Clear();
        dirty.Clear();

        for (var row = 0; row < sheet.Rows.Count; row++)
        {
            for (var column = 0; column < sheet.Rows[row].Count; column++)
            {
                var raw = sheet.Rows[row][column].Value;
                if (IsFormula(raw)) RegisterFormula(new FormulaCellAddress(row, column), raw);
            }
        }

        synchronizedVersion = sheet.Version;
        var changes = RecalculateDirtyCore(prior);
        foreach (var removed in prior.Keys.Where(address => !formulas.ContainsKey(address)))
            changes.Add(removed, ReadLiteral(removed));
        changes.MarkFullRefresh();
        return changes;
    }

    /// <summary>
    /// Applies one complete model-owned mutation. The token is required proof that the update
    /// list belongs to the adjacent worksheet version being synchronized.
    /// </summary>
    internal FormulaChangeSet ApplyMutation(IEnumerable<FormulaCellUpdate> updates, WorksheetMutationToken token)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var materialized = Materialize(updates);
        if (materialized.Count == 0)
        {
            EnsureCurrent();
            return new FormulaChangeSet();
        }

        if (synchronizedVersion == sheet.Version) return new FormulaChangeSet();

        // Incremental updates are safe only when this notification immediately follows the
        // graph's known model version. A skipped notification may hide another changed root,
        // so a version gap must rebuild instead of blessing incomplete state.
        if (synchronizedVersion != token.PreviousVersion ||
            sheet.Version != token.CurrentVersion ||
            token.CurrentVersion != token.PreviousVersion + 1)
            return Rebuild();

        // This API is deliberately post-write: stale or incomplete notifications fall back to
        // a safe full rebuild rather than leaving the graph inconsistent with its worksheet.
        if (materialized.Any(update => sheet.GetRawValue(update.Address.Row, update.Address.Column) != update.RawValue))
            return Rebuild();

        var affected = CollectAffected(materialized.Select(update => update.Address));
        var prior = affected
            .Where(formulas.ContainsKey)
            .ToDictionary(address => address, address => formulas[address].CachedValue);

        foreach (var update in materialized)
        {
            RemoveFormula(update.Address);
            if (IsFormula(update.RawValue)) RegisterFormula(update.Address, update.RawValue);
            MarkDirty(update.Address);
        }

        synchronizedVersion = sheet.Version;
        var changes = RecalculateDirtyCore(prior);
        foreach (var update in materialized)
            changes.Add(update.Address, GetValueCore(update.Address, new EvaluationState()));
        return changes;
    }

    public FormulaValue GetValue(FormulaCellAddress address)
    {
        EnsureCurrent();
        return GetValueCore(address, new EvaluationState());
    }

    public FormulaResult GetResult(FormulaCellAddress address)
    {
        var value = GetValue(address);
        return FormulaEngine.ToLegacyResult(value);
    }

    public FormulaValue Evaluate(FormulaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        EnsureCurrent();
        return EvaluateExpression(expression, new EvaluationState());
    }

    public FormulaChangeSet RecalculateDirty()
    {
        EnsureCurrent();
        var prior = dirty.Where(formulas.ContainsKey)
            .ToDictionary(address => address, address => formulas[address].CachedValue);
        return RecalculateDirtyCore(prior);
    }

    /// <summary>
    /// Acknowledges a model version that did not change raw cell values (for example,
    /// formatting). This deliberately preserves parsed nodes, edges, and cached values.
    /// </summary>
    public FormulaChangeSet SynchronizeVersion(WorksheetMutationToken token)
    {
        if (synchronizedVersion == sheet.Version) return new FormulaChangeSet();
        if (synchronizedVersion != token.PreviousVersion ||
            sheet.Version != token.CurrentVersion ||
            token.CurrentVersion != token.PreviousVersion + 1)
            return Rebuild();
        synchronizedVersion = sheet.Version;
        return new FormulaChangeSet();
    }

    private static List<FormulaCellUpdate> Materialize(IEnumerable<FormulaCellUpdate> updates) => updates
        .GroupBy(update => update.Address)
        .Select(group => group.Last() with { RawValue = group.Last().RawValue ?? "" })
        .ToList();

    public bool TryGetNode(FormulaCellAddress address, out FormulaNode? node)
    {
        EnsureCurrent();
        return formulas.TryGetValue(address, out node);
    }

    private void EnsureCurrent()
    {
        FormulaEngine.EnsureFormulaReadsAllowed(sheet);
        if (synchronizedVersion != sheet.Version) Rebuild();
    }

    private void RegisterFormula(FormulaCellAddress address, string rawFormula)
    {
        var parsed = FormulaParser.Parse(rawFormula);
        Metrics.ParsedFormulaCount++;
        var dependencies = parsed.Expression is null
            ? Array.Empty<FormulaDependency>()
            : FormulaDependencyExtractor.Extract(parsed.Expression);
        var node = new FormulaNode
        {
            Address = address,
            RawFormula = rawFormula,
            Expression = parsed.Expression,
            ParseError = parsed.Error,
            Precedents = dependencies
        };
        formulas[address] = node;
        dirty.Add(address);
        AddEdges(node);
    }

    private void RemoveFormula(FormulaCellAddress address)
    {
        if (!formulas.Remove(address, out var node)) return;
        dirty.Remove(address);
        RemoveEdges(node);
    }

    private void AddEdges(FormulaNode node)
    {
        foreach (var dependency in node.Precedents)
        {
            switch (dependency)
            {
                case CellDependency cell:
                    AddExactDependent(cell.Address, node.Address);
                    break;
                case RangeDependency range when range.Range.CellCount <= expandedRangeLimit:
                    foreach (var address in Addresses(range.Range)) AddExactDependent(address, node.Address);
                    break;
                case RangeDependency range:
                    largeRangeDependents.Add(new IndexedRangeDependency(range.Range, node.Address));
                    break;
            }
        }
    }

    private void RemoveEdges(FormulaNode node)
    {
        foreach (var dependency in node.Precedents)
        {
            switch (dependency)
            {
                case CellDependency cell:
                    RemoveExactDependent(cell.Address, node.Address);
                    break;
                case RangeDependency range when range.Range.CellCount <= expandedRangeLimit:
                    foreach (var address in Addresses(range.Range)) RemoveExactDependent(address, node.Address);
                    break;
            }
        }
        largeRangeDependents.RemoveAll(item => item.Dependent == node.Address);
    }

    private void AddExactDependent(FormulaCellAddress precedent, FormulaCellAddress dependent)
    {
        if (!exactDependents.TryGetValue(precedent, out var dependents))
            exactDependents[precedent] = dependents = [];
        dependents.Add(dependent);
    }

    private void RemoveExactDependent(FormulaCellAddress precedent, FormulaCellAddress dependent)
    {
        if (!exactDependents.TryGetValue(precedent, out var dependents)) return;
        dependents.Remove(dependent);
        if (dependents.Count == 0) exactDependents.Remove(precedent);
    }

    private HashSet<FormulaCellAddress> CollectAffected(IEnumerable<FormulaCellAddress> roots)
    {
        var affected = new HashSet<FormulaCellAddress>();
        var queue = new Queue<FormulaCellAddress>();
        foreach (var root in roots)
        {
            if (affected.Add(root)) queue.Enqueue(root);
        }
        while (queue.Count > 0)
        {
            var address = queue.Dequeue();
            foreach (var dependent in DependentsOf(address))
                if (affected.Add(dependent)) queue.Enqueue(dependent);
        }
        return affected;
    }

    private void MarkDirty(FormulaCellAddress root)
    {
        foreach (var address in CollectAffected([root]))
        {
            if (!formulas.TryGetValue(address, out var node)) continue;
            if (!node.Dirty) Metrics.DirtyNodeCount++;
            node.Dirty = true;
            dirty.Add(address);
        }
    }

    private IEnumerable<FormulaCellAddress> DependentsOf(FormulaCellAddress address)
    {
        var found = new HashSet<FormulaCellAddress>();
        if (exactDependents.TryGetValue(address, out var exact)) found.UnionWith(exact);
        if (largeRangeDependents.Count > 0)
        {
            Metrics.RangeDependencyQueries++;
            foreach (var item in largeRangeDependents)
                if (item.Range.Contains(address)) found.Add(item.Dependent);
        }
        return found;
    }

    private FormulaChangeSet RecalculateDirtyCore(IReadOnlyDictionary<FormulaCellAddress, FormulaValue> prior)
    {
        var pending = dirty.ToArray();
        foreach (var address in pending) GetValueCore(address, new EvaluationState());

        var changes = new FormulaChangeSet();
        foreach (var address in pending)
        {
            if (!formulas.TryGetValue(address, out var node)) continue;
            if (!prior.TryGetValue(address, out var oldValue) || oldValue != node.CachedValue)
                changes.Add(address, node.CachedValue);
        }
        return changes;
    }

    private FormulaValue GetValueCore(FormulaCellAddress address, EvaluationState state)
    {
        if (!formulas.TryGetValue(address, out var node)) return ReadLiteral(address);
        if (!node.Dirty)
        {
            Metrics.CacheHitCount++;
            return node.CachedValue;
        }

        if (state.StackIndices.ContainsKey(address))
        {
            var members = StronglyConnectedMembers(address);
            MarkCircular(members);
            state.CycleMembers.UnionWith(members);
            return new ErrorValue(FormulaError.CircularReference, "Circular cell reference.");
        }

        if (state.Stack.Count >= MaximumEvaluationDepth)
        {
            if (TryMarkReachableCycle(address, state))
                return new ErrorValue(FormulaError.CircularReference, "Circular cell reference.");
            return new ErrorValue(
                FormulaError.Number,
                $"Formula dependency depth exceeds {MaximumEvaluationDepth} cells.");
        }

        state.Push(address);
        Metrics.EvaluatedNodeCount++;
        FormulaValue value;
        try
        {
            if (node.ParseError is not null)
                value = node.ParseError;
            else
                value = EvaluateExpression(node.Expression!, state);
        }
        finally
        {
            state.Pop(address);
        }

        if (state.CycleMembers.Contains(address))
            value = new ErrorValue(FormulaError.CircularReference, "Circular cell reference.");
        node.CachedValue = value;
        node.Dirty = false;
        dirty.Remove(address);
        return value;
    }

    /// <summary>
    /// At the recursion boundary, walks formula precedents iteratively. This distinguishes a
    /// genuinely deep acyclic chain from a cycle whose closing edge lies beyond the safe call
    /// depth, and records every member of the discovered cycle without consuming call stack.
    /// </summary>
    private bool TryMarkReachableCycle(FormulaCellAddress start, EvaluationState state)
    {
        var path = new List<FormulaCellAddress>(state.Stack.Count + 16);
        path.AddRange(state.Stack);
        var pathIndices = new Dictionary<FormulaCellAddress, int>(state.StackIndices);
        var visited = new HashSet<FormulaCellAddress>(state.Stack);
        pathIndices.Add(start, path.Count);
        path.Add(start);
        visited.Add(start);

        var frames = new Stack<DependencyTraversalFrame>();
        frames.Push(new DependencyTraversalFrame(FormulaPrecedents(start).GetEnumerator()));
        try
        {
            while (frames.Count > 0)
            {
                var frame = frames.Peek();
                if (frame.Precedents.MoveNext())
                {
                    var precedent = frame.Precedents.Current;
                    if (!formulas.ContainsKey(precedent)) continue;
                    if (pathIndices.ContainsKey(precedent))
                    {
                        var members = StronglyConnectedMembers(precedent);
                        MarkCircular(members);
                        state.CycleMembers.UnionWith(members);
                        return true;
                    }
                    if (!visited.Add(precedent)) continue;
                    pathIndices.Add(precedent, path.Count);
                    path.Add(precedent);
                    frames.Push(new DependencyTraversalFrame(FormulaPrecedents(precedent).GetEnumerator()));
                    continue;
                }

                frames.Pop().Precedents.Dispose();
                var completed = path[^1];
                path.RemoveAt(path.Count - 1);
                pathIndices.Remove(completed);
            }
            return false;
        }
        finally
        {
            foreach (var frame in frames) frame.Precedents.Dispose();
        }
    }

    private IEnumerable<FormulaCellAddress> FormulaPrecedents(FormulaCellAddress address)
    {
        if (!formulas.TryGetValue(address, out var node)) yield break;
        foreach (var dependency in node.Precedents)
        {
            switch (dependency)
            {
                case CellDependency cell:
                    yield return cell.Address;
                    break;
                case RangeDependency range:
                    foreach (var formulaAddress in formulas.Keys)
                        if (range.Range.Contains(formulaAddress)) yield return formulaAddress;
                    break;
            }
        }
    }

    private void MarkCircular(IEnumerable<FormulaCellAddress> members)
    {
        var error = new ErrorValue(FormulaError.CircularReference, "Circular cell reference.");
        foreach (var member in members)
        {
            if (!formulas.TryGetValue(member, out var node)) continue;
            node.CachedValue = error;
            node.Dirty = false;
            dirty.Remove(member);
        }
    }

    private HashSet<FormulaCellAddress> StronglyConnectedMembers(FormulaCellAddress anchor)
    {
        var forward = ReachableFormulaAddresses(anchor, forward: true);
        var reverse = ReachableFormulaAddresses(anchor, forward: false);
        forward.IntersectWith(reverse);
        return forward;
    }

    private HashSet<FormulaCellAddress> ReachableFormulaAddresses(FormulaCellAddress start, bool forward)
    {
        var reached = new HashSet<FormulaCellAddress> { start };
        var pending = new Stack<FormulaCellAddress>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var address = pending.Pop();
            var adjacent = forward ? FormulaPrecedents(address) : DependentsOf(address);
            foreach (var candidate in adjacent)
            {
                if (!formulas.ContainsKey(candidate) || !reached.Add(candidate)) continue;
                pending.Push(candidate);
            }
        }
        return reached;
    }

    private FormulaValue EvaluateExpression(FormulaExpression expression, EvaluationState state)
    {
        try
        {
            return expression switch
            {
                NumberExpression number => new NumberValue(number.Value),
                TextExpression text => new TextValue(text.Value),
                BooleanExpression boolean => new BooleanValue(boolean.Value),
                ErrorExpression error => new ErrorValue(error.Error),
                CellReferenceExpression cell => GetValueCore(cell.Address, state),
                RangeReferenceExpression => new ErrorValue(FormulaError.Value, "A range is only valid as a function argument."),
                UnaryExpression unary => EvaluateUnary(unary, state),
                BinaryExpression binary => EvaluateBinary(binary, state),
                FunctionExpression function => EvaluateFunction(function, state),
                _ => new ErrorValue(FormulaError.Value, "Unsupported expression.")
            };
        }
        catch (OverflowException)
        {
            return new ErrorValue(FormulaError.Number, "Number too large.");
        }
    }

    private FormulaValue EvaluateUnary(UnaryExpression unary, EvaluationState state)
    {
        var operand = EvaluateExpression(unary.Operand, state);
        if (operand is ErrorValue) return operand;
        if (!TryNumber(operand, out var number)) return NotNumeric(operand);
        return unary.Operator switch
        {
            '+' => new NumberValue(number),
            '-' => new NumberValue(-number),
            _ => new ErrorValue(FormulaError.Value, $"Unknown operator '{unary.Operator}'.")
        };
    }

    private FormulaValue EvaluateBinary(BinaryExpression binary, EvaluationState state)
    {
        var left = EvaluateExpression(binary.Left, state);
        if (left is ErrorValue) return left;
        var right = EvaluateExpression(binary.Right, state);
        if (right is ErrorValue) return right;
        if (!TryNumber(left, out var leftNumber)) return NotNumeric(left);
        if (!TryNumber(right, out var rightNumber)) return NotNumeric(right);

        return binary.Operator switch
        {
            '+' => new NumberValue(leftNumber + rightNumber),
            '-' => new NumberValue(leftNumber - rightNumber),
            '*' => new NumberValue(leftNumber * rightNumber),
            '/' when rightNumber == 0 => new ErrorValue(FormulaError.DivisionByZero, "Division by zero."),
            '/' => new NumberValue(leftNumber / rightNumber),
            '^' => Power(leftNumber, rightNumber),
            _ => new ErrorValue(FormulaError.Value, $"Unknown operator '{binary.Operator}'.")
        };
    }

    private static FormulaValue Power(decimal left, decimal right)
    {
        var powered = Math.Pow((double)left, (double)right);
        if (!double.IsFinite(powered)) return new ErrorValue(FormulaError.Number, "Result out of range.");
        try { return new NumberValue((decimal)powered); }
        catch (OverflowException) { return new ErrorValue(FormulaError.Number, "Number too large."); }
    }

    private FormulaValue EvaluateFunction(FunctionExpression function, EvaluationState state)
    {
        var supported = function.Name is "SUM" or "AVERAGE" or "MIN" or "MAX" or "COUNT" or "CONCAT";
        decimal sum = 0;
        decimal numericCount = 0;
        decimal? minimum = null;
        decimal? maximum = null;
        System.Text.StringBuilder? concatenated = function.Name == "CONCAT" ? new() : null;

        foreach (var value in FunctionValues(function, state))
        {
            if (value is ErrorValue error) return error;
            if (!supported) continue;
            if (concatenated is not null)
            {
                concatenated.Append(FormulaValueFormatter.Format(value));
                continue;
            }

            if (!TryNumber(value, out var number)) continue;
            numericCount++;
            if (function.Name != "COUNT")
            {
                sum += number;
                minimum = minimum is null || number < minimum ? number : minimum;
                maximum = maximum is null || number > maximum ? number : maximum;
            }
        }

        return function.Name switch
        {
            "SUM" => new NumberValue(sum),
            "AVERAGE" when numericCount > 0 => new NumberValue(sum / numericCount),
            "MIN" when minimum is not null => new NumberValue(minimum.Value),
            "MAX" when maximum is not null => new NumberValue(maximum.Value),
            "COUNT" => new NumberValue(numericCount),
            "CONCAT" => new TextValue(concatenated!.ToString()),
            "AVERAGE" or "MIN" or "MAX" => new ErrorValue(FormulaError.Value, $"{function.Name} requires a numeric value."),
            _ => new ErrorValue(FormulaError.Name, $"Unknown function '{function.Name}'.")
        };
    }

    private IEnumerable<FormulaValue> FunctionValues(FunctionExpression function, EvaluationState state)
    {
        foreach (var argument in function.Arguments)
        {
            if (argument is RangeReferenceExpression range)
            {
                var addressRange = new FormulaRangeAddress(range.Start.Address, range.End.Address);
                foreach (var address in StoredAddresses(addressRange))
                    yield return GetValueCore(address, state);
            }
            else
            {
                yield return EvaluateExpression(argument, state);
            }
        }
    }

    private FormulaValue ReadLiteral(FormulaCellAddress address)
    {
        var text = sheet.GetRawValue(address.Row, address.Column);
        if (text.Length == 0) return FormulaValue.Blank;
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? new NumberValue(number)
            : new TextValue(text);
    }

    private static bool TryNumber(FormulaValue value, out decimal number)
    {
        if (value is NumberValue numeric)
        {
            number = numeric.Value;
            return true;
        }
        if (value is TextValue text && decimal.TryParse(text.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return true;
        number = default;
        return false;
    }

    private static ErrorValue NotNumeric(FormulaValue value) =>
        new(FormulaError.Value, $"'{FormulaValueFormatter.Format(value)}' is not numeric.");

    private static bool IsFormula(string rawValue) => rawValue.TrimStart().StartsWith('=');

    private static IEnumerable<FormulaCellAddress> Addresses(FormulaRangeAddress range)
    {
        for (var row = range.Top; row <= range.Bottom; row++)
            for (var column = range.Left; column <= range.Right; column++)
                yield return new FormulaCellAddress(row, column);
    }

    /// <summary>
    /// Enumerates only physically stored cells. Cells outside this intersection are blank,
    /// which contributes nothing to the currently supported aggregates, COUNT, or CONCAT.
    /// Normalizing through FormulaRangeAddress preserves reversed-range behavior.
    /// </summary>
    private IEnumerable<FormulaCellAddress> StoredAddresses(FormulaRangeAddress range)
    {
        var top = Math.Max(range.Top, 0);
        var bottom = Math.Min(range.Bottom, sheet.Rows.Count - 1);
        if (bottom < top) yield break;

        for (var row = top; row <= bottom; row++)
        {
            var left = Math.Max(range.Left, 0);
            var right = Math.Min(range.Right, sheet.Rows[row].Count - 1);
            for (var column = left; column <= right; column++)
                yield return new FormulaCellAddress(row, column);
        }
    }

    private sealed class EvaluationState
    {
        public List<FormulaCellAddress> Stack { get; } = [];
        public Dictionary<FormulaCellAddress, int> StackIndices { get; } = [];
        public HashSet<FormulaCellAddress> CycleMembers { get; } = [];

        public void Push(FormulaCellAddress address)
        {
            StackIndices.Add(address, Stack.Count);
            Stack.Add(address);
        }

        public void Pop(FormulaCellAddress address)
        {
            Stack.RemoveAt(Stack.Count - 1);
            StackIndices.Remove(address);
        }
    }

    private sealed record DependencyTraversalFrame(IEnumerator<FormulaCellAddress> Precedents);

    private readonly record struct IndexedRangeDependency(FormulaRangeAddress Range, FormulaCellAddress Dependent);
}
