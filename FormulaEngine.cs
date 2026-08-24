using System.Globalization;

namespace SheetLite;

/// <summary>Evaluates small, Excel-style formulas without changing the sheet.</summary>
internal static class FormulaEngine
{
    public static FormulaResult Evaluate(SheetModel sheet, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return CreateContext(sheet).Evaluate(row, column);
    }

    public static FormulaEvaluationContext CreateContext(SheetModel sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return new FormulaEvaluationContext(sheet);
    }

    internal sealed class FormulaEvaluationContext(SheetModel sheet)
    {
        private readonly Dictionary<(int Row, int Column), object?> memo = [];

        public FormulaResult Evaluate(int row, int column)
        {
        try
        {
            var visiting = new HashSet<(int Row, int Column)>();
            var value = EvaluateCell(sheet, row, column, visiting, memo);
            return new FormulaResult(true, Format(value), null);
        }
        catch (FormulaException ex)
        {
            return new FormulaResult(false, "", ex.Message);
        }
        catch (OverflowException)
        {
            return new FormulaResult(false, "", "Number too large.");
        }
        }
    }

    public static FormulaResult EvaluateExpression(SheetModel sheet, string expression)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        try
        {
            var text = expression.TrimStart();
            if (text.StartsWith('=')) text = text[1..];
            if (text.Contains('!')) throw new FormulaException("Cross-sheet references are not supported.");
            var value = new Parser(sheet, text, [], []).Parse();
            return new FormulaResult(true, Format(value), null);
        }
        catch (FormulaException ex)
        {
            return new FormulaResult(false, "", ex.Message);
        }
        catch (OverflowException)
        {
            return new FormulaResult(false, "", "Number too large.");
        }
    }

    private static object? EvaluateCell(SheetModel sheet, int row, int column, HashSet<(int, int)> visiting, Dictionary<(int, int), object?> memo)
    {
        if (row < 0 || column < 0 || row >= sheet.Rows.Count || column >= sheet.Rows[row].Count) return "";
        if (memo.TryGetValue((row, column), out object? cached)) return cached;
        var text = sheet.Rows[row][column].Value;
        if (!text.TrimStart().StartsWith('=')) return memo[(row, column)] = ParseLiteral(text);
        if (text.Contains('!')) throw new FormulaException("Cross-sheet references are not supported.");
        if (!visiting.Add((row, column))) throw new FormulaException("Circular cell reference.");
        try { return memo[(row, column)] = new Parser(sheet, text.TrimStart()[1..], visiting, memo).Parse(); }
        finally { visiting.Remove((row, column)); }
    }

    private static object ParseLiteral(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : text;

    private static string Format(object? value) => value switch
    {
        null => "",
        decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
        double number => number.ToString("G15", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "TRUE" : "FALSE",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private readonly record struct CellRange(int Row1, int Column1, int Row2, int Column2);

    private sealed class Parser(SheetModel sheet, string source, HashSet<(int, int)> visiting, Dictionary<(int, int), object?> memo)
    {
        private int position;

        public object? Parse()
        {
            var value = ParseAdditive();
            SkipWhite();
            if (position != source.Length) throw Error("Unexpected input");
            return value;
        }

        private object? ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                SkipWhite();
                if (Take('+')) left = Number(left) + Number(ParseMultiplicative());
                else if (Take('-')) left = Number(left) - Number(ParseMultiplicative());
                else return left;
            }
        }

        private object? ParseMultiplicative()
        {
            var left = ParsePower();
            while (true)
            {
                SkipWhite();
                if (Take('*')) left = Number(left) * Number(ParsePower());
                else if (Take('/'))
                {
                    var divisor = Number(ParsePower());
                    if (divisor == 0) throw new FormulaException("Division by zero.");
                    left = Number(left) / divisor;
                }
                else return left;
            }
        }

        private object? ParsePower()
        {
            var left = ParseUnary();
            SkipWhite();
            if (!Take('^')) return left;
            var powered = Math.Pow((double)Number(left), (double)Number(ParsePower()));
            if (!double.IsFinite(powered)) throw new FormulaException("Result out of range.");
            return (decimal)powered;
        }

        private object? ParseUnary()
        {
            SkipWhite();
            if (Take('+')) return Number(ParseUnary());
            if (Take('-')) return -Number(ParseUnary());
            return ParsePrimary();
        }

        private object? ParsePrimary()
        {
            SkipWhite();
            if (Take('('))
            {
                var value = ParseAdditive();
                Require(')');
                return value;
            }
            if (Peek() is '\'' or '"') return ParseString();
            if (char.IsDigit(Peek()) || Peek() == '.') return ParseNumber();

            var identifier = ParseIdentifier();
            if (identifier.Length == 0) throw Error("Expected a value");
            SkipWhite();
            if (Take('(')) return ParseFunction(identifier);
            if (TryCell(identifier, out var row, out var column))
                return EvaluateCell(sheet, row, column, visiting, memo);
            throw new FormulaException($"Unknown name '{identifier}'.");
        }

        private object? ParseFunction(string name)
        {
            var values = new List<object?>();
            SkipWhite();
            if (!Take(')'))
            {
                while (true)
                {
                    SkipWhite();
                    var saved = position;
                    var first = ParseIdentifier();
                    SkipWhite();
                    if (TryCell(first, out var row1, out var column1) && Take(':'))
                    {
                        var second = ParseIdentifier();
                        if (!TryCell(second, out var row2, out var column2)) throw Error("Invalid range end");
                        AddRange(values, new CellRange(row1, column1, row2, column2));
                    }
                    else
                    {
                        position = saved;
                        values.Add(ParseAdditive());
                    }
                    SkipWhite();
                    if (Take(')')) break;
                    Require(',');
                }
            }

            var upper = name.ToUpperInvariant();
            if (upper == "CONCAT") return string.Concat(values.Select(Format));
            if (upper == "COUNT") return values.Count(TryNumber);
            var numbers = values.Where(TryNumber).Select(Number).ToList();
            return upper switch
            {
                "SUM" => numbers.Sum(),
                "AVERAGE" when numbers.Count > 0 => numbers.Average(),
                "MIN" when numbers.Count > 0 => numbers.Min(),
                "MAX" when numbers.Count > 0 => numbers.Max(),
                "AVERAGE" or "MIN" or "MAX" => throw new FormulaException($"{upper} requires a numeric value."),
                _ => throw new FormulaException($"Unknown function '{name}'.")
            };
        }

        private void AddRange(List<object?> values, CellRange range)
        {
            var top = Math.Min(range.Row1, range.Row2);
            var bottom = Math.Max(range.Row1, range.Row2);
            var left = Math.Min(range.Column1, range.Column2);
            var right = Math.Max(range.Column1, range.Column2);
            for (var row = top; row <= bottom; row++)
                for (var column = left; column <= right; column++)
                    values.Add(EvaluateCell(sheet, row, column, visiting, memo));
        }

        private string ParseString()
        {
            var quote = source[position++];
            var result = new System.Text.StringBuilder();
            while (position < source.Length)
            {
                var ch = source[position++];
                if (ch == quote)
                {
                    if (position < source.Length && source[position] == quote) { result.Append(quote); position++; continue; }
                    return result.ToString();
                }
                result.Append(ch);
            }
            throw Error("Unterminated string");
        }

        private decimal ParseNumber()
        {
            var start = position;
            while (char.IsDigit(Peek()) || Peek() is '.' or 'e' or 'E' or '+' or '-')
            {
                if ((Peek() is '+' or '-') && position > start && source[position - 1] is not ('e' or 'E')) break;
                position++;
            }
            if (!decimal.TryParse(source[start..position], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw Error("Invalid number");
            return value;
        }

        private string ParseIdentifier()
        {
            SkipWhite();
            var start = position;
            if (Peek() == '$') position++;
            while (char.IsLetter(Peek())) position++;
            if (Peek() == '$') position++;
            while (char.IsDigit(Peek())) position++;
            return source[start..position];
        }

        private static bool TryCell(string value, out int row, out int column)
        {
            row = column = -1;
            value = value.Replace("$", "", StringComparison.Ordinal);
            var split = 0;
            while (split < value.Length && char.IsLetter(value[split])) split++;
            if (split == 0 || split == value.Length || !int.TryParse(value[split..], out var oneBasedRow) || oneBasedRow < 1) return false;
            long oneBasedColumn = 0;
            foreach (var ch in value[..split].ToUpperInvariant())
            {
                oneBasedColumn = oneBasedColumn * 26 + ch - 'A' + 1;
                if (oneBasedColumn > int.MaxValue) return false;
            }
            row = oneBasedRow - 1;
            column = (int)oneBasedColumn - 1;
            return true;
        }

        private static bool TryNumber(object? value) => value is decimal ||
            decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        private static decimal Number(object? value)
        {
            if (value is decimal number) return number;
            if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
            throw new FormulaException($"'{Format(value)}' is not numeric.");
        }

        private void SkipWhite() { while (char.IsWhiteSpace(Peek())) position++; }
        private char Peek() => position < source.Length ? source[position] : '\0';
        private bool Take(char value) { if (Peek() != value) return false; position++; return true; }
        private void Require(char value) { SkipWhite(); if (!Take(value)) throw Error($"Expected '{value}'"); }
        private FormulaException Error(string message) => new($"{message} at position {position + 1}.");
    }

    private sealed class FormulaException(string message) : Exception(message);
}

internal readonly record struct FormulaResult(bool Success, string Value, string? Error);
