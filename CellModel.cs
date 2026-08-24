namespace SheetLite;

internal sealed class CellModel
{
    public string Value { get; set; } = "";
    public Color? BackColor { get; set; }
    public Color? ForeColor { get; set; }
    public bool Bold { get; set; }
    public CellModel Clone() => new() { Value = Value, BackColor = BackColor, ForeColor = ForeColor, Bold = Bold };
}

internal sealed class SheetModel
{
    public List<List<CellModel>> Rows { get; } = [];
    public int FrozenRows { get; set; }
    public int FrozenColumns { get; set; }
    public char CsvDelimiter { get; set; } = ',';
    public bool CsvUtf8Bom { get; set; } = true;
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);
    public void EnsureSize(int rows, int columns)
    {
        while (Rows.Count < rows) Rows.Add([]);
        foreach (var row in Rows) while (row.Count < columns) row.Add(new());
    }
    public SheetModel Clone()
    {
        var copy = new SheetModel { FrozenRows = FrozenRows, FrozenColumns = FrozenColumns, CsvDelimiter = CsvDelimiter, CsvUtf8Bom = CsvUtf8Bom };
        foreach (var row in Rows) copy.Rows.Add(row.Select(c => c.Clone()).ToList());
        return copy;
    }
}

internal sealed class WorksheetModel(string name, SheetModel sheet)
{
    public string Name { get; set; } = name;
    public SheetModel Sheet { get; set; } = sheet;
    public WorksheetModel Clone() => new(Name, Sheet.Clone());
}

internal sealed class WorkbookModel
{
    public List<WorksheetModel> Sheets { get; } = [];
    public int ActiveSheetIndex { get; set; }
    public WorksheetModel ActiveSheet => Sheets.Count == 0
        ? throw new InvalidOperationException("A workbook must contain at least one worksheet.")
        : Sheets[Math.Clamp(ActiveSheetIndex, 0, Sheets.Count - 1)];

    public static WorkbookModel CreateBlank(string name = "Sheet1")
    {
        var sheet = new SheetModel(); sheet.EnsureSize(100, 26);
        var workbook = new WorkbookModel(); workbook.Sheets.Add(new(NormalizeSheetName(name), sheet)); return workbook;
    }

    public static WorkbookModel FromSheet(SheetModel sheet, string name = "Sheet1")
    {
        var workbook = new WorkbookModel(); workbook.Sheets.Add(new(NormalizeSheetName(name), sheet)); return workbook;
    }

    public WorkbookModel Clone()
    {
        var copy = new WorkbookModel { ActiveSheetIndex = ActiveSheetIndex };
        copy.Sheets.AddRange(Sheets.Select(sheet => sheet.Clone())); return copy;
    }

    public string NextSheetName()
    {
        int number = 1; string candidate;
        do candidate = $"Sheet{number++}"; while (Sheets.Any(sheet => sheet.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)));
        return candidate;
    }

    public string UniqueSheetName(string requested, int? exceptIndex = null)
    {
        string baseName = NormalizeSheetName(requested); string candidate = baseName; int suffix = 2;
        while (Sheets.Where((_, index) => index != exceptIndex).Any(sheet => sheet.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            string ending = $" ({suffix++})"; candidate = baseName[..Math.Min(baseName.Length, 31 - ending.Length)] + ending;
        }
        return candidate;
    }

    public static string NormalizeSheetName(string name)
    {
        string cleaned = new(name.Where(character => !"[]:*?/\\".Contains(character)).ToArray());
        cleaned = cleaned.Trim().Trim('\''); if (cleaned.Length == 0) cleaned = "Sheet";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
