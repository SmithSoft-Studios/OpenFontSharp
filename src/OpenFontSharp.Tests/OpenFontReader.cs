namespace OpenFontSharp.Tests;

/// <summary>
/// Tests for OpenFontReader — reading TTF and OTF fonts.
/// </summary>
public class OpenFontReaderTests
{
    private static string FontsDir => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Resources", "Fonts");

    [Fact]
    public void ReadPreview_RobotoTtf_ReturnsAllFonts()
    {
        var reader = new OpenFontReader();
        var ttfFonts = Directory.GetFiles(Path.Combine(FontsDir, "TTF", "Roboto"), "*.ttf");

        var previews = new List<PreviewFontInfo>();
        foreach (var font in ttfFonts)
        {
            using var stream = File.OpenRead(font);
            previews.Add(reader.ReadPreview(stream));
        }

        previews.Count.Should().Be(12);
        previews.Should().Contain(p => p.Name == "Roboto" && p.SubFamilyName == "Italic");
    }

    [Fact]
    public void Read_RobotoTtf_ReturnsTypefaceWithMetrics()
    {
        var reader = new OpenFontReader();
        var ttfFonts = Directory.GetFiles(Path.Combine(FontsDir, "TTF", "Roboto"), "*.ttf");

        var typefaces = new List<Typeface>();
        foreach (var font in ttfFonts)
        {
            using var stream = File.OpenRead(font);
            typefaces.Add(reader.Read(stream));
        }

        typefaces.Count.Should().Be(12);
        var regular = typefaces.First(t => t.FontSubFamily == "Regular");
        regular.Name.Should().StartWith("Roboto");
        regular.UnitsPerEm.Should().BeGreaterThan(0);
        regular.Ascender.Should().BeGreaterThan(0);
        regular.GlyphCount.Should().BeGreaterThan(100);
    }

    [Fact]
    public void Read_GreatVibesOtf_ReturnsTypeface()
    {
        var reader = new OpenFontReader();
        var otfPath = Path.Combine(FontsDir, "OTF", "great-vibes", "GreatVibes-Regular.otf");
        if (!File.Exists(otfPath)) return;

        using var stream = File.OpenRead(otfPath);
        var typeface = reader.Read(stream);

        typeface.Should().NotBeNull();
        typeface.PostScriptName.Should().NotBeNullOrEmpty();
    }
}
