using SheetLite;
using System.IO.Compression;
using System.Xml.Linq;

namespace SheetLite.Tests;

internal sealed class FormulaBoundaryTests
{
    private static readonly string[] ErrorFormulas =
    [
        "=1/0",
        "=#REF!",
        "=1+)",
        "=MISSING(1)",
        "=E1",
        "=10^100"
    ];

    private static readonly string[] ErrorTexts =
    [
        "#DIV/0!",
        "#REF!",
        "#VALUE!",
        "#NAME?",
        "#CIRC!",
        "#NUM!"
    ];

    [Test] public void Typed_formula_errors_are_preserved_at_display_and_sql_boundaries()
    {
        SheetModel sheet = ErrorSheet();
        var context = FormulaEngine.CreateContext(sheet);

        Assert.Sequence(ErrorTexts, Enumerable.Range(0, ErrorTexts.Length)
            .Select(column => sheet.EvaluatedValue(0, column, context)));

        SqlQueryResult result = SqlQueryEngine.Execute(sheet, "SELECT *");

        Assert.True(result.Success, result.Error);
        Assert.Single(result.Rows);
        Assert.Sequence(ErrorTexts, result.Rows[0].Select(cell => cell.Value));
        Assert.Sequence(ErrorFormulas, Enumerable.Range(0, ErrorFormulas.Length)
            .Select(column => sheet.GetRawValue(0, column)));
    }

    [Test] public void Xlsx_formula_cache_uses_typed_error_cells_and_preserves_success_types()
    {
        SheetModel sheet = ErrorSheet();
        sheet.SetCellValue(0, 6, "=1+1");
        sheet.SetCellValue(0, 7, "='ok'");
        string directory = Path.Combine(Path.GetTempPath(), "sheetlite-boundary-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "typed-errors.xlsx");
        Directory.CreateDirectory(directory);

        try
        {
            XlsxCodec.Save(path, sheet);

            using var zip = ZipFile.OpenRead(path);
            using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var cells = XDocument.Load(stream).Descendants(spreadsheet + "c")
                .ToDictionary(cell => (string)cell.Attribute("r")!);

            for (int column = 0; column < ErrorTexts.Length; column++)
            {
                XElement cell = cells[$"{(char)('A' + column)}1"];
                Assert.Equal("e", (string?)cell.Attribute("t"));
                Assert.Equal(ErrorFormulas[column][1..], cell.Element(spreadsheet + "f")!.Value);
                Assert.Equal(ErrorTexts[column], cell.Element(spreadsheet + "v")!.Value);
            }

            Assert.Null((string?)cells["G1"].Attribute("t"));
            Assert.Equal("2", cells["G1"].Element(spreadsheet + "v")!.Value);
            Assert.Equal("str", (string?)cells["H1"].Attribute("t"));
            Assert.Equal("ok", cells["H1"].Element(spreadsheet + "v")!.Value);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static SheetModel ErrorSheet()
    {
        var sheet = new SheetModel();
        sheet.EnsureSize(1, ErrorFormulas.Length);
        using (sheet.BeginUpdate())
            for (int column = 0; column < ErrorFormulas.Length; column++)
                sheet.SetCellValue(0, column, ErrorFormulas[column]);
        return sheet;
    }
}
