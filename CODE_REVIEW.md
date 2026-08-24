# SheetLite — Code Review

**Scope:** full source review of all 19 files (~4,300 LOC), WinForms / .NET 9. **No code was changed.**
Findings are grouped by severity: 🔴 Critical, 🟠 Functional bugs, 🟡 Performance, 🔵 Maintainability, ⚪ Dead/removable, 🔒 Security, 🧹 Project hygiene. Each item includes a concrete fix suggestion.

---

## 1. What's good (worth keeping as-is)

- `CsvCodec`, `XlsxCodec`, `FormulaEngine`, `SqlQueryEngine` are clean, self-contained, and testable; the recursive-descent formula parser is readable and correct for its supported grammar.
- `[GeneratedRegex]` usage in `FormulaReferenceUpdater`, careful quoted-string awareness (`IsInsideQuotedString`), and range-deletion rewriting (`RewriteDeletionFormula`) are well done.
- Drag/drop zone architecture (`FileDropZone` + `externalDropTargets` dedupe) is tidy.
- Help content is accurate against the actual implementation (shortcuts match `TryProcessAppShortcut`).
- `chromeGlyphs`/`boldCellFont` disposal is handled; DPI scaling is attempted consistently via `DeviceDpi`.

---

## 2. 🔴 Critical

### C1. Unhandled `OverflowException` can crash the app from a single cell entry
`FormulaEngine.Evaluate` only catches `FormulaException`. But:
- `ParsePower`: `(decimal)Math.Pow((double)Number(left), ...)` throws `OverflowException` for e.g. `=9^9^9` (`Math.Pow` → `Infinity` → cast throws).
- Decimal arithmetic in `ParseAdditive`/`ParseMultiplicative` overflows on huge operands.

The exception escapes `EvaluateCell` → `Evaluate` → callers such as `RecalculateFormulaCells()` and `ApplyCell()`, which run during render/edit — an unhandled exception on the UI thread.

**Fix:**
```csharp
catch (FormulaException ex) { ... }
catch (Exception ex) when (ex is OverflowException)
{
    return new FormulaResult(false, "", "Number too large.");
}
```
Also guard `Math.Pow` results: `if (!double.IsFinite(p)) throw new FormulaException("Result out of range.")`.

### C2. Sort panel silently discards edits made while it is open
`ShowSortPanel()` clones the workbook into `sortBaselineWorkbook` the first time the panel opens. The panel is non-modal, so the user can keep editing cells. `ApplyDockedSort()` then does `workbook = sortBaselineWorkbook.Clone()` — every edit made after opening the panel is wiped from the live document.

**Fix (pick one):**
- Re-baseline whenever a grid edit occurs while the panel is open (hook `CellValueChanged` → if `sortBaselineWorkbook != null && !sortPreviewApplied`, re-clone baseline);
- or set `grid.ReadOnly = true` while a preview session is open;
- or rebase inside `PushUndo()` when `sortBaselineWorkbook` is non-null and no preview has been applied yet.

---

## 3. 🟠 Functional bugs

### F1. Destructive shortcuts fire while the Help page (or welcome screen) is visible
`TryProcessAppShortcut(Keys.Delete / Ctrl+X / Ctrl+Shift+Up …)` never checks overlay visibility. Open Help (F1), press Delete → cells are cleared *behind* the help page (with an undo snapshot pushed). Same for `infoPanel`/welcome states.

**Fix:** early-return (or restrict to navigation shortcuts) when `helpPage.Visible || infoPanel.Visible || welcome.Visible` for all state-mutating actions.

### F2. Independent right-pane documents have no working undo/redo
`PaneDocumentSession.Undo/Redo` stacks exist but are only ever populated for the *primary* session (`InitializeDocumentSessions`, `CapturePrimaryDocument`). Secondary sessions are created without stacks, `Undo()/Redo()` always operate on the primary workbook, and `ActivateSecondaryDocument` doesn't swap undo state. Ctrl+Z with the right pane active undoes *left-pane* edits. The help text ("Separate right-pane documents support … undo/redo") is therefore wrong today.

**Fix:** wire per-session stacks — swap `undo/redo` fields in `ActivateSecondaryDocument`, add `SecondaryUndo/Redo` routed by the same `secondary` selector used for Cut/Copy/Paste — or remove the claim from Help until implemented.

### F3. Likely double execution of Cut/Copy/Paste/Delete (duplicate undo snapshots)
Menu items register real shortcut keys (`Item("Cut", Keys.Control | Keys.X, …)`, `Item("Delete contents", Keys.Delete, …)`), while `TryProcessAppShortcut` handles the same keys. MenuStrip item shortcuts are processed globally, so both paths can run per keypress. Consequence: `PushUndo()` runs twice per edit → identical snapshots stack up and the user must press Ctrl+Z twice to escape one edit. (Verify once with a breakpoint; if confirmed:)

**Fix:** remove `Keys.*` from menu items and keep only `ShortcutKeyDisplayString` for display; let `TryProcessAppShortcut` be the single router (see also Q1).

### F4. Filter state machine has three half-connected representations
State lives in `filter` (string label), `headerFilterColumn/headerFilterValues/headerFilterOperator/…`, and the docked-bar combos.
- `FilterByCurrentValue` sets only `filter`; after any edit/render the rows come back visible but the filter label persists, and `ReapplyDockedFilterIfActive()` can resurrect an unrelated *docked* combo filter because `filter != null`.
- Shared-view divergence: primary-grid row visibility isn't mirrored to the secondary grid (`RefreshSharedSecondaryFromModel` copies values/freeze only).

**Fix:** one `ActiveFilter` model object (column, predicate delegate, label) that both grids consume; derive row visibility from it in one place.

### F5. Reference rewriter corrupts identifiers ending in digits
`FormulaReferenceUpdater.CellReferencePattern` matches *any* 1–3 letters + digits outside quotes. A token like `LOG10` (preceded by `(`, `,`, `=` …) parses as column `LOG`, row `10`, and gets reformatted into garbage: `=LOG10(A1)` becomes something like `=OFL10(A1)` after an insert/delete/sort. Any user-typed function name or unquoted token ending in digits is at risk.

**Fix:** before rewriting, skip matches whose next character is `(` (function-call context), and/or bound the column part to Excel's real maximum (`XFD`, i.e., value ≤ 16384) — `LOG` is a legal column name, so the `(` lookahead is the reliable discriminator.

### F6. XLSX load turns split panes into frozen panes
`LoadSheet` reads `pane/@xSplit/@ySplit` without checking `@state="frozen"`. Files saved by Excel with a *split* (scrollable) pane load as frozen.

**Fix:** `if ((string?)pane?.Attribute("state") == "frozen") { … }`.

### F7. CSV round-trip loses delimiter and writes a stray line for empty sheets
- Load detects `, \t ; |` but Save always emits commas + UTF-8 BOM — a TSV/semicolon file silently normalizes on save.
- An entirely empty sheet writes one blank line (`r <= Math.Max(lastRow, 0)` with `lastRow == -1`).

**Fix:** remember the detected delimiter on `SheetModel` (or return it from `Load`) and reuse in `Save`; skip the write loop when `lastRow < 0`.

### F8. Fill handle only works down/right
`ApplyFill` handles `target.Bottom > source.Bottom` and `target.Right > source.Right` only; dragging up/left leaves the preview showing but applies nothing.

**Fix:** add mirrored branches using negative offsets (pattern index math already supports it via `offset % pattern.Count` with proper flooring for negatives).

### F9. Column resize marks the document dirty although widths are never saved
`grid.ColumnWidthChanged → SetDirty()`, but neither XLSX nor CSV persistence stores widths (help text says "session-only"). Users get "discard unsaved changes?" prompts for changes that cannot be saved.

**Fix:** don't call `SetDirty()` there (keep `UpdateEditOutline()`), or start persisting widths in XLSX `<cols>`.

### F10. Minor correctness nits
- `OpenDialog` (Ctrl+O) filter omits `.tsv/.txt`, though drag-drop and `OpenAnotherDialog` accept them — unify filters.
- `PopulateColumnTools` preserves `SelectedIndex` positionally across repopulation; after insert/delete the combo points at the wrong column.
- `FinishCellEdit` sets `typeLabel.Text` directly; it stays stale ("Formula"/error) until the next selection change.
- Welcome card is fixed 780 px wide but `MinimumSize` allows 720 px client width → clipped at minimum window size.
- `WorkbookModel.ActiveSheet` still throws if `Sheets` is ever empty despite clamping (internal invariant — consider `ThrowIfNoSheets` helper for clearer failures).

---

## 4. 🟡 Performance

### P1. Formula recalculation is O(F × depth) with no memoization
`RecalculateFormulaCells()` evaluates every formula cell after *every* edit; each `Evaluate` recursively re-parses referenced cells (`visiting` set prevents cycles but nothing is cached). A column of `=A1+A2`-style chains degrades quadratically; deep chains get worse.

**Fix:** evaluate lazily with a per-pass cache: `Dictionary<(int,int), object?> memo` threaded through `Parser`/`EvaluateCell`, cleared on each recalc pass. One-line-ish change, large payoff.

### P2. Every edit commits clone the whole workbook + scan the whole grid
`PushUndo()` → `SyncAll()` (RowCount × ColumnCount iteration) → `workbook.Clone()` (deep clone of every cell), capped at 40 snapshots. Fine at 100×26 default, heavy at thousands of rows.

**Fix:** snapshot only the affected sheet, or switch undo to coarse command objects; at minimum skip `SyncAll()` when `!cellEditing` transitions guarantee the grid is already synced.

### P3. `CsvCodec.DetectEncoding` reads the entire file twice
`File.ReadAllText(path, DetectEncoding(path))` where `DetectEncoding` does `File.ReadAllBytes(path).Take(3)` — full file buffered just for a BOM.

**Fix:**
```csharp
using var fs = File.OpenRead(path);
Span<byte> b = stackalloc byte[3]; int n = fs.Read(b);
var enc = n >= 3 && b[0]==0xEF && b[1]==0xBB && b[2]==0xBF ? new UTF8Encoding(true) : new UTF8Encoding(false);
fs.Position = 0;
using var reader = new StreamReader(fs, enc);
```

### P4. `XlsxCodec.LoadSheet` is O(R²) via per-cell `EnsureSize`
Each `EnsureSize` iterates all existing rows to pad columns. For wide sheets this squares row count.

**Fix:** pre-scan `row/@r` and cell `@r` refs, size the sheet once (`EnsureSize(maxRow, maxCol)`), then fill.

### P5. Typing in Find counts matches over the whole grid per keystroke
`UpdateFindStatus` LINQ-scans every visible cell on each `TextChanged`.

**Fix:** debounce ~250 ms (`Timer`) or compute count on Enter/next-navigation only.

### P6. Hot mouse paths allocate via LINQ
`SelectionRange()` does `SelectedCells.Cast<DataGridViewCell>().ToList()`; it is called from `TryGetFillRange` on every `MouseMove` (`ContinueFillDrag`, cursor switching) and from `SelectHeaderRange`'s clicked-selection check which enumerates all rows × cells per header click.

**Fix:** track min/max during `SelectionChanged` instead of recomputing; use index-based loops for the header check.

### P7. Full `Render()` rebuild after every structural op
Insert/delete/move/sheet-switch rebuild all columns+rows and re-evaluate all formulas. Acceptable now, but combined with P1 it caps practical sheet size. Consider DataGridView *virtual mode* long-term.

### P8. ToolTip leak on the welcome screen
`RefreshRecentFiles` creates `new ToolTip()` per button per refresh; old instances are never disposed.

**Fix:** one `private readonly ToolStripMenuItem… ToolTip recentToolTip = new();` reused.

---

## 5. 🔵 Maintainability

### Q1. Four overlapping keyboard paths
MenuStrip `ShortcutKeys` + `Form.KeyDown` + `Form.ProcessCmdKey → TryProcessAppShortcut` + `SmoothDataGridView.ProcessCmdKey` forwarding back to the form. This is the root cause of F3/F6-style duplication and makes shortcut behavior hard to reason about.

**Fix:** make `TryProcessAppShortcut` the single router (menu items show display strings only; delete the `OnKeyDown` duplicates — its Ctrl+V branch is already unreachable because `ProcessCmdKey` intercepts first).

### Q2. Parallel primary/secondary field pairs everywhere
`workbook/model/path/dirty/undo/redo/filter` vs `secondaryWorkbook/secondaryModel/secondaryPath/secondaryDirty/…` with hand-written mirroring in dozens of methods (`SyncAll/SyncSecondaryAll`, `Render/RenderSecondaryModel`, `ApplyCell/ApplySecondaryCell`…). This duplication is where pane bugs live (e.g., F2, F4).

**Fix:** extract a `GridPane` class encapsulating {DataGridView, WorkbookModel, path, dirty, undo/redo, Render/Sync} and instantiate two. This is the single highest-value refactor in the codebase.

### Q3. Duplicated logic worth consolidating
- `EvaluatedCellValue` (MainForm) vs `EvaluatedValueAt` (SqlQueryEngine) vs inline evaluation in `ApplyCell/ApplySecondaryCell` → one `FormulaEvaluator.Evaluate(model,r,c)`.
- `CompareCells` vs `SqlQueryEngine.CellValueComparer` (same semantics, two implementations).
- `ColumnName/ColumnIndex/CellReference` duplicated across `MainForm`, `FormulaReferenceUpdater`, `XlsxCodec`.
- `CsvEscape` duplicated (`HeaderMenus.CsvEscape` vs `CsvCodec.Escape` — make the latter internal).
- `PaintHeader` vs `PaintSecondaryHeader` (unify with a `bool allowDropdown` flag).
- `MainForm.HeaderMenus.ParseDelimitedText` reimplements `CsvCodec.Load` parsing on a string — extract `CsvCodec.Parse(TextReader, char delimiter)` and reuse.

### Q4. `Tag` doubles as a formula store
Grid cells stash the raw formula in `cell.Tag`, while find-bar buttons also use `Tag="replace"`. It works but is invisible coupling; a future contributor will break it.

**Fix:** subclass `DataGridViewTextBoxCell` with a `Formula` property, or move formulas fully into the model and always display evaluated values (edit-start swaps value↔formula as today, minus Tag).

### Q5. Manual pixel layouts with magic numbers
`LayoutFilterBar`, `LayoutSort`, `LayoutFindBar`, `LayoutHelpPage` hardcode coordinates (some controls have both `Anchor` *and* manual Left/Width — conflicting systems). Extract named constants or move to `TableLayoutPanel`; at minimum delete the redundant anchors.

### Q6. Brittle menu construction detail
`file.DropDownItems.Insert(5, Item("New SQL console", …))` depends on the literal layout of the array above. Append after construction with a comment instead of a positional index.

### Q7. Accessibility gaps
Custom-drawn tabs/buttons mostly lack roles/states; `DocumentTab.TabStop = true` inserts every doc tab into the Tab order (set `false`); `FilterCheckedListBox.ItemHeight = 28` ignores DPI; header dropdown hit-zone is mouse-only (add keyboard access via the existing context menu — it exists, so document it).

### Q8. Culture-sensitive data written into documents
`SetFilledCell` writes `(last + delta).ToString(CurrentCulture)` and `ToShortDateString()` into the model → locale-dependent text persisted to CSV/XLSX. Store invariant (`CultureInfo.InvariantCulture` / ISO date), format at display time like everything else.

### Q9. Cross-sheet formulas degrade silently
`=Sheet2!A1` round-trips through XLSX load into a model the engine can't parse (`Unknown name 'Sheet2'` → `#ERROR!`), and the reference updater may mangle fragments. At minimum detect the `Name!` prefix and surface "cross-sheet references are not supported" once, rather than per-cell errors.

---

## 6. ⚪ Dead / removable code (verified unused)

| Item | Location | Evidence |
|---|---|---|
| `Theme.Apply(Control)` | Theme.cs:25 | No call sites (only `NativeTheme.ApplyDarkWindow/ApplyWindowChrome` used) |
| `ShowShortcuts()` | MainForm.cs:382 | Definition only; both callers use `ShowHelpPage("Keyboard shortcuts")` lambdas |
| `Theme.ScrollTrack` | Theme.cs:9 | Referenced only by its own aliases (`CellBackground`) |
| `?? Icon` self-fallback | MainForm.cs ctor: `Icon = Icon.ExtractAssociatedIcon(...) ?? Icon` | `Icon` here is the property being assigned — no-op; also `ExtractAssociatedIcon` re-reads the icon already embedded via csproj |
| `OnKeyDown` Ctrl+V branch | MainForm.cs | Unreachable — `ProcessCmdKey` chain consumes it first |
| `PaneDocumentSession.Undo/Redo` (secondary sessions) | MainForm.Documents.cs | Never populated/used for secondary — dead until F2 is fixed |
| `bin/` + `obj/` (~233 MB incl. Release publish output, `PublishOutputs.*.txt`) | repo tree | Build artifacts; see hygiene section |
| Double activation path | `DocumentTab.OnMouseDown` raises `Activated` *and* `OnGotFocus` raises it again on the same click | Keep the `GotFocus` one; in `OnMouseDown` raise only `CloseRequested` |

---

## 7. 🔒 Security & privacy notes

- **CSV formula injection (S1):** exported CSV/XLSX cells beginning with `=` `+` `-` `@` execute in Excel on reopen (classic CSV-injection). Low risk for a local tool, but cheap to mitigate: optional setting to prefix a `'` for leading metacharacters on CSV export.
- **XML safety:** OK — `XDocument.Load` prohibits DTDs by default (no XXE/billion-laughs), and zip entries are read in memory, never extracted to disk (no Zip-Slip).
- **Privacy claims** (no network/registry/persistence) hold up against the code — nothing contradicts them.

---

## 8. 🧹 Project hygiene

1. **Add `.gitignore` and drop build output** — `bin/` contains a full self-contained publish (~230 MB with runtime DLLs); `obj/` contains machine-local caches (`PublishOutputs.*.txt`, `.cache`). Standard .NET gitignore covers all of it.
2. **csproj polish**
   - `DebugType none` + `DebugSymbols false` apply to *all* builds, so even dev builds have no PDBs — scope them: `<DebugType Condition="'$(Configuration)'=='Release'">none</DebugType>` (or move publish-only settings into a publish profile).
   - Add metadata: `<Version>`, `<Authors>`, `<Description>`, `<InformationalVersion>` — currently the About dialog hardcodes "0.3".
   - Consider `<AnalysisLevel>latest</AnalysisLevel>` + `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`; nullable is already enabled (good).
3. **No tests.** `FormulaEngine`, `FormulaReferenceUpdater` (esp. the deletion/range math), `CsvCodec`, `SqlQueryEngine.Parser`, and `WorkbookModel` name logic are pure and trivially testable. A small xUnit project would have caught C1/F5/F7 immediately. Highest-leverage improvement available.
4. **app.manifest** is fine (`asInvoker`, longPathAware, Win10 GUID). No changes needed.

---

## 9. Prioritized fix list

| # | Item | Effort | Impact |
|---|------|--------|--------|
| 1 | C1 catch `OverflowException` in `FormulaEngine` | XS | Crash fix |
| 2 | F1 gate destructive shortcuts on overlay visibility | S | Silent-data-loss fix |
| 3 | F3 deduplicate shortcut routing (Q1 refactor included) | M | Undo-history correctness |
| 4 | C2 sort-preview rebasing | S | Data-loss footgun |
| 5 | F5 regex guard for identifier-like tokens | S | Formula corruption fix |
| 6 | P1 memoized formula evaluation pass | S→M | Big perf win on formula-heavy sheets |
| 7 | F7 CSV delimiter/BOM/empty-file fidelity | S | Data fidelity |
| 8 | F6 frozen-vs-split pane check | XS | Fidelity |
| 9 | Tests for engines/codecs (item 8.3) | M | Prevents regressions |
| 10 | Q2 `GridPane` extraction | L | Kills the pane-bug class (F2/F4) |
| 11 | Dead-code removals (section 6) | XS | Noise reduction |
| 12 | Hygiene items (gitignore, csproj) | XS | Repo health |

*XS < 1 h · S ≈ hours · M ≈ a day · L = multi-day refactor*

---

## Verified non-issues

- Source files are valid UTF-8; arrows/ellipses seen as `?` were console-rendering artifacts of the review tooling, not corruption.
- Document-tab click handlers close over method *parameters* (stable copies), not loop variables — no classic closure-capture bug there.
- `MoveWorksheetBetweenPanes`/`MoveDocumentBetweenPanes` index arithmetic (removal shift, clamp, blank-sheet replacement for the last remaining tab) checks out.
- Clipboard paste/paste-special bounds handling (`EnsureGrid`) is consistent; no out-of-range paths found.
