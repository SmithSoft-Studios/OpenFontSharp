using OpenFontSharp.Subsetting;

namespace OpenFontSharp.Tests.Subsetting;

/// <summary>
/// CFF subsetting features exercised on minimal hand-built CFF tables
/// because no shipped font uses them.
/// </summary>
public class CffSubsetBuilderTests
{
    private const byte EndChar = 14;
    private const int OpPrivate = 18;
    private const int OpSubrs = 19;
    private const int OpCharset = 15;
    private const int OpCharStrings = 17;
    private const int OpFdArray = 1236;
    private const int OpFdSelect = 1237;

    [Fact]
    public void SeacComposite_ThrowsNotSupported()
    {
        // "0 0 65 66 endchar": the deprecated seac form, which builds the glyph from
        // the StandardEncoding glyphs 'A' and 'B' that the subset would not contain.
        var cff = MinimalCff(seacGlyph: [Int(0), Int(0), Int(65), Int(66), EndChar]);

        var act = () => CffSubsetBuilder.Build(cff, [0, 1]);

        act.Should().Throw<NotSupportedException>().WithMessage("*seac*");
    }

    [Fact]
    public void SeacComposite_NotRetained_DoesNotThrow()
    {
        var cff = MinimalCff(seacGlyph: [Int(0), Int(0), Int(65), Int(66), EndChar]);

        var act = () => CffSubsetBuilder.Build(cff, [0]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EndCharWithWidthOnly_IsNotSeac()
    {
        var cff = MinimalCff(seacGlyph: [Int(50), EndChar]);

        var act = () => CffSubsetBuilder.Build(cff, [0, 1]);

        act.Should().NotThrow();
    }

    [Fact]
    public void CidKeyedFont_RetainsUsedFontDictsAndRewritesFormat3FdSelectAndCharset()
    {
        var source = MinimalCidCff(fdSelectFormat: 3);

        var subset = CffSubsetBuilder.Build(source, [0, 2, 4]);
        var actual = InspectCidCff(subset);
        var original = InspectCidCff(source);

        actual.FontDicts.Should().HaveCount(2);
        actual.FdSelectFormat.Should().Be(3);
        actual.Selectors.Should().Equal(0, 1, 1);
        actual.Cids.Should().Equal(0, 30, 90);
        actual.CharStrings.Select(Convert.ToHexString).Should().Equal(
            new[] { original.CharStrings[0], original.CharStrings[2], original.CharStrings[4] }
                .Select(Convert.ToHexString));
        actual.LocalSubrs.Should().HaveCount(2);
        actual.LocalSubrs[0].Select(Convert.ToHexString).Should().Equal(original.LocalSubrs[0].Select(Convert.ToHexString));
        actual.LocalSubrs[1].Select(Convert.ToHexString).Should().Equal(original.LocalSubrs[1].Select(Convert.ToHexString));
    }

    [Fact]
    public void CidKeyedFont_RewritesFormat0FdSelect()
    {
        var source = MinimalCidCff(fdSelectFormat: 0);

        var subset = CffSubsetBuilder.Build(source, [0, 2, 4]);
        var actual = InspectCidCff(subset);
        var original = InspectCidCff(source);

        actual.FontDicts.Should().HaveCount(2);
        actual.FdSelectFormat.Should().Be(0);
        actual.Selectors.Should().Equal(0, 1, 1);
        actual.Cids.Should().Equal(0, 30, 90);
        actual.CharStrings.Select(Convert.ToHexString).Should().Equal(
            new[] { original.CharStrings[0], original.CharStrings[2], original.CharStrings[4] }
                .Select(Convert.ToHexString));
    }

    [Fact]
    public void CidKeyedFont_SubsetReadsThroughOpenFontReader()
    {
        var source = MinimalCidCff(fdSelectFormat: 3);
        var font = MinimalCffOpenType(source, glyphCount: 5);

        var subset = FontSubsetter.Subset(font, new HashSet<ushort> { 2, 4 });

        subset.GlyphIdMap.Should().Equal(new Dictionary<ushort, ushort> { [0] = 0, [2] = 1, [4] = 2 });
        var reread = FontFiles.Load(subset.FontData);
        reread.GlyphCount.Should().Be(3);
        reread.IsCffFont.Should().BeTrue();
        SfntInspector.Parse(subset.FontData).SfntVersion.Should().Be(0x4F54544Fu);

        var cff = InspectCidCff(SfntInspector.Parse(subset.FontData).Bytes("CFF "));
        cff.FontDicts.Should().HaveCount(2);
        cff.Selectors.Should().Equal(0, 1, 1);
        cff.Cids.Should().Equal(0, 30, 90);
    }

    [Fact]
    public void CidKeyedFont_DropsUnusedFontDictWhenRetainedGlyphsShareOne()
    {
        var source = MinimalCidCff(fdSelectFormat: 3);

        var subset = CffSubsetBuilder.Build(source, [0, 3]);
        var actual = InspectCidCff(subset);

        actual.FontDicts.Should().HaveCount(1);
        actual.TopDict.Should().NotContainKey(OpFdSelect);
        actual.Selectors.Should().Equal(0, 0);
        actual.Cids.Should().Equal(0, 45);
    }

    private static byte Int(int value) => (byte)(value + 139); // one-byte operand, -107..107

    /// <summary>A two-glyph name-keyed CFF table (ISOAdobe charset, empty Private DICT).</summary>
    private static byte[] MinimalCff(byte[] seacGlyph, bool cidKeyed = false)
    {
        byte[] header = [1, 0, 4, 1];
        byte[] nameIndex = [0, 1, 1, 1, 2, (byte)'T'];
        byte[] ros = cidKeyed ? [Int(0), Int(0), Int(0), 12, 30] : [];
        byte[] TopDict(int charStrings, int priv) => [.. ros, .. Long(charStrings), 17, Int(0), .. Long(priv), 18];
        int topIndexLength = 5 + TopDict(0, 0).Length;
        byte[] emptyIndex = [0, 0];

        int charStringsOffset = header.Length + nameIndex.Length + topIndexLength + emptyIndex.Length + emptyIndex.Length;
        byte[] charStrings = [0, 2, 1, 1, 2, (byte)(2 + seacGlyph.Length), EndChar, .. seacGlyph];
        int privateOffset = charStringsOffset + charStrings.Length;

        byte[] topDict = TopDict(charStringsOffset, privateOffset);
        byte[] topIndex = [0, 1, 1, 1, (byte)(topDict.Length + 1), .. topDict];

        return [.. header, .. nameIndex, .. topIndex, .. emptyIndex, .. emptyIndex, .. charStrings];
    }

    private static byte[] MinimalCidCff(byte fdSelectFormat)
    {
        byte[] header = [1, 0, 4, 1];
        byte[] nameIndex = [0, 1, 1, 1, 2, (byte)'T'];
        byte[] emptyIndex = [0, 0];
        byte[][] charStrings = Enumerable.Range(0, 5).Select(i => new[] { Int(i + 1), EndChar }).ToArray();
        byte[] charStringsIndex = WriteIndex(charStrings);
        byte[] charset = [0, 0, 12, 0, 30, 0, 45, 0, 90];
        byte[] fdSelect = fdSelectFormat == 0
            ? [0, 0, 1, 1, 0, 1]
            : [3, 0, 4, 0, 0, 0, 0, 2, 1, 0, 3, 0, 0, 4, 1, 0, 5];

        byte[] privateDict = [.. Long(6), OpSubrs];
        byte[][] localSubrs = [WriteIndex([[11]]), WriteIndex([[12, 11]])];
        byte[][] fontDicts =
        [
            [.. Long(privateDict.Length), .. Long(0), OpPrivate],
            [.. Long(privateDict.Length), .. Long(0), OpPrivate],
        ];
        byte[] fdArray = WriteIndex(fontDicts);

        byte[] Top(int charsetOffset, int charStringsOffset, int fdArrayOffset, int fdSelectOffset) =>
        [
            Int(0), Int(0), Int(0), 12, 30, // ROS
            .. Long(charsetOffset), OpCharset,
            .. Long(charStringsOffset), OpCharStrings,
            .. Long(fdArrayOffset), 12, 36,
            .. Long(fdSelectOffset), 12, 37,
        ];

        int topDictLength = Top(0, 0, 0, 0).Length;
        int topIndexLength = IndexLength(topDictLength);
        int prefixLength = header.Length + nameIndex.Length + topIndexLength + emptyIndex.Length + emptyIndex.Length;
        int charsetOffset = prefixLength;
        int fdSelectOffset = charsetOffset + charset.Length;
        int charStringsOffset = fdSelectOffset + fdSelect.Length;
        int fdArrayOffset = charStringsOffset + charStringsIndex.Length;
        int private0Offset = fdArrayOffset + fdArray.Length;
        int private1Offset = private0Offset + privateDict.Length + localSubrs[0].Length;
        fontDicts =
        [
            [.. Long(privateDict.Length), .. Long(private0Offset), OpPrivate],
            [.. Long(privateDict.Length), .. Long(private1Offset), OpPrivate],
        ];
        fdArray = WriteIndex(fontDicts);
        byte[] topDict = Top(charsetOffset, charStringsOffset, fdArrayOffset, fdSelectOffset);
        byte[] topIndex = WriteIndex([topDict]);

        return
        [
            .. header, .. nameIndex, .. topIndex, .. emptyIndex, .. emptyIndex,
            .. charset, .. fdSelect, .. charStringsIndex, .. fdArray,
            .. privateDict, .. localSubrs[0], .. privateDict, .. localSubrs[1],
        ];
    }

    private static byte[] MinimalCffOpenType(byte[] cff, ushort glyphCount)
    {
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
        hhea[34] = (byte)(glyphCount >> 8);
        hhea[35] = (byte)glyphCount;
        writer.AddTable("hhea", hhea);

        byte[] maxp = [0, 0, 0x50, 0, (byte)(glyphCount >> 8), (byte)glyphCount];
        writer.AddTable("maxp", maxp);
        writer.AddTable("hmtx", new byte[glyphCount * 4]);
        writer.AddTable("cmap", MinimalCmap());
        writer.AddTable("name", [0, 0, 0, 0, 0, 6]);
        writer.AddTable("post", new byte[32]);
        writer.AddTable("CFF ", cff);
        return writer.Build();
    }

    private static byte[] MinimalCmap() =>
    [
        0, 0, 0, 1, 0, 3, 0, 1, 0, 0, 0, 12,
        0, 4, 0, 24, 0, 0, 0, 2, 0, 2, 0, 0, 0, 0,
        255, 255, 0, 0, 255, 255, 0, 1, 0, 0, 0, 0,
    ];

    private static CidCffInfo InspectCidCff(byte[] cff)
    {
        int headerEnd = cff[2];
        var nameIndex = ReadIndex(cff, headerEnd);
        var topIndex = ReadIndex(cff, nameIndex.End);
        var stringIndex = ReadIndex(cff, topIndex.End);
        var globalSubrs = ReadIndex(cff, stringIndex.End);
        var topDict = ReadDict(topIndex.Items[0]);
        var charStrings = ReadIndex(cff, topDict[OpCharStrings][0]);
        var fdArray = ReadIndex(cff, topDict[OpFdArray][0]);
        int[] selectors = topDict.TryGetValue(OpFdSelect, out var fdSelectOperand)
            ? ReadFdSelect(cff, fdSelectOperand[0], charStrings.Items.Length)
            : new int[charStrings.Items.Length];
        var cids = ReadCharset(cff, topDict[OpCharset][0], charStrings.Items.Length);
        var privateDicts = fdArray.Items.Select(item =>
        {
            var dict = ReadDict(item);
            return (dict[OpPrivate][1], dict[OpPrivate][0]);
        }).ToArray();
        var localSubrs = privateDicts.Select((p) =>
        {
            var dict = ReadDict(cff.AsSpan(p.Item1, p.Item2).ToArray());
            return dict.TryGetValue(OpSubrs, out var subrs)
                ? ReadIndex(cff, p.Item1 + subrs[0]).Items
                : [];
        }).ToArray();
        _ = globalSubrs;

        int? fdSelectFormat = topDict.TryGetValue(OpFdSelect, out fdSelectOperand)
            ? cff[fdSelectOperand[0]]
            : null;
        return new CidCffInfo(topDict, charStrings.Items, cids, selectors, fdArray.Items, localSubrs, fdSelectFormat);
    }

    private static int[] ReadFdSelect(byte[] cff, int offset, int glyphCount)
    {
        if (cff[offset] == 0)
            return cff.AsSpan(offset + 1, glyphCount).ToArray().Select(b => (int)b).ToArray();

        if (cff[offset] != 3)
            throw new InvalidDataException("Unexpected FDSelect format.");

        int rangeCount = ReadU16(cff, offset + 1);
        var selectors = new int[glyphCount];
        int cursor = offset + 3;
        for (int range = 0; range < rangeCount; range++)
        {
            int first = ReadU16(cff, cursor);
            int fd = cff[cursor + 2];
            int end = ReadU16(cff, cursor + 3);
            for (int glyph = first; glyph < Math.Min(end, glyphCount); glyph++)
                selectors[glyph] = fd;
            cursor += 3;
        }
        return selectors;
    }

    private static ushort[] ReadCharset(byte[] cff, int offset, int glyphCount)
    {
        var cids = new ushort[glyphCount];
        for (int glyph = 1; glyph < glyphCount; glyph++)
            cids[glyph] = (ushort)ReadU16(cff, offset + 1 + ((glyph - 1) * 2));
        return cids;
    }

    private static Dictionary<int, int[]> ReadDict(byte[] data)
    {
        var result = new Dictionary<int, int[]>();
        var operands = new List<int>();
        for (int pos = 0; pos < data.Length;)
        {
            byte value = data[pos++];
            if (value == 29)
            {
                operands.Add((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
                pos += 4;
            }
            else if (value == 12)
                result[1200 + data[pos++]] = operands.ToArray();
            else if (value <= 21)
                result[value] = operands.ToArray();
            else
                operands.Add(value - 139);
            if (value <= 21 && value != 29 && value != 12)
                operands.Clear();
            if (value == 12 || value <= 21 && value != 29)
                operands.Clear();
        }
        return result;
    }

    private static (byte[][] Items, int End) ReadIndex(byte[] data, int start)
    {
        int count = ReadU16(data, start);
        if (count == 0)
            return ([], start + 2);
        int offSize = data[start + 2];
        int offsetsStart = start + 3;
        int dataStart = offsetsStart + ((count + 1) * offSize) - 1;
        var items = new byte[count][];
        int ReadOffset(int i)
        {
            int result = 0;
            for (int b = 0; b < offSize; b++)
                result = (result << 8) | data[offsetsStart + (i * offSize) + b];
            return result;
        }
        for (int i = 0; i < count; i++)
            items[i] = data.AsSpan(dataStart + ReadOffset(i), ReadOffset(i + 1) - ReadOffset(i)).ToArray();
        return (items, dataStart + ReadOffset(count));
    }

    private static byte[] WriteIndex(IReadOnlyList<byte[]> items)
    {
        if (items.Count == 0)
            return [0, 0];
        int dataLength = items.Sum(item => item.Length);
        int offSize = dataLength + 1 <= 255 ? 1 : dataLength + 1 <= 65535 ? 2 : 3;
        using var output = new MemoryStream();
        WriteU16(output, (ushort)items.Count);
        output.WriteByte((byte)offSize);
        int nextOffset = 1;
        for (int i = 0; i <= items.Count; i++)
        {
            for (int b = offSize - 1; b >= 0; b--)
                output.WriteByte((byte)(nextOffset >> (8 * b)));
            if (i < items.Count)
                nextOffset += items[i].Length;
        }
        foreach (var item in items)
            output.Write(item);
        return output.ToArray();
    }

    private static int IndexLength(int itemLength) => 5 + itemLength;

    private static int ReadU16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

    private static void WriteU16(Stream output, ushort value)
    {
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private sealed record CidCffInfo(
        Dictionary<int, int[]> TopDict,
        byte[][] CharStrings,
        ushort[] Cids,
        int[] Selectors,
        byte[][] FontDicts,
        byte[][][] LocalSubrs,
        int? FdSelectFormat);

    private static byte[] Long(int value) => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
