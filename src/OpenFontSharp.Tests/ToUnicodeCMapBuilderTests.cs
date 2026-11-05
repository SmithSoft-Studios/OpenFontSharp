using System.Text;
using OpenFontSharp.Metrics;

namespace OpenFontSharp.Tests;

public class ToUnicodeCMapBuilderTests
{
    [Fact]
    public void Build_BmpScalar_ShouldMatchExpectedCMap()
    {
        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(
            new Dictionary<int, int> { [42] = 0x41 }));

        Assert.Equal(ExpectedCMap(
            "1 beginbfchar",
            "<002A> <0041>",
            "endbfchar"), cmap);
    }

    [Fact]
    public void Build_BmpSequence_ShouldMatchExpectedCMap()
    {
        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(
            new Dictionary<int, int[]> { [42] = new[] { 0x66, 0x66, 0x69 } }));

        Assert.Equal(ExpectedCMap(
            "1 beginbfchar",
            "<002A> <006600660069>",
            "endbfchar"), cmap);
    }

    [Fact]
    public void Build_SupplementaryScalar_ShouldMatchExpectedCMap()
    {
        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(
            new Dictionary<int, int> { [42] = 0x1F600 }));

        Assert.Equal(ExpectedCMap(
            "1 beginbfchar",
            "<002A> <D83DDE00>",
            "endbfchar"), cmap);
    }

    [Fact]
    public void Build_SupplementarySequence_ShouldMatchExpectedCMap()
    {
        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(
            new Dictionary<int, int[]> { [42] = new[] { 0x41, 0x1F600, 0x1F642 } }));

        Assert.Equal(ExpectedCMap(
            "1 beginbfchar",
            "<002A> <0041D83DDE00D83DDE42>",
            "endbfchar"), cmap);
    }

    [Fact]
    public void Build_ConsecutiveMappings_ShouldUseBfRange()
    {
        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(
            new Dictionary<int, int>
            {
                [0x10] = 0x41,
                [0x11] = 0x42,
                [0x12] = 0x43
            }));

        Assert.Equal(ExpectedCMap(
            "1 beginbfrange",
            "<0010> <0012> <0041>",
            "endbfrange"), cmap);
    }

    [Theory]
    [InlineData(0x1F7FF, 0x1F800, "D83DDFFF", "D83EDC00")]
    [InlineData(0xFFFF, 0x10000, "FFFF", "D800DC00")]
    [InlineData(0x10FF, 0x1100, "10FF", "1100")]
    public void Build_NonIncrementingUtf16Destinations_ShouldUseBfChar(
        int first, int second, string firstDestination, string secondDestination)
    {
        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(
            new Dictionary<int, int>
            {
                [0x10] = first,
                [0x11] = second
            }));

        Assert.Equal(ExpectedCMap(
            "2 beginbfchar",
            $"<0010> <{firstDestination}>",
            $"<0011> <{secondDestination}>",
            "endbfchar"), cmap);
    }

    [Fact]
    public void Build_SequenceWithInvalidScalar_ShouldReject()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ToUnicodeCMapBuilder.Build(new Dictionary<int, int[]> { [42] = new[] { 0x41, 0xD800 } }));

        Assert.Equal("glyphToUnicode", exception.ParamName);
    }

    [Fact]
    public void Build_EmptySequence_ShouldReject()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ToUnicodeCMapBuilder.Build(new Dictionary<int, int[]> { [42] = Array.Empty<int>() }));

        Assert.Equal("glyphToUnicode", exception.ParamName);
    }

    [Fact]
    public void Build_SequenceWithOutOfRangeCid_ShouldReject()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ToUnicodeCMapBuilder.Build(new Dictionary<int, int[]> { [0x10000] = new[] { 0x41 } }));

        Assert.Equal("glyphToUnicode", exception.ParamName);
    }

    [Theory]
    [InlineData(0, "0000")]
    [InlineData(0x41, "0041")]
    [InlineData(0xFFFF, "FFFF")]
    [InlineData(0x10000, "D800DC00")]
    [InlineData(0x10780, "D801DF80")]
    [InlineData(0x1F600, "D83DDE00")]
    [InlineData(0x10FFFF, "DBFFDFFF")]
    public void Build_UnicodeScalar_ShouldWriteUtf16BeDestination(int scalar, string destination)
    {
        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(
            new Dictionary<int, int> { [42] = scalar }));

        Assert.Contains($"<002A> <{destination}>", cmap);
        Assert.DoesNotContain("<FEFF", cmap);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0xD800)]
    [InlineData(0xDFFF)]
    [InlineData(0x110000)]
    [InlineData(int.MaxValue)]
    public void Build_InvalidUnicodeScalar_ShouldReject(int scalar)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ToUnicodeCMapBuilder.Build(new Dictionary<int, int> { [42] = scalar }));

        Assert.Equal("glyphToUnicode", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x10000)]
    [InlineData(int.MaxValue)]
    public void Build_CidOutsideDeclaredTwoByteCodeSpace_ShouldReject(int cid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ToUnicodeCMapBuilder.Build(new Dictionary<int, int> { [cid] = 0x41 }));
    }

    [Fact]
    public void Build_NullMapping_ShouldRejectExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => ToUnicodeCMapBuilder.Build(null!));
    }

    [Fact]
    public void Build_UnsortedMappings_ShouldSortCidsAndSplitIntoHundredEntryRanges()
    {
        var mappings = Enumerable.Range(0, 201).Reverse().ToDictionary(cid => cid, cid => 0x10000 + cid);

        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(mappings));

        Assert.Equal(2, cmap.Split("1 beginbfrange").Length - 1);
        Assert.Contains("1 beginbfchar", cmap);
        Assert.Contains("<0000> <0063> <D800DC00>", cmap);
        Assert.Contains("<0064> <00C7> <D800DC64>", cmap);
        Assert.True(cmap.IndexOf("<0000> <0063>", StringComparison.Ordinal)
            < cmap.IndexOf("<00C8> <D800DCC8>", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_MoreThanHundredNonconsecutiveMappings_ShouldSplitBfCharBlocks()
    {
        var mappings = Enumerable.Range(0, 201).Reverse().ToDictionary(cid => cid * 2, _ => 0x41);

        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(mappings));

        Assert.Equal(2, cmap.Split("100 beginbfchar").Length - 1);
        Assert.Contains("1 beginbfchar", cmap);
    }

    [Fact]
    public void Build_MaximumCid_ShouldRetainTwoByteSourceCode()
    {
        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(
            new Dictionary<int, int> { [0xFFFF] = 0x10FFFF }));

        Assert.Contains("<FFFF> <DBFFDFFF>", cmap);
    }

    private static string ExpectedCMap(params string[] mappingBlock)
    {
        var lines = new List<string>
        {
            "/CIDInit /ProcSet findresource begin",
            "12 dict begin",
            "begincmap",
            "/CIDSystemInfo",
            "<< /Registry (Adobe)",
            "/Ordering (UCS)",
            "/Supplement 0",
            ">> def",
            "/CMapName /Adobe-Identity-UCS def",
            "/CMapType 2 def",
            "1 begincodespacerange",
            "<0000> <FFFF>",
            "endcodespacerange"
        };
        lines.AddRange(mappingBlock);
        lines.AddRange(new[]
        {
            "endcmap",
            "CMapName currentdict /CMap defineresource pop",
            "end",
            "end"
        });
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
