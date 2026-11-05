using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
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

        CompareWithFontToolsOutlineReference(converted);
    }

    private static void CompareWithFontToolsOutlineReference(Typeface font)
    {
        byte[] reference = File.ReadAllBytes(Fonts("TTF", "Roboto", "Roboto-Regular.fonttools.gz"));
        using var decompressed = new MemoryStream();
        using (var compressed = new GZipStream(new MemoryStream(reference), CompressionMode.Decompress))
            compressed.CopyTo(decompressed);
        decompressed.Position = 0;
        using var reader = new BinaryReader(decompressed);
        reader.ReadBytes(4).Should().Equal("RBR1"u8.ToArray());
        ushort glyphCount = ReadU16(reader);
        font.GlyphCount.Should().Be(glyphCount);
        var onCurveField = typeof(GlyphPointF).GetField("onCurve", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(onCurveField);

        for (ushort glyphId = 0; glyphId < glyphCount; glyphId++)
        {
            ushort advance = ReadU16(reader);
            short leftSideBearing = ReadI16(reader);
            ushort endpointCount = ReadU16(reader);
            ushort pointCount = ReadU16(reader);
            ushort[] endpoints = Enumerable.Range(0, endpointCount).Select(_ => ReadU16(reader)).ToArray();
            var actual = font.GetGlyph(glyphId);
            (actual.EndPoints ?? Array.Empty<ushort>()).Should().Equal(endpoints, $"glyph {glyphId} contour endpoints");
            font.GetAdvanceWidthFromGlyphIndex(glyphId).Should().Be(advance, $"glyph {glyphId} advance");
            font.GetLeftSideBearing(glyphId).Should().Be(leftSideBearing, $"glyph {glyphId} left side bearing");

            var points = actual.GlyphPoints ?? Array.Empty<GlyphPointF>();
            points.Should().HaveCount(pointCount, $"glyph {glyphId} point count");
            for (int point = 0; point < pointCount; point++)
            {
                float x = ReadSingle(reader);
                float y = ReadSingle(reader);
                bool onCurve = reader.ReadByte() != 0;
                points[point].X.Should().Be(x, $"glyph {glyphId} point {point} X");
                points[point].Y.Should().Be(y, $"glyph {glyphId} point {point} Y");
                ((bool)onCurveField.GetValue(points[point])!).Should().Be(onCurve, $"glyph {glyphId} point {point} on-curve flag");
            }
        }

        reader.BaseStream.Position.Should().Be(reader.BaseStream.Length, "the reference must contain exactly the declared glyph data");
    }

    private static ushort ReadU16(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[2];
        reader.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    private static short ReadI16(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[2];
        reader.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt16BigEndian(bytes);
    }

    private static float ReadSingle(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[4];
        reader.ReadExactly(bytes);
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes));
    }
}
