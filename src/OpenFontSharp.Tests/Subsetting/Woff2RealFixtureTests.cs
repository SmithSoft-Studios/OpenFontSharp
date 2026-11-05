using System.IO.Compression;
using OpenFontSharp.WebFont;

namespace OpenFontSharp.Tests.Subsetting;

/// <summary>Converts a WOFF2 produced by an independent tool (fontTools) and compares it with the original TTF.</summary>
public class Woff2RealFixtureTests
{
    private static string Fonts(params string[] parts) =>
        Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "Resources", "Fonts" }.Concat(parts).ToArray());

    private static Typeface Read(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var typeface = new OpenFontReader().Read(stream);
        Assert.NotNull(typeface);
        return typeface;
    }

    [Fact]
    public void RealRobotoWoff2_ConvertsToTheSameGlyphsAsTheTtf()
    {
        var woff2 = File.ReadAllBytes(Fonts("WOFF2", "Roboto-Regular.woff2"));
        var ttf = File.ReadAllBytes(Fonts("TTF", "Roboto", "Roboto-Regular.ttf"));

        var sfnt = Woff2Converter.ToSfnt(woff2, (input, output) =>
        {
            using var source = new MemoryStream(input);
            using var brotli = new BrotliStream(source, CompressionMode.Decompress);
            brotli.CopyTo(output);
            return true;
        });

        var converted = Read(sfnt);
        var original = Read(ttf);

        var convertedTables = SfntInspector.Parse(sfnt);
        var originalTables = SfntInspector.Parse(ttf);
        convertedTables.Directory.Select(table => table.Tag)
            .Should().Equal(originalTables.Directory.Select(table => table.Tag));
        foreach (var table in originalTables.Directory)
        {
            if (table.Tag is "head" or "glyf" or "loca" or "hmtx")
                continue;

            convertedTables.Bytes(table.Tag).Should().Equal(
                originalTables.Bytes(table.Tag), $"fontTools reference table '{table.Tag}'");
        }

        converted.GlyphCount.Should().Be(original.GlyphCount);
        converted.UnitsPerEm.Should().Be(original.UnitsPerEm);
        for (ushort glyph = 0; glyph < original.GlyphCount; glyph++)
        {
            converted.GetAdvanceWidthFromGlyphIndex(glyph).Should().Be(original.GetAdvanceWidthFromGlyphIndex(glyph), $"advance of glyph {glyph}");
            var a = converted.GetGlyph(glyph).GlyphPoints ?? Array.Empty<GlyphPointF>();
            var b = original.GetGlyph(glyph).GlyphPoints ?? Array.Empty<GlyphPointF>();
            a.Length.Should().Be(b.Length, $"point count of glyph {glyph}");
            for (int i = 0; i < a.Length; i++)
            {
                a[i].X.Should().Be(b[i].X, $"glyph {glyph} point {i} x");
                a[i].Y.Should().Be(b[i].Y, $"glyph {glyph} point {i} y");
            }
        }
        for (int codePoint = 32; codePoint < 0x250; codePoint++)
            converted.GetGlyphIndex(codePoint).Should().Be(original.GetGlyphIndex(codePoint));
    }
}
