namespace SheetLite;

internal sealed record HelpSection(string Title, string Body)
{
    public override string ToString() => Title;
}

internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> All { get; } =
    [
        new("Welcome",
            "SheetLite is a portable, dark-first CSV and XLSX editor for Windows. It uses the Dracula palette, keeps recent files only for the current session, performs no network requests, and needs no installer.\n\n" +
            "Use File → New or Ctrl+N for a blank workbook. Use File → Open or Ctrl+O for CSV/XLSX; in split view, Open targets the active pane. File → Open another or Ctrl+Shift+O opens selected files as additional workbook tabs in the active pane and focuses the first new workbook. Save with Ctrl+S, which follows the active workbook in the highlighted pane. Closing or replacing a modified workbook offers Save, Discard, and Cancel. Use the tabs along the bottom to add and switch worksheets. Formatting, frozen panes, formulas, and multiple worksheets require XLSX; CSV stores the active sheet's values only."),

        new("Interface and document tabs",
            "The unified top bar contains SheetLite's icon, File/Edit/Grid/View/Help menus, and custom minimize, maximize/restore, and close buttons. Drag an unused area of the top bar to move the window, double-click it to maximize or restore, and resize from any edge or corner.\n\n" +
            "Open workbooks appear as tabs above the grid and can be reordered within a pane or moved between split panes. A purple top line and purple title mark the active workbook. A purple dot means that document has unsaved changes; hover the dot/close area to reveal ×. The active cell has a purple outline in either pane, and the inactive grid dims slightly so the working side is immediately clear. The icon command strip moves to the active pane. The bottom status bar follows the active workbook and reports its cell/range, value type or character count, dimensions, and Saved/Modified state."),

        new("Grid editing",
            "• Click a cell to select it; double-click or type to edit.\n" +
            "• Drag across cells for a rectangular selection. The active cell has a solid purple outline and the rest of the range uses a dotted purple outline.\n" +
            "• Click a row number or column letter to select the entire row or column. Ctrl/Shift-click headers expands the selection.\n" +
            "• Right-click cells or selected row/column headers for relevant commands.\n" +
            "• Cut, copy, and paste multi-cell ranges with Ctrl+X/C/V.\n" +
            "• Insert above/below or left/right; delete and move selected rows or columns from the Grid menu or header context menus.\n" +
            "• Undo/redo uses up to 40 snapshots per open document, including independent right-pane workbooks. Resize columns by dragging a header divider; use Grid → Columns → Auto-size selected for content sizing."),

        new("Header menus and column filters",
            "Right-click a row number for row-specific commands: sort the selected rows, insert above/below, delete rows or duplicate rows, move up/down, cut/copy/paste, Copy As/Paste As, hide/unhide/delete hidden rows, and freeze/unfreeze through the selection.\n\n" +
            "Right-click a column letter for column-specific commands: text/number/date/length sorting plus Advanced sort, insert left/right, delete, move, cut/copy/paste, Copy As/Paste As, freeze/unfreeze, hide/unhide/delete hidden columns, auto-fit widths, and Filter this column.\n\n" +
            "Copy As supports CSV, raw tabular values, Markdown with or without a header, HTML with or without a header, JSON arrays, JSON objects, and SQL INSERT statements. Paste As accepts CSV/delimited text, Markdown tables, JSON arrays/objects, or a transposed tabular range.\n\n" +
            "The small chevron on the active column header opens a compact quick-filter card aligned to that column (or right-aligned when needed to stay inside the grid). Sort ascending/descending, expand More options for text/number/date/length choices or Advanced Sort, and switch between Filter by values and Filter by condition. Value mode supports search, individual checks, Select all, and Clear; condition mode supports the same operators as the docked Filter bar. (Blanks) represents empty cells. Clear Filter restores all rows. The active filtered column keeps a purple chevron indicator."),

        new("Worksheet tabs",
            "Worksheet tabs appear along the bottom of the grid, like Excel or Google Sheets.\n\n" +
            "• Select a tab to switch worksheets.\n" +
            "• Select + to add a worksheet.\n" +
            "• Double-click a left-pane tab name to rename it inline.\n" +
            "• Select × at the right of a tab to remove it when another worksheet remains. With only one worksheet, the left × closes the document and the right × closes the split pane. Undo restores an accidentally removed worksheet in the primary/shared workbook.\n" +
            "• Drag worksheet tabs left or right to reorder them. In split view, drag a tab to the other pane to move it between independent workbooks; when both panes show the same workbook, either tab bar reorders the shared sheets live.\n" +
            "• Drag workbook tabs along the top bar to reorder them, or drag them to the other split pane to move the workbook there.\n" +
            "• XLSX preserves every sheet name, value, supported formula, style, and frozen pane.\n" +
            "• CSV can contain only one sheet, so saving CSV writes the active worksheet and shows a reminder when other sheets exist."),

        new("Fill handle",
            "Drag the purple handle at the lower-right of a selection to fill up, down, left, or right. The handle and enlarged plus-cursor grab area work in either split pane; shared-view fills appear live on both sides.\n\n" +
            "• A single value is repeated.\n" +
            "• Two or more numbers, dates, or date-times extend their series without discarding time values.\n" +
            "• Formulas adjust relative references, so dragging =A1+B1 down produces =A2+B2.\n" +
            "• Absolute references such as $A$1 remain fixed.\n" +
            "• Cell colors, text colors, and bold formatting are copied with the fill."),

        new("Formatting",
            "Select cells and use Grid → Cell appearance:\n\n" +
            "• Cell background — choose a fill color.\n" +
            "• Text color — choose a foreground color.\n" +
            "• Toggle bold — Ctrl+B.\n" +
            "• Clear formatting — Ctrl+Shift+Space.\n" +
            "• Auto-size selected columns — Ctrl+Alt+A.\n\n" +
            "Basic background/text colors and bold text round-trip through XLSX. CSV cannot store formatting."),

        new("Freeze rows and columns",
            "Choose Grid → Freeze panes → Freeze at current cell (Ctrl+Shift+F) to freeze every row above the active cell and every column to its left. Rows and columns can be frozen at the same time.\n\n" +
            "You can also select row numbers or column letters, right-click, and choose Freeze through selected row(s)/column(s). Use the independent unfreeze commands in those menus or Grid → Freeze panes → Unfreeze panes."),

        new("Find and replace",
            "Ctrl+F opens the docked Find bar. Ctrl+H opens it with Replace expanded.\n\n" +
            "• Enter or the down arrow selects the next visible match. Shift+Enter or the up arrow selects the previous match.\n" +
            "• Replace changes the current match; Replace all changes all matches.\n" +
            "• The result count follows the current filter and excludes hidden rows.\n" +
            "• Escape or × closes the bar."),

        new("Filtering",
            "Ctrl+L opens the docked Filter bar. Choose a column, operator, and value, then select Apply filter.\n\n" +
            "Operators: contains, equals, not equals, starts with, ends with, >, >=, <, <=, is blank, and is not blank.\n\n" +
            "Select Conditions to add a second condition joined with AND or OR. Clear restores every row. Filtering treats row 1 as the header and keeps it visible. The current builder supports two conditions and does not yet support nested groups."),

        new("Sorting",
            "Open Grid → Sort or press Ctrl+Shift+T.\n\n" +
            "• Sort all data rows or only the selected rows.\n" +
            "• Choose ascending/descending and blanks first/last independently.\n" +
            "• Add a second sort column for tie-breaking.\n" +
            "• Quick sort uses Ctrl+Up/Ctrl+Down on the current column.\n\n" +
            "Select Sort to preview the result. Select Save sort to keep it, or Revert (or the panel's x button) to restore the exact pre-sort workbook. Row 1 is treated as the header. Formula columns are sorted by calculated values, and formula references follow reordered rows."),

        new("Formula reference",
            "Enter a formula by starting a cell with =. Supported operators are +, -, *, /, ^, unary +/-, and parentheses. References use A1 notation and may use $ for absolute rows or columns. Ranges use a colon, such as A1:B10.\n\n" +
            "Supported functions:\n" +
            "SUM(range or values) — adds numeric values\n" +
            "AVERAGE(range or values) — arithmetic mean\n" +
            "MIN(range or values) — smallest numeric value\n" +
            "MAX(range or values) — largest numeric value\n" +
            "COUNT(range or values) — counts numeric values\n" +
            "CONCAT(values) — joins text and values\n\n" +
            "Examples:\n" +
            "=A1+B1*2\n" +
            "=(A1+B1)^2\n" +
            "=SUM(B2:B20)\n" +
            "=AVERAGE(C2:C20)\n" +
            "=CONCAT(A2, \" — \", B2)\n\n" +
            "Formula cells recalculate after editing, paste, replace, fill, sorting, and row/column changes. Circular references and numbers outside the supported decimal range display an error instead of interrupting the app. Cross-sheet references are preserved when opening/saving XLSX but are not calculated by SheetLite. Supported formulas save as native XLSX formulas with cached values."),

        new("SQL console",
            "Open View → SQL console, use the database icon, or press Ctrl+`. The docked workspace shows the current workbook, worksheets, and columns on the left and a query editor on the right. Double-click a worksheet or column to insert it. Use the Row 1 is header switch to choose between header names and c1/c2 names. Run with F5, Ctrl+Enter, or the purple play icon. Queries are read-only and results open as an editable result document in the right pane. Formula cells are queried by their calculated values.\n\n" +
            "Supported clauses, in order:\n" +
            "SELECT columns | *\n" +
            "FROM source        optional; accepted for readability\n" +
            "WHERE column operator value\n" +
            "ORDER BY column ASC | DESC\n" +
            "LIMIT non-negative-number\n\n" +
            "WHERE operators: =, !=, <>, <, <=, >, >=, CONTAINS\n\n" +
            "Column names may be a header, A/B/C, c1/c2/c3, Column1/Column2, \"a quoted header\", or [a bracketed header]. Text values use single quotes.\n\n" +
            "Examples:\n" +
            "SELECT * LIMIT 100\n\n" +
            "SELECT Name, Score FROM current\n" +
            "WHERE Score >= 90\n" +
            "ORDER BY Score DESC\n" +
            "LIMIT 25\n\n" +
            "SELECT [Full Name], City WHERE City CONTAINS 'York';\n\n" +
            "Current limits: one WHERE condition, one ORDER BY column, and no joins, aggregates, GROUP BY, UPDATE, INSERT, or DELETE."),

        new("Split view",
            "Choose View → Split view or press Ctrl+Alt+S to open a second, editable view of the current worksheet. The document name appears above both panes. The two panes share one live workbook: scroll to different places, edit either side, and the change is immediately reflected in the other view. Switching worksheets in either shared pane switches both. Each pane keeps its own scroll position and selection. Clicking a pane gives its active cell a purple outline and dims the other grid slightly.\n\n" +
            "To open a different CSV/XLSX in one side, select that pane and use File → Open or Ctrl+O. You can also choose View → Open file in left pane or Open file in right pane explicitly. The selected file replaces only that pane, and SheetLite offers Save / Don't Save / Cancel before replacing a modified pane. Separate right-pane documents support cell/formula editing, cut/copy/paste, clearing cells, worksheet add/remove, independent undo/redo, and Ctrl+S/Save As. Closing the split asks whether to save a modified independent right-pane file; a shared second view closes without a duplicate prompt.\n\n" +
            "Both panes have worksheet tabs and numbered row headers. Use + in either pane to add a worksheet and × to remove one when another worksheet remains; shared-view changes appear in both panes. Undo/redo and the icon commands synchronize shared views. With two independent files, Find, Filter, Sort, Freeze, formatting, and structural commands currently target the left document; undo/redo follows the active pane. SQL results open as independent editable result documents; use Save As to keep them."),

        new("Drag and drop",
            "Drag CSV, TSV, TXT, or XLSX files from Explorer onto SheetLite. On the welcome screen, a purple Drop to open target appears and the first file opens as a document. With a workbook open, dropping on grid cells shows a purple cell overlay and opens each file as a new focused workbook tab in that pane; the current workbook stays open.\n\n" +
            "Dropping directly on the bottom worksheet bar uses a separate purple bar highlight and imports the file's worksheets into the current workbook without changing the active worksheet. XLSX imports every worksheet and preserves supported formatting. Imported XLSX sheets use File name - Sheet name so their origin remains clear; duplicate names receive a numeric suffix. Use File → Open another (Ctrl+Shift+O) to open additional workbook tabs without dragging. Drag bottom worksheet tabs to reorder or move them between split panes, and drag top workbook tabs to reorder or move workbooks between panes."),

        new("Keyboard shortcuts",
            "F1                 Help\n" +
            "Ctrl+N             New spreadsheet\n" +
            "Ctrl+O             Open CSV/XLSX in active pane\n" +
            "Ctrl+Shift+O       Open another workbook tab\n" +
            "Ctrl+S             Save active document\n" +
            "Ctrl+Shift+S       Save active document as\n" +
            "Ctrl+Shift+T       Sort setup\n" +
            "Ctrl+Shift+Up      Insert row above\n" +
            "Ctrl+Shift+Down    Insert row below\n" +
            "Ctrl+Shift+Left    Insert column left\n" +
            "Ctrl+Shift+Right   Insert column right\n" +
            "Ctrl+Subtract      Delete selected rows\n" +
            "Ctrl+Shift+Subtract Delete selected columns\n" +
            "Ctrl+Z / Ctrl+Y    Undo / Redo\n" +
            "Ctrl+X/C/V         Cut / Copy / Paste\n" +
            "Delete             Clear selected contents\n" +
            "Ctrl+F / Ctrl+H    Find / Replace\n" +
            "Ctrl+L             Filter\n" +
            "Ctrl+Shift+L       Clear filter\n" +
            "Ctrl+Up/Down       Quick sort current column\n" +
            "Ctrl+`             SQL console\n" +
            "Ctrl+Alt+S         Split view\n" +
            "Ctrl+Shift+F       Freeze at current cell\n" +
            "Ctrl+B             Toggle bold\n" +
            "Alt+Arrow          Move selected row/column"),

        new("File support and limits",
            "CSV/TSV/TXT: UTF-8 values with comma, tab, semicolon, or pipe delimiter detection. SheetLite reuses the detected delimiter and BOM choice when saving the same document. CSV does not store colors, fonts, column widths, formulas as native formulas, or frozen panes.\n\n" +
            "XLSX: multiple named worksheets, values, supported native formulas, basic background/text colors, bold text, and simultaneous frozen rows/columns.\n\n" +
            "Current limits: no merged cells, charts, conditional formatting, validation, macros, named ranges, or advanced Excel styling. Column widths are session-only. Commands such as Find, Filter, Sort, Freeze, and formatting target the shared workbook when both panes show it; with two independent files, select the left pane for those commands. Native Windows file, color, and unsaved-change dialogs are retained."),

        new("Privacy and portability",
            "SheetLite is a self-contained Windows x64 executable with no installer. It performs no network requests, collects no telemetry, writes no registry keys, and does not persist a recent-files list. Files are read and written only when you request it.\n\n" +
            "The executable is unsigned, so Windows SmartScreen may warn on first launch. Verify the supplied SHA-256 checksum before allowing an unsigned build from a source you trust.")
    ];
}
