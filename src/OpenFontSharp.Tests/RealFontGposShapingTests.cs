using OpenFontSharp.Shaping;

namespace OpenFontSharp.Tests;

/// <summary>GPOS pair adjustment checked on real fonts, including one that wraps its lookups in extension lookups.</summary>
public class RealFontGposShapingTests
{
    private static Typeface Load(params string[] relative)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "Resources", "Fonts" }.Concat(relative).ToArray());
        using var stream = File.OpenRead(path);
        var typeface = new OpenFontReader().Read(stream);
        Assert.NotNull(typeface);
        return typeface;
    }

    public static IEnumerable<object[]> Fonts()
    {
        yield return new object[] { "Roboto", new[] { "TTF", "Roboto", "Roboto-Regular.ttf" } };
        // Every GPOS lookup in this font is an extension lookup (type 9).
        yield return new object[] { "Shantell Sans", new[] { "TTF", "Shantell_Sans", "static", "ShantellSans-Regular.ttf" } };
    }

    public static IEnumerable<object[]> HarfBuzzPairReferences()
    {
        yield return new object[]
        {
            "Roboto", new[] { "TTF", "Roboto", "Roboto-Regular.ttf" },
            new ushort[] { 37, 58 }, new int[] { 1249, 1303 }
        };
        yield return new object[]
        {
            "Shantell Sans", new[] { "TTF", "Shantell_Sans", "static", "ShantellSans-Regular.ttf" },
            new ushort[] { 4, 164 }, new int[] { 674, 675 }
        };
        yield return new object[]
        {
            "Lato subset", new[] { "TTF", "Lato", "OpenFontSharp-Lato-Positioning.ttf" },
            new ushort[] { 1, 3 }, new int[] { 1246, 1354 }
        };
    }

    public static IEnumerable<object[]> HarfBuzzMarkReferences()
    {
        yield return new object[]
        {
            "Shantell Sans", new[] { "TTF", "Shantell_Sans", "static", "ShantellSans-Regular.ttf" },
            new ushort[] { 4, 630 }, new int[] { 726, 0 }, new int[] { 0, -358 }
        };
        yield return new object[]
        {
            "Lato subset", new[] { "TTF", "Lato", "OpenFontSharp-Lato-Positioning.ttf" },
            new ushort[] { 1, 6 }, new int[] { 1354, 0 }, new int[] { 0, -280 }
        };
    }

    /// <summary>Expected glyph positions were generated with HarfBuzz via uharfbuzz 0.57.0.</summary>
    [Theory]
    [MemberData(nameof(HarfBuzzPairReferences))]
    public void GposPairMatchesHarfBuzzReference(string name, string[] path, ushort[] glyphIds, int[] advances)
    {
        var result = BasicLatinShaper.Shape(
            Load(path), "AV", new ShapingOptions { EnableGposPositioning = true });

        result.GlyphIds.Should().Equal(glyphIds, name);
        result.Advances.Should().Equal(advances, name);
        result.XOffsets.Should().Equal(0, 0);
        result.YOffsets.Should().Equal(0, 0);
    }

    /// <summary>Expected glyph positions were generated with HarfBuzz via uharfbuzz 0.57.0.</summary>
    [Theory]
    [MemberData(nameof(HarfBuzzMarkReferences))]
    public void MarkBaseMatchesHarfBuzzReference(
        string name, string[] path, ushort[] glyphIds, int[] advances, int[] xOffsets)
    {
        var result = BasicLatinShaper.Shape(
            Load(path), "A\u0327", new ShapingOptions { EnableGposPositioning = true });

        result.GlyphIds.Should().Equal(glyphIds, name);
        result.Advances.Should().Equal(advances, name);
        result.XOffsets.Should().Equal(xOffsets, name);
        result.YOffsets.Should().Equal(0, 0);
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public void Gpos_KernsAVAndToOnRealFonts(string name, string[] path)
    {
        var font = Load(path);
        foreach (var text in new[] { "AV", "To" })
        {
            var off = BasicLatinShaper.Shape(font, text);
            var on = BasicLatinShaper.Shape(font, text, new ShapingOptions { EnableGposPositioning = true });

            on.Advances[0].Should().BeLessThan(off.Advances[0], $"{name} should kern \"{text}\"");
            on.Advances[1].Should().Be(off.Advances[1]);
            on.GlyphIds.Should().Equal(off.GlyphIds);
        }
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public void Gpos_IsOffByDefault(string name, string[] path)
    {
        var font = Load(path);

        var defaultResult = BasicLatinShaper.Shape(font, "AV");
        var explicitOff = BasicLatinShaper.Shape(font, "AV", new ShapingOptions());

        defaultResult.Advances.Should().Equal(explicitOff.Advances);
    }
}
