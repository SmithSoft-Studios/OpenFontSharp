namespace OpenFontSharp.Shaping;

/// <summary>
/// The result of text shaping — contains glyph IDs, advances, and complex script indication.
/// </summary>
/// <param name="GlyphIds">Ordered glyph IDs after shaping.</param>
/// <param name="Advances">X-advance per glyph in font design units.</param>
/// <param name="RequiresComplexShaping">True if the input text contains scripts that need HarfBuzzSharp.</param>
public record ShapingResult(
    ushort[] GlyphIds,
    int[] Advances,
    bool RequiresComplexShaping);
