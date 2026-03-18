namespace OpenFontSharp.Metrics;

/// <summary>
/// Loads TTF/OTF fonts and extracts consolidated metrics.
/// Returns <see cref="FontInfo"/> with all data needed for font embedding and text measurement.
/// </summary>
public static class FontLoader
{
    /// <summary>
    /// Loads a font from a file path and extracts metrics.
    /// </summary>
    /// <param name="filePath">Path to the TTF/OTF font file.</param>
    /// <returns>Consolidated font metadata with metrics and glyph widths.</returns>
    public static FontInfo LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Font file not found: {filePath}", filePath);

        var fontData = File.ReadAllBytes(filePath);
        return LoadFromBytes(fontData);
    }

    /// <summary>
    /// Loads a font from raw bytes and extracts metrics.
    /// </summary>
    /// <param name="fontData">Raw TTF/OTF font bytes.</param>
    /// <returns>Consolidated font metadata with metrics and glyph widths.</returns>
    public static FontInfo LoadFromBytes(byte[] fontData)
    {
        using var ms = new MemoryStream(fontData);
        var reader = new OpenFontReader();
        var typeface = reader.Read(ms);

        if (typeface is null)
            throw new InvalidOperationException("Failed to read font from byte array.");

        return ExtractFontInfo(typeface, fontData);
    }

    /// <summary>
    /// Extracts <see cref="FontInfo"/> from an already-parsed <see cref="Typeface"/>.
    /// </summary>
    /// <param name="typeface">The parsed font.</param>
    /// <param name="fontData">The raw font bytes (stored in FontInfo for embedding).</param>
    /// <returns>Consolidated font metadata.</returns>
    public static FontInfo FromTypeface(Typeface typeface, byte[] fontData)
    {
        return ExtractFontInfo(typeface, fontData);
    }

    private static FontInfo ExtractFontInfo(Typeface typeface, byte[] fontData)
    {
        var glyphCount = typeface.GlyphCount;
        var widths = new ushort[glyphCount];
        for (ushort i = 0; i < glyphCount; i++)
        {
            widths[i] = typeface.GetAdvanceWidthFromGlyphIndex(i);
        }

        return new FontInfo(
            FamilyName: typeface.Name ?? "Unknown",
            SubFamily: typeface.FontSubFamily ?? "Regular",
            Ascent: typeface.Ascender,
            Descent: typeface.Descender,
            CapHeight: typeface.OS2Table?.sCapHeight ?? typeface.Ascender,
            XHeight: 0, // OS2Table.sXHeight not directly exposed
            UnitsPerEm: typeface.UnitsPerEm,
            GlyphWidths: widths,
            GlyphCount: glyphCount,
            FontData: fontData);
    }
}
