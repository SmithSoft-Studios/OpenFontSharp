//Apache2, 2016-present, WinterDev


namespace OpenFontSharp.Tables.AdvancedLayout
{
    /// <summary>
    /// replaceable glyph index list
    /// </summary>
    public interface IGlyphIndexList
    {
        /// <summary>Gets the number of glyph indices in the list.</summary>
        int Count { get; }
        /// <summary>Gets the glyph index at the specified position.</summary>
        /// <param name="index">The zero-based position.</param>
        /// <returns>The glyph index at that position.</returns>
        ushort this[int index] { get; }

        /// <summary>
        /// remove:add_new 1:1
        /// </summary>
        /// <param name="index">The zero-based position to replace.</param>
        /// <param name="newGlyphIndex">The glyph index to insert.</param>
        void Replace(int index, ushort newGlyphIndex);
        /// <summary>
        /// remove:add_new >=1:1
        /// </summary>
        /// <param name="index">The zero-based position at which to replace glyphs.</param>
        /// <param name="removeLen">The number of glyphs to remove.</param>
        /// <param name="newGlyphIndex">The glyph index to insert.</param>
        void Replace(int index, int removeLen, ushort newGlyphIndex);
        /// <summary>
        /// remove: add_new 1:>=1
        /// </summary>
        /// <param name="index">The zero-based position at which to insert glyphs.</param>
        /// <param name="newGlyphIndices">The glyph indices to insert.</param>
        void Replace(int index, ushort[] newGlyphIndices);
    }

}
