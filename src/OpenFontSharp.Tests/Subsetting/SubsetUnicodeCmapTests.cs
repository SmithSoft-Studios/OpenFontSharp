using System.Buffers.Binary;
using OpenFontSharp.Subsetting;

namespace OpenFontSharp.Tests.Subsetting;

public class SubsetUnicodeCmapTests
{
    private static readonly (int CodePoint, ushort Glyph)[] Mappings =
    [
        (0, 4), (0x41, 4), (0x42, 2), (0x43, 3), (0x44, 4),
        (0xFFFE, 2), (0xFFFF, 4), (0x10000, 2), (0x1F600, 4), (0x10FFFF, 4)
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptIn_RoundTripsRetainedUnicodeAndRenumberedGlyphs(bool fromTypeface)
    {
        byte[] source = MakeFont();
        var glyphs = new HashSet<ushort> { 2, 4 };
        var result = fromTypeface
            ? FontSubsetter.Subset(FontFiles.Load(source), glyphs, includeUnicodeCmap: true)
            : FontSubsetter.Subset(source, glyphs, includeUnicodeCmap: true);

        result.GlyphIdMap.Should().Equal(new Dictionary<ushort, ushort> { [0] = 0, [2] = 1, [4] = 2 });
        var reread = FontFiles.Load(result.FontData);
        foreach (var (codePoint, glyph) in Mappings)
            reread.GetGlyphIndex(codePoint).Should().Be(glyph == 3 ? (ushort)0 : result.GlyphIdMap[glyph]);
        reread.GetGlyphIndex(0x45).Should().Be(0);
        reread.GetGlyphIndex(0xD800).Should().Be(0);

        byte[] cmap = SfntInspector.Parse(result.FontData).Bytes("cmap");
        SfntInspector.U16(cmap, 2).Should().Be(2);
        SfntInspector.U16(cmap, 4).Should().Be(3);
        SfntInspector.U16(cmap, 6).Should().Be(1);
        SfntInspector.U16(cmap, 12).Should().Be(3);
        SfntInspector.U16(cmap, 14).Should().Be(10);
        int format4 = (int)SfntInspector.U32(cmap, 8);
        int format12 = (int)SfntInspector.U32(cmap, 16);
        SfntInspector.U16(cmap, format4).Should().Be(4);
        SfntInspector.U16(cmap, format12).Should().Be(12);

        // Read format 4 alone too: a correct format 12 must not hide a broken BMP map.
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        var bmpOnly = FontFiles.Load(ReplaceCmap(result.FontData, cmap));
        foreach (var (codePoint, glyph) in Mappings.Where(m => m.CodePoint < 0xFFFF))
            bmpOnly.GetGlyphIndex(codePoint).Should().Be(glyph == 3 ? (ushort)0 : result.GlyphIdMap[glyph]);

        cmap.AsSpan(12, 8).CopyTo(cmap.AsSpan(4, 8));
        var fullOnly = FontFiles.Load(ReplaceCmap(result.FontData, cmap));
        foreach (var (codePoint, glyph) in Mappings)
            fullOnly.GetGlyphIndex(codePoint).Should().Be(glyph == 3 ? (ushort)0 : result.GlyphIdMap[glyph]);
    }

    [Fact]
    public void DefaultAndExplicitFalse_KeepLegacyEmptyCmapAndIdenticalBytes()
    {
        byte[] source = MakeFont();
        var glyphs = new HashSet<ushort> { 2, 4 };
        var legacy = FontSubsetter.Subset(source, glyphs);
        var explicitFalse = FontSubsetter.Subset(source, glyphs, includeUnicodeCmap: false);

        explicitFalse.FontData.Should().Equal(legacy.FontData);
        byte[] cmap = SfntInspector.Parse(legacy.FontData).Bytes("cmap");
        cmap.Length.Should().Be(36);
        SfntInspector.U16(cmap, 2).Should().Be(1);
        var reread = FontFiles.Load(legacy.FontData);
        foreach (var (codePoint, _) in Mappings)
            reread.GetGlyphIndex(codePoint).Should().Be(0);
    }

    [Fact]
    public void AllGlyphsRetained_StillReturnsOriginalBytesAndEmptyMap()
    {
        byte[] source = MakeFont();
        var result = FontSubsetter.Subset(source, new HashSet<ushort> { 1, 2, 3, 4 }, includeUnicodeCmap: true);
        result.FontData.Should().Equal(source);
        result.GlyphIdMap.Should().BeEmpty();
    }

    [Fact]
    public void CffAndCollectionOverloads_KeepUnicodeMappings()
    {
        var glyphs = new HashSet<ushort> { 2, 4 };
        byte[] cff = ReplaceCmap(FontFiles.Bytes(FontFiles.GreatVibes), BuildSourceCmap());
        var cffResult = FontSubsetter.Subset(cff, glyphs, includeUnicodeCmap: true);
        FontFiles.Load(cffResult.FontData).GetGlyphIndex(0x1F600).Should().Be(cffResult.GlyphIdMap[4]);

        var original = FontFiles.Load(FontFiles.RobotoRegular);
        ushort glyph = original.GetGlyphIndex('A');
        var collectionResult = FontSubsetter.Subset(
            FontFiles.Bytes(FontFiles.RobotoCollection), 0, new HashSet<ushort> { glyph }, includeUnicodeCmap: true);
        FontFiles.Load(collectionResult.FontData).GetGlyphIndex('A').Should().Be(collectionResult.GlyphIdMap[glyph]);
    }

    [Fact]
    public void Format4Source_AppliesGlyphArrayDeltaAndPreservesMissingEntries()
    {
        var cmap = new byte[52];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
        ushort[] words = [4, 40, 0, 4, 4, 1, 0, 0x44, 0xFFFF, 0, 0x41, 0xFFFF, 1, 1, 4, 0, 3, 1, 2, 0];
        for (int i = 0; i < words.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(12 + 2 * i), words[i]);

        var result = FontSubsetter.Subset(ReplaceCmap(MakeFont(), cmap), new HashSet<ushort> { 2, 4 }, includeUnicodeCmap: true);
        var reread = FontFiles.Load(result.FontData);
        reread.GetGlyphIndex(0x41).Should().Be(2);
        reread.GetGlyphIndex(0x42).Should().Be(1);
        reread.GetGlyphIndex(0x43).Should().Be(0);
        reread.GetGlyphIndex(0x44).Should().Be(0);
    }

    [Fact]
    public void DenseBmpAliases_FitFormat4UsingGlyphArray()
    {
        var mappings = Enumerable.Range(1, 20000).Select(c => (c, (ushort)4)).ToArray();
        byte[] source = ReplaceCmap(MakeFont(), BuildSourceCmap(mappings));
        var result = FontSubsetter.Subset(source, new HashSet<ushort> { 4 }, includeUnicodeCmap: true);
        byte[] cmap = SfntInspector.Parse(result.FontData).Bytes("cmap");
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        var reread = FontFiles.Load(ReplaceCmap(result.FontData, cmap));
        foreach (var (codePoint, _) in mappings)
            reread.GetGlyphIndex(codePoint).Should().Be(1);
    }

    [Theory]
    [InlineData(8, uint.MaxValue)] // subtable offset
    [InlineData(16, uint.MaxValue)] // subtable length
    [InlineData(24, uint.MaxValue)] // group count
    [InlineData(32, uint.MaxValue)] // first group end
    public void MalformedSourceCmap_ThrowsInvalidDataOnlyWhenOptedIn(int offset, uint value)
    {
        var cmap = BuildSourceCmap();
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(offset), value);
        byte[] source = ReplaceCmap(MakeFont(), cmap);
        var glyphs = new HashSet<ushort> { 2, 4 };
        var act = () => FontSubsetter.Subset(source, glyphs, includeUnicodeCmap: true);
        act.Should().Throw<InvalidDataException>();
        var legacy = () => FontSubsetter.Subset(source, glyphs);
        legacy.Should().NotThrow();
    }

    [Fact]
    public void NoMappedGlyphs_ProducesReadableEmptyUnicodeSubtables()
    {
        var result = FontSubsetter.Subset(MakeFont(), new HashSet<ushort>(), includeUnicodeCmap: true);
        var reread = FontFiles.Load(result.FontData);
        foreach (var (codePoint, _) in Mappings)
            reread.GetGlyphIndex(codePoint).Should().Be(0);
    }

    private static byte[] MakeFont()
    {
        var writer = new TtfWriter();
        var head = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);
        var hhea = new byte[36];
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), 5);
        var maxp = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), 5);
        writer.AddTable("head", head);
        writer.AddTable("hhea", hhea);
        writer.AddTable("maxp", maxp);
        writer.AddTable("hmtx", new byte[20]);
        writer.AddTable("glyf", []);
        writer.AddTable("loca", new byte[24]);
        writer.AddTable("OS/2", new byte[78]);
        writer.AddTable("name", new byte[6]);
        var post = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(post, 0x00030000);
        writer.AddTable("post", post);
        writer.AddTable("cmap", BuildSourceCmap());
        return writer.Build();
    }

    private static byte[] BuildSourceCmap((int CodePoint, ushort Glyph)[]? mappings = null)
    {
        mappings ??= Mappings;
        // A Unicode-platform format 12 source; the output must also supply format 4.
        var cmap = new byte[28 + 12 * mappings.Length];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 4);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(12), 12);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(16), (uint)(cmap.Length - 12));
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(24), (uint)mappings.Length);
        for (int i = 0; i < mappings.Length; i++)
        {
            int offset = 28 + 12 * i;
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(offset), (uint)mappings[i].CodePoint);
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(offset + 4), (uint)mappings[i].CodePoint);
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(offset + 8), mappings[i].Glyph);
        }
        return cmap;
    }

    private static byte[] ReplaceCmap(byte[] source, byte[] cmap)
    {
        var font = SfntInspector.Parse(source);
        var writer = new TtfWriter(font.SfntVersion);
        foreach (var table in font.Directory.Where(t => t.Tag != "cmap"))
            writer.AddTable(table.Tag, font.Bytes(table.Tag));
        writer.AddTable("cmap", cmap);
        return writer.Build();
    }
}
