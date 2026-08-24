# SheetLite — Code Review Round 3

**Date:** 2026-08-23 · **Scope:** full source review of all 19 files (~4.2k LOC), WinForms / .NET 9. **No code was changed.**
This round verifies the fixes claimed in `CODE_REVIEW_2.md` and hunts for regressions and new issues.

**Verdict:** The codebase is in good shape — both prior critical bugs are properly fixed and nearly all round-2 items landed correctly. However, this round found **1 new crash-class bug (N1) and 2 state-management bugs (N2/N3)**, concentrated in the interaction seams between features (sort preview × document tabs × split panes).

---

## 1. Verification of Round-2 findings

| ID | Finding | Status |
|----|---------|--------|
| R1 | Per-cell eval contexts in render paths | ✅ **FIXED** — `Render()` / `RenderSecondaryModel()` now thread a shared `FormulaEvaluationContext` through `ApplyCellCore` / `ApplySecondaryCell`. Residual: `ReplaceAllDocked` / `ReplaceCurrent` still call `ApplyCell` (null context) per match (`MainForm.DockedTools.cs:519-520`). |
| R2 | Full shared-secondary refresh per edit | ✅ **FIXED (for cell edits)** — `SetDirtyCell` → `RefreshSharedSecondaryCell` updates only the edited cell plus formulas (`MainForm.cs:962`). The `SetDirty()` full mirror remains but only fires on structural ops. |
| R3 | Undo/Redo vs active sort preview | ✅ **FIXED** — both revert an active preview first and consume the undo op when a preview was applied (`MainForm.cs:840-854`). |
| R4 | Ctrl+Up/Down swallowed in text editors | ✅ **FIXED** — `!textFocused` guard present (`MainForm.cs:1035-1036`), now also routed via `RunPrimaryCommand`. |
| R5 | `'!'` anywhere disabled reference rewriting | ✅ **FIXED** — `ContainsUnquotedExclamation` respects quoted strings (`FormulaReferenceUpdater.cs:122`). |
| R6 | Unbounded column refs in regex | ✅ **FIXED** — `HasValidColumns` caps at XFD/16,383 (`FormulaReferenceUpdater.cs:129`). But see **N1** below: rows are still unbounded → crash. |
| R7 | No-op sort marks document dirty | ✅ **FIXED** — `dirty = changed \|\| sortBaselineDirty` (`MainForm.DockedTools.cs:582`). |
| R8 | Version duplicated in two places | ✅ **FIXED** — derived from assembly version (`MainForm.cs:10`). |
| R9 | Shortcuts silently target left pane | 🟡 **PARTIAL** — structural ops and sort route through `RunPrimaryCommand` with a notice; **Ctrl+Shift+F (Freeze), Ctrl+B (Bold), Ctrl+Shift+Space (Clear formatting), Ctrl+Alt+A (Auto-size) still silently edit the left document** while the right pane is active (`MainForm.cs:1039-1042`). |

---

## 2. New findings this round

### 🔴 N1 · Crash: `int.Parse` overflow on oversized row references

`FormulaReferenceUpdater.ParseReference` (`FormulaReferenceUpdater.cs:161-165`) does a raw
`int.Parse(match.Groups[...].Value)` on the `\d+` row part with **no upper bound**; `HasValidColumns`
bounds only the column letters. Repro:

1. Paste or type a formula like `=A99999999999999+1`. The engine's `TryCell` safely rejects it
   ("Unknown name"), so the text stays in the cell.
2. Perform any reference-rewriting operation — insert/delete/move a row or column, sort,
   delete duplicate rows, delete hidden rows/columns, or drag-fill (`OffsetReferences`).

The regex matches `A99999999999999`, `ParseReference` throws `OverflowException`, and nothing on
the call chain catches it (`ApplyRowOrder` `MainForm.cs:900`, `InsertRow` `:867`,
`ApplyFill` `:717`, …) → unhandled exception on the UI thread, app dies.

**Fix:** treat out-of-range rows like over-range columns: `int.TryParse` + cap at Excel's
1,048,576 rows inside a `HasValidRange` check next to `HasValidColumns`, skipping the match when invalid.

### 🟠 N2 · Sort preview survives document-tab switching and can overwrite the wrong document

`sortBaselineWorkbook` is committed/reverted on worksheet switch (`MainForm.DockedTools.cs:679`),
save (`:803`), push-undo (`:837`), and Undo/Redo — but **not** when the active *document* changes:

- `ActivatePrimaryDocument` (`MainForm.Documents.cs:85`)
- `ClosePrimaryDocumentAt` (`MainForm.Documents.cs:148`)
- `OpenFilesAsDocuments` (`MainForm.Documents.cs:170`)
- `MoveDocumentBetweenPanes` → `ApplyPrimaryDocumentSession` (`MainForm.DragDrop.cs:294`)

Repro: open doc A → open the sort panel (baseline = A) → click doc B's tab (panel stays open;
`ShowSortPanel` won't rebase because it is already visible) → click **Apply sort**.
`ApplyDockedSort` line 548 executes `workbook = sortBaselineWorkbook.Clone(); model = …; Render();`
— doc B is replaced by a snapshot of doc A. Not pushed to undo either; recovery depends on B's own
undo stack.

**Fix:** in all four sites call `SaveSortPreview()` / `RevertSortPreview()` before swapping sessions
(same pattern as `SwitchPrimarySheet`), or record which session owns the baseline and auto-commit on mismatch.

### 🟠 N3 · Cross-pane worksheet moves are only half-undoable

`MoveWorksheetBetweenPanes` (`MainForm.DragDrop.cs:231-263`) mutates two workbooks but snapshots only one:
`if (sourcePrimary || targetPrimary || secondarySharesPrimary) PushUndo(); else SyncSecondaryAll();`.
Moving a sheet right→left pushes a primary snapshot; the secondary workbook loses the sheet with no
secondary undo entry (and vice versa). This contradicts the per-pane undo promise documented in Help
("independent right-pane workbooks"). Data isn't lost (it lives in the other pane) but the move cannot
be undone from its source pane.

**Fix:** push snapshots to both panes' stacks for cross-pane moves.

### 🟡 N4 · Exit never offers to save

`ConfirmLoseChanges` (`MainForm.cs:996`) is a Yes/No "Discard unsaved changes?" prompt used by app close
(`OnClosing` `:997`), New, and Open — there is no Save option anywhere on the primary-document exit path,
while the secondary path *does* offer YesNoCancel-with-save (`ConfirmSecondaryClose`,
`MainForm.DockedTools.cs:927`). Inconsistent and data-loss prone.

**Fix:** adopt a Save/Discard/Cancel trifold everywhere (reuse the secondary dialog pattern).

### 🟡 N5 · No global exception handler

`Program.cs` runs `Application.Run` bare. Combined with N1-style gaps, any missed engine exception kills
the process with no dialog or log. Cheap insurance:

```csharp
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
Application.ThreadException += …;
AppDomain.CurrentDomain.UnhandledException += …;
```

### 🟡 N6 · Likely-unintended File menu item

`MainForm.cs:84` — the File menu contains **"New SQL console" (Ctrl+`)**, placed between "Save As…" and
Exit, duplicating View → SQL console. The label/placement doesn't fit File semantics; looks like a
leftover experiment. Remove it or move it to View.

### 🟡 N7 · Performance residuals

- Every committed cell edit runs `RecalculateFormulaCells()` — a full-grid pass (`MainForm.cs:514-523`);
  fine at 100×26, noticeable on large formula-heavy sheets.
- In shared split view, `RefreshSharedSecondaryCell` re-applies **all** formula cells after every
  keystroke commit (`MainForm.DockedTools.cs:831-841`).
- `SqlQueryEngine.EvaluatedValueAt` creates a fresh evaluation context per cell with no memoization
  across filter/order passes (`SqlQueryEngine.cs:71-78`).
- Carry-forwards unchanged (accepted): P2 whole-workbook clone per edit; P7 full `Render()` rebuilds
  after structural ops; Q2 `GridPane` extraction — the mirrored Secondary* surface keeps growing
  (~30 members now), making this the highest-leverage refactor remaining.

### 🔵 N8 · Dead code

- `FormulaEngine.EvaluateExpression` (`FormulaEngine.cs:43`) — zero callers anywhere.
- `Theme.Cyan/Green/Orange/Pink/Red/Yellow` (`Theme.cs:16-22`) — unused palette entries.

### ⚪ N9 · Minor nits

| Item | Location | Detail |
|------|----------|--------|
| Orphaned `.tmp` on failed save | `XlsxCodec.cs:81-93` | temp file not cleaned up if an exception occurs mid-write — wrap in try/finally delete |
| Date fill truncation | `MainForm.cs:768` | fill-series date extrapolation formats `"yyyy-MM-dd"`, silently dropping time components |
| Culture inconsistency in comparisons | `MainForm.cs:909` vs `SqlQueryEngine.cs:134-146` | grid sort compares numbers with `CurrentCulture`; SQL ORDER BY compares numbers invariantly but dates culturally — same data can order differently across features |
| `UnhideAllRows` doesn't mirror or refresh find status | `MainForm.HeaderMenus.cs:123` | unlike `ClearFilter`, no `MirrorPrimaryRowVisibilityToSharedSecondary()` / `UpdateFindStatus()` |
| Help highlight covers title too | `MainForm.DockedTools.cs:305-313` | search highlighting scans from index 0, coloring title text as well as body |
| No-op undo snapshot | `MainForm.cs:941` | `ToggleBold` with an empty selection still pushes an empty undo snapshot |
| CSV parser edge case | `CsvCodec.cs:43` | final row consisting solely of empty quoted fields is dropped when file lacks trailing newline (flush condition requires non-empty field/row) — pathological input only |
| Delimiter detection header-only | `CsvCodec.cs:72-77` | counts candidate delimiters on line 1 only; a comma-containing header pins `,` even for semicolon data |

---

## 3. Security & hygiene (carry-forward, unchanged)

- **S1 CSV formula injection on save** — remains accepted/documented risk.
- XML posture safe (no DTD processing, no archive extraction paths → no Zip-Slip).
- `bin/` + `obj/` (~350 MB combined: 232 + 118 MB measured this round) still present in the tree.
- Still no `.gitignore`; **still zero unit tests** — note that N1 would have been caught instantly by a
  `FormulaReferenceUpdater` test using long digit strings.

---

## 4. Suggested priority

| # | Item | Effort | Impact |
|---|------|--------|--------|
| 1 | N1 row-ref overflow crash guard | XS | prevents hard crash from pasted content + common op |
| 2 | N2 sort baseline on document switch | S | prevents cross-document state corruption |
| 3 | N4 save-on-close trifold | S | data-loss UX |
| 4 | N5 global exception handlers | XS | crash containment |
| 5 | N3 cross-pane move undo | S | undo consistency |
| 6 | R9 residue: route formatting/freeze shortcuts through `RunPrimaryCommand` | XS | pane consistency |
| 7 | N8 dead-code removal + `.gitignore`/bin-obj prune + test scaffold for the four engines | XS–M | hygiene/regression safety net |
| 8 | N6/N9 nits | XS each | polish |

---

## 5. Regression check — verified sound this round

- **Formula engine**: memoized evaluation with cycle detection correct, including exception paths
  (`visiting` removal in `finally`; memo unpolluted on failure); overflow guards in place; division-by-zero
  and range handling verified.
- **Sort remapping semantics**: `RemapRows` applied before physical reorder produces correct outgoing/incoming
  reference behavior (references follow moved rows; refs *from* moved cells stay put).
- **Drag & drop index math**: `MoveWorksheetBetweenPanes` insertion clamps, decrement-on-forward-move, and
  blank-placeholder substitution re-checked against session code — correct. Tab/document lambdas capture
  `index` as method parameters — safe.
- **Disposal**: message filter removed, chrome glyphs / `boldCellFont` / tooltips / debounce timer disposed.
- **Atomic XLSX save** (temp + `File.Move`) and debounced find bar hold up.
- **Encoding**: files valid UTF-8; console-rendering artifacts in earlier rounds were display-only.

## 6. What's good

The four engines (`CsvCodec`, `FormulaEngine`, `FormulaReferenceUpdater`, `SqlQueryEngine`) remain clean,
self-contained, and testable. Both prior criticals were fixed properly rather than patched over. Remaining
risk is concentrated in feature interaction seams (sort-preview × documents × panes) — exactly where
N2/N3 live — which argues for the long-deferred `GridPane` extraction and a test scaffold before more
features land.
