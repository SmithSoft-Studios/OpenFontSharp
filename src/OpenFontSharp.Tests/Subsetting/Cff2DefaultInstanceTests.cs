using OpenFontSharp.Subsetting;
using OpenFontSharp.Tables.CFF;

namespace OpenFontSharp.Tests.Subsetting;

public class Cff2DefaultInstanceTests
{
    [Fact]
    public void OpenTypeCff2_IsReadableAtItsDefaultInstance()
    {
        var typeface = FontFiles.Load(MinimalOpenTypeFont());

        typeface.IsCffFont.Should().BeTrue();
        typeface.GlyphCount.Should().Be(3);

        var glyph = typeface.GetGlyph(1);
        glyph.IsCffGlyph.Should().BeTrue();
        var outline = new Outline();
        new CffEvaluationEngine().Run(outline, glyph.GetCff1GlyphData()!);
        outline.Commands.Should().Contain("M120 -50");
    }

    [Fact]
    public void OpenTypeCff2_SubsetsToStaticCffAndDropsVariationTables()
    {
        byte[] source = MinimalOpenTypeFont(includeVariationTables: true);
        var original = FontFiles.Load(MinimalOpenTypeFont());

        var result = FontSubsetter.Subset(source, new HashSet<ushort> { 1 });

        SubsetVerifier.VerifyFaithful(original, result);
        result.RetainedGlyphCount.Should().Be(2);
        var output = SfntInspector.Parse(result.FontData);
        output.SfntVersion.Should().Be(0x4F54544Fu);
        output.Has("CFF ").Should().BeTrue();
        output.Has("CFF2").Should().BeFalse();
        foreach (string tag in new[] { "fvar", "avar", "HVAR", "MVAR", "STAT" })
            output.Has(tag).Should().BeFalse();
    }

    [Fact]
    public void OpenTypeCff2_WithEveryGlyphStillBecomesStatic()
    {
        var result = FontSubsetter.Subset(
            MinimalOpenTypeFont(includeVariationTables: true), new HashSet<ushort> { 0, 1, 2 });

        result.GlyphIdMap.Should().BeEmpty();
        result.RetainedGlyphCount.Should().Be(3);
        var output = SfntInspector.Parse(result.FontData);
        output.Has("CFF ").Should().BeTrue();
        output.Has("CFF2").Should().BeFalse();
        foreach (string tag in new[] { "fvar", "avar", "HVAR", "MVAR", "STAT" })
            output.Has(tag).Should().BeFalse();
    }

    internal static byte[] MinimalOpenTypeFont(bool includeVariationTables = false)
    {
        byte[] cff2 = MinimalCff2();
        var writer = new TtfWriter(TtfWriter.CffVersion);
        var head = new byte[54];
        head[12] = 0x5F;
        head[13] = 0x0F;
        head[14] = 0x3C;
        head[15] = 0xF5;
        head[18] = 3;
        head[19] = 0xE8;
        writer.AddTable("head", head);
        writer.AddTable("OS/2", new byte[78]);

        var hhea = new byte[36];
        hhea[34] = 0;
        hhea[35] = 3;
        writer.AddTable("hhea", hhea);
        writer.AddTable("maxp", [0, 0, 0x50, 0, 0, 3]);
        writer.AddTable("hmtx", new byte[12]);
        writer.AddTable("cmap", MinimalCmap());
        writer.AddTable("name", [0, 0, 0, 0, 0, 6]);
        writer.AddTable("post", new byte[32]);
        writer.AddTable("CFF2", cff2);

        if (includeVariationTables)
        {
            foreach (string tag in new[] { "fvar", "avar", "HVAR", "MVAR", "STAT" })
                writer.AddTable(tag, [0]);
        }

        return writer.Build();
    }

    private static byte[] MinimalCff2()
    {
        byte[][] charStrings =
        [
            [],
            [Int(20), Int(0), Int(100), Int(-50), Int(0), 15, Int(2), 16, Int(10), Int(0), 21, Int(10), Int(0), 5],
            [Int(40), Int(0), 21, Int(10), Int(0), 5],
        ];
        byte[] charStringsIndex = WriteCff2Index(charStrings);
        byte[] fdArrayIndex = WriteCff2Index([[]]);
        byte[] variationStore = MinimalVariationStore();
        int charStringsOffset = 5 + 19 + 4;
        int fdArrayOffset = charStringsOffset + charStringsIndex.Length;
        int variationStoreOffset = fdArrayOffset + fdArrayIndex.Length;
        byte[] topDict =
        [
            .. Long(charStringsOffset), 17,
            .. Long(fdArrayOffset), 12, 36,
            .. Long(variationStoreOffset), 24,
        ];

        return
        [
            2, 0, 5, 0, (byte)topDict.Length,
            .. topDict,
            0, 0, 0, 0, // empty CFF2 Global Subrs INDEX
            .. charStringsIndex,
            .. fdArrayIndex,
            .. variationStore,
        ];
    }

    private static byte[] MinimalVariationStore()
    {
        byte[] itemVariationStore =
        [
            0, 1,             // format
            0, 0, 0, 12,      // VariationRegionList offset
            0, 1,             // ItemVariationData count
            0, 0, 0, 22,      // ItemVariationData offset
            0, 1, 0, 1,       // one axis, one region
            0xC0, 0, 0, 0, 0x40, 0, // peak zero makes the region scalar one at the default instance
            0, 0, 0, 0, 0, 1, 0, 0, // no items, one region index (0)
        ];
        return [.. U16(itemVariationStore.Length), .. itemVariationStore];
    }

    private static byte[] WriteCff2Index(IReadOnlyList<byte[]> items)
    {
        using var output = new MemoryStream();
        WriteU32(output, (uint)items.Count);
        if (items.Count == 0)
            return output.ToArray();

        int dataLength = items.Sum(item => item.Length);
        int offSize = dataLength + 1 <= 255 ? 1 : dataLength + 1 <= 65535 ? 2 : 3;
        output.WriteByte((byte)offSize);
        int nextOffset = 1;
        for (int i = 0; i <= items.Count; i++)
        {
            for (int b = offSize - 1; b >= 0; b--)
                output.WriteByte((byte)(nextOffset >> (8 * b)));
            if (i < items.Count)
                nextOffset += items[i].Length;
        }
        foreach (byte[] item in items)
            output.Write(item);
        return output.ToArray();
    }

    private static byte[] MinimalCmap() =>
    [
        0, 0, 0, 1, 0, 3, 0, 1, 0, 0, 0, 12,
        0, 4, 0, 24, 0, 0, 0, 2, 0, 2, 0, 0, 0, 0,
        255, 255, 0, 0, 255, 255, 0, 1, 0, 0, 0, 0,
    ];

    private static byte Int(int value)
    {
        if (value is >= -107 and <= 107)
            return (byte)(value + 139);
        throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static byte[] Long(int value) => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];

    private static void WriteU32(Stream output, uint value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private sealed class Outline : IGlyphTranslator
    {
        public List<string> Commands { get; } = [];
        public void BeginRead(int contourCount) { }
        public void EndRead() { }
        public void MoveTo(float x0, float y0) => Commands.Add($"M{x0:0} {y0:0}");
        public void LineTo(float x1, float y1) => Commands.Add($"L{x1:0} {y1:0}");
        public void Curve3(float x1, float y1, float x2, float y2) { }
        public void Curve4(float x1, float y1, float x2, float y2, float x3, float y3) { }
        public void CloseContour() { }
    }
}
