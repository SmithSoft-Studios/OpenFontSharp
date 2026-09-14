namespace OpenFontSharp.Subsetting;

/// <summary>
/// Subsets a TrueType font by retaining only the specified glyphs, remapping glyph ids
/// contiguously, and rebuilding the affected tables.
/// </summary>
/// <remarks>
/// <para>
/// The subset copies glyph records verbatim from the source font. Tables that describe
/// the font as a whole (head, hhea, OS/2, cmap, name, post) are copied from the source
/// and patched where the subset changes them, rather than synthesised, so nothing the
/// original declared — units per em, licensing bits, style — is silently altered.
/// </para>
/// <para>
/// Subsetting requires the original font bytes. The <see cref="Typeface"/>-only overload
/// cannot copy outlines, so it returns the font unchanged rather than producing a font
/// whose glyphs are blank.
/// </para>
/// </remarks>
public static class FontSubsetter
{
    private static readonly Random s_random = new();

    /// <summary>
    /// Subsets a font from its raw bytes, retaining only the specified glyphs.
    /// Returns the font unchanged when it cannot be subset safely.
    /// </summary>
    public static SubsetResult Subset(byte[] fontData, ISet<ushort> usedGlyphIds)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        ArgumentNullException.ThrowIfNull(usedGlyphIds);

        var tables = RawTtfTables.TryRead(fontData);
        if (tables is null)
        {
            // CFF outlines, or a font we cannot parse. Returning it unchanged keeps the
            // document correct; the caller sees an empty map and embeds the whole font.
            return Unchanged(fontData);
        }

        var retained = GlyfSubsetBuilder.CollectGlyphs(tables, usedGlyphIds);
        if (retained.Count == 0 || retained.Count >= tables.NumGlyphs)
            return Unchanged(fontData);

        var glyphIdMap = new Dictionary<ushort, ushort>(retained.Count);
        for (int i = 0; i < retained.Count; i++)
            glyphIdMap[retained[i]] = (ushort)i;

        var (glyf, loca) = GlyfSubsetBuilder.Build(tables, retained, glyphIdMap);

        var writer = new TtfWriter();
        writer.AddTable("head", BuildHead(tables));
        writer.AddTable("hhea", BuildHhea(tables, retained.Count));
        writer.AddTable("maxp", BuildMaxp(tables, retained.Count));
        writer.AddTable("hmtx", BuildHmtx(tables, retained));
        writer.AddTable("glyf", glyf);
        writer.AddTable("loca", loca);

        // Carried across unchanged when present: these describe the font, not its glyphs.
        CopyIfPresent(writer, tables, "OS/2");
        CopyIfPresent(writer, tables, "name");
        CopyIfPresent(writer, tables, "cvt ");
        CopyIfPresent(writer, tables, "fpgm");
        CopyIfPresent(writer, tables, "prep");
        CopyIfPresent(writer, tables, "gasp");

        // post is replaced with a minimal version 3 table: glyph names are large and a
        // PDF embedding never needs them.
        writer.AddTable("post", BuildMinimalPost());

        // cmap is rebuilt against the new glyph ids. PDF Type0/Identity-H embedding does
        // not consult it, but a font carrying a stale cmap is invalid on its own terms.
        writer.AddTable("cmap", BuildIdentityCmap(retained, glyphIdMap));

        var subsetData = writer.Build();

        return new SubsetResult(
            FontData: subsetData,
            GlyphIdMap: glyphIdMap,
            RetainedGlyphCount: retained.Count,
            OriginalGlyphCount: tables.NumGlyphs,
            SubsetPrefix: GenerateSubsetPrefix());
    }

    /// <summary>
    /// Subsets a parsed font. Only works when the typeface can supply its original
    /// bytes; otherwise the font is returned unchanged, because outlines cannot be
    /// copied from the parsed model without losing information.
    /// </summary>
    public static SubsetResult Subset(Typeface typeface, ISet<ushort> usedGlyphIds)
    {
        ArgumentNullException.ThrowIfNull(typeface);

        var original = typeface.GetOriginalFontData();
        return original is { Length: > 0 }
            ? Subset(original, usedGlyphIds)
            : Unchanged(original ?? []);
    }

    /// <summary>Generates a six-character subset prefix per the PDF spec, e.g. "ABCDEF+".</summary>
    public static string GenerateSubsetPrefix()
    {
        var chars = new char[7];
        for (int i = 0; i < 6; i++)
            chars[i] = (char)('A' + s_random.Next(26));
        chars[6] = '+';
        return new string(chars);
    }

    /// <summary>
    /// Returns the font as-is. The empty glyph map is how a caller detects that no
    /// subset was produced; the counts still report the real glyph total, because an
    /// unchanged font retains every glyph it had.
    /// </summary>
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

    /// <summary>Copies head, forcing long loca offsets to match what the builder writes.</summary>
    private static byte[] BuildHead(RawTtfTables tables)
    {
        var head = tables.TableBytes("head");

        // checkSumAdjustment must be zeroed; the writer recomputes it.
        head[8] = head[9] = head[10] = head[11] = 0;

        // indexToLocFormat = 1 (long offsets)
        head[50] = 0;
        head[51] = 1;

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
    /// Builds a format 4 cmap mapping the subset glyph ids into the Unicode private use
    /// area. The font is embedded as Identity-H, so nothing consults this mapping; it
    /// exists so the file remains a valid font in its own right.
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
