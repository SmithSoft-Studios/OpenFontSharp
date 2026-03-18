namespace OpenFontSharp;

/// <summary>
/// A kerning adjustment between two glyphs.
/// </summary>
/// <param name="LeftGlyphId">Left glyph in the pair.</param>
/// <param name="RightGlyphId">Right glyph in the pair.</param>
/// <param name="Value">Kerning adjustment in font design units (negative = tighter).</param>
/// <param name="Source">Whether the kerning came from the kern table or GPOS.</param>
public readonly record struct KerningPair(
    ushort LeftGlyphId,
    ushort RightGlyphId,
    short Value,
    KerningSource Source);

/// <summary>
/// The source of a kerning value.
/// </summary>
public enum KerningSource
{
    /// <summary>From the legacy kern table.</summary>
    KernTable,

    /// <summary>From GPOS pair adjustment lookups.</summary>
    GPOS
}
