namespace OpenFontSharp.Shaping;

/// <summary>
/// Basic text shaper for Latin, Cyrillic, and Greek scripts.
/// Performs UTF-16 character cmap lookup, available GSUB substitutions,
/// and legacy kern-table adjustments by default. GPOS can be enabled explicitly.
/// Does NOT handle complex scripts (Arabic, Devanagari, CJK, etc.).
/// </summary>
public static class BasicLatinShaper
{
    private const uint KernFeatureTag = 0x6B65726E;
    private const uint MarkFeatureTag = 0x6D61726B;

    /// <summary>
    /// Shapes text using the given font, returning glyph IDs and advances.
    /// If the text contains complex scripts, sets RequiresComplexShaping = true
    /// but still performs basic cmap lookup for all characters.
    /// </summary>
    /// <param name="typeface">The original parsed font, before subsetting.</param>
    /// <param name="text">The UTF-16 text to shape.</param>
    /// <returns>Glyph IDs, advances in font design units, and a complex-shaping warning flag.</returns>
    /// <remarks>Does not provide bidi layout, supplementary-plane mapping, or source clusters for ligatures.</remarks>
    public static ShapingResult Shape(Typeface typeface, string text) =>
        Shape(typeface, text, new ShapingOptions());

    /// <summary>
    /// Shapes text with optional GPOS positioning for the 'kern' and 'mark' features.
    /// Complex scripts, bidirectional layout, and Indic reordering remain out of scope.
    /// </summary>
    /// <param name="typeface">The original parsed font, before subsetting.</param>
    /// <param name="text">The UTF-16 text to shape.</param>
    /// <param name="options">Options controlling optional GPOS positioning.</param>
    /// <returns>Glyph IDs, advances and offsets in font design units, and a complex-shaping warning flag.</returns>
    public static ShapingResult Shape(Typeface typeface, string text, ShapingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
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

        var positionedGlyphs = new PositionedGlyphs(typeface, glyphIds, advances);

        // Step 4: Apply optional GPOS positioning and legacy kerning.
        if (!needsComplex)
        {
            if (options.EnableGposPositioning)
            {
                bool hasGposKern = ApplyGposFeature(typeface, positionedGlyphs, KernFeatureTag, 2);
                if (!hasGposKern)
                    ApplyLegacyKerning(typeface, glyphIds, advances);

                ApplyGposFeature(typeface, positionedGlyphs, MarkFeatureTag, 4);
            }
            else
            {
                ApplyLegacyKerning(typeface, glyphIds, advances);
            }
        }

        return new ShapingResult(
            glyphIds.ToArray(),
            advances,
            needsComplex)
        {
            XOffsets = positionedGlyphs.XOffsets,
            YOffsets = positionedGlyphs.YOffsets
        };
    }

    private const uint LigaFeatureTag = 0x6C696761; // 'liga'
    private const uint CligFeatureTag = 0x636C6967; // 'clig'

    /// <summary>
    /// Applies GSUB format 4 (ligature substitution) lookups for features
    /// like 'liga' and 'clig'. Modifies the glyph list in-place.
    /// </summary>
    private static void ApplyLigatures(Typeface typeface, List<ushort> glyphIds)
    {
        var gsub = typeface.GSUBTable;
        if (gsub?.LookupList == null) return;

        // Only lookups that belong to the 'liga' or 'clig' features may run. Applying every lookup in the table
        // would also run alternate, contextual and locale substitutions and replace ordinary letters.
        var ligatureLookups = new HashSet<int>();
        var featureTables = gsub.FeatureList?.featureTables;
        if (featureTables == null) return;
        foreach (var feature in featureTables)
        {
            if (feature.FeatureTag == LigaFeatureTag || feature.FeatureTag == CligFeatureTag)
            {
                foreach (ushort index in feature.LookupListIndices)
                    ligatureLookups.Add(index);
            }
        }

        for (int lookupIndex = 0; lookupIndex < gsub.LookupList.Count; lookupIndex++)
        {
            if (!ligatureLookups.Contains(lookupIndex)) continue;
            var lookup = gsub.LookupList[lookupIndex];
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
    /// Applies legacy kern-table adjustments to the advance widths.
    /// </summary>
    private static void ApplyLegacyKerning(Typeface typeface, List<ushort> glyphIds, int[] advances)
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

    private static bool ApplyGposFeature(
        Typeface typeface,
        PositionedGlyphs glyphs,
        uint featureTag,
        ushort lookupType)
    {
        var gpos = typeface.GPOSTable;
        var featureTables = gpos?.FeatureList?.featureTables;
        if (gpos == null || featureTables == null)
            return false;

        var lookupIndices = new HashSet<ushort>();
        foreach (var feature in featureTables)
        {
            if (feature.FeatureTag == featureTag)
            {
                foreach (ushort index in feature.LookupListIndices)
                    lookupIndices.Add(index);
            }
        }

        bool appliedFeature = false;
        for (int i = 0; i < gpos.LookupList.Count; i++)
        {
            if (i > ushort.MaxValue || !lookupIndices.Contains((ushort)i))
                continue;

            var lookup = gpos.LookupList[i];
            // Type 9 is an extension lookup: the reader already resolved it to the wrapped subtables, so large
            // fonts that wrap their kerning and mark lookups in extensions are handled too.
            if ((lookup.LookupType != lookupType && lookup.LookupType != 9) ||
                !lookup.SubTables.Any(subTable => subTable is not Tables.AdvancedLayout.GPOS.UnImplementedLookupSubTable))
            {
                continue;
            }

            lookup.DoGlyphPosition(glyphs, 0, glyphs.Count);
            appliedFeature = true;
        }

        return appliedFeature;
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

    private sealed class PositionedGlyphs : IGlyphPositions
    {
        private readonly Typeface _typeface;
        private readonly List<ushort> _glyphIds;
        private readonly int[] _advances;

        public PositionedGlyphs(Typeface typeface, List<ushort> glyphIds, int[] advances)
        {
            _typeface = typeface;
            _glyphIds = glyphIds;
            _advances = advances;
            XOffsets = new int[glyphIds.Count];
            YOffsets = new int[glyphIds.Count];
        }

        public int[] XOffsets { get; }
        public int[] YOffsets { get; }
        public int Count => _glyphIds.Count;

        public GlyphClassKind GetGlyphClassKind(int index) =>
            _typeface.GetGlyph(_glyphIds[index]).GlyphClass;

        public void AppendGlyphOffset(int index, short appendOffsetX, short appendOffsetY)
        {
            XOffsets[index] += appendOffsetX;
            YOffsets[index] += appendOffsetY;
        }

        public void AppendGlyphAdvance(int index, short appendAdvX, short appendAdvY) =>
            _advances[index] += appendAdvX;

        public ushort GetGlyph(int index, out short advW)
        {
            advW = ToShort(_advances[index]);
            return _glyphIds[index];
        }

        public ushort GetGlyph(
            int index,
            out ushort inputOffset,
            out short offsetX,
            out short offsetY,
            out short advW)
        {
            inputOffset = (ushort)Math.Min(index, ushort.MaxValue);
            offsetX = ToShort(XOffsets[index]);
            offsetY = ToShort(YOffsets[index]);
            advW = ToShort(_advances[index]);
            return _glyphIds[index];
        }

        public void GetOffset(int index, out short offsetX, out short offsetY)
        {
            offsetX = ToShort(XOffsets[index]);
            offsetY = ToShort(YOffsets[index]);
        }

        private static short ToShort(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);
    }
}
