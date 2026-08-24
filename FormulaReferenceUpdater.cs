using System.Text;
using System.Text.RegularExpressions;

namespace SheetLite;

internal static partial class FormulaReferenceUpdater
{
    private static readonly Regex CellOrRangeReference = CellOrRangeReferencePattern();

    public static void InsertRows(SheetModel sheet, int index, int count = 1) =>
        Rewrite(sheet, reference => reference with { Row = reference.Row >= index ? reference.Row + count : reference.Row });

    public static void InsertColumns(SheetModel sheet, int index, int count = 1) =>
        Rewrite(sheet, reference => reference with { Column = reference.Column >= index ? reference.Column + count : reference.Column });

    public static void DeleteRows(SheetModel sheet, IReadOnlyCollection<int> deletedRows) =>
        RewriteForDeletion(sheet, deletedRows.ToHashSet(), rows: true);

    public static void DeleteColumns(SheetModel sheet, IReadOnlyCollection<int> deletedColumns) =>
        RewriteForDeletion(sheet, deletedColumns.ToHashSet(), rows: false);

    public static void SwapRows(SheetModel sheet, int first, int second) =>
        Rewrite(sheet, reference => reference with { Row = reference.Row == first ? second : reference.Row == second ? first : reference.Row });

    public static void SwapColumns(SheetModel sheet, int first, int second) =>
        Rewrite(sheet, reference => reference with { Column = reference.Column == first ? second : reference.Column == second ? first : reference.Column });

    public static void RemapRows(SheetModel sheet, IReadOnlyDictionary<int, int> oldToNew) =>
        Rewrite(sheet, reference => reference with { Row = oldToNew.GetValueOrDefault(reference.Row, reference.Row) });

    public static string OffsetReferences(string formula, int rowOffset, int columnOffset) =>
        RewriteFormula(formula, reference => reference with
        {
            Row = reference.FixedRow ? reference.Row : Math.Max(0, reference.Row + rowOffset),
            Column = reference.FixedColumn ? reference.Column : Math.Max(0, reference.Column + columnOffset)
        });

    private static void Rewrite(SheetModel sheet, Func<Reference, Reference?> map)
    {
        foreach (var row in sheet.Rows)
        foreach (var cell in row)
        {
            if (!cell.Value.TrimStart().StartsWith('=')) continue;
            cell.Value = RewriteFormula(cell.Value, map);
        }
    }

    private static void RewriteForDeletion(SheetModel sheet, HashSet<int> deleted, bool rows)
    {
        foreach (var row in sheet.Rows)
        foreach (var cell in row)
        {
            if (!cell.Value.TrimStart().StartsWith('=')) continue;
            cell.Value = RewriteDeletionFormula(cell.Value, deleted, rows);
        }
    }

    private static string RewriteFormula(string formula, Func<Reference, Reference?> map) =>
        ContainsUnquotedExclamation(formula) ? formula : RewriteMatches(formula, CellOrRangeReference, match =>
        {
            var mapped = map(ParseReference(match, 1));
            if (mapped is null) return "#REF!";
            if (!match.Groups[5].Success) return FormatReference(mapped.Value);
            var mappedEnd = map(ParseReference(match, 5));
            return mappedEnd is null ? "#REF!" : FormatReference(mapped.Value) + ":" + FormatReference(mappedEnd.Value);
        });

    private static string RewriteDeletionFormula(string formula, HashSet<int> deleted, bool rows) =>
        ContainsUnquotedExclamation(formula) ? formula : RewriteMatches(formula, CellOrRangeReference, match =>
        {
            var first = ParseReference(match, 1);
            if (!match.Groups[5].Success)
            {
                var mapped = MapDeletedReference(first, deleted, rows);
                return mapped is null ? "#REF!" : FormatReference(mapped.Value);
            }

            var second = ParseReference(match, 5);
            int firstAxis = rows ? first.Row : first.Column;
            int secondAxis = rows ? second.Row : second.Column;
            int low = Math.Min(firstAxis, secondAxis), high = Math.Max(firstAxis, secondAxis);
            var survivors = Enumerable.Range(low, high - low + 1).Where(index => !deleted.Contains(index)).ToList();
            if (survivors.Count == 0) return "#REF!";

            int mappedLow = ShiftAfterDeletion(survivors[0], deleted);
            int mappedHigh = ShiftAfterDeletion(survivors[^1], deleted);
            bool forward = firstAxis <= secondAxis;
            int mappedFirst = forward ? mappedLow : mappedHigh;
            int mappedSecond = forward ? mappedHigh : mappedLow;
            first = rows ? first with { Row = mappedFirst } : first with { Column = mappedFirst };
            second = rows ? second with { Row = mappedSecond } : second with { Column = mappedSecond };
            return FormatReference(first) + ":" + FormatReference(second);
        });

    private static Reference? MapDeletedReference(Reference reference, HashSet<int> deleted, bool rows)
    {
        int axis = rows ? reference.Row : reference.Column;
        if (deleted.Contains(axis)) return null;
        int mapped = ShiftAfterDeletion(axis, deleted);
        return rows ? reference with { Row = mapped } : reference with { Column = mapped };
    }

    private static int ShiftAfterDeletion(int index, HashSet<int> deleted) =>
        index - deleted.Count(candidate => candidate < index);

    private static string RewriteMatches(string formula, Regex regex, Func<Match, string> replacement)
    {
        var result = new StringBuilder(formula.Length);
        int copiedThrough = 0;
        foreach (Match match in regex.Matches(formula))
        {
            if (IsInsideQuotedString(formula, match.Index) || IsFunctionCallIdentifier(formula, match) || !HasValidReference(match)) continue;
            result.Append(formula, copiedThrough, match.Index - copiedThrough);
            result.Append(replacement(match));
            copiedThrough = match.Index + match.Length;
        }
        if (copiedThrough == 0) return formula;
        result.Append(formula, copiedThrough, formula.Length - copiedThrough);
        return result.ToString();
    }

    private static bool ContainsUnquotedExclamation(string formula)
    {
        for (int index = 0; index < formula.Length; index++)
            if (formula[index] == '!' && !IsInsideQuotedString(formula, index)) return true;
        return false;
    }

    private static bool HasValidReference(Match match)
    {
        const int maximumExcelColumn = 16_383, maximumExcelRow = 1_048_576; // Column is zero based; matched rows are one based.
        if (ParseColumn(match.Groups[2].Value) > maximumExcelColumn) return false;
        if (!int.TryParse(match.Groups[4].Value, out int firstRow) || firstRow is < 1 or > maximumExcelRow) return false;
        if (match.Groups.Count <= 6 || !match.Groups[6].Success) return true;
        return ParseColumn(match.Groups[6].Value) <= maximumExcelColumn && int.TryParse(match.Groups[8].Value, out int secondRow) && secondRow is >= 1 and <= maximumExcelRow;
    }

    private static bool IsFunctionCallIdentifier(string formula, Match match)
    {
        int next = match.Index + match.Length;
        while (next < formula.Length && char.IsWhiteSpace(formula[next])) next++;
        return next < formula.Length && formula[next] == '(';
    }

    private static bool IsInsideQuotedString(string value, int position)
    {
        char activeQuote = '\0';
        for (int index = 0; index < position; index++)
        {
            char character = value[index];
            if (activeQuote == '\0')
            {
                if (character is '\'' or '"') activeQuote = character;
                continue;
            }
            if (character != activeQuote) continue;
            if (index + 1 < position && value[index + 1] == activeQuote) { index++; continue; }
            activeQuote = '\0';
        }
        return activeQuote != '\0';
    }

    private static Reference ParseReference(Match match, int groupOffset) => new(
        int.Parse(match.Groups[groupOffset + 3].Value) - 1,
        ParseColumn(match.Groups[groupOffset + 1].Value),
        match.Groups[groupOffset + 2].Value == "$",
        match.Groups[groupOffset].Value == "$");

    private static string FormatReference(Reference reference) =>
        (reference.FixedColumn ? "$" : "") + ColumnName(reference.Column) +
        (reference.FixedRow ? "$" : "") + (reference.Row + 1);

    private static int ParseColumn(string letters)
    {
        int column = 0;
        foreach (char character in letters.ToUpperInvariant()) column = column * 26 + character - 'A' + 1;
        return column - 1;
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private readonly record struct Reference(int Row, int Column, bool FixedRow, bool FixedColumn);

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(\$?)([A-Za-z]{1,3})(\$?)(\d+)(?::(\$?)([A-Za-z]{1,3})(\$?)(\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex CellOrRangeReferencePattern();
}
