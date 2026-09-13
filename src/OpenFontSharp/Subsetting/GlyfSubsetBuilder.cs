namespace OpenFontSharp.Subsetting;

/// <summary>
/// Builds the glyf and loca tables of a subset by copying glyph records verbatim from
/// the source font and remapping the glyph ids that composite glyphs reference.
/// </summary>
/// <remarks>
/// Copying raw records is what preserves the outline. An earlier implementation wrote
/// every glyph as zero contours with only a bounding box, which shrank the font
/// dramatically and rendered every character blank — the file looked right by every
/// measure except the one that matters.
/// </remarks>
public static class GlyfSubsetBuilder
{
    // Composite glyph component flags, per the OpenType spec.
    private const ushort ArgsAreWords = 0x0001;
    private const ushort WeHaveAScale = 0x0008;
    private const ushort MoreComponents = 0x0020;
    private const ushort WeHaveXAndYScale = 0x0040;
    private const ushort WeHaveTwoByTwo = 0x0080;

    /// <summary>
    /// Collects every glyph the subset needs, following composite components
    /// transitively so a composite never loses the pieces it is built from.
    /// </summary>
    public static IReadOnlyList<ushort> CollectGlyphs(RawTtfTables tables, ISet<ushort> usedGlyphIds)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(usedGlyphIds);

        var needed = new HashSet<ushort> { 0 }; // .notdef must always survive

        foreach (var glyphId in usedGlyphIds)
        {
            if (glyphId < tables.NumGlyphs)
                needed.Add(glyphId);
        }

        var pending = new Queue<ushort>(needed);
        while (pending.Count > 0)
        {
            var glyphId = pending.Dequeue();
            foreach (var component in ComponentIdsOf(tables.GlyphRecord(glyphId)))
            {
                if (component < tables.NumGlyphs && needed.Add(component))
                    pending.Enqueue(component);
            }
        }

        var sorted = needed.ToList();
        sorted.Sort();
        return sorted;
    }

    /// <summary>
    /// Produces the subset glyf and loca tables. Loca is always written in long
    /// format, so the caller must set head.indexToLocFormat to 1.
    /// </summary>
    public static (byte[] Glyf, byte[] Loca) Build(
        RawTtfTables tables,
        IReadOnlyList<ushort> retainedGlyphs,
        IReadOnlyDictionary<ushort, ushort> glyphIdMap)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(retainedGlyphs);
        ArgumentNullException.ThrowIfNull(glyphIdMap);

        using var glyf = new MemoryStream();
        var loca = new uint[retainedGlyphs.Count + 1];

        for (int i = 0; i < retainedGlyphs.Count; i++)
        {
            loca[i] = (uint)glyf.Position;

            var record = tables.GlyphRecord(retainedGlyphs[i]);
            if (!record.IsEmpty)
            {
                var bytes = record.ToArray();

                // A composite glyph names its parts by glyph id, so those ids must be
                // rewritten to the subset numbering.
                if (RawTtfTables.ReadI16(bytes, 0) < 0)
                    RemapComponentIds(bytes, glyphIdMap);

                glyf.Write(bytes);

                // Glyph records must start on an even boundary.
                if (glyf.Position % 2 != 0)
                    glyf.WriteByte(0);
            }
        }

        loca[^1] = (uint)glyf.Position;

        var locaBytes = new byte[loca.Length * 4];
        for (int i = 0; i < loca.Length; i++)
        {
            locaBytes[i * 4] = (byte)(loca[i] >> 24);
            locaBytes[(i * 4) + 1] = (byte)(loca[i] >> 16);
            locaBytes[(i * 4) + 2] = (byte)(loca[i] >> 8);
            locaBytes[(i * 4) + 3] = (byte)loca[i];
        }

        return (glyf.ToArray(), locaBytes);
    }

    /// <summary>
    /// Rewrites the glyph ids inside a composite glyph record in place.
    /// </summary>
    private static void RemapComponentIds(byte[] record, IReadOnlyDictionary<ushort, ushort> glyphIdMap)
    {
        int offset = 10; // skip numberOfContours and the bounding box

        while (offset + 4 <= record.Length)
        {
            ushort flags = RawTtfTables.ReadU16(record, offset);
            int glyphIndexOffset = offset + 2;
            ushort componentId = RawTtfTables.ReadU16(record, glyphIndexOffset);

            if (glyphIdMap.TryGetValue(componentId, out var newId))
            {
                record[glyphIndexOffset] = (byte)(newId >> 8);
                record[glyphIndexOffset + 1] = (byte)(newId & 0xFF);
            }

            offset = glyphIndexOffset + 2;
            offset += (flags & ArgsAreWords) != 0 ? 4 : 2;

            if ((flags & WeHaveAScale) != 0)
                offset += 2;
            else if ((flags & WeHaveXAndYScale) != 0)
                offset += 4;
            else if ((flags & WeHaveTwoByTwo) != 0)
                offset += 8;

            if ((flags & MoreComponents) == 0)
                break;
        }
    }

    /// <summary>
    /// Returns the glyph ids a composite record references, or nothing for a simple glyph.
    /// </summary>
    private static IEnumerable<ushort> ComponentIdsOf(ReadOnlySpan<byte> record)
    {
        var components = new List<ushort>();

        if (record.Length < 10 || RawTtfTables.ReadI16(record.ToArray(), 0) >= 0)
            return components;

        var bytes = record.ToArray();
        int offset = 10;

        while (offset + 4 <= bytes.Length)
        {
            ushort flags = RawTtfTables.ReadU16(bytes, offset);
            components.Add(RawTtfTables.ReadU16(bytes, offset + 2));

            offset += 4;
            offset += (flags & ArgsAreWords) != 0 ? 4 : 2;

            if ((flags & WeHaveAScale) != 0)
                offset += 2;
            else if ((flags & WeHaveXAndYScale) != 0)
                offset += 4;
            else if ((flags & WeHaveTwoByTwo) != 0)
                offset += 8;

            if ((flags & MoreComponents) == 0)
                break;
        }

        return components;
    }
}
