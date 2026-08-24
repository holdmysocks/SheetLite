using System.Globalization;

namespace SheetLite;

internal readonly record struct FormulaCellAddress(int Row, int Column)
{
    public override string ToString() => $"{ColumnName(Column)}{Row + 1}";

    private static string ColumnName(int zeroBasedColumn)
    {
        if (zeroBasedColumn < 0) return "#REF!";
        var value = zeroBasedColumn + 1;
        var name = "";
        while (value > 0)
        {
            value--;
            name = (char)('A' + value % 26) + name;
            value /= 26;
        }
        return name;
    }
}

internal readonly record struct FormulaRangeAddress(FormulaCellAddress Start, FormulaCellAddress End)
{
    public int Top => Math.Min(Start.Row, End.Row);
    public int Bottom => Math.Max(Start.Row, End.Row);
    public int Left => Math.Min(Start.Column, End.Column);
    public int Right => Math.Max(Start.Column, End.Column);
    public long CellCount => (long)(Bottom - Top + 1) * (Right - Left + 1);
    public bool Contains(FormulaCellAddress address) =>
        address.Row >= Top && address.Row <= Bottom && address.Column >= Left && address.Column <= Right;
}

internal enum FormulaError
{
    DivisionByZero,
    Reference,
    Value,
    Name,
    CircularReference,
    Number
}

internal abstract record FormulaValue
{
    public static readonly BlankValue Blank = new();
}

internal sealed record NumberValue(decimal Value) : FormulaValue;
internal sealed record TextValue(string Value) : FormulaValue;
internal sealed record BooleanValue(bool Value) : FormulaValue;
internal sealed record BlankValue : FormulaValue;
internal sealed record ErrorValue(FormulaError Error, string? Detail = null) : FormulaValue;

internal static class FormulaValueFormatter
{
    public static string Format(FormulaValue value) => value switch
    {
        BlankValue => "",
        NumberValue number => number.Value.ToString("0.############################", CultureInfo.InvariantCulture),
        TextValue text => text.Value,
        BooleanValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => ErrorText(error.Error),
        _ => ""
    };

    public static string ErrorText(FormulaError error) => error switch
    {
        FormulaError.DivisionByZero => "#DIV/0!",
        FormulaError.Reference => "#REF!",
        FormulaError.Value => "#VALUE!",
        FormulaError.Name => "#NAME?",
        FormulaError.CircularReference => "#CIRC!",
        FormulaError.Number => "#NUM!",
        _ => "#VALUE!"
    };

    public static string LegacyError(ErrorValue error) => error.Detail ?? error.Error switch
    {
        FormulaError.DivisionByZero => "Division by zero.",
        FormulaError.Reference => "Invalid cell reference.",
        FormulaError.Value => "Invalid value.",
        FormulaError.Name => "Unknown name.",
        FormulaError.CircularReference => "Circular cell reference.",
        FormulaError.Number => "Number too large.",
        _ => "Formula error."
    };
}

internal abstract record FormulaExpression;
internal sealed record NumberExpression(decimal Value) : FormulaExpression;
internal sealed record TextExpression(string Value) : FormulaExpression;
internal sealed record BooleanExpression(bool Value) : FormulaExpression;
internal sealed record ErrorExpression(FormulaError Error) : FormulaExpression;
internal sealed record UnaryExpression(char Operator, FormulaExpression Operand) : FormulaExpression;
internal sealed record BinaryExpression(char Operator, FormulaExpression Left, FormulaExpression Right) : FormulaExpression;
internal sealed record CellReferenceExpression(FormulaCellAddress Address, bool AbsoluteRow, bool AbsoluteColumn) : FormulaExpression;
internal sealed record RangeReferenceExpression(CellReferenceExpression Start, CellReferenceExpression End) : FormulaExpression;
internal sealed record FunctionExpression(string Name, IReadOnlyList<FormulaExpression> Arguments) : FormulaExpression;

internal readonly record struct FormulaParseResult(FormulaExpression? Expression, ErrorValue? Error)
{
    public bool Success => Expression is not null && Error is null;
}

internal static class FormulaParser
{
    public static FormulaParseResult Parse(string formula)
    {
        if (formula is null) throw new ArgumentNullException(nameof(formula));
        try
        {
            var text = formula.TrimStart();
            if (text.StartsWith('=')) text = text[1..];
            if (ContainsUnsupportedSheetReference(text))
                return Failed(FormulaError.Reference, "Cross-sheet references are not supported.");
            return new FormulaParseResult(new Parser(text).Parse(), null);
        }
        catch (FormulaParseException ex)
        {
            return Failed(ex.Error, ex.Message);
        }
        catch (OverflowException)
        {
            return Failed(FormulaError.Number, "Number too large.");
        }
    }

    private static FormulaParseResult Failed(FormulaError error, string detail) =>
        new(null, new ErrorValue(error, detail));

    private static bool ContainsUnsupportedSheetReference(string text)
    {
        char quote = '\0';
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (character != quote) continue;
                if (index + 1 < text.Length && text[index + 1] == quote)
                {
                    index++;
                    continue;
                }
                quote = '\0';
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }
            if (character != '!') continue;
            if (index >= 4 && text.AsSpan(index - 4, 5).Equals("#REF!", StringComparison.OrdinalIgnoreCase))
                continue;
            return true;
        }
        return false;
    }

    private sealed class Parser(string source)
    {
        private int position;

        public FormulaExpression Parse()
        {
            var expression = ParseAdditive();
            SkipWhite();
            if (position != source.Length) throw Error("Unexpected input");
            return expression;
        }

        private FormulaExpression ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                SkipWhite();
                if (Take('+')) left = new BinaryExpression('+', left, ParseMultiplicative());
                else if (Take('-')) left = new BinaryExpression('-', left, ParseMultiplicative());
                else return left;
            }
        }

        private FormulaExpression ParseMultiplicative()
        {
            var left = ParsePower();
            while (true)
            {
                SkipWhite();
                if (Take('*')) left = new BinaryExpression('*', left, ParsePower());
                else if (Take('/')) left = new BinaryExpression('/', left, ParsePower());
                else return left;
            }
        }

        private FormulaExpression ParsePower()
        {
            var left = ParseUnary();
            SkipWhite();
            return Take('^') ? new BinaryExpression('^', left, ParsePower()) : left;
        }

        private FormulaExpression ParseUnary()
        {
            SkipWhite();
            if (Take('+')) return new UnaryExpression('+', ParseUnary());
            if (Take('-')) return new UnaryExpression('-', ParseUnary());
            return ParsePrimary();
        }

        private FormulaExpression ParsePrimary()
        {
            SkipWhite();
            if (Take('('))
            {
                var expression = ParseAdditive();
                Require(')');
                return expression;
            }
            if (Peek() is '\'' or '"') return new TextExpression(ParseString());
            if (char.IsDigit(Peek()) || Peek() == '.') return new NumberExpression(ParseNumber());
            if (StartsWith("#REF!"))
            {
                position += 5;
                return new ErrorExpression(FormulaError.Reference);
            }

            var identifier = ParseIdentifier(out var absoluteRow, out var absoluteColumn);
            if (identifier.Length == 0) throw Error("Expected a value");
            SkipWhite();
            if (Take('(')) return ParseFunction(identifier);
            if (identifier.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return new BooleanExpression(true);
            if (identifier.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return new BooleanExpression(false);
            if (TryCell(identifier, out var address))
                return new CellReferenceExpression(address, absoluteRow, absoluteColumn);
            throw new FormulaParseException(FormulaError.Name, $"Unknown name '{identifier}'.");
        }

        private FormulaExpression ParseFunction(string name)
        {
            var arguments = new List<FormulaExpression>();
            SkipWhite();
            if (!Take(')'))
            {
                while (true)
                {
                    var argument = ParseAdditive();
                    SkipWhite();
                    if (argument is CellReferenceExpression start && Take(':'))
                    {
                        var end = ParsePrimary();
                        if (end is not CellReferenceExpression endReference) throw Error("Invalid range end");
                        argument = new RangeReferenceExpression(start, endReference);
                    }
                    arguments.Add(argument);
                    SkipWhite();
                    if (Take(')')) break;
                    Require(',');
                }
            }
            return new FunctionExpression(name.ToUpperInvariant(), arguments);
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
                    if (position < source.Length && source[position] == quote)
                    {
                        result.Append(quote);
                        position++;
                        continue;
                    }
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

        private string ParseIdentifier(out bool absoluteRow, out bool absoluteColumn)
        {
            SkipWhite();
            var start = position;
            absoluteColumn = Take('$');
            while (char.IsLetter(Peek())) position++;
            absoluteRow = Take('$');
            while (char.IsDigit(Peek())) position++;
            return source[start..position];
        }

        private static bool TryCell(string value, out FormulaCellAddress address)
        {
            address = default;
            value = value.Replace("$", "", StringComparison.Ordinal);
            var split = 0;
            while (split < value.Length && char.IsLetter(value[split])) split++;
            if (split == 0 || split == value.Length ||
                !int.TryParse(value[split..], out var oneBasedRow) || oneBasedRow < 1)
                return false;
            long oneBasedColumn = 0;
            foreach (var ch in value[..split].ToUpperInvariant())
            {
                oneBasedColumn = oneBasedColumn * 26 + ch - 'A' + 1;
                if (oneBasedColumn > int.MaxValue) return false;
            }
            address = new FormulaCellAddress(oneBasedRow - 1, (int)oneBasedColumn - 1);
            return true;
        }

        private bool StartsWith(string value) => source.AsSpan(position).StartsWith(value, StringComparison.OrdinalIgnoreCase);
        private void SkipWhite() { while (char.IsWhiteSpace(Peek())) position++; }
        private char Peek() => position < source.Length ? source[position] : '\0';
        private bool Take(char value) { if (Peek() != value) return false; position++; return true; }
        private void Require(char value) { SkipWhite(); if (!Take(value)) throw Error($"Expected '{value}'"); }
        private FormulaParseException Error(string message) => new(FormulaError.Value, $"{message} at position {position + 1}.");
    }

    private sealed class FormulaParseException(FormulaError error, string message) : Exception(message)
    {
        public FormulaError Error { get; } = error;
    }
}

internal abstract record FormulaDependency;
internal sealed record CellDependency(FormulaCellAddress Address) : FormulaDependency;
internal sealed record RangeDependency(FormulaRangeAddress Range) : FormulaDependency;

internal static class FormulaDependencyExtractor
{
    public static IReadOnlyCollection<FormulaDependency> Extract(FormulaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var dependencies = new HashSet<FormulaDependency>();
        Visit(expression, dependencies);
        return dependencies;
    }

    private static void Visit(FormulaExpression expression, HashSet<FormulaDependency> dependencies)
    {
        switch (expression)
        {
            case CellReferenceExpression cell:
                dependencies.Add(new CellDependency(cell.Address));
                break;
            case RangeReferenceExpression range:
                dependencies.Add(new RangeDependency(new FormulaRangeAddress(range.Start.Address, range.End.Address)));
                break;
            case UnaryExpression unary:
                Visit(unary.Operand, dependencies);
                break;
            case BinaryExpression binary:
                Visit(binary.Left, dependencies);
                Visit(binary.Right, dependencies);
                break;
            case FunctionExpression function:
                foreach (var argument in function.Arguments) Visit(argument, dependencies);
                break;
        }
    }
}
