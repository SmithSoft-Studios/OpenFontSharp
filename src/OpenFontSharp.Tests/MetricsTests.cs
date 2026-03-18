using OpenFontSharp.Metrics;

namespace OpenFontSharp.Tests;

/// <summary>
/// Tests for FontLoader, TextMeasurer, and Typeface public APIs.
/// </summary>
public class MetricsTests
{
    private static string RobotoPath => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Resources", "Fonts",
        "TTF", "Roboto", "Roboto-Regular.ttf");

    [Fact]
    public void FontLoader_LoadFromFile_ReturnsFontInfo()
    {
        var info = FontLoader.LoadFromFile(RobotoPath);

        info.FamilyName.Should().Be("Roboto");
        info.Ascent.Should().BeGreaterThan(0);
        info.Descent.Should().BeLessThan(0);
        info.UnitsPerEm.Should().BeGreaterThan(0);
        info.GlyphCount.Should().BeGreaterThan(100);
        info.GlyphWidths.Length.Should().Be(info.GlyphCount);
    }

    [Fact]
    public void FontLoader_LoadFromBytes_ReturnsFontInfo()
    {
        var bytes = File.ReadAllBytes(RobotoPath);
        var info = FontLoader.LoadFromBytes(bytes);

        info.FamilyName.Should().Be("Roboto");
        info.FontData.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public void TextMeasurer_MeasureWidth_ReturnsPositiveValue()
    {
        var reader = new OpenFontReader();
        using var stream = File.OpenRead(RobotoPath);
        var typeface = reader.Read(stream);

        var width = TextMeasurer.MeasureWidth(typeface, "Hello", 12);

        width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TextMeasurer_EmptyText_ReturnsZero()
    {
        var reader = new OpenFontReader();
        using var stream = File.OpenRead(RobotoPath);
        var typeface = reader.Read(stream);

        TextMeasurer.MeasureWidth(typeface, "", 12).Should().Be(0);
    }

    [Fact]
    public void Typeface_GetGlyphName_ReturnsNameOrNull()
    {
        var reader = new OpenFontReader();
        using var stream = File.OpenRead(RobotoPath);
        var typeface = reader.Read(stream);

        // Glyph 0 is .notdef — may or may not have a name depending on font
        var name = typeface.GetGlyphName(0);
        // Just verify it doesn't throw
    }

    [Fact]
    public void Typeface_GetAllKerningPairs_DoesNotThrow()
    {
        var reader = new OpenFontReader();
        using var stream = File.OpenRead(RobotoPath);
        var typeface = reader.Read(stream);

        var pairs = typeface.GetAllKerningPairs();

        pairs.Should().NotBeNull();
        // Roboto may or may not have kern table pairs
    }

    [Fact]
    public void Typeface_HasTtfOutline_TrueForTtf()
    {
        var reader = new OpenFontReader();
        using var stream = File.OpenRead(RobotoPath);
        var typeface = reader.Read(stream);

        typeface.HasTtfOutline.Should().BeTrue();
    }
}
