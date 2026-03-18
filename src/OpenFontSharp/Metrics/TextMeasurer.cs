namespace OpenFontSharp.Metrics;

/// <summary>
/// Measures text width using font metrics (per-glyph advance widths).
/// Works with <see cref="Typeface"/> or <see cref="FontInfo"/> for custom fonts.
/// </summary>
public static class TextMeasurer
{
    /// <summary>
    /// Measures the width of text using a Typeface at the given font size.
    /// Returns width in the same units as fontSize (typically points).
    /// </summary>
    /// <param name="typeface">The font to measure with.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="fontSize">Font size (typically in points).</param>
    /// <returns>Text width in the same units as fontSize.</returns>
    public static double MeasureWidth(Typeface typeface, string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        double totalWidth = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            ushort glyphIndex = typeface.GetGlyphIndex(rune.Value);
            totalWidth += typeface.GetAdvanceWidthFromGlyphIndex(glyphIndex);
        }

        return totalWidth * fontSize / typeface.UnitsPerEm;
    }

    /// <summary>
    /// Measures text width using a FontInfo record.
    /// </summary>
    /// <param name="fontInfo">Font metadata with glyph widths.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="fontSize">Font size (typically in points).</param>
    /// <returns>Text width in the same units as fontSize.</returns>
    public static double MeasureWidth(FontInfo fontInfo, string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        double totalWidth = 0;
        foreach (char c in text)
        {
            int index = c;
            if (index >= 0 && index < fontInfo.GlyphWidths.Length)
                totalWidth += fontInfo.GlyphWidths[index];
            else if (fontInfo.GlyphWidths.Length > 0)
                totalWidth += fontInfo.GlyphWidths[0]; // Fallback
        }

        return totalWidth * fontSize / fontInfo.UnitsPerEm;
    }
}
