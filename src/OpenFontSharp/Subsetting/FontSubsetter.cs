namespace OpenFontSharp.Subsetting;

/// <summary>
/// Subsets a font by retaining only the specified glyphs, remapping glyph ids
/// contiguously, and rebuilding the affected tables.
/// </summary>
/// <remarks>
/// <para>
/// The subset copies glyph records verbatim from the source font. Tables that describe
/// the font as a whole (head, hhea, OS/2, name) are copied from the source and patched
/// where the subset changes them, rather than synthesised, so nothing the original
/// declared — units per em, licensing bits, style — is silently altered.
/// </para>
/// <para>
/// Supported inputs: TrueType outlines (including variable fonts, which are reduced to
/// their default instance), name-keyed CFF outlines (OpenType 'OTTO'), WOFF 1.0
/// wrapping either, and one font of a collection via
/// <see cref="Subset(byte[], int, ISet{ushort})"/>. Anything else throws
/// <see cref="NotSupportedException"/>: a subsetter that cannot copy outlines must not
/// hand back a font that merely looks valid.
/// </para>
/// </remarks>
public static class FontSubsetter
{
    /// <summary>
    /// Subsets a font from its raw bytes, retaining only the specified glyphs.
    /// </summary>
    /// <remarks>
    /// The output is always a plain sfnt font (never WOFF). When every glyph
    /// is retained, the font is returned whole with an empty
    /// <see cref="SubsetResult.GlyphIdMap"/>.
    /// </remarks>
    /// <param name="fontData">The original standalone font or WOFF1 bytes.</param>
    /// <param name="usedGlyphIds">Glyph IDs mapped or shaped against the original font.</param>
    /// <returns>The font bytes and old-to-new ID map; an empty map means IDs are unchanged.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="NotSupportedException">
    /// The font's format cannot be subset (collection without an index, WOFF2, CFF2,
    /// CID-keyed CFF, or unrecognised data).
    /// </exception>
    public static SubsetResult Subset(byte[] fontData, ISet<ushort> usedGlyphIds)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        ArgumentNullException.ThrowIfNull(usedGlyphIds);

        switch (SfntContainers.Detect(fontData))
        {
            case FontContainerFormat.TrueType:
            case FontContainerFormat.OpenTypeCff:
                return SubsetSfnt(fontData, usedGlyphIds);

            case FontContainerFormat.Woff:
                byte[] sfnt;
                try
                {
                    sfnt = SfntContainers.DecodeWoff(fontData);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                {
                    throw new NotSupportedException("The WOFF font could not be decoded, so it cannot be subset.", ex);
                }
                return SubsetSfnt(sfnt, usedGlyphIds);

            case FontContainerFormat.Collection:
                throw new NotSupportedException(
                    "The data is a font collection (TTC). Subset one of its fonts with " +
                    "FontSubsetter.Subset(fontData, fontIndex, usedGlyphIds).");

            case FontContainerFormat.Woff2:
                throw new NotSupportedException(
                    "WOFF2 fonts cannot be subset: decoding WOFF2's transformed glyf/loca " +
                    "tables is not implemented. Convert the font to TTF/OTF first.");

            default:
                throw new NotSupportedException(
                    "The data is not a recognised font (expected a TrueType, OpenType, TTC or WOFF signature).");
        }
    }

    /// <summary>
    /// Subsets one font of a TrueType/OpenType collection (TTC).
    /// </summary>
    /// <param name="fontData">The complete collection bytes.</param>
    /// <param name="fontIndex">The zero-based collection member index.</param>
    /// <param name="usedGlyphIds">Glyph IDs from the selected original member.</param>
    /// <returns>The standalone member subset and ID remapping.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="NotSupportedException">The data is not a collection, or the chosen font cannot be subset.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontIndex"/> is not in the collection.</exception>
    public static SubsetResult Subset(byte[] fontData, int fontIndex, ISet<ushort> usedGlyphIds)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        ArgumentNullException.ThrowIfNull(usedGlyphIds);

        if (SfntContainers.Detect(fontData) != FontContainerFormat.Collection)
            throw new NotSupportedException("A font index can only be given for a font collection (TTC).");

        byte[] member;
        try
        {
            member = SfntContainers.ExtractFromCollection(fontData, fontIndex);
        }
        catch (InvalidDataException ex)
        {
            throw new NotSupportedException("The font collection is malformed, so it cannot be subset.", ex);
        }

        return Subset(member, usedGlyphIds);
    }

    /// <summary>
    /// Subsets a parsed font from the original bytes it was read from.
    /// </summary>
    /// <param name="typeface">The original parsed font with retained source bytes.</param>
    /// <param name="usedGlyphIds">Glyph IDs mapped or shaped against that font.</param>
    /// <returns>The standalone subset and ID remapping.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="NotSupportedException">
    /// The typeface holds no original bytes (it was read from a non-seekable stream), or
    /// its format cannot be subset.
    /// </exception>
    public static SubsetResult Subset(Typeface typeface, ISet<ushort> usedGlyphIds)
    {
        ArgumentNullException.ThrowIfNull(typeface);

        var original = typeface.GetOriginalFontData();
        if (original is not { Length: > 0 })
        {
            throw new NotSupportedException(
                "The typeface does not hold its original font bytes (it was read from a " +
                "non-seekable stream), and outlines cannot be copied from the parsed model. " +
                "Read the font from a seekable stream or pass the font bytes instead.");
        }

        return Subset(original, usedGlyphIds);
    }

    private static SubsetResult SubsetSfnt(byte[] fontData, ISet<ushort> usedGlyphIds)
    {
        var tables = RawTtfTables.TryRead(fontData);
        if (tables is null)
        {
            // A TrueType-flavoured font without usable glyf/loca (bitmap-only, or a
            // table directory we cannot follow). Returned whole, its outlines — if
            // any — are intact; the caller sees an empty map and embeds the full font.
            return Unchanged(fontData);
        }

        return tables.IsCff
            ? SubsetCff(tables, usedGlyphIds)
            : SubsetTrueType(tables, usedGlyphIds);
    }

    private static SubsetResult SubsetCff(RawTtfTables tables, ISet<ushort> usedGlyphIds)
    {
        if (tables.Has("CFF2") || !tables.Has("CFF "))
            throw new NotSupportedException("Fonts with CFF2 outlines (variable OpenType/CFF) cannot be subset.");

        // CFF glyphs are self-contained (seac composites are rejected by the builder),
        // so the retained set is .notdef plus the requested glyphs.
        var needed = new SortedSet<ushort> { 0 };
        foreach (var glyphId in usedGlyphIds)
        {
            if (glyphId < tables.NumGlyphs)
                needed.Add(glyphId);
        }

        var retained = needed.ToList();
        if (retained.Count >= tables.NumGlyphs)
            return Unchanged(tables);

        var glyphIdMap = BuildGlyphIdMap(retained);

        var writer = new TtfWriter(TtfWriter.CffVersion);
        writer.AddTable("head", BuildHead(tables, longLoca: false));
        writer.AddTable("CFF ", CffSubsetBuilder.Build(tables.TableBytes("CFF "), retained));

        return Finish(writer, tables, retained, glyphIdMap);
    }

    private static SubsetResult SubsetTrueType(RawTtfTables tables, ISet<ushort> usedGlyphIds)
    {
        var retained = GlyfSubsetBuilder.CollectGlyphs(tables, usedGlyphIds);
        if (retained.Count >= tables.NumGlyphs)
            return Unchanged(tables);

        var glyphIdMap = BuildGlyphIdMap(retained);
        var (glyf, loca) = GlyfSubsetBuilder.Build(tables, retained, glyphIdMap);

        var writer = new TtfWriter();
        writer.AddTable("head", BuildHead(tables, longLoca: true));
        writer.AddTable("glyf", glyf);
        writer.AddTable("loca", loca);

        // Hinting is glyph-independent and survives subsetting unchanged.
        CopyIfPresent(writer, tables, "cvt ");
        CopyIfPresent(writer, tables, "fpgm");
        CopyIfPresent(writer, tables, "prep");
        CopyIfPresent(writer, tables, "gasp");

        // Variation tables (fvar, gvar, HVAR, ...) are deliberately not copied: the
        // glyf outlines are the default instance, and that is what the subset holds.
        return Finish(writer, tables, retained, glyphIdMap);
    }

    /// <summary>Adds the tables shared by both outline formats and builds the result.</summary>
    private static SubsetResult Finish(
        TtfWriter writer, RawTtfTables tables, IReadOnlyList<ushort> retained, Dictionary<ushort, ushort> glyphIdMap)
    {
        writer.AddTable("hhea", BuildHhea(tables, retained.Count));
        writer.AddTable("maxp", BuildMaxp(tables, retained.Count));
        writer.AddTable("hmtx", BuildHmtx(tables, retained));

        // Carried across unchanged when present: these describe the font, not its glyphs.
        CopyIfPresent(writer, tables, "OS/2");
        CopyIfPresent(writer, tables, "name");

        // post is replaced with a minimal version 3 table: glyph names are large and a
        // PDF embedding never needs them.
        writer.AddTable("post", BuildMinimalPost());

        // cmap is rebuilt against the new glyph ids. PDF Type0/Identity-H embedding does
        // not consult it, but a font carrying a stale cmap is invalid on its own terms.
        writer.AddTable("cmap", BuildIdentityCmap(retained, glyphIdMap));

        return new SubsetResult(
            FontData: writer.Build(),
            GlyphIdMap: glyphIdMap,
            RetainedGlyphCount: retained.Count,
            OriginalGlyphCount: tables.NumGlyphs,
            SubsetPrefix: GenerateSubsetPrefix());
    }

    private static Dictionary<ushort, ushort> BuildGlyphIdMap(IReadOnlyList<ushort> retained)
    {
        var glyphIdMap = new Dictionary<ushort, ushort>(retained.Count);
        for (int i = 0; i < retained.Count; i++)
            glyphIdMap[retained[i]] = (ushort)i;
        return glyphIdMap;
    }

    /// <summary>Generates a six-character subset prefix per the PDF spec, e.g. "ABCDEF+".</summary>
    public static string GenerateSubsetPrefix()
    {
        var chars = new char[7];
        for (int i = 0; i < 6; i++)
            chars[i] = (char)('A' + Random.Shared.Next(26));
        chars[6] = '+';
        return new string(chars);
    }

    /// <summary>
    /// Returns the font as-is. The empty glyph map is how a caller detects that no
    /// subset was produced; the counts still report the real glyph total, because an
    /// unchanged font retains every glyph it had.
    /// </summary>
    private static SubsetResult Unchanged(RawTtfTables tables) => Unchanged(tables.FontData);

    private static SubsetResult Unchanged(byte[] fontData)
    {
        int glyphCount = RawTtfTables.TryReadGlyphCount(fontData);

        return new SubsetResult(
            FontData: fontData,
            GlyphIdMap: [],
            RetainedGlyphCount: glyphCount,
            OriginalGlyphCount: glyphCount,
            SubsetPrefix: GenerateSubsetPrefix());
    }

    private static void CopyIfPresent(TtfWriter writer, RawTtfTables tables, string tag)
    {
        if (tables.Has(tag))
            writer.AddTable(tag, tables.TableBytes(tag));
    }

    /// <summary>Copies head, optionally forcing long loca offsets to match what the glyf builder writes.</summary>
    private static byte[] BuildHead(RawTtfTables tables, bool longLoca)
    {
        var head = tables.TableBytes("head");

        // checkSumAdjustment must be zeroed; the writer recomputes it.
        head[8] = head[9] = head[10] = head[11] = 0;

        if (longLoca)
        {
            // indexToLocFormat = 1 (long offsets)
            head[50] = 0;
            head[51] = 1;
        }

        return head;
    }

    /// <summary>Copies hhea, patching the number of horizontal metrics.</summary>
    private static byte[] BuildHhea(RawTtfTables tables, int retainedCount)
    {
        var hhea = tables.TableBytes("hhea");
        if (hhea.Length < 36)
            return hhea;

        hhea[34] = (byte)(retainedCount >> 8);
        hhea[35] = (byte)(retainedCount & 0xFF);
        return hhea;
    }

    /// <summary>Copies maxp, patching the glyph count.</summary>
    private static byte[] BuildMaxp(RawTtfTables tables, int retainedCount)
    {
        var maxp = tables.TableBytes("maxp");
        if (maxp.Length < 6)
            return maxp;

        maxp[4] = (byte)(retainedCount >> 8);
        maxp[5] = (byte)(retainedCount & 0xFF);
        return maxp;
    }

    /// <summary>
    /// Rebuilds hmtx with one full metric per retained glyph, carrying each glyph's
    /// original advance width and left side bearing.
    /// </summary>
    private static byte[] BuildHmtx(RawTtfTables tables, IReadOnlyList<ushort> retained)
    {
        var hmtxSpan = tables.Span("hmtx");
        var hmtx = tables.TableBytes("hmtx");
        var hheaBytes = tables.TableBytes("hhea");

        int originalMetricCount = hheaBytes.Length >= 36
            ? RawTtfTables.ReadU16(hheaBytes, 34)
            : 0;

        var output = new byte[retained.Count * 4];

        for (int i = 0; i < retained.Count; i++)
        {
            ushort glyphId = retained[i];
            ushort advance = 0;
            short lsb = 0;

            if (originalMetricCount > 0 && hmtxSpan.Length > 0)
            {
                if (glyphId < originalMetricCount)
                {
                    int offset = glyphId * 4;
                    if (offset + 4 <= hmtx.Length)
                    {
                        advance = RawTtfTables.ReadU16(hmtx, offset);
                        lsb = RawTtfTables.ReadI16(hmtx, offset + 2);
                    }
                }
                else
                {
                    // Monospaced tail: the last full metric supplies the advance and
                    // the trailing array supplies this glyph's side bearing.
                    int lastMetric = (originalMetricCount - 1) * 4;
                    if (lastMetric + 4 <= hmtx.Length)
                        advance = RawTtfTables.ReadU16(hmtx, lastMetric);

                    int lsbOffset = (originalMetricCount * 4) + ((glyphId - originalMetricCount) * 2);
                    if (lsbOffset + 2 <= hmtx.Length)
                        lsb = RawTtfTables.ReadI16(hmtx, lsbOffset);
                }
            }

            output[i * 4] = (byte)(advance >> 8);
            output[(i * 4) + 1] = (byte)(advance & 0xFF);
            output[(i * 4) + 2] = (byte)((lsb >> 8) & 0xFF);
            output[(i * 4) + 3] = (byte)(lsb & 0xFF);
        }

        return output;
    }

    /// <summary>Minimal version 3.0 post table, which carries no glyph names.</summary>
    private static byte[] BuildMinimalPost()
    {
        var post = new byte[32];
        post[0] = 0x00;
        post[1] = 0x03;
        post[2] = 0x00;
        post[3] = 0x00;
        return post;
    }

    /// <summary>
    /// Builds a format 4 cmap with only the terminator segment.
    /// Identity-H embedding addresses glyphs directly and does not use Unicode cmap lookup.
    /// </summary>
    private static byte[] BuildIdentityCmap(
        IReadOnlyList<ushort> retained, IReadOnlyDictionary<ushort, ushort> glyphIdMap)
    {
        _ = retained;
        _ = glyphIdMap;

        // A single-segment format 4 table with only the required terminator segment.
        // segCountX2 = 2, one segment ending at 0xFFFF mapping to glyph 0.
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        WriteBE16(w, 0);       // version
        WriteBE16(w, 1);       // numTables
        WriteBE16(w, 3);       // platformID = Windows
        WriteBE16(w, 1);       // encodingID = Unicode BMP
        WriteBE32(w, 12);      // offset to the subtable

        WriteBE16(w, 4);       // format
        WriteBE16(w, 24);      // length
        WriteBE16(w, 0);       // language
        WriteBE16(w, 2);       // segCountX2
        WriteBE16(w, 2);       // searchRange
        WriteBE16(w, 0);       // entrySelector
        WriteBE16(w, 0);       // rangeShift
        WriteBE16(w, 0xFFFF);  // endCode[0]
        WriteBE16(w, 0);       // reservedPad
        WriteBE16(w, 0xFFFF);  // startCode[0]
        WriteBE16(w, 1);       // idDelta[0]
        WriteBE16(w, 0);       // idRangeOffset[0]

        return ms.ToArray();
    }

    private static void WriteBE16(BinaryWriter w, ushort value)
    {
        w.Write((byte)(value >> 8));
        w.Write((byte)(value & 0xFF));
    }

    private static void WriteBE32(BinaryWriter w, uint value)
    {
        w.Write((byte)(value >> 24));
        w.Write((byte)(value >> 16));
        w.Write((byte)(value >> 8));
        w.Write((byte)(value & 0xFF));
    }
}
