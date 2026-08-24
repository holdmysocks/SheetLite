# SheetLite — Code Review Round 2 (Post-Fix Verification)

**Date:** 2026-08-23 · **Scope:** verification of CODE_REVIEW.md findings + regression/new-issue hunt · **No source changes made**

15 of 19 source files were modified after Round 1. LOC went from ~4.3k → 4,209 lines (net simplification despite new features like independent secondary undo).

**Verdict:** Excellent response to the review. Both critical bugs are fixed correctly, all 10 functional bugs are fixed or deliberately closed, 6 of 8 performance items are fixed, and nearly all verified dead code is gone. A few residuals and 4 new minor findings below — nothing critical.

---

## 1. Round-1 findings — status

### 🔴 Critical

| ID | Finding | Status |
|----|---------|--------|
| C1 | `9^9^9` OverflowException crashed the UI | ✅ **FIXED** — `OverflowException` caught → `"Number too large."`; `ParsePower` now throws `FormulaException` via `double.IsFinite` guard |
| C2 | Non-modal sort panel wiped edits made while open | ✅ **FIXED (well designed)** — `SetDirty()` rebases `sortBaselineWorkbook` while panel is open pre-preview; edits after Apply go through `PushUndo → SaveSortPreview` (auto-commit); Revert restores baseline *and* prior dirty state; sheet switch / save auto-commits |

### 🟠 Functional bugs

| ID | Finding | Status |
|----|---------|--------|
| F1 | Delete fired behind Help/Info overlays | ✅ FIXED — `gridCommandsBlocked = helpPage.Visible || infoPanel.Visible || welcome.Visible` gates all destructive commands; Esc closes overlays |
| F2 | Right-pane documents had no working undo/redo | ✅ FIXED — full `PushSecondaryUndo` / `SecondaryUndo` / `SecondaryRedo` / `SecondaryCut/Copy/Paste/DeleteContents`; per-session stacks; Ctrl+Z follows the active pane; help text corrected |
| F3 | Shortcuts registered twice (menu + router) → double execution | ✅ FIXED — all menu items use `ShortcutKeys = Keys.None` + display strings; `TryProcessAppShortcut` is the single router (comment explains why) |
| F4 | Filter state split across three stores | ✅ FIXED (mostly) — header-filter fields are the source of truth; `FilterByCurrentValue` routes through `ApplyHeaderValueFilter`; `ClearFilter`/`UnhideAllRows`/`DeleteHiddenRows` reset everything; `MirrorPrimaryRowVisibilityToSharedSecondary` added; `ReapplyDockedFilterIfActive` has sane precedence |
| F5 | Reference rewriter corrupted tokens like `LOG10` | ✅ FIXED — `IsFunctionCallIdentifier` skips matches followed by optional whitespace + `(` (residuals → R5/R6 below) |
| F6 | Excel split panes loaded as frozen | ✅ FIXED — `state == "frozen"` attribute checked |
| F7 | CSV save forced commas + BOM; stray line on empty sheet | ✅ FIXED — delimiter/BOM persisted on `SheetModel` and reused on save; `lastRow < 0 → return`; delimiter-aware `Escape()` |
| F8 | Fill handle did nothing up/left | ✅ FIXED — all four directions implemented with correct `Mod()`-based cyclic patterns and series continuation in both directions (verified math) |
| F9 | Column-width change marked document dirty | ✅ FIXED — `ColumnWidthChanged` now only updates the edit outline (widths remain unsaved-by-design, but no false prompt) |
| F10 | Nit batch (dialog filter, combo restore, typeLabel, welcome clip, ActiveSheet throw) | ✅ ALL ADDRESSED — filter includes .tsv/.txt; combos restored semantically by header suffix; typeLabel shows formula/error; welcome card clamped 640–780px (fits 720px minimum); `ActiveSheet` throw kept but save paths guard with clear messages |

### 🟡 Performance

| ID | Finding | Status |
|----|---------|--------|
| P1 | No formula memoization | 🟡 **PARTIAL** — `FormulaEvaluationContext` with memo dict is used by `RecalculateFormulaCells` and `RecalculateSecondaryFormulaCells`. But `Render() → ApplyCell` still evaluates each formula cell via fresh `FormulaEngine.Evaluate` (new memo *per cell*) → formula-heavy renders remain O(F×depth). See R1. |
| P2 | PushUndo = SyncAll + whole-workbook Clone per edit | ⚪ Open (unchanged; accepted tradeoff) |
| P3 | Full-file read just for BOM | ✅ FIXED — streamed 3-byte check with `stackalloc` |
| P4 | XLSX load O(R²) per-cell EnsureSize | ✅ FIXED — max row/column pre-scanned, sized once. Bonus: saves are now atomic via `.tmp` + `File.Move` |
| P5 | Find status scanned all cells per keystroke | ✅ FIXED — 250 ms debounce timer (disposed properly) |
| P6 | `SelectionRange().Cast().ToList()` per MouseMove | ✅ FIXED — plain min/max loop |
| P7 | Full grid rebuild after every structural op | ⚪ Open (long-term virtual-mode item; unchanged) |
| P8 | New `ToolTip` per recent-file button | ✅ FIXED — single reused instance, disposed along with `sqlToolTips` and `findDebounce` |

### 🔵 Maintainability

| ID | Finding | Status |
|----|---------|--------|
| Q1 | Four overlapping keyboard paths | ✅ RESOLVED — single router; menu `ShortcutKeys` disabled; the dead `OnKeyDown` Ctrl+V branch is gone |
| Q2 | Extract `GridPane` class (primary/secondary mirroring) | ⚪ Open — and now more pressing: the Secondary* surface grew (see §4) |
| Q3 | Duplicated logic | 🟡 Partial — `ParseDelimitedText` now delegates to `CsvCodec.ParseRows` ✔; `ColumnName`/`CellReference` still triplicated (MainForm, FormulaReferenceUpdater, XlsxCodec); `PaintHeader` vs `PaintSecondaryHeader` still near-duplicates; `CompareCells`/`EvaluatedCellValue` still parallel SqlQueryEngine copies |
| Q4 | `cell.Tag` doubles as formula store | ⚪ Open (works; find-bar buttons use `Control.Tag`, so no runtime conflict — purely a design smell) |
| Q5 | Manual pixel layouts with magic numbers | ⚪ Open (unchanged) |
| Q6 | Positional `file.DropDownItems.Insert(5, …)` | ✅ RESOLVED — menus built with `AddRange` |
| Q7 | Accessibility gaps | 🟡 Mostly fixed — `DocumentTab.TabStop=false` + `AccessibleRole=PageTab`; `FilterCheckedListBox.ItemHeight` DPI-scaled; chrome/toolbar buttons have `AccessibleName`s. Remaining: header-dropdown filter hot-zone is mouse-only (Ctrl+L bar is the keyboard alternative) |
| Q8 | Locale-dependent persisted values | ✅ FIXED — fill-series numbers/dates use `InvariantCulture`; literal parsing/formatting invariant end-to-end |
| Q9 | Cross-sheet refs silently became `#ERROR!` | ✅ Improved — explicit `"Cross-sheet references are not supported."` error; XLSX export writes `#VALUE!` cached value; help documents the limit |

### ⚪ Dead code — all removed ✔
`Theme.Apply`, `Theme.ScrollTrack`, `ShowShortcuts`, `Icon … ?? Icon` self-fallback, unreachable `OnKeyDown` Ctrl+V branch, `DocumentTab` double `Activated` firing (OnGotFocus override deleted; OnMouseDown-only now). Verified by grep + read.

### 🔒 Security
S1 (CSV formula injection) — unchanged, accepted risk as documented. XML posture still safe (DTD prohibited, no archive extraction → no Zip-Slip). Relationship targets now normalized/trimmed (small hardening win). `TryCell` gained an int-overflow guard (nice).

### 🧹 Hygiene

| Item | Status |
|------|--------|
| csproj metadata (Version/Authors/Description) | ✅ Added (0.4.0; About panel matches) |
| `DebugType none` applied to all configurations | ✅ Fixed — conditioned on `Release` |
| `.gitignore` | ❌ **Still missing** |
| Committed `bin/` + `obj/` (~233 MB self-contained publish output) | ❌ **Still present** (116.5 + 116.4 MB) |
| Unit tests | ❌ None added |

---

## 2. 🆕 New findings this round

### R1 · Performance (M) — evaluation contexts created per cell in render paths
`ApplySecondaryCell(row, col, FormulaEvaluationContext? context = null)` accepts a context precisely to avoid this, and `RecalculateSecondaryFormulaCells` passes one correctly. But `RenderSecondaryModel` and `RefreshSharedSecondaryFromModel` call it *without* the argument → `CreateContext` (fresh memo) per formula cell. Same shape in the primary pane: `Render() → ApplyCell(r,c)` calls `FormulaEngine.Evaluate(model, r, c)` per cell with no context parameter at all.
**Fix:** hoist `var ctx = FormulaEngine.CreateContext(modelOrSecondaryModel);` before the loops and thread it through (add the parameter to `ApplyCell`). One-line-per-site change; completes P1.

### R2 · Performance (M) — every edit refreshes the entire shared secondary grid
`SetDirty()` → `RefreshSharedSecondaryFromModel()` → else-branch loops **all** rows×columns calling `ApplySecondaryCell` (style assignment per cell, plus formula evals per R1). In shared split view, committing a single keystroke costs 2,600+ cell updates at the default 100×26 size — more on real data.
**Fix:** update only the edited cell(s) in the mirror (the caller knows them), or diff-and-skip identical cells; keep the full loop for structural ops.

### R3 · Bug, minor (S) — Undo/Redo ignore an active sort preview
If a sort preview is applied and the user presses Ctrl+Z, `Undo()` swaps `workbook` behind the panel's back: `sortBaselineWorkbook` becomes stale. Clicking **Revert** then silently discards the state the user undid *to*, and **Save sort** pushes that stale pre-sort baseline onto the undo stack (out-of-order snapshot).
**Fix:** in `Undo`/`Redo`, if `sortBaselineWorkbook is not null`, either `RevertSortPreview()` first or rebase the baseline from the restored workbook (same trick `SetDirty` uses).

### R4 · Bug, minor (XS) — Ctrl+Up / Ctrl+Down swallowed in text editors
In `TryProcessAppShortcut`, the sort shortcuts lack the `!textFocused` guard every other editing command has:
```csharp
Keys.Control | Keys.Up when !gridCommandsBlocked => () => Sort(true),
```
With focus in the SQL editor (multiline) or find box, Ctrl+Up/Down sorts instead of moving the caret.
**Fix:** add `&& !textFocused` to both arms.

### R5 · Correctness, low (XS) — `'!'` anywhere disables reference rewriting
`RewriteFormula`: `formula.Contains('!') ? formula : …`. This was added to dodge unsupported cross-sheet refs, but it also matches `!` inside quoted strings: `="Total!"&A1` is never rewritten on insert/delete/sort.
**Fix:** reuse the existing `IsInsideQuotedString` helper — skip only when `!` occurs outside quotes.

### R6 · Correctness, low (XS) — no column bound in the reference regex
Round-1 suggestion to bound columns ≤ XFD (16,384) wasn't taken. `[A-Za-z]{1,3}\d+` still treats tokens like `ZZZ99` (col 18,277) or `ABC99999` as rewritable cell refs when they're really identifiers (e.g., a pasted name in a CONCAT chain). Rare, but the fix is a cheap post-match filter alongside `IsFunctionCallIdentifier`.

### R7 · Nit (XS) — no-op sort marks document dirty
`FinishSortPreview` sets `dirty = true` unconditionally; sorting an already-sorted range flips the title to *Modified*. Compare against `sortBaselineDirty` before flagging, or compare row order.

### R8 · Nit (XS) — version lives in two places
`csproj` `<Version>0.4.0</Version>` and `MainForm.AppVersion = "0.4.0"`. They agree today; they won't forever.
**Fix:** `const string AppVersion = "0.4.0"` → derive from assembly: `typeof(MainForm).Assembly.GetName().Version?.ToString(3)`.

### R9 · Keyboard/pane consistency (S)
Structural shortcuts (Insert/Delete row/col, Ctrl+Up/Down sort) still operate on the **left** document even when the right pane is active. The *toolbar* shows a helpful "targets the left document" notice (`PrimaryCommand`), but the *keyboard* silently edits the left pane — inconsistent feedback for the same command. Either route these through the same guard/notice, or extend the Secondary* treatment (help text does disclose the limitation).

---

## 3. Carry-forward recommendations (unchanged from Round 1)

1. **Tests (M, highest value/$)** — still zero. `FormulaEngine` (incl. overflow, cycles, ranges), `FormulaReferenceUpdater` (function-name guard, quoted strings, deletions), `CsvCodec` round-trips, and `WorkbookModel` naming are pure logic begging for xUnit. Would have caught C1/F5/F7 automatically.
2. **Q2 `GridPane` extraction (L)** — the mirrored field/method surface grew again (`SecondaryUndo/Redo/Cut/Copy/Paste/DeleteContents/PushSecondaryUndo/SyncSecondaryAll/RenderSecondaryModel/…`). This is the highest-leverage structural refactor remaining.
3. **P2 snapshot cost (M)** — `SyncAll()` + `workbook.Clone()` per edit; consider dirty-region tracking or cell-level undo.
4. **P7 virtual mode (L, long-term)** — full `Render()` rebuilds persist.
5. **Hygiene (XS)** — add `.gitignore` (`bin/`, `obj/`, `.vs/`), delete the committed build output (~233 MB), optionally `AnalysisLevel=latest` + `EnforceCodeStyleInBuild`.
6. **S1 CSV injection (optional)** — `'` prefix mitigation on save for cells starting with `=`/`+`/`-`/`@`.

---

## 4. Suggested priority for this round

| # | Item | Effort | Impact |
|---|------|--------|--------|
| 1 | R3 undo-vs-sort-preview interaction | S | prevents silent data-state surprise |
| 2 | R4 Ctrl+Up/Down textFocused guard | XS | obvious keynav bug |
| 3 | R1 thread eval contexts through render paths | S–M | completes P1 |
| 4 | R2 targeted shared-view refresh | M | big interactive-latency win in split mode |
| 5 | R5/R6 regex refinements | XS each | reference-rewrite correctness |
| 6 | Hygiene: .gitignore + prune bin/obj | XS | repo health |
| 7 | Test scaffold for the 4 pure engines | M | regression safety net |
| 8 | R7/R8/R9 nits | XS | polish |
| 9 | Q2 GridPane refactor | L | long-term maintainability |

---

## 5. Regression check — things I re-verified as still sound

- **Drag & drop**: `MoveWorksheetBetweenPanes` / `MoveDocumentBetweenPanes` index math re-checked against the new session code — insertion clamps, decrement-on-forward-move, blank-placeholder substitution all correct.
- **Closure capture**: tab/document-tab lambdas receive `index` as a *method parameter* (not the `for` loop variable) — safe; verified again after the tabs rewrite.
- **Memo + cycle detection**: exception propagation leaves the memo unpolluted; `visiting` removal in `finally` correct; diamond dependencies cached properly.
- **Secondary undo wiring**: every mutating secondary path (cell edit, paste, delete, sheet add/delete) funnels through `PushSecondaryUndo`; shared-mode correctly delegates to primary undo.
- **Atomic XLSX save** (temp + move) is a genuine durability improvement beyond the review list.
- **Encoding**: files are valid UTF-8 (stray `?` glyphs in console output remain cmd-rendering artifacts, not corruption).
