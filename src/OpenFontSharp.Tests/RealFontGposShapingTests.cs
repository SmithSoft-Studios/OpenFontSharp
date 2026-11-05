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
