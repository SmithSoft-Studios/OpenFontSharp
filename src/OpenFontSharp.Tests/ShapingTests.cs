using OpenFontSharp.Shaping;

namespace OpenFontSharp.Tests;

/// <summary>
/// Tests for BasicLatinShaper and ScriptDetector within OpenFontSharp.
/// </summary>
public class ShapingTests
{
    private static Typeface LoadRoboto()
    {
        var reader = new OpenFontReader();
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Resources", "Fonts",
            "TTF", "Roboto", "Roboto-Regular.ttf");
        using var stream = File.OpenRead(path);
        var typeface = reader.Read(stream);
        Assert.NotNull(typeface);
        return typeface;
    }

    private static Typeface LoadFont(params string[] relative)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "Resources", "Fonts" }.Concat(relative).ToArray());
        using var stream = File.OpenRead(path);
        var typeface = new OpenFontReader().Read(stream);
        Assert.NotNull(typeface);
        return typeface;
    }

    [Theory]
    [InlineData("AV")]
    [InlineData("Hello")]
    [InlineData("Wa")]
    public void BasicLatinShaper_PlainText_KeepsCmapGlyphsWhenNoLigatureApplies(string text)
    {
        // Regression: every GSUB lookup used to be applied, not only the liga/clig features, which replaced
        // ordinary letters with unrelated alternate glyphs.
        foreach (var font in new[] { LoadFont("TTF", "Roboto", "Roboto-Regular.ttf"), LoadFont("TTF", "Shantell_Sans", "static", "ShantellSans-Regular.ttf") })
        {
            var expected = text.Select(c => font.GetGlyphIndex(c)).ToArray();

            var result = BasicLatinShaper.Shape(font, text);

            result.GlyphIds.Should().Equal(expected);
        }
    }

    [Fact]
    public void BasicLatinShaper_AppliesRobotoFfiLigature()
    {
        var result = BasicLatinShaper.Shape(LoadRoboto(), "office");

        result.GlyphIds.Should().Equal(83, 446, 71, 73);
        result.Advances.Should().Equal(1168, 1748, 1072, 1085);
    }

    [Fact]
    public void ScriptDetector_LatinText_DoesNotRequireComplexShaping()
    {
        ScriptDetector.RequiresComplexShaping("Hello World").Should().BeFalse();
    }

    [Fact]
    public void ScriptDetector_ArabicText_RequiresComplexShaping()
    {
        ScriptDetector.RequiresComplexShaping("مرحبا").Should().BeTrue();
    }

    [Fact]
    public void ScriptDetector_CyrillicText_DoesNotRequireComplexShaping()
    {
        ScriptDetector.RequiresComplexShaping("Привет").Should().BeFalse();
    }

    [Fact]
    public void BasicLatinShaper_SingleChar_ReturnsGlyphAndAdvance()
    {
        var typeface = LoadRoboto();
        var result = BasicLatinShaper.Shape(typeface, "A");

        result.GlyphIds.Should().HaveCount(1);
        result.Advances.Should().HaveCount(1);
        result.Advances[0].Should().BeGreaterThan(0);
        result.RequiresComplexShaping.Should().BeFalse();
    }

    [Fact]
    public void BasicLatinShaper_EmptyText_ReturnsEmptyResult()
    {
        var typeface = LoadRoboto();
        var result = BasicLatinShaper.Shape(typeface, "");

        result.GlyphIds.Should().BeEmpty();
        result.Advances.Should().BeEmpty();
    }

    [Fact]
    public void BasicLatinShaper_ComplexScript_FlagsRequiresComplexShaping()
    {
        var typeface = LoadRoboto();
        var result = BasicLatinShaper.Shape(typeface, "مرحبا");

        result.RequiresComplexShaping.Should().BeTrue();
    }
}
