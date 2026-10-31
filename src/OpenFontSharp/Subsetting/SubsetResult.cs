namespace OpenFontSharp.Subsetting;

/// <summary>
/// The result of font subsetting: a standalone TrueType or CFF font and glyph ID remapping.
/// </summary>
/// <param name="FontData">The standalone sfnt font binary.</param>
/// <param name="GlyphIdMap">Old glyph ID → new glyph ID mapping; empty when the font is returned whole and IDs are unchanged.</param>
/// <param name="RetainedGlyphCount">Number of glyphs in the subset (including .notdef).</param>
/// <param name="OriginalGlyphCount">Number of glyphs in the original font.</param>
/// <param name="SubsetPrefix">Six-letter tag plus a '+' separator (e.g., "ABCDEF+"), or empty for an unchanged font.</param>
public record SubsetResult(
    byte[] FontData,
    Dictionary<ushort, ushort> GlyphIdMap,
    int RetainedGlyphCount,
    int OriginalGlyphCount,
    string SubsetPrefix);
