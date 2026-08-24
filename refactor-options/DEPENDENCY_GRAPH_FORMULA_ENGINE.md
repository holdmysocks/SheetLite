# Refactor: Dependency-Graph Formula Engine

Status: Implemented for the scoped single-worksheet engine; cross-sheet and advanced indexing work is deferred

Priority: Complete for the current formula language

Scope: Formula parsing, typed evaluation, dependency tracking, incremental recalculation, mutation events, undo, structural rebuilds, and split-view invalidation

## Outcome

SheetLite now parses supported formulas into reusable expression trees and keeps one dependency graph and typed-value cache per `SheetModel`. A normal cell edit updates only the changed formula registrations and recalculates only their direct and transitive dependents. Clean formulas keep their cached results, so an unrelated edit does not reparse or reevaluate them.

The worksheet model is the authoritative mutation boundary. It batches paste/fill/replace loops, advances its version once, synchronizes the graph, and publishes one `WorksheetChangeSet` containing the edited addresses plus formula results whose displayed values changed. Both virtual-grid panes subscribe to that event and invalidate only affected visible cells unless a structural change or safety fallback requires a full repaint.

This implementation deliberately remains single-sheet. `FormulaCellAddress` contains row and column only, worksheet-qualified references are rejected by evaluation, and cross-sheet formulas loaded from XLSX are preserved for round-trip rather than calculated.

## Implemented architecture

### Parsed syntax and typed values

`FormulaSyntax.cs` separates parsing from evaluation. It contains reusable expression nodes for numbers, text, Booleans, unary and binary operators, cell and range references, function calls, and typed errors. `FormulaDependencyExtractor` walks the same tree to collect precedents.

The graph evaluates to `FormulaValue` instances rather than immediately converting everything to strings:

- `NumberValue`
- `TextValue`
- `BooleanValue`
- `BlankValue`
- `ErrorValue`

Supported errors retain their type internally and format specifically as `#DIV/0!`, `#REF!`, `#VALUE!`, `#NAME?`, `#CIRC!`, or `#NUM!`. Display and SQL format typed results at their boundaries; XLSX writes cached typed errors as error cells rather than ordinary strings. The legacy `FormulaEngine` result API remains available as an adapter for existing callers and detailed error messages.

### One shared graph per worksheet model

`FormulaEngine` associates one `FormulaDependencyGraph` with each live `SheetModel`. Evaluation contexts and all readers of the same worksheet share that graph instead of creating independent per-pane caches.

Each formula node owns:

- its raw formula and parsed expression;
- its cell or range precedents;
- its cached typed result;
- its dirty state and parse error, if any.

The graph maintains forward dependency metadata on formula nodes and reverse dependent indexes. Editing a formula removes obsolete reverse edges, registers its new tree and edges, dirties affected dependents, and recalculates the dirty subgraph. Calculation metrics cover parsed formulas, evaluated nodes, cache hits, dirty nodes, range queries, and safety rebuilds.

Cycle detection marks every member of a circular component with `#CIRC!`. When a later edit breaks the cycle, the affected nodes are dirtied and recover to ordinary cached values. Evaluation depth is bounded so extreme chains produce a typed error instead of overflowing the process stack.

### Hybrid range dependencies

Ranges up to the configured threshold expand into exact reverse edges. Larger ranges remain single compact `RangeDependency` records, avoiding one allocation per referenced cell. Aggregate evaluation visits only physically stored cells inside a range, so very tall sparse or reversed ranges remain practical.

The compact large-range index is currently a list. A changed cell is checked against that list, which is correct but linear in the number of large-range formulas. An interval tree or comparable scalable range index is deferred.

### Authoritative model mutations and events

All supported raw-value and structural changes flow through `SheetModel` mutation methods. `BeginUpdate()` provides nestable batching for operations such as paste, fill, replace, and undo restoration. The outermost batch:

1. advances the model version once;
2. sends the complete raw-value update set to the graph, or requests a structural rebuild;
3. recalculates affected formulas;
4. publishes one `WorksheetChangeSet` with direct and calculated changes.

Adjacent-version mutation tokens prevent an incomplete or out-of-order update from being accepted as incremental state. If the graph detects version divergence, it safely rebuilds and requests a full refresh. Formula reads are rejected while a raw-value batch is still open, preventing callers from observing partially synchronized results.

Structural row/column insert, delete, move, and sort operations continue to rewrite formula text through `FormulaReferenceUpdater`. After the structure changes, the graph is rebuilt once from the authoritative worksheet. Undo/redo restores cell batches incrementally and rebuilds after structural restoration.

`WorksheetModel` now has a stable `Guid` preserved by workbook cloning. Undo entries target that identity, so they continue to find the logical worksheet even if tabs are reordered or workbook snapshots replace model instances.

### UI and integration boundaries

`SheetModelDataSource` forwards model change events and provides contexts backed by the shared graph. `WorksheetPaneController` maps changed model addresses through its pane-specific view and invalidates only visible affected cells. It uses a full repaint for structural/full-refresh events or when a targeted update exceeds the safety threshold. Shared split panes therefore receive the same formula change set without rescanning the worksheet.

SQL reads formula results through a shared evaluation context, so filtering, ordering, and projected results consume the graph cache and receive current formatted typed values. Sorting and filtering likewise use evaluated worksheet values. XLSX export writes native formula text plus the graph's cached result and emits typed formula errors using the Open XML error-cell type.

## Migration checklist

### Phase 1: Parser extraction

- [x] Add expression-tree and typed-value classes.
- [x] Convert the existing formula grammar to parse without evaluating.
- [x] Evaluate expression trees with parity for current operators and functions.
- [x] Retain current entry points through an adapter.
- [x] Add parser and evaluator parity tests for whitespace, absolute references, ranges, strings, errors, invalid input, and extreme ranges.

### Phase 2: Single-sheet dependency graph

- [x] Create one shared dependency graph per `SheetModel`.
- [x] Extract precedents from expression trees.
- [x] Build forward and reverse graph edges.
- [x] Cache typed results and implement transitive dirty propagation.
- [x] Detect, display, and recover from circular references.
- [x] Replace version-wide formula invalidation with calculated change sets.
- [ ] Add worksheet identity to `FormulaCellAddress` (deferred with cross-sheet references).

### Phase 3: Edits, undo, and structural operations

- [x] Route cell edit, paste, clear, fill, replace, and undo through authoritative model mutations.
- [x] Batch raw-value changes, formula synchronization, recalculation, versioning, and event publication.
- [x] Give `WorksheetModel` stable IDs and target undo entries by logical worksheet identity.
- [x] Rebuild graph state safely during structural changes, structural undo/redo, workbook replacement, and load.
- [ ] Transform expression trees during row/column insert, delete, move, copy, and fill.
- [ ] Replace `FormulaReferenceUpdater` text scanning with AST-based rewriting. The regex updater remains in use, followed by a structural graph rebuild.

### Phase 4: Ranges, panes, and data boundaries

- [x] Keep large ranges compact above a safe expansion threshold.
- [x] Notify both panes about affected addresses and selectively invalidate visible cells.
- [x] Ensure SQL, sort, and filter operations consume current cached formula values.
- [x] Write XLSX cached results from typed graph values, including typed error cells.
- [x] Batch large paste, fill, replace, restore, and import-style mutation loops.
- [ ] Replace the large-range list scan with a scalable interval index.

### Phase 5: Cross-sheet references

- [ ] Parse quoted and unquoted worksheet qualifiers.
- [ ] Resolve formula addresses to stable worksheet IDs.
- [ ] Update serialized formula text when worksheets are renamed.
- [ ] Emit `#REF!` when referenced worksheets are deleted.
- [ ] Support dependency edges and recalculation across worksheet tabs.

## Compatibility and acceptance

The graph-backed engine preserves the currently supported single-sheet behavior:

- arithmetic operators, parentheses, unary signs, and exponentiation;
- A1 cell and range references with accepted absolute markers;
- `SUM`, `AVERAGE`, `MIN`, `MAX`, `COUNT`, and `CONCAT`;
- invariant numeric parsing and formatting;
- typed and recoverable circular-reference handling;
- formula-aware SQL, sorting, filtering, fill, copy/paste, XLSX save, undo/redo, and split view.

Tests cover parser/evaluator parity, dependency replacement, transitive recalculation, clean-cache reads, unrelated edits, cycle recovery, deep chains, compact large ranges, batched mutation events, version-divergence fallback, stable undo identity, structural graph rebuilds, selective pane invalidation, SQL results, and XLSX typed cached values.

## Deferred work and non-goals

The following are intentionally not part of the completed single-sheet refactor:

- worksheet-qualified `FormulaCellAddress` values and cross-sheet parsing/evaluation;
- AST-based reference rewriting; the existing regex updater remains authoritative for structural edits;
- a scalable interval index for compact large ranges; invalidation currently scans the large-range list;
- full Excel formula-language compatibility;
- external workbook references, named ranges, tables, array formulas, or dynamic arrays;
- iterative circular calculation, volatile-function scheduling, or multithreaded evaluation.

The delivered goal is predictable incremental recalculation for SheetLite's existing formula language, with safe rebuild fallbacks where structure changes or synchronization cannot be applied incrementally.
