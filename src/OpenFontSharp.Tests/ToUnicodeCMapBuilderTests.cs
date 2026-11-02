using System.Text;
using OpenFontSharp.Metrics;

namespace OpenFontSharp.Tests;

public class ToUnicodeCMapBuilderTests
{
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
    public void Build_UnsortedMappings_ShouldSortCidsAndSplitIntoHundredEntryBlocks()
    {
        var mappings = Enumerable.Range(0, 201).Reverse().ToDictionary(cid => cid, cid => 0x10000 + cid);

        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(mappings));

        Assert.Equal(2, cmap.Split("100 beginbfchar").Length - 1);
        Assert.Contains("1 beginbfchar", cmap);
        Assert.True(cmap.IndexOf("<0000> <D800DC00>", StringComparison.Ordinal)
            < cmap.IndexOf("<00C8> <D800DCC8>", StringComparison.Ordinal));
        Assert.Contains("<0000> <FFFF>", cmap);
    }

    [Fact]
    public void Build_MaximumCid_ShouldRetainTwoByteSourceCode()
    {
        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(
            new Dictionary<int, int> { [0xFFFF] = 0x10FFFF }));

        Assert.Contains("<FFFF> <DBFFDFFF>", cmap);
    }
}
