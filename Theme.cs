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
    public static readonly Color SelectedHeader = ColorTranslator.FromHtml("#2A2538");
    public static readonly Color Background = Surface;
    public static readonly Color CurrentLine = ColorTranslator.FromHtml("#383B4A");
    public static readonly Color Foreground = ColorTranslator.FromHtml("#F8F8F2");
    public static readonly Color Comment = ColorTranslator.FromHtml("#8A91A3");
    public static readonly Color Purple = ColorTranslator.FromHtml("#BD93F9");

    /// <summary>Returns whichever neutral text color has the stronger WCAG contrast against <paramref name="background"/>.</summary>
    public static Color AdaptiveCellText(Color background)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255D;
            return value <= 0.04045D ? value / 12.92D : Math.Pow((value + 0.055D) / 1.055D, 2.4D);
        }

        double luminance = 0.2126D * Linear(background.R) + 0.7152D * Linear(background.G) + 0.0722D * Linear(background.B);
        double whiteContrast = 1.05D / (luminance + 0.05D);
        double blackContrast = (luminance + 0.05D) / 0.05D;
        return blackContrast >= whiteContrast ? Color.Black : Theme.Foreground;
    }
}
