# Refactor Option: Dependency-Graph Formula Engine

Status: Proposed  
Priority: High before adding substantially more formula functionality  
Scope: Formula parsing, dependency tracking, recalculation, errors, reference rewriting, and split-view notifications

## Objective

Replace repeated full-sheet formula scans with an incremental formula engine that parses formulas into expression trees, records dependencies, caches results, and recalculates only cells affected by an edit.

This option should be completed before the formula language grows significantly. It also prepares SheetLite for cross-sheet references and a virtualized grid.

## Current design and limitation

The current `FormulaEngine` recursively parses and evaluates formulas. A `FormulaEvaluationContext` memoizes cell results during one batch, which avoids repeated evaluation within that context and detects recursive cycles.

After common edits, `MainForm.RecalculateFormulaCells()` still scans every populated grid cell to find and reevaluate formulas. The parser evaluates while parsing and does not retain a reusable syntax tree or dependency information.

Consequences:

- changing an unrelated cell may reevaluate every formula;
- dependency information must be rediscovered on every calculation;
- reference rewriting and evaluation use separate parsing logic;
- cross-sheet references cannot be represented;
- the UI normally displays a general `#ERROR!` rather than a typed spreadsheet error;
- large ranges can make repeated recalculation expensive;
- both split panes may refresh more cells than necessary.

Relevant current code:

- `FormulaEngine.cs`: recursive parser, evaluator, memoization, and cycle detection.
- `FormulaReferenceUpdater.cs`: separate formula-reference recognition and rewriting.
- `MainForm.cs`: `RecalculateFormulaCells()` and formula display updates.
- `MainForm.DockedTools.cs`: secondary-grid formula refresh and SQL formula evaluation.

## Proposed architecture

### 1. Parse formulas into an abstract syntax tree

Separate parsing from evaluation. A formula such as:

```excel
=SUM(A1:A10) + B2
```

should produce reusable nodes similar to:

```text
BinaryExpression(+)
├─ FunctionExpression(SUM)
│  └─ RangeReferenceExpression(A1:A10)
└─ CellReferenceExpression(B2)
```

Suggested expression families:

- numeric, text, and Boolean literals;
- unary and binary operators;
- cell references;
- range references;
- function calls;
- typed error expressions where applicable.

The same syntax tree should support evaluation, dependency extraction, and reference rewriting after structural worksheet changes.

### 2. Use stable workbook-level addresses

Introduce a stable address that includes worksheet identity:

```csharp
internal readonly record struct FormulaCellAddress(
    Guid WorksheetId,
    int Row,
    int Column);
```

`WorksheetModel` should receive a persistent ID. Formulas may initially remain limited to the active sheet, but workbook-level addresses allow later support for:

```excel
=Sheet2!A1
='Sales Data'!B7
=SUM(January!A1:A31)
```

Renaming a worksheet should update display text or serialized references without changing graph ownership.

### 3. Maintain forward and reverse dependencies

Each formula node records its precedents, and the engine records reverse dependents:

```csharp
Dictionary<FormulaCellAddress, FormulaNode> formulas;
Dictionary<FormulaCellAddress, HashSet<FormulaCellAddress>> dependents;
```

For these formulas:

```excel
B1 = A1 * 2
C1 = B1 + 10
D1 = SUM(A1:A20)
```

the graph records:

```text
B1 depends on A1
C1 depends on B1
D1 depends on A1:A20

A1 affects B1 and D1
B1 affects C1
```

### 4. Cache evaluated results and track dirty state

A formula node should contain its parsed expression, cached result, dependency metadata, and calculation state:

```csharp
internal sealed class FormulaNode
{
    public required FormulaCellAddress Address { get; init; }
    public required FormulaExpression Expression { get; init; }
    public HashSet<FormulaDependency> Precedents { get; } = [];
    public FormulaValue CachedValue { get; set; }
    public bool Dirty { get; set; }
}
```

When a cell changes, the engine should:

1. update or remove the edited cell's formula node;
2. remove obsolete dependency edges;
3. add the new dependency edges;
4. mark direct and transitive dependents dirty;
5. recalculate dirty nodes in dependency order;
6. return the addresses whose displayed values changed.

Both split panes can invalidate only those visible addresses.

### 5. Detect cycles as formulas are registered

The existing evaluator detects recursion during evaluation. The graph should detect cycles when dependency edges are changed and represent them consistently:

```text
A1 → B1 → C1 → A1
```

All involved nodes should receive a typed circular-reference result. The engine must also clear that error when an edit breaks the cycle.

### 6. Introduce typed values and errors

Use internal values instead of formatting every result immediately as a string:

```csharp
internal abstract record FormulaValue;
internal sealed record NumberValue(decimal Value) : FormulaValue;
internal sealed record TextValue(string Value) : FormulaValue;
internal sealed record BooleanValue(bool Value) : FormulaValue;
internal sealed record BlankValue : FormulaValue;
internal sealed record ErrorValue(FormulaError Error) : FormulaValue;
```

Recommended initial errors:

- `#DIV/0!`;
- `#REF!`;
- `#VALUE!`;
- `#NAME?`;
- `#CIRC!`;
- `#NUM!`.

Formatting for display, CSV, XLSX, SQL, and status text should happen at integration boundaries. Formula errors should propagate as typed values rather than ordinary strings.

### 7. Index range dependencies

Naively creating one graph edge per cell in `A1:A1000000` is not acceptable. Use a staged design:

First implementation:

- enumerate dependencies for ranges below a safe threshold;
- store larger ranges as explicit `RangeDependency` objects;
- query large range dependencies when a cell changes.

Later optimization:

- index ranges by worksheet and row/column interval;
- use an interval tree or comparable range index;
- invalidate formulas whose range contains the edited address without scanning every formula.

Structural edits must transform both single-cell and range dependencies.

### 8. Unify reference parsing and rewriting

`FormulaReferenceUpdater` should eventually operate on the parsed syntax tree rather than matching formula text independently. Insert, delete, copy, move, and fill operations can then transform reference nodes while preserving:

- absolute row and column markers;
- ranges;
- worksheet qualifiers;
- string literals that resemble references;
- valid `#REF!` outcomes.

The original formula text can be regenerated from the transformed tree or preserved through token spans where practical.

### 9. Define calculation events

The engine should expose a narrow API and change notification:

```csharp
FormulaChangeSet UpdateCell(FormulaCellAddress address, string rawValue);
FormulaValue GetValue(FormulaCellAddress address);
IReadOnlyCollection<FormulaCellAddress> RecalculateDirty();
```

`FormulaChangeSet` should identify changed results and errors. The virtual or existing grid can repaint those cells without scanning the worksheet.

## Migration plan

### Phase 1: Parser extraction

- [ ] Add expression-tree and typed-value classes.
- [ ] Convert the existing formula grammar to parse without evaluating.
- [ ] Evaluate expression trees with parity for current operators and functions.
- [ ] Retain current public/internal entry points through an adapter.
- [ ] Add parser tests for whitespace, absolute references, ranges, strings, errors, and invalid input.

### Phase 2: Single-sheet dependency graph

- [ ] Add stable worksheet IDs and formula addresses.
- [ ] Extract precedents from expression trees.
- [ ] Build forward and reverse graph edges.
- [ ] Cache results and implement transitive dirty propagation.
- [ ] Detect, display, and recover from circular references.
- [ ] Replace `RecalculateFormulaCells()` with change-set recalculation.

### Phase 3: Integrate edits and structural operations

- [ ] Route cell edit, paste, clear, fill, replace, and undo through graph updates.
- [ ] Transform expression trees during row/column insert, delete, move, copy, and fill.
- [ ] Replace `FormulaReferenceUpdater` text scanning where feature parity exists.
- [ ] Rebuild or restore graph state safely during undo/redo and workbook load.

### Phase 4: Range index and split-view notifications

- [ ] Add scalable large-range dependency lookup.
- [ ] Notify both panes only about affected addresses.
- [ ] Ensure SQL and sort/filter operations consume cached typed values.
- [ ] Add calculation batching for large paste and import operations.

### Phase 5: Cross-sheet references

- [ ] Parse quoted and unquoted worksheet qualifiers.
- [ ] Resolve names to stable worksheet IDs.
- [ ] Update serialized formula text when worksheets are renamed.
- [ ] Emit `#REF!` when referenced worksheets or cells are deleted.
- [ ] Support dependencies across worksheet tabs and shared split panes.

## Compatibility requirements

The first graph-backed release must preserve all currently supported formula behavior:

- arithmetic operators and parentheses;
- unary signs and exponentiation;
- cell and range references;
- absolute-reference syntax accepted by the current parser;
- `SUM`, `AVERAGE`, `MIN`, `MAX`, `COUNT`, and `CONCAT`;
- invariant numeric parsing and formatting;
- circular-reference detection;
- formula-aware SQL, sorting, filtering, fill, copy/paste, save, and split view.

Cross-sheet references and new functions should be added only after parity is established.

## Testing and acceptance criteria

Correctness:

- Existing formula, fill, reference-update, SQL, CSV, XLSX, and UI tests remain green.
- Editing a precedent recalculates every transitive dependent exactly once per calculation batch.
- Editing an unrelated cell does not evaluate unaffected formula nodes.
- Breaking a circular reference clears the circular error and recalculates dependents.
- Undo/redo restores raw formulas, graph edges, cached values, and errors.
- Both split panes show recalculated values immediately.
- Structural row/column edits preserve absolute and relative reference semantics.

Performance tests:

- A worksheet with many formulas and a small dependency chain recalculates in proportion to the affected chain rather than total formula count.
- Large paste operations batch invalidation and perform one dependency-ordered recalculation.
- Large range formulas do not allocate one graph edge per cell above the configured threshold.
- Repeated reads of a clean formula return its cached typed result.

Instrumentation should count parsed formulas, evaluated nodes, cache hits, dirty nodes, and range-index queries so tests can assert incremental behavior without relying only on elapsed time.

## Risks and mitigations

- **Semantic regression:** preserve the current evaluator behind parity tests until the tree evaluator passes the same corpus.
- **Graph corruption after structural edits:** centralize every worksheet mutation and validate graph invariants in debug/test builds.
- **Range-index complexity:** begin with a correct hybrid threshold strategy before implementing an interval tree.
- **Undo integration:** store raw model changes and rebuild affected graph sections if restoring graph deltas is unsafe initially.
- **Cross-sheet complexity:** design addresses for it now, but defer user-visible syntax until the single-sheet graph is stable.
- **Volatile functions:** when functions such as `NOW` or `RAND` are added, mark them explicitly volatile and recalculate them by calculation cycle rather than ordinary dependency invalidation.

## Non-goals for the first implementation

- Full Excel formula-language compatibility.
- External workbook references.
- Iterative calculation for intentionally circular models.
- Array formulas, dynamic arrays, named ranges, or table references.
- Multi-threaded calculation before deterministic single-threaded graph evaluation is proven.

The first goal is predictable, incremental recalculation with exact parity for SheetLite's existing formula language.
