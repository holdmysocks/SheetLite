using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace SheetLite;

internal static class XlsxCodec
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static SheetModel Load(string path) => LoadWorkbook(path).ActiveSheet.Sheet;

    public static WorkbookModel LoadWorkbook(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var shared = ReadSharedStrings(zip);
        var styles = ReadStyles(zip);
        var workbookXml = LoadXml(zip, "xl/workbook.xml");
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationships = LoadXml(zip, "xl/_rels/workbook.xml.rels").Descendants(packageRel + "Relationship")
            .Where(element => element.Attribute("Id") is not null && element.Attribute("Target") is not null)
            .ToDictionary(element => element.Attribute("Id")!.Value, element => element.Attribute("Target")!.Value);
        var result = new WorkbookModel();
        foreach (var sheetElement in workbookXml.Descendants(Main + "sheet"))
        {
            string name = WorkbookModel.NormalizeSheetName((string?)sheetElement.Attribute("name") ?? $"Sheet{result.Sheets.Count + 1}");
            string? relationshipId = sheetElement.Attribute(Rel + "id")?.Value;
            if (relationshipId is null || !relationships.TryGetValue(relationshipId, out string? target)) continue;
            string sheetPath = target.Replace('\\', '/').TrimStart('/'); if (!sheetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) sheetPath = "xl/" + sheetPath;
            result.Sheets.Add(new(name, LoadSheet(zip, sheetPath, shared, styles)));
        }
        if (result.Sheets.Count == 0) result.Sheets.Add(new("Sheet1", LoadSheet(zip, "xl/worksheets/sheet1.xml", shared, styles)));
        result.ActiveSheetIndex = Math.Clamp((int?)workbookXml.Descendants(Main + "workbookView").FirstOrDefault()?.Attribute("activeTab") ?? 0, 0, result.Sheets.Count - 1);
        return result;
    }

    private static SheetModel LoadSheet(ZipArchive zip, string sheetPath, IReadOnlyList<string> shared, IReadOnlyDictionary<int, Style> styles)
    {
        var xml = LoadXml(zip, sheetPath); var result = new SheetModel();
        var pane = xml.Descendants(Main + "pane").FirstOrDefault();
        if (string.Equals((string?)pane?.Attribute("state"), "frozen", StringComparison.OrdinalIgnoreCase))
        {
            result.FrozenRows = (int?)pane?.Attribute("ySplit") ?? 0;
            result.FrozenColumns = (int?)pane?.Attribute("xSplit") ?? 0;
        }
        var indexedRows = new List<(XElement Element, int Index)>(); int nextRow = 0, maxRow = -1, maxColumn = -1;
        foreach (var rowElement in xml.Descendants(Main + "row"))
        {
            int rowIndex = ((int?)rowElement.Attribute("r") ?? nextRow + 1) - 1; nextRow = rowIndex + 1; maxRow = Math.Max(maxRow, rowIndex); indexedRows.Add((rowElement, rowIndex));
            foreach (var cellElement in rowElement.Elements(Main + "c")) maxColumn = Math.Max(maxColumn, ColumnIndex((string?)cellElement.Attribute("r") ?? "A1"));
        }
        if (maxRow >= 0) result.EnsureSize(maxRow + 1, Math.Max(1, maxColumn + 1));
        foreach (var (rowElement, rowIndex) in indexedRows)
        {
            foreach (var cellElement in rowElement.Elements(Main + "c"))
            {
                string reference = (string?)cellElement.Attribute("r") ?? "A1";
                int column = ColumnIndex(reference);
                string type = (string?)cellElement.Attribute("t") ?? "";
                string raw = cellElement.Element(Main + "v")?.Value ?? cellElement.Element(Main + "is")?.Element(Main + "t")?.Value ?? "";
                string value = type == "s" && int.TryParse(raw, out int si) && si < shared.Count ? shared[si] : raw;
                if (type == "b") value = raw == "1" ? "TRUE" : "FALSE";
                if (cellElement.Element(Main + "f") is XElement formula) value = "=" + formula.Value;
                var cell = result.Rows[rowIndex][column];
                cell.Value = value;
                int styleIndex = (int?)cellElement.Attribute("s") ?? 0;
                if (styles.TryGetValue(styleIndex, out var style)) { cell.BackColor = style.BackColor; cell.ForeColor = style.ForeColor; cell.Bold = style.Bold; }
            }
        }
        if (result.Rows.Count == 0) result.EnsureSize(100, 26);
        return result;
    }

    public static void Save(string path, SheetModel sheet) => SaveWorkbook(path, WorkbookModel.FromSheet(sheet));

    public static void SaveWorkbook(string path, WorkbookModel workbook)
    {
        if (workbook.Sheets.Count == 0) throw new InvalidDataException("A workbook must contain at least one worksheet.");
        string temp = path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                Write(zip, "[Content_Types].xml", ContentTypes(workbook.Sheets.Count));
                Write(zip, "_rels/.rels", RootRelationships());
                Write(zip, "xl/workbook.xml", Workbook(workbook));
                Write(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships(workbook.Sheets.Count));
                var styleMap = BuildStyleMap(workbook.Sheets.Select(worksheet => worksheet.Sheet));
                Write(zip, "xl/styles.xml", StylesXml(styleMap.Keys.ToList()));
                for (int index = 0; index < workbook.Sheets.Count; index++) Write(zip, $"xl/worksheets/sheet{index + 1}.xml", SheetXml(workbook.Sheets[index].Sheet, styleMap));
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private record Style(Color? BackColor, Color? ForeColor, bool Bold);
    private static Dictionary<Style, int> BuildStyleMap(IEnumerable<SheetModel> sheets)
    {
        var map = new Dictionary<Style, int> { [new(null, null, false)] = 0 };
        foreach (var cell in sheets.SelectMany(sheet => sheet.Rows).SelectMany(row => row))
        {
            var style = new Style(cell.BackColor, cell.ForeColor, cell.Bold);
            if (!map.ContainsKey(style)) map[style] = map.Count;
        }
        return map;
    }

    private static string SheetXml(SheetModel sheet, Dictionary<Style, int> styles)
    {
        var data = new XElement(Main + "sheetData");
        int lastRow = sheet.Rows.FindLastIndex(r => r.Any(c => c.Value.Length > 0 || c.BackColor is not null || c.ForeColor is not null));
        for (int r = 0; r <= lastRow; r++)
        {
            var row = new XElement(Main + "row", new XAttribute("r", r + 1));
            for (int c = 0; c < sheet.Rows[r].Count; c++)
            {
                var value = sheet.Rows[r][c];
                if (value.Value.Length == 0 && value.BackColor is null && value.ForeColor is null) continue;
                var style = new Style(value.BackColor, value.ForeColor, value.Bold);
                var cell = new XElement(Main + "c", new XAttribute("r", CellReference(r, c)), new XAttribute("s", styles[style]));
                if (value.Value.TrimStart().StartsWith('='))
                {
                    var result = FormulaEngine.Evaluate(sheet, r, c); bool numeric = result.Success && double.TryParse(result.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _); if (!numeric) cell.SetAttributeValue("t", "str");
                    cell.Add(new XElement(Main + "f", value.Value.TrimStart()[1..])); cell.Add(new XElement(Main + "v", result.Success ? result.Value : "#VALUE!"));
                }
                else if (double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) cell.Add(new XElement(Main + "v", value.Value));
                else if (bool.TryParse(value.Value, out bool flag)) { cell.Add(new XAttribute("t", "b"), new XElement(Main + "v", flag ? "1" : "0")); }
                else cell.Add(new XAttribute("t", "inlineStr"), new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value.Value)));
                row.Add(cell);
            }
            data.Add(row);
        }
        var worksheet = new XElement(Main + "worksheet");
        if (sheet.FrozenRows > 0 || sheet.FrozenColumns > 0)
        {
            string topLeft = CellReference(sheet.FrozenRows, sheet.FrozenColumns);
            worksheet.Add(new XElement(Main + "sheetViews", new XElement(Main + "sheetView", new XAttribute("workbookViewId", 0),
                new XElement(Main + "pane", new XAttribute("xSplit", sheet.FrozenColumns), new XAttribute("ySplit", sheet.FrozenRows), new XAttribute("topLeftCell", topLeft), new XAttribute("activePane", "bottomRight"), new XAttribute("state", "frozen")))));
        }
        worksheet.Add(data, new XElement(Main + "autoFilter", new XAttribute("ref", $"A1:{CellReference(Math.Max(lastRow, 0), Math.Max(sheet.ColumnCount - 1, 0))}")));
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), worksheet).ToString(SaveOptions.DisableFormatting);
    }

    private static string StylesXml(List<Style> styles)
    {
        var fonts = new XElement(Main + "fonts", new XAttribute("count", styles.Count));
        var fills = new XElement(Main + "fills", new XAttribute("count", styles.Count + 2),
            new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "none"))),
            new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "gray125"))));
        foreach (var s in styles)
        {
            var font = new XElement(Main + "font", new XElement(Main + "sz", new XAttribute("val", 11)), new XElement(Main + "name", new XAttribute("val", "Segoe UI")));
            if (s.Bold) font.Add(new XElement(Main + "b"));
            if (s.ForeColor is Color fc) font.Add(new XElement(Main + "color", new XAttribute("rgb", ToArgb(fc))));
            fonts.Add(font);
            fills.Add(new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "solid"), new XElement(Main + "fgColor", new XAttribute("rgb", ToArgb(s.BackColor ?? Color.White))), new XElement(Main + "bgColor", new XAttribute("indexed", 64)))));
        }
        var xfs = new XElement(Main + "cellXfs", new XAttribute("count", styles.Count));
        for (int i = 0; i < styles.Count; i++) xfs.Add(new XElement(Main + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", i), new XAttribute("fillId", styles[i].BackColor is null ? 0 : i + 2), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyFont", 1), new XAttribute("applyFill", styles[i].BackColor is null ? 0 : 1)));
        var root = new XElement(Main + "styleSheet", fonts, fills, new XElement(Main + "borders", new XAttribute("count", 1), new XElement(Main + "border", new XElement(Main + "left"), new XElement(Main + "right"), new XElement(Main + "top"), new XElement(Main + "bottom"), new XElement(Main + "diagonal"))), new XElement(Main + "cellStyleXfs", new XAttribute("count", 1), new XElement(Main + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))), xfs);
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).ToString(SaveOptions.DisableFormatting);
    }

    private static Dictionary<int, Style> ReadStyles(ZipArchive zip)
    {
        var result = new Dictionary<int, Style>();
        var entry = zip.GetEntry("xl/styles.xml"); if (entry is null) return result;
        using var stream = entry.Open(); var doc = XDocument.Load(stream);
        var fonts = doc.Descendants(Main + "fonts").Elements(Main + "font").ToList();
        var fills = doc.Descendants(Main + "fills").Elements(Main + "fill").ToList();
        int i = 0;
        foreach (var xf in doc.Descendants(Main + "cellXfs").Elements(Main + "xf"))
        {
            int fontId = (int?)xf.Attribute("fontId") ?? 0, fillId = (int?)xf.Attribute("fillId") ?? 0;
            Color? fg = fontId < fonts.Count ? ReadColor(fonts[fontId].Element(Main + "color")) : null;
            Color? bg = fillId < fills.Count ? ReadColor(fills[fillId].Descendants(Main + "fgColor").FirstOrDefault()) : null;
            bool bold = fontId < fonts.Count && fonts[fontId].Element(Main + "b") is not null;
            result[i++] = new(bg, fg, bold);
        }
        return result;
    }
    private static Color? ReadColor(XElement? e)
    {
        string? rgb = e?.Attribute("rgb")?.Value; if (rgb is null || rgb.Length < 6) return null;
        try { return ColorTranslator.FromHtml("#" + rgb[^6..]); } catch { return null; }
    }
    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml"); if (entry is null) return [];
        using var stream = entry.Open(); var doc = XDocument.Load(stream);
        return doc.Descendants(Main + "si").Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value))).ToList();
    }
    private static XDocument LoadXml(ZipArchive zip, string name) { using var s = zip.GetEntry(name)?.Open() ?? throw new InvalidDataException($"Missing {name}"); return XDocument.Load(s); }
    private static void Write(ZipArchive zip, string name, string text) { var e = zip.CreateEntry(name, CompressionLevel.Optimal); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(text); }
    private static string ToArgb(Color c) => $"FF{c.R:X2}{c.G:X2}{c.B:X2}";
    private static int ColumnIndex(string reference) { int value = 0; foreach (char c in reference.TakeWhile(char.IsLetter)) value = value * 26 + char.ToUpperInvariant(c) - 'A' + 1; return value - 1; }
    private static string CellReference(int row, int column) { string col = ""; for (int n = column + 1; n > 0; n = (n - 1) / 26) col = (char)('A' + (n - 1) % 26) + col; return col + (row + 1); }
    private static string ContentTypes(int sheetCount)
    {
        XNamespace content = "http://schemas.openxmlformats.org/package/2006/content-types";
        var root = new XElement(content + "Types",
            new XElement(content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(content + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(content + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
        for (int index = 1; index <= sheetCount; index++) root.Add(new XElement(content + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{index}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).ToString(SaveOptions.DisableFormatting);
    }
    private static string RootRelationships() => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";
    private static string Workbook(WorkbookModel workbook)
    {
        var sheets = new XElement(Main + "sheets");
        for (int index = 0; index < workbook.Sheets.Count; index++) sheets.Add(new XElement(Main + "sheet", new XAttribute("name", workbook.Sheets[index].Name), new XAttribute("sheetId", index + 1), new XAttribute(Rel + "id", $"rId{index + 1}")));
        var root = new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel), new XElement(Main + "bookViews", new XElement(Main + "workbookView", new XAttribute("activeTab", Math.Clamp(workbook.ActiveSheetIndex, 0, workbook.Sheets.Count - 1)))), sheets);
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).ToString(SaveOptions.DisableFormatting);
    }
    private static string WorkbookRelationships(int sheetCount)
    {
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        var root = new XElement(relationships + "Relationships");
        for (int index = 1; index <= sheetCount; index++) root.Add(new XElement(relationships + "Relationship", new XAttribute("Id", $"rId{index}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", $"worksheets/sheet{index}.xml")));
        root.Add(new XElement(relationships + "Relationship", new XAttribute("Id", $"rId{sheetCount + 1}"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")));
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).ToString(SaveOptions.DisableFormatting);
    }
}
