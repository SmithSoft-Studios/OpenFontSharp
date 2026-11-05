using System.Buffers.Binary;
using OpenFontSharp.Shaping;

namespace OpenFontSharp.Tests;

public class SyntheticGposShapingTests
{
    private static byte[] LoadRobotoBytes()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Resources", "Fonts",
            "TTF", "Roboto", "Roboto-Regular.ttf");
        return File.ReadAllBytes(path);
    }

    private static Typeface ReadFont(byte[] bytes)
    {
        var typeface = new OpenFontReader().Read(new MemoryStream(bytes));
        Assert.NotNull(typeface);
        return typeface;
    }

    [Fact]
    public void PairPositionFormat1_AppliesBothValueRecordsWhenEnabled()
    {
        byte[] source = LoadRobotoBytes();
        Typeface original = ReadFont(source);
        var baseline = BasicLatinShaper.Shape(original, "AV");
        ushort first = baseline.GlyphIds[0];
        ushort second = baseline.GlyphIds[1];
        byte[] gpos = BuildGpos("kern", 2, BuildPairFormat1(first, second));
        Typeface typeface = ReadFont(ReplaceTable(source, "GPOS", gpos));
        typeface.GPOSTable!.LookupList[0].LookupType.Should().Be(2);
        typeface.GPOSTable.FeatureList!.featureTables[0].TagName.Should().Be("kern");
        typeface.GPOSTable.FeatureList.featureTables[0].LookupListIndices.Should().Equal(0);
        typeface.GPOSTable.LookupList[0].SubTables[0].Should().NotBeOfType<OpenFontSharp.Tables.AdvancedLayout.GPOS.UnImplementedLookupSubTable>();

        var result = BasicLatinShaper.Shape(typeface, "AV", new ShapingOptions { EnableGposPositioning = true });

        result.GlyphIds.Should().Equal(first, second);
        result.XOffsets.Should().Equal(0, 12);
        result.Advances[0].Should().Be(typeface.GetAdvanceWidthFromGlyphIndex(first) - 80);
        result.Advances[1].Should().Be(typeface.GetAdvanceWidthFromGlyphIndex(second) + 6);
        result.YOffsets.Should().Equal(0, 0);
    }

    [Fact]
    public void PairPositionFormat2_AppliesClassPairAdjustmentsWhenEnabled()
    {
        byte[] source = LoadRobotoBytes();
        Typeface original = ReadFont(source);
        var baseline = BasicLatinShaper.Shape(original, "AV");
        ushort first = baseline.GlyphIds[0];
        ushort second = baseline.GlyphIds[1];
        byte[] gpos = BuildGpos("kern", 2, BuildPairFormat2(first, second));
        Typeface typeface = ReadFont(ReplaceTable(source, "GPOS", gpos));

        var result = BasicLatinShaper.Shape(typeface, "AV", new ShapingOptions { EnableGposPositioning = true });

        result.Advances[0].Should().Be(typeface.GetAdvanceWidthFromGlyphIndex(first) - 40);
        result.Advances[1].Should().Be(typeface.GetAdvanceWidthFromGlyphIndex(second) + 3);
        result.XOffsets.Should().Equal(0, 5);
    }

    [Fact]
    public void MarkToBasePositionsCombiningMarkWhenEnabled()
    {
        byte[] source = LoadRobotoBytes();
        Typeface original = ReadFont(source);
        var baseline = BasicLatinShaper.Shape(original, "A\u0301");
        ushort baseGlyph = baseline.GlyphIds[0];
        ushort markGlyph = baseline.GlyphIds[1];
        byte[] gpos = BuildGpos("mark", 4, BuildMarkToBase(baseGlyph, markGlyph));
        Typeface typeface = ReadFont(ReplaceTable(source, "GPOS", gpos));
        typeface.GPOSTable!.LookupList[0].LookupType.Should().Be(4);
        typeface.GPOSTable.FeatureList!.featureTables[0].TagName.Should().Be("mark");

        var result = BasicLatinShaper.Shape(typeface, "A\u0301", new ShapingOptions { EnableGposPositioning = true });

        result.GlyphIds.Should().HaveCount(2);
        result.XOffsets[1].Should().Be(46 - baseline.Advances[0]);
        result.YOffsets[1].Should().Be(94);
    }

    [Fact]
    public void FontWithoutGposAndMalformedLookupOffsetDoNotChangeLegacyShaping()
    {
        byte[] source = LoadRobotoBytes();
        Typeface baselineFont = ReadFont(source);
        var baseline = BasicLatinShaper.Shape(baselineFont, "AV");

        Typeface noGpos = ReadFont(RemoveTable(source, "GPOS"));
        var noGposResult = BasicLatinShaper.Shape(noGpos, "AV", new ShapingOptions { EnableGposPositioning = true });
        noGposResult.Advances.Should().Equal(baseline.Advances);
        noGposResult.XOffsets.Should().Equal(0, 0);

        ushort first = baselineFont.GetGlyphIndex('A');
        ushort second = baselineFont.GetGlyphIndex('V');
        byte[] malformed = BuildGpos("kern", 2, BuildPairFormat1(first, second), malformedLookupOffset: true);
        Typeface malformedFont = ReadFont(ReplaceTable(source, "GPOS", malformed));
        var malformedResult = BasicLatinShaper.Shape(malformedFont, "AV", new ShapingOptions { EnableGposPositioning = true });
        malformedResult.Advances.Should().Equal(baseline.Advances);
    }

    private static byte[] BuildPairFormat1(ushort first, ushort second)
    {
        var bytes = new List<byte>();
        U16(bytes, 1);
        U16(bytes, 22);
        U16(bytes, 4);
        U16(bytes, 5);
        U16(bytes, 1);
        U16(bytes, 12);
        U16(bytes, 1);
        U16(bytes, second);
        S16(bytes, -80);
        S16(bytes, 12);
        S16(bytes, 6);
        U16(bytes, 1);
        U16(bytes, 1);
        U16(bytes, first);
        return bytes.ToArray();
    }

    private static byte[] BuildPairFormat2(ushort first, ushort second)
    {
        var bytes = new List<byte>();
        U16(bytes, 2);
        U16(bytes, 0);
        U16(bytes, 4);
        U16(bytes, 5);
        U16(bytes, 0);
        U16(bytes, 0);
        U16(bytes, 2);
        U16(bytes, 2);

        for (int class1 = 0; class1 < 2; class1++)
        {
            for (int class2 = 0; class2 < 2; class2++)
            {
                S16(bytes, (short)(class1 == 1 && class2 == 1 ? -40 : 0));
                S16(bytes, (short)(class1 == 1 && class2 == 1 ? 5 : 0));
                S16(bytes, (short)(class1 == 1 && class2 == 1 ? 3 : 0));
            }
        }

        ushort classDef1Offset = (ushort)bytes.Count;
        ClassDef1(bytes, first, 1);
        ushort classDef2Offset = (ushort)bytes.Count;
        ClassDef1(bytes, second, 1);
        ushort coverageOffset = (ushort)bytes.Count;
        U16(bytes, 1);
        U16(bytes, 1);
        U16(bytes, first);
        SetU16(bytes, 2, coverageOffset);
        SetU16(bytes, 8, classDef1Offset);
        SetU16(bytes, 10, classDef2Offset);
        return bytes.ToArray();
    }

    private static void ClassDef1(List<byte> bytes, ushort glyph, ushort classValue)
    {
        U16(bytes, 1);
        U16(bytes, glyph);
        U16(bytes, 1);
        U16(bytes, classValue);
    }

    private static byte[] BuildMarkToBase(ushort baseGlyph, ushort markGlyph)
    {
        var bytes = new List<byte>();
        U16(bytes, 1);
        U16(bytes, 0);
        U16(bytes, 0);
        U16(bytes, 1);
        U16(bytes, 0);
        U16(bytes, 0);

        ushort markArrayOffset = (ushort)bytes.Count;
        U16(bytes, 1);
        U16(bytes, 0);
        U16(bytes, 6);
        U16(bytes, 1);
        S16(bytes, 4);
        S16(bytes, 6);

        ushort baseArrayOffset = (ushort)bytes.Count;
        U16(bytes, 1);
        U16(bytes, 4);
        U16(bytes, 1);
        S16(bytes, 50);
        S16(bytes, 100);

        ushort markCoverageOffset = (ushort)bytes.Count;
        U16(bytes, 1);
        U16(bytes, 1);
        U16(bytes, markGlyph);
        ushort baseCoverageOffset = (ushort)bytes.Count;
        U16(bytes, 1);
        U16(bytes, 1);
        U16(bytes, baseGlyph);
        SetU16(bytes, 2, markCoverageOffset);
        SetU16(bytes, 4, baseCoverageOffset);
        SetU16(bytes, 8, markArrayOffset);
        SetU16(bytes, 10, baseArrayOffset);
        return bytes.ToArray();
    }

    private static byte[] BuildGpos(string featureTag, ushort lookupType, byte[] subtable, bool malformedLookupOffset = false)
    {
        var bytes = new List<byte>();
        U16(bytes, 1);
        U16(bytes, 0);
        U16(bytes, 10);
        U16(bytes, 12);
        U16(bytes, 26);
        U16(bytes, 0);
        U16(bytes, 1);
        U32(bytes, Tag(featureTag));
        U16(bytes, 8);
        U16(bytes, 0);
        U16(bytes, 1);
        U16(bytes, 0);
        U16(bytes, 1);
        U16(bytes, 4);
        U16(bytes, lookupType);
        U16(bytes, 0);
        U16(bytes, 1);
        U16(bytes, malformedLookupOffset ? ushort.MaxValue : (ushort)8);
        if (!malformedLookupOffset)
            bytes.AddRange(subtable);
        return bytes.ToArray();
    }

    private static byte[] ReplaceTable(byte[] font, string tag, byte[] replacement)
    {
        byte[] result = [.. font];
        int tableCount = ReadU16(result, 4);
        int record = FindTableRecord(result, tableCount, tag);
        int tableOffset = (result.Length + 3) & ~3;
        Array.Resize(ref result, tableOffset + replacement.Length);
        replacement.CopyTo(result, tableOffset);
        WriteU32(result, record + 8, (uint)tableOffset);
        WriteU32(result, record + 12, (uint)replacement.Length);
        return result;
    }

    private static byte[] RemoveTable(byte[] font, string tag)
    {
        int tableCount = ReadU16(font, 4);
        int record = FindTableRecord(font, tableCount, tag);
        byte[] result = [.. font];
        int directoryEnd = 12 + tableCount * 16;
        Array.Copy(font, record + 16, result, record, directoryEnd - record - 16);
        WriteU16(result, 4, (ushort)(tableCount - 1));
        return result;
    }

    private static int FindTableRecord(byte[] font, int tableCount, string tag)
    {
        byte[] tagBytes = System.Text.Encoding.ASCII.GetBytes(tag);
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            if (font.AsSpan(record, 4).SequenceEqual(tagBytes))
                return record;
        }
        throw new InvalidOperationException($"Table {tag} was not found.");
    }

    private static uint Tag(string tag) =>
        BinaryPrimitives.ReadUInt32BigEndian(System.Text.Encoding.ASCII.GetBytes(tag));

    private static int ReadU16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));

    private static void U16(List<byte> bytes, ushort value)
    {
        Span<byte> data = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(data, value);
        bytes.AddRange(data.ToArray());
    }

    private static void S16(List<byte> bytes, short value) => U16(bytes, unchecked((ushort)value));

    private static void U32(List<byte> bytes, uint value)
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        bytes.AddRange(data.ToArray());
    }

    private static void SetU16(List<byte> bytes, int offset, ushort value)
    {
        Span<byte> data = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(data, value);
        bytes[offset] = data[0];
        bytes[offset + 1] = data[1];
    }

    private static void WriteU16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);

    private static void WriteU32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
}
