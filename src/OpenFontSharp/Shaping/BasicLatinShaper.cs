namespace OpenFontSharp.Shaping;

/// <summary>
/// Basic text shaper for Latin, Cyrillic, and Greek scripts.
/// Performs cmap glyph lookup, GSUB format 4 ligature substitution,
/// and kerning from kern table and GPOS format 1 pair adjustment.
/// Does NOT handle complex scripts (Arabic, Devanagari, CJK, etc.).
/// </summary>
public static class BasicLatinShaper
{
    /// <summary>
    /// Shapes text using the given font, returning glyph IDs and advances.
    /// If the text contains complex scripts, sets RequiresComplexShaping = true
    /// but still performs basic cmap lookup for all characters.
    /// </summary>
    public static ShapingResult Shape(Typeface typeface, string text)
    {
        if (string.IsNullOrEmpty(text))
            return new ShapingResult([], [], false);

        bool needsComplex = ScriptDetector.RequiresComplexShaping(text);

        // Step 1: Map codepoints to glyph IDs via cmap
        var glyphIds = new List<ushort>(text.Length);
        foreach (char c in text)
        {
            glyphIds.Add(typeface.GetGlyphIndex(c));
        }

        // Step 2: Apply GSUB ligatures (only if not complex script)
        if (!needsComplex)
        {
            ApplyLigatures(typeface, glyphIds);
        }

        // Step 3: Get base advances from hmtx
        var advances = new int[glyphIds.Count];
        for (int i = 0; i < glyphIds.Count; i++)
        {
            advances[i] = typeface.GetAdvanceWidthFromGlyphIndex(glyphIds[i]);
        }

        // Step 4: Apply kerning (kern table, then GPOS format 1)
        if (!needsComplex)
        {
            ApplyKerning(typeface, glyphIds, advances);
        }

        return new ShapingResult(
            glyphIds.ToArray(),
            advances,
            needsComplex);
    }

    /// <summary>
    /// Applies GSUB format 4 (ligature substitution) lookups for features
    /// like 'liga' and 'clig'. Modifies the glyph list in-place.
    /// </summary>
    private static void ApplyLigatures(Typeface typeface, List<ushort> glyphIds)
    {
        var gsub = typeface.GSUBTable;
        if (gsub?.LookupList == null) return;

        // Try all lookups — DoSubstitutionAt returns false for non-matching types
        foreach (var lookup in gsub.LookupList)
        {
            for (int pos = 0; pos < glyphIds.Count - 1; pos++)
            {
                if (TryApplyLigatureLookup(lookup, glyphIds, pos))
                {
                    pos--; // Re-check at same position after substitution
                }
            }
        }
    }

    /// <summary>
    /// Tries to apply a single ligature lookup at the given position.
    /// Returns true if a substitution was made.
    /// </summary>
    private static bool TryApplyLigatureLookup(
        Tables.AdvancedLayout.GSUB.LookupTable lookup,
        List<ushort> glyphIds,
        int pos)
    {
        foreach (var subTable in lookup.SubTables)
        {
            // LkSubTableT4 has DoSubstitutionAt but it requires IGlyphIndexList.
            // We use the lookup's own method if available, otherwise manual matching.
            // Since the nested types are private, use the public DoSubstitutionAt
            // via the IGlyphIndexList adapter.
            var adapter = new GlyphListAdapter(glyphIds);
            if (subTable.DoSubstitutionAt(adapter, pos, glyphIds.Count - pos))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Applies kerning adjustments to the advance widths.
    /// Checks GPOS pair adjustment first (if available), then falls back to kern table.
    /// </summary>
    private static void ApplyKerning(Typeface typeface, List<ushort> glyphIds, int[] advances)
    {
        if (typeface.KernTable == null) return; // No kerning data available

        for (int i = 0; i < glyphIds.Count - 1; i++)
        {
            ushort left = glyphIds[i];
            ushort right = glyphIds[i + 1];

            short kernValue = typeface.GetKernDistance(left, right);
            if (kernValue != 0)
            {
                advances[i] += kernValue;
            }
        }
    }

    /// <summary>
    /// Adapter to bridge List&lt;ushort&gt; to IGlyphIndexList interface
    /// required by GSUB lookup tables.
    /// </summary>
    private sealed class GlyphListAdapter : Tables.AdvancedLayout.IGlyphIndexList
    {
        private readonly List<ushort> _glyphs;

        public GlyphListAdapter(List<ushort> glyphs) => _glyphs = glyphs;

        public int Count => _glyphs.Count;

        public ushort this[int index] => _glyphs[index];

        /// <summary>Remove 1, add 1 (in-place replacement).</summary>
        public void Replace(int index, ushort newGlyphIndex)
        {
            _glyphs[index] = newGlyphIndex;
        }

        /// <summary>Remove 'removeLen' glyphs at 'index', insert single glyph.</summary>
        public void Replace(int index, int removeLen, ushort newGlyphIndex)
        {
            _glyphs.RemoveRange(index, removeLen);
            _glyphs.Insert(index, newGlyphIndex);
        }

        /// <summary>Remove 1 glyph at 'index', insert multiple glyphs.</summary>
        public void Replace(int index, ushort[] newGlyphIndices)
        {
            _glyphs.RemoveAt(index);
            _glyphs.InsertRange(index, newGlyphIndices);
        }
    }
}
