namespace OpenFontSharp.Tests.Subsetting;

/// <summary>
/// An independent, test-only reader of sfnt structure, so subset output is checked by
/// code that does not share the subsetter's own parsing.
/// </summary>
internal sealed class SfntInspector
{
    public sealed record Table(string Tag, int Offset, int Length, uint Checksum);

    private SfntInspector(byte[] data) => Data = data;

    public byte[] Data { get; }

    public uint SfntVersion { get; private set; }

    public List<Table> Directory { get; } = [];

    public static SfntInspector Parse(byte[] data)
    {
        var font = new SfntInspector(data) { SfntVersion = U32(data, 0) };
        int numTables = U16(data, 4);
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + (i * 16);
            font.Directory.Add(new Table(
                System.Text.Encoding.ASCII.GetString(data, record, 4),
                (int)U32(data, record + 8),
                (int)U32(data, record + 12),
                U32(data, record + 4)));
        }
        return font;
    }

    public bool Has(string tag) => Directory.Any(t => t.Tag == tag);

    public Table Get(string tag) => Directory.Single(t => t.Tag == tag);

    public byte[] Bytes(string tag)
    {
        var table = Get(tag);
        return Data.AsSpan(table.Offset, table.Length).ToArray();
    }

    public int NumGlyphs => U16(Bytes("maxp"), 4);

    public int NumberOfHMetrics => U16(Bytes("hhea"), 34);

    public bool LongLoca => I16(Bytes("head"), 50) == 1;

    public uint[] Loca()
    {
        var loca = Bytes("loca");
        var offsets = new uint[NumGlyphs + 1];
        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = LongLoca ? U32(loca, i * 4) : (uint)(U16(loca, i * 2) * 2);
        return offsets;
    }

    public byte[] GlyphRecord(int glyphId)
    {
        var loca = Loca();
        var glyf = Get("glyf");
        return Data.AsSpan(glyf.Offset + (int)loca[glyphId], (int)(loca[glyphId + 1] - loca[glyphId])).ToArray();
    }

    /// <summary>Component glyph ids of a composite glyph record, or empty for a simple glyph.</summary>
    public static List<ushort> ComponentsOf(byte[] record)
    {
        var components = new List<ushort>();
        if (record.Length < 10 || I16(record, 0) >= 0)
            return components;

        int offset = 10;
        while (true)
        {
            ushort flags = U16(record, offset);
            components.Add(U16(record, offset + 2));
            offset += 4 + ((flags & 0x0001) != 0 ? 4 : 2);
            if ((flags & 0x0008) != 0) offset += 2;
            else if ((flags & 0x0040) != 0) offset += 4;
            else if ((flags & 0x0080) != 0) offset += 8;
            if ((flags & 0x0020) == 0)
                return components;
        }
    }

    /// <summary>Every glyph id the format 4 Unicode BMP cmap subtable maps to.</summary>
    public List<int> CmapGlyphIds()
    {
        var cmap = Bytes("cmap");
        var glyphs = new List<int>();
        int numTables = U16(cmap, 2);
        for (int t = 0; t < numTables; t++)
        {
            int sub = (int)U32(cmap, 4 + (t * 8) + 4);
            if (U16(cmap, sub) != 4)
                continue;

            int segCount = U16(cmap, sub + 6) / 2;
            int ends = sub + 14, starts = ends + (segCount * 2) + 2, deltas = starts + (segCount * 2), ranges = deltas + (segCount * 2);
            for (int s = 0; s < segCount; s++)
            {
                int end = U16(cmap, ends + (s * 2)), start = U16(cmap, starts + (s * 2));
                int delta = I16(cmap, deltas + (s * 2)), rangeOffset = U16(cmap, ranges + (s * 2));
                for (int c = start; c <= end; c++)
                {
                    if (c == 0xFFFF)
                        continue; // the required terminator segment maps nothing
                    int glyph = rangeOffset == 0
                        ? (c + delta) & 0xFFFF
                        : U16(cmap, ranges + (s * 2) + rangeOffset + ((c - start) * 2));
                    if (glyph != 0)
                        glyphs.Add(glyph);
                }
            }
        }
        return glyphs;
    }

    public static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int i = 0; i < data.Length; i += 4)
        {
            uint word = 0;
            for (int b = 0; b < 4; b++)
                word = (word << 8) | (i + b < data.Length ? data[i + b] : 0u);
            sum += word;
        }
        return sum;
    }

    public static ushort U16(byte[] d, int o) => (ushort)((d[o] << 8) | d[o + 1]);

    public static short I16(byte[] d, int o) => (short)((d[o] << 8) | d[o + 1]);

    public static uint U32(byte[] d, int o) => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];
}
