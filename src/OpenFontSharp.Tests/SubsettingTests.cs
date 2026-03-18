using OpenFontSharp.Subsetting;

namespace OpenFontSharp.Tests;

/// <summary>
/// Tests for font subsetting within the OpenFontSharp test project.
/// </summary>
public class SubsettingTests
{
    private static string RobotoPath => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Resources", "Fonts",
        "TTF", "Roboto", "Roboto-Regular.ttf");

    private static Typeface LoadRoboto()
    {
        var reader = new OpenFontReader();
        using var stream = File.OpenRead(RobotoPath);
        return reader.Read(stream);
    }

    [Fact]
    public void Subset_ProducesSmallerFont()
    {
        var typeface = LoadRoboto();
        var originalSize = new FileInfo(RobotoPath).Length;

        var usedGlyphs = new HashSet<ushort>();
        foreach (char c in "Hello World")
            usedGlyphs.Add(typeface.GetGlyphIndex(c));

        var result = FontSubsetter.Subset(typeface, usedGlyphs);

        result.FontData.Length.Should().BeLessThan((int)originalSize);
        result.RetainedGlyphCount.Should().BeLessThan(typeface.GlyphCount);
    }

    [Fact]
    public void Subset_GlyphIdMapIsContiguous()
    {
        var typeface = LoadRoboto();
        var usedGlyphs = new HashSet<ushort>
        {
            typeface.GetGlyphIndex('A'),
            typeface.GetGlyphIndex('Z')
        };

        var result = FontSubsetter.Subset(typeface, usedGlyphs);

        var newIds = result.GlyphIdMap.Values.OrderBy(x => x).ToList();
        for (int i = 0; i < newIds.Count; i++)
            newIds[i].Should().Be((ushort)i);
    }

    [Fact]
    public void Subset_AlwaysIncludesNotdef()
    {
        var typeface = LoadRoboto();
        var result = FontSubsetter.Subset(typeface, new HashSet<ushort> { typeface.GetGlyphIndex('X') });

        result.GlyphIdMap.Should().ContainKey((ushort)0);
        result.GlyphIdMap[0].Should().Be(0);
    }

    [Fact]
    public void TtfWriter_ProducesValidSignature()
    {
        var writer = new TtfWriter();
        writer.AddTable("head", new byte[54]);

        var output = writer.Build();

        output[0].Should().Be(0x00);
        output[1].Should().Be(0x01);
        output[2].Should().Be(0x00);
        output[3].Should().Be(0x00);
    }

    [Fact]
    public void GlyphCollector_IncludesNotdefAndRequestedGlyphs()
    {
        var typeface = LoadRoboto();
        var glyphA = typeface.GetGlyphIndex('A');

        var result = GlyphCollector.Collect(typeface, new HashSet<ushort> { glyphA });

        result.Should().Contain((ushort)0);
        result.Should().Contain(glyphA);
        result.Should().BeInAscendingOrder();
    }
}
