namespace SheetLite;

/// <summary>A zero-based grid-independent cell coordinate.</summary>
internal readonly record struct CellAddress(int Row, int Column) : IComparable<CellAddress>
{
    public static bool operator <(CellAddress left, CellAddress right) => left.Row < right.Row || (left.Row == right.Row && left.Column < right.Column);
    public static bool operator >(CellAddress left, CellAddress right) => right < left;
    public int CompareTo(CellAddress other) => this < other ? -1 : other < this ? 1 : 0;
    public CellAddress Offset(int rows, int columns) => new(Row + rows, Column + columns);
    public bool IsInside(CellRange range) => range.Contains(this);
    public override string ToString() => $"{ColumnName(Column)}{Row + 1}";

    public static string ColumnName(int index)
    {
        string s = "";
        for (int n = index + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }
}

/// <summary>A rectangular, normalized (Left&lt;=Right, Top&lt;=Bottom) block of cells in model coordinates.</summary>
internal readonly record struct CellRange
{
    public CellRange(int left, int top, int right, int bottom) : this()
    {
        Left = Math.Min(left, right); Right = Math.Max(left, right);
        Top = Math.Min(top, bottom); Bottom = Math.Max(top, bottom);
    }

    public int Left { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }
    public int Width => Right - Left + 1;
    public int Height => Bottom - Top + 1;
    public CellAddress Start => new(Top, Left);
    public CellAddress End => new(Bottom, Right);
    public IEnumerable<int> Rows => Enumerable.Range(Top, Height);
    public IEnumerable<int> Columns => Enumerable.Range(Left, Width);

    public bool Contains(CellAddress address) => address.Row >= Top && address.Row <= Bottom && address.Column >= Left && address.Column <= Right;
    public bool Contains(int row, int column) => row >= Top && row <= Bottom && column >= Left && column <= Right;
    public bool Intersects(CellRange other) => Left <= other.Right && other.Left <= Right && Top <= other.Bottom && other.Top <= Bottom;
    public CellRange Offset(int rows, int columns) => new(Left + columns, Top + rows, Right + columns, Bottom + rows);
    public bool EqualsSingleCell(CellAddress address) => Width == 1 && Height == 1 && Start == address;
    public override string ToString() => $"{Start}:{End}";

    public static CellRange FromSize(int topRow, int leftColumn, int height, int width)
        => new(leftColumn, topRow, leftColumn + width - 1, topRow + height - 1);
}

/// <summary>An intent to change one cell: value and/or formatting. Unset members leave the cell untouched.</summary>
internal readonly struct CellEdit
{
    public string? Value { get; init; }
    public Color? BackColor { get; init; }
    public Color? ForeColor { get; init; }
    public bool? Bold { get; init; }
    public bool ClearFormatting { get; init; }

    public static CellEdit SetValue(string value) => new() { Value = value };
    public static CellEdit ClearValue() => new() { Value = "" };
    public static CellEdit Format(Color? backColor = null, Color? foreColor = null, bool? bold = null) => new() { BackColor = backColor, ForeColor = foreColor, Bold = bold };
    public static CellEdit ResetFormatting() => new() { ClearFormatting = true };

    public void ApplyTo(CellModel cell)
    {
        if (Value is not null) cell.Value = Value;
        if (ClearFormatting) { cell.BackColor = null; cell.ForeColor = null; cell.Bold = false; return; }
        if (BackColor is not null) cell.BackColor = BackColor;
        if (ForeColor is not null) cell.ForeColor = ForeColor;
        if (Bold is not null) cell.Bold = Bold.Value;
    }
}

/// <summary>The fully resolved presentation of one cell: evaluated text plus effective styling.</summary>
internal readonly record struct CellDisplayValue(string Text, Color BackColor, Color ForeColor, bool Bold);
