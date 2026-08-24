namespace SheetLite;

internal static class Theme
{
    // A restrained Dracula-derived chrome with one purple interaction accent.
    public static readonly Color Surface = ColorTranslator.FromHtml("#1F222A");
    public static readonly Color Inset = ColorTranslator.FromHtml("#191B22");
    public static readonly Color HeaderBackground = Inset;
    public static readonly Color CellBackground = ColorTranslator.FromHtml("#171717");
    public static readonly Color Hover = ColorTranslator.FromHtml("#343746");
    public static readonly Color Selection = ColorTranslator.FromHtml("#3A3153");
    public static readonly Color Background = Surface;
    public static readonly Color CurrentLine = ColorTranslator.FromHtml("#383B4A");
    public static readonly Color Foreground = ColorTranslator.FromHtml("#F8F8F2");
    public static readonly Color Comment = ColorTranslator.FromHtml("#8A91A3");
    public static readonly Color Purple = ColorTranslator.FromHtml("#BD93F9");
}
