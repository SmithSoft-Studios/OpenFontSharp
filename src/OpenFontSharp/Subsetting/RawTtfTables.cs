namespace OpenFontSharp.Subsetting;

/// <summary>
/// Reads the sfnt table directory of a TrueType font so subsetting can work on raw
/// table bytes.
/// </summary>
/// <remarks>
/// Subsetting has to copy glyph outlines verbatim. Re-serialising outlines from parsed
/// <see cref="Glyph"/> data loses information (composite component transforms,
/// hinting instructions, point flags), so the subsetter reads the original bytes
/// instead of relying on the parsed model.
/// </remarks>
public sealed class RawTtfTables
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);

    private RawTtfTables(byte[] data) => _data = data;

    /// <summary>Number of glyphs declared by the maxp table.</summary>
    public int NumGlyphs { get; private set; }

    /// <summary>True when loca uses 32-bit offsets (head.indexToLocFormat = 1).</summary>
    public bool LongLoca { get; private set; }

    /// <summary>Glyph offsets from the loca table, with NumGlyphs + 1 entries.</summary>
    public uint[] Loca { get; private set; } = [];

    /// <summary>
    /// Parses the table directory, or returns null when the font is not a usable
    /// TrueType outline font (CFF fonts and malformed files included).
    /// </summary>
    public static RawTtfTables? TryRead(byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(fontData);

        try
        {
            if (fontData.Length < 12)
                return null;

            var tables = new RawTtfTables(fontData);

            uint sfntVersion = ReadU32(fontData, 0);

            // 0x00010000 is TrueType outlines; "true" is the legacy Apple tag. "OTTO"
            // is CFF, which this subsetter does not handle.
            if (sfntVersion != 0x00010000 && sfntVersion != 0x74727565)
                return null;

            int numTables = ReadU16(fontData, 4);
            int directoryEnd = 12 + (numTables * 16);
            if (directoryEnd > fontData.Length)
                return null;

            for (int i = 0; i < numTables; i++)
            {
                int record = 12 + (i * 16);
                string tag = System.Text.Encoding.ASCII.GetString(fontData, record, 4);
                int offset = (int)ReadU32(fontData, record + 8);
                int length = (int)ReadU32(fontData, record + 12);

                if (offset < 0 || length < 0 || offset + length > fontData.Length)
                    continue; // skip a table the directory describes incorrectly

                tables._tables[tag] = (offset, length);
            }

            if (!tables.Has("head") || !tables.Has("maxp") || !tables.Has("loca") || !tables.Has("glyf"))
                return null;

            var head = tables.Span("head");
            if (head.Length < 54)
                return null;

            tables.LongLoca = ReadI16(fontData, head.Offset + 50) == 1;

            var maxp = tables.Span("maxp");
            if (maxp.Length < 6)
                return null;

            tables.NumGlyphs = ReadU16(fontData, maxp.Offset + 4);
            if (tables.NumGlyphs <= 0)
                return null;

            if (!tables.ReadLoca())
                return null;

            return tables;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A font we cannot make sense of is simply not subsettable.
            return null;
        }
    }

    /// <summary>
    /// Reads just the glyph count from any sfnt font, including CFF fonts this
    /// subsetter cannot process, so callers can still report it accurately.
    /// </summary>
    public static int TryReadGlyphCount(byte[] fontData)
    {
        try
        {
            if (fontData is null || fontData.Length < 12)
                return 0;

            int numTables = ReadU16(fontData, 4);
            for (int i = 0; i < numTables; i++)
            {
                int record = 12 + (i * 16);
                if (record + 16 > fontData.Length)
                    break;

                if (System.Text.Encoding.ASCII.GetString(fontData, record, 4) != "maxp")
                    continue;

                int offset = (int)ReadU32(fontData, record + 8);
                return offset + 6 <= fontData.Length ? ReadU16(fontData, offset + 4) : 0;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return 0;
        }

        return 0;
    }

    /// <summary>True when the font contains a table with this tag.</summary>
    public bool Has(string tag) => _tables.ContainsKey(tag);

    /// <summary>Offset and length of a table, or (0, 0) when absent.</summary>
    public (int Offset, int Length) Span(string tag)
        => _tables.TryGetValue(tag, out var entry) ? entry : (0, 0);

    /// <summary>Returns a copy of a table's bytes, or an empty array when absent.</summary>
    public byte[] TableBytes(string tag)
    {
        if (!_tables.TryGetValue(tag, out var entry))
            return [];

        var copy = new byte[entry.Length];
        Array.Copy(_data, entry.Offset, copy, 0, entry.Length);
        return copy;
    }

    /// <summary>
    /// Returns the raw glyph record for a glyph id, or an empty span for a glyph with
    /// no outline (loca entries equal, which is legal and means a blank glyph).
    /// </summary>
    public ReadOnlySpan<byte> GlyphRecord(ushort glyphId)
    {
        if (glyphId + 1 >= Loca.Length)
            return default;

        uint start = Loca[glyphId];
        uint end = Loca[glyphId + 1];

        if (end <= start)
            return default;

        var glyf = Span("glyf");
        if (start >= (uint)glyf.Length || end > (uint)glyf.Length)
            return default;

        return _data.AsSpan(glyf.Offset + (int)start, (int)(end - start));
    }

    private bool ReadLoca()
    {
        var loca = Span("loca");
        int entryCount = NumGlyphs + 1;
        int required = LongLoca ? entryCount * 4 : entryCount * 2;

        if (loca.Length < required)
            return false;

        Loca = new uint[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            // Short loca stores offsets divided by two.
            Loca[i] = LongLoca
                ? ReadU32(_data, loca.Offset + (i * 4))
                : (uint)(ReadU16(_data, loca.Offset + (i * 2)) * 2);
        }

        return true;
    }

    internal static ushort ReadU16(byte[] data, int offset)
        => (ushort)((data[offset] << 8) | data[offset + 1]);

    internal static short ReadI16(byte[] data, int offset)
        => (short)((data[offset] << 8) | data[offset + 1]);

    internal static uint ReadU32(byte[] data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
           | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
