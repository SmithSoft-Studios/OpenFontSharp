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
        return reader.Read(stream);
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
