using System.Globalization;

namespace SheetLite;

/// <summary>A deliberately small, read-only SQL dialect for the current sheet.</summary>
internal static class SqlQueryEngine
{
    public static SqlQueryResult Execute(SheetModel sheet, string sql, SqlQueryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        options ??= new SqlQueryOptions();
        try
        {
            var query = new Parser(sql).Parse();
            var sourceColumnCount = UsedColumnCount(sheet);
            var resolver = new ColumnResolver(sheet, options.FirstRowIsHeader, sourceColumnCount);
            var projections = query.AllColumns
                ? Enumerable.Range(0, sourceColumnCount).ToArray()
                : query.Columns.Select(resolver.Resolve).ToArray();
            var headers = projections.Select(resolver.DisplayName).ToArray();
            var firstDataRow = options.FirstRowIsHeader && sheet.Rows.Count > 0 ? 1 : 0;
            int usedRowCount = UsedRowCount(sheet, firstDataRow);
            var evaluationContext = FormulaEngine.CreateContext(sheet);
            IEnumerable<(List<CellModel> Row, int Index)> rows = sheet.Rows.Skip(firstDataRow).Take(Math.Max(0, usedRowCount - firstDataRow))
                .Select((row, offset) => (row, offset + firstDataRow));

            if (query.Condition is { } condition)
            {
                var conditionColumn = resolver.Resolve(condition.Column);
                rows = rows.Where(item => Matches(EvaluatedValueAt(evaluationContext, item.Row, item.Index, conditionColumn), condition.Operator, condition.Value));
            }
            if (query.Order is { } order)
            {
                var orderColumn = resolver.Resolve(order.Column);
                rows = order.Descending
                    ? rows.OrderByDescending(item => EvaluatedValueAt(evaluationContext, item.Row, item.Index, orderColumn), CellValueComparer.Instance).ThenBy(item => item.Index)
                    : rows.OrderBy(item => EvaluatedValueAt(evaluationContext, item.Row, item.Index, orderColumn), CellValueComparer.Instance).ThenBy(item => item.Index);
            }
            if (query.Limit is { } limit) rows = rows.Take(limit);

            var output = rows.Select(item => projections.Select(column =>
            {
                if (column >= item.Row.Count) return new CellModel();
                var cell = item.Row[column].Clone();
                cell.Value = EvaluatedValueAt(evaluationContext, item.Row, item.Index, column);
                return cell;
            }).ToList()).ToList();
            return new SqlQueryResult(true, headers, output, null);
        }
        catch (QueryException ex)
        {
            return new SqlQueryResult(false, [], [], ex.Message);
        }
    }

    private static int UsedColumnCount(SheetModel sheet)
    {
        int last = -1;
        foreach (var row in sheet.Rows)
            for (int column = row.Count - 1; column > last; column--)
                if (!string.IsNullOrWhiteSpace(row[column].Value)) { last = column; break; }
        return Math.Max(1, last + 1);
    }

    private static int UsedRowCount(SheetModel sheet, int minimum)
    {
        int count = sheet.Rows.Count;
        while (count > minimum && sheet.Rows[count - 1].All(cell => string.IsNullOrWhiteSpace(cell.Value))) count--;
        return count;
    }

    private static string EvaluatedValueAt(FormulaEngine.FormulaEvaluationContext context, List<CellModel> row, int rowIndex, int column)
    {
        if (column >= row.Count) return "";
        var raw = row[column].Value;
        if (!raw.TrimStart().StartsWith('=')) return raw;
        var result = context.Evaluate(rowIndex, column);
        return result.Success ? result.Value : "#ERROR!";
    }

    private static bool Matches(string left, string op, string right)
    {
        if (op == "CONTAINS") return left.Contains(right, StringComparison.OrdinalIgnoreCase);
        var comparison = CellValueComparer.Instance.Compare(left, right);
        return op switch
        {
            "=" => comparison == 0,
            "!=" or "<>" => comparison != 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            _ => false
        };
    }

    private sealed class ColumnResolver(SheetModel sheet, bool firstRowIsHeader, int sourceColumnCount)
    {
        public int Resolve(string name)
        {
            var normalized = name.Trim();
            if (normalized.StartsWith('[') && normalized.EndsWith(']')) normalized = normalized[1..^1];
            if (firstRowIsHeader && sheet.Rows.Count > 0)
            {
                var index = sheet.Rows[0].FindIndex(cell => cell.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) return index;
            }
            if (normalized.StartsWith("Column", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(normalized[6..].Trim(), out var ordinal) && ordinal > 0) return Check(ordinal - 1, name);
            if ((normalized.StartsWith('c') || normalized.StartsWith('C')) &&
                int.TryParse(normalized[1..], out ordinal) && ordinal > 0) return Check(ordinal - 1, name);
            if (normalized.All(char.IsLetter) && normalized.Length > 0)
            {
                long column = 0;
                foreach (var ch in normalized.ToUpperInvariant()) column = column * 26 + ch - 'A' + 1;
                if (column <= int.MaxValue) return Check((int)column - 1, name);
            }
            throw new QueryException($"Unknown column '{name}'. Use A, c1, Column1, or a header name.");
        }

        public string DisplayName(int column)
        {
            if (firstRowIsHeader && sheet.Rows.Count > 0 && column < sheet.Rows[0].Count && sheet.Rows[0][column].Value.Length > 0)
                return sheet.Rows[0][column].Value;
            return $"c{column + 1}";
        }

        private int Check(int column, string name)
        {
            if (column >= 0 && column < sourceColumnCount) return column;
            throw new QueryException($"Column '{name}' is outside the sheet.");
        }
    }

    private sealed class CellValueComparer : IComparer<string>
    {
        public static readonly CellValueComparer Instance = new();
        public int Compare(string? x, string? y)
        {
            x ??= ""; y ??= "";
            if (decimal.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var nx) &&
                decimal.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var ny)) return nx.CompareTo(ny);
            if (DateTime.TryParse(x, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dx) &&
                DateTime.TryParse(y, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dy)) return dx.CompareTo(dy);
            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }

    private sealed class Parser(string sql)
    {
        private readonly Lexer lexer = new(sql);
        private Token current;

        public Query Parse()
        {
            current = lexer.Next();
            Require("SELECT");
            var columns = new List<string>();
            var all = Take("*");
            if (!all)
            {
                do { columns.Add(Identifier()); } while (Take(","));
                if (columns.Count == 0) throw Error("SELECT requires a column or *");
            }

            // The source is already supplied by the caller. FROM is accepted for console-friendly queries and ignored.
            if (Take("FROM"))
            {
                if (current.Kind == TokenKind.End) throw Error("FROM requires a source name");
                current = lexer.Next();
            }

            Condition? condition = null;
            if (Take("WHERE"))
            {
                var column = Identifier();
                var op = current.Text.ToUpperInvariant();
                if (op is not ("=" or "!=" or "<>" or "<" or "<=" or ">" or ">=" or "CONTAINS")) throw Error("Unsupported WHERE operator");
                current = lexer.Next();
                condition = new Condition(column, Scalar());
                condition = condition with { Operator = op };
            }

            Order? order = null;
            if (Take("ORDER"))
            {
                Require("BY");
                var column = Identifier();
                var descending = Take("DESC");
                if (!descending) Take("ASC");
                order = new Order(column, descending);
            }

            int? limit = null;
            if (Take("LIMIT"))
            {
                if (!int.TryParse(current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0)
                    throw Error("LIMIT requires a non-negative integer");
                limit = count;
                current = lexer.Next();
            }
            Take(";");
            if (current.Kind != TokenKind.End) throw Error($"Unexpected token '{current.Text}'");
            return new Query(all, columns, condition, order, limit);
        }

        private string Identifier()
        {
            if (current.Kind is not (TokenKind.Word or TokenKind.QuotedIdentifier)) throw Error("Expected a column name");
            var value = current.Text;
            current = lexer.Next();
            return value;
        }

        private string Scalar()
        {
            if (current.Kind is not (TokenKind.Word or TokenKind.String or TokenKind.QuotedIdentifier)) throw Error("Expected a comparison value");
            var value = current.Text;
            current = lexer.Next();
            return value;
        }

        private bool Take(string text)
        {
            if (!current.Text.Equals(text, StringComparison.OrdinalIgnoreCase)) return false;
            current = lexer.Next();
            return true;
        }

        private void Require(string text) { if (!Take(text)) throw Error($"Expected {text}"); }
        private QueryException Error(string message) => new($"{message} at position {current.Position + 1}.");
    }

    private sealed class Lexer(string source)
    {
        private int position;
        public Token Next()
        {
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
            var start = position;
            if (position >= source.Length) return new(TokenKind.End, "", position);
            var ch = source[position++];
            if (ch == '\'') return Quoted(TokenKind.String, '\'', start);
            if (ch == '"') return Quoted(TokenKind.QuotedIdentifier, '"', start);
            if (ch == '[')
            {
                var end = source.IndexOf(']', position);
                if (end < 0) throw new QueryException($"Unterminated column name at position {start + 1}.");
                var text = source[position..end]; position = end + 1;
                return new(TokenKind.QuotedIdentifier, text, start);
            }
            if (",*;".Contains(ch)) return new(TokenKind.Symbol, ch.ToString(), start);
            if ("=<>!".Contains(ch))
            {
                if (position < source.Length && (source[position] == '=' || ch == '<' && source[position] == '>')) position++;
                return new(TokenKind.Symbol, source[start..position], start);
            }
            while (position < source.Length && !char.IsWhiteSpace(source[position]) && !",*;=<>!".Contains(source[position])) position++;
            return new(TokenKind.Word, source[start..position], start);
        }

        private Token Quoted(TokenKind kind, char quote, int start)
        {
            var value = new System.Text.StringBuilder();
            while (position < source.Length)
            {
                var ch = source[position++];
                if (ch == quote)
                {
                    if (position < source.Length && source[position] == quote) { value.Append(quote); position++; continue; }
                    return new(kind, value.ToString(), start);
                }
                value.Append(ch);
            }
            throw new QueryException($"Unterminated quoted value at position {start + 1}.");
        }
    }

    private enum TokenKind { End, Word, String, QuotedIdentifier, Symbol }
    private readonly record struct Token(TokenKind Kind, string Text, int Position);
    private sealed record Query(bool AllColumns, IReadOnlyList<string> Columns, Condition? Condition, Order? Order, int? Limit);
    private sealed record Condition(string Column, string Value) { public string Operator { get; init; } = "="; }
    private sealed record Order(string Column, bool Descending);
    private sealed class QueryException(string message) : Exception(message);
}

internal sealed record SqlQueryOptions
{
    public bool FirstRowIsHeader { get; init; }
}

internal sealed record SqlQueryResult(
    bool Success,
    IReadOnlyList<string> Columns,
    IReadOnlyList<List<CellModel>> Rows,
    string? Error)
{
    public SheetModel ToSheetModel(bool includeHeader = true)
    {
        var sheet = new SheetModel();
        if (includeHeader) sheet.Rows.Add(Columns.Select(value => new CellModel { Value = value }).ToList());
        foreach (var row in Rows) sheet.Rows.Add(row.Select(cell => cell.Clone()).ToList());
        return sheet;
    }
}
