using System.Text;

namespace SheetLite;

internal static class CsvCodec
{
    public static SheetModel Load(string path)
    {
        string text = File.ReadAllText(path, DetectEncoding(path));
        char delimiter = DetectDelimiter(text);
        var sheet = new SheetModel { CsvDelimiter = delimiter, CsvUtf8Bom = HasUtf8Bom(path) };
        foreach (var values in ParseRows(text, delimiter)) sheet.Rows.Add(values.Select(value => new CellModel { Value = value }).ToList());
        if (sheet.Rows.Count == 0) sheet.EnsureSize(100, 26);
        return sheet;
    }

    internal static List<List<string>> ParseRows(string text, char? requestedDelimiter = null)
    {
        char delimiter = requestedDelimiter ?? DetectDelimiter(text);
        var rows = new List<List<string>>();
        var row = new List<CellModel>();
        var field = new StringBuilder();
        bool quoted = false, rowTouched = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                rowTouched = true;
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
            }
            else if (ch == '"' && field.Length == 0) { quoted = true; rowTouched = true; }
            else if (ch == delimiter) { rowTouched = true; row.Add(new() { Value = field.ToString() }); field.Clear(); }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(new() { Value = field.ToString() }); field.Clear();
                rows.Add(row.Select(cell => cell.Value).ToList()); row = []; rowTouched = false;
            }
            else { rowTouched = true; field.Append(ch); }
        }
        if (rowTouched || field.Length > 0 || row.Count > 0) { row.Add(new() { Value = field.ToString() }); rows.Add(row.Select(cell => cell.Value).ToList()); }
        return rows;
    }

    public static void Save(string path, SheetModel sheet)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(sheet.CsvUtf8Bom));
        int lastRow = sheet.Rows.FindLastIndex(r => r.Any(c => c.Value.Length > 0));
        if (lastRow < 0) return;
        int lastCol = sheet.Rows.Take(lastRow + 1).Max(r => r.FindLastIndex(c => c.Value.Length > 0)) + 1;
        for (int r = 0; r <= lastRow; r++)
        {
            var values = Enumerable.Range(0, lastCol).Select(c => Escape(c < sheet.Rows[r].Count ? sheet.Rows[r][c].Value : "", sheet.CsvDelimiter));
            writer.WriteLine(string.Join(sheet.CsvDelimiter, values));
        }
    }

    internal static string Escape(string value, char delimiter = ',') => value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    private static Encoding DetectEncoding(string path)
    {
        return HasUtf8Bom(path) ? new UTF8Encoding(true) : new UTF8Encoding(false);
    }
    private static bool HasUtf8Bom(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> bytes = stackalloc byte[3];
        int count = stream.Read(bytes);
        return count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    }
    private static char DetectDelimiter(string text)
    {
        char[] candidates = [',', '\t', ';', '|'];
        var counts = candidates.ToDictionary(candidate => candidate, _ => new List<int>()); int[] current = new int[candidates.Length]; bool quoted = false, rowTouched = false; int rows = 0;
        for (int index = 0; index < text.Length && rows < 10; index++)
        {
            char character = text[index];
            if (quoted)
            {
                rowTouched = true;
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"') index++;
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"') { quoted = true; rowTouched = true; continue; }
            int candidateIndex = Array.IndexOf(candidates, character); if (candidateIndex >= 0) { current[candidateIndex]++; rowTouched = true; continue; }
            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                for (int candidate = 0; candidate < candidates.Length; candidate++) { counts[candidates[candidate]].Add(current[candidate]); current[candidate] = 0; }
                rows++; rowTouched = false; continue;
            }
            rowTouched = true;
        }
        if (rowTouched || current.Any(value => value > 0)) for (int candidate = 0; candidate < candidates.Length; candidate++) counts[candidates[candidate]].Add(current[candidate]);
        return candidates
            .OrderByDescending(candidate => counts[candidate].Count(value => value > 0))
            .ThenByDescending(candidate => counts[candidate].Where(value => value > 0).GroupBy(value => value).Select(group => group.Count()).DefaultIfEmpty(0).Max())
            .ThenByDescending(candidate => counts[candidate].Sum())
            .First();
    }
}
