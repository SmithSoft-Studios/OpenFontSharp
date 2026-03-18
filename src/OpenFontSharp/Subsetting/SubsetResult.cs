namespace OpenFontSharp.Subsetting;

/// <summary>
/// The result of font subsetting — contains the trimmed TTF binary and glyph ID remapping.
/// </summary>
/// <param name="FontData">The subsetted TTF binary.</param>
/// <param name="GlyphIdMap">Old glyph ID → new glyph ID mapping.</param>
/// <param name="RetainedGlyphCount">Number of glyphs in the subset (including .notdef).</param>
/// <param name="OriginalGlyphCount">Number of glyphs in the original font.</param>
/// <param name="SubsetPrefix">6-character prefix (e.g., "ABCDEF+") per PDF specification.</param>
public record SubsetResult(
    byte[] FontData,
    Dictionary<ushort, ushort> GlyphIdMap,
    int RetainedGlyphCount,
    int OriginalGlyphCount,
    string SubsetPrefix);
