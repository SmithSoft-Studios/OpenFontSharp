namespace OpenFontSharp.Metrics;

/// <summary>
/// Consolidated font metadata — family name, style, metrics, and per-glyph widths.
/// </summary>
/// <param name="FamilyName">Font family name from the name table.</param>
/// <param name="SubFamily">Sub-family/style name (e.g., "Regular", "Bold").</param>
/// <param name="Ascent">Ascender in font design units.</param>
/// <param name="Descent">Descender in font design units (typically negative).</param>
/// <param name="CapHeight">Capital letter height in font design units.</param>
/// <param name="XHeight">Lowercase 'x' height in font design units.</param>
/// <param name="UnitsPerEm">Design units per em square (typically 1000 or 2048).</param>
/// <param name="GlyphWidths">Advance width per glyph ID in font design units.</param>
/// <param name="GlyphCount">Total number of glyphs in the font.</param>
/// <param name="FontData">Raw TTF/OTF binary data.</param>
public record FontInfo(
    string FamilyName,
    string SubFamily,
    int Ascent,
    int Descent,
    int CapHeight,
    int XHeight,
    int UnitsPerEm,
    ushort[] GlyphWidths,
    int GlyphCount,
    byte[] FontData);
