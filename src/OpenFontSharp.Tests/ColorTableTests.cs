using System.Buffers.Binary;
using OpenFontSharp.Subsetting;

namespace OpenFontSharp.Tests;

public class ColorTableTests
{
    [Fact]
    public void Read_ExposesReadOnlyColorApi()
    {
        var face = Read(Colr(), Cpal());
        var layers = face.GetColorLayers(1);
        Assert.Equal(2, layers.Count);
        Assert.Equal((ushort)2, layers[0].GlyphId);
        Assert.Equal((ushort)1, layers[0].PaletteIndex);
        Assert.Equal((ushort)0xffff, layers[1].PaletteIndex);
        Assert.Empty(face.GetColorLayers(0));
        Assert.Equal(2, face.PaletteCount);
        var palette = face.GetPalette(1);
        Assert.Equal(2, palette.Count);
        Assert.Equal((10, 20, 30, 40), ((int)palette[0].Blue, (int)palette[0].Green,
            (int)palette[0].Red, (int)palette[0].Alpha));
        Assert.Equal((byte)80, palette[1].Alpha);
        Assert.Throws<NotSupportedException>(() => ((IList<ColorLayer>)layers)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<PaletteColor>)palette)[0] = default);

        face.COLRTable!.GlyphLayers[0] = 0;
        face.COLRTable.LayerCounts.Clear();
        face.CPALTable!.Palettes[1] = 0;
        Assert.Equal((ushort)2, face.GetColorLayers(1)[0].GlyphId);
        Assert.Equal((byte)10, face.GetPalette(1)[0].Blue);
    }

    [Fact]
    public void Read_AbsentColorTablesAndInvalidCallerIndicesAreExplicit()
    {
        var face = Read();
        Assert.Empty(face.GetColorLayers(1));
        Assert.Equal(0, face.PaletteCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetPalette(0));
        face = Read(Colr(), Cpal());
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetPalette(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetPalette(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => face.CPALTable!.GetColor(-1, out _, out _, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => face.CPALTable!.GetColor(int.MaxValue, out _, out _, out _, out _));
    }

    [Fact]
    public void TrimAndRestore_ColorAccessorsFollowLoadedState()
    {
        var face = Read(Colr(), Cpal());
        byte[] original = face.GetOriginalFontData()!;
        var ticket = face.TrimDown();
        Assert.NotNull(ticket);
        Assert.Empty(face.GetColorLayers(1));
        Assert.Equal(0, face.PaletteCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => face.GetPalette(0));
        using var stream = new MemoryStream(original);
        face.RestoreUp(ticket, stream);
        Assert.False(face.IsTrimmed());
        Assert.Equal(2, face.GetColorLayers(1).Count);
        Assert.Equal((byte)10, face.GetPalette(1)[0].Blue);
    }

    [Fact]
    public void Read_CpalVersion1RetainsPalettesWithoutRequiringOptionalMetadata()
    {
        byte[] source = Cpal(), cpal = new byte[source.Length + 12];
        source.AsSpan(0, 16).CopyTo(cpal);
        source.AsSpan(16).CopyTo(cpal.AsSpan(28));
        Put16(cpal, 0, 1);
        Put32(cpal, 8, 28);
        Assert.Equal((byte)10, Read(Colr(), cpal).GetPalette(1)[0].Blue);
        Put32(cpal, 16, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => Read(Colr(), cpal));
    }

    [Fact]
    public void Read_SharedLargePalettesAreNotExpanded()
    {
        const ushort count = ushort.MaxValue;
        var cpal = new byte[12 + count * 2 + count * 4];
        Put16(cpal, 2, count);
        Put16(cpal, 4, count);
        Put16(cpal, 6, count);
        Put32(cpal, 8, 12 + count * 2);
        var face = Read(null, cpal);
        Assert.Equal(count, face.PaletteCount);
        Assert.Equal(count, face.GetPalette(count - 1).Count);
    }

    [Fact]
    public void Read_EveryTruncatedTableIsRejectedWithinItsOwnBounds()
    {
        byte[] colr = Colr(), cpal = Cpal();
        for (int length = 0; length < colr.Length; length++)
            Assert.Throws<InvalidDataException>(() => Read(colr[..length], cpal));
        for (int length = 0; length < cpal.Length; length++)
            Assert.Throws<InvalidDataException>(() => Read(colr, cpal[..length]));
    }

    [Fact]
    public void Read_ColorRecordsAreBgraAndLayersKeepPaletteIndices()
    {
        var face = Read(Colr(), Cpal());
        Assert.Equal(new ushort[] { 2, 3 }, face.COLRTable!.GlyphLayers);
        Assert.Equal(new ushort[] { 1, 0xffff }, face.COLRTable.GlyphPalettes);
        face.CPALTable!.GetColor(1, out byte r, out byte g, out byte b, out byte a);
        Assert.Equal((30, 20, 10, 40), ((int)r, (int)g, (int)b, (int)a));
    }

    [Theory]
    [InlineData("COLR", 2, 65535)]
    [InlineData("COLR", 18, 65535)]
    [InlineData("COLR", 20, 65535)]
    [InlineData("COLR", 22, 2)]
    [InlineData("CPAL", 2, 65535)]
    [InlineData("CPAL", 4, 65535)]
    [InlineData("CPAL", 6, 65535)]
    [InlineData("CPAL", 12, 65535)]
    public void Read_InvalidCountsAndIndicesThrowInvalidData(string tag, int offset, ushort value)
    {
        byte[] colr = Colr(), cpal = Cpal();
        Put16(tag == "COLR" ? colr : cpal, offset, value);
        Assert.Throws<InvalidDataException>(() => Read(colr, cpal));
    }

    [Theory]
    [InlineData("COLR", 4)]
    [InlineData("COLR", 8)]
    [InlineData("CPAL", 8)]
    public void Read_OutOfTableOffsetsThrowInvalidData(string tag, int offset)
    {
        byte[] colr = Colr(), cpal = Cpal();
        Put32(tag == "COLR" ? colr : cpal, offset, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => Read(colr, cpal));
    }

    [Theory]
    [InlineData("COLR")]
    [InlineData("CPAL")]
    public void Read_UnsupportedVersionsAreSpecific(string tag)
    {
        byte[] colr = Colr(), cpal = Cpal();
        Put16(tag == "COLR" ? colr : cpal, 0, 9);
        var error = Assert.Throws<OpenFontNotSupportedException>(() => Read(colr, cpal));
        Assert.Contains(tag, error.Message);
        Assert.Contains("9", error.Message);
    }

    internal static Typeface Read(byte[]? colr = null, byte[]? cpal = null)
    {
        var writer = new TtfWriter();
        byte[] head = new byte[54], maxp = new byte[32], hhea = new byte[36];
        Put32(head, 0, 0x10000);
        Put32(head, 12, 0x5f0f3cf5);
        Put16(head, 18, 1000);
        Put32(maxp, 0, 0x10000);
        Put16(maxp, 4, 4);
        Put16(hhea, 34, 1);
        writer.AddTable("head", head);
        writer.AddTable("maxp", maxp);
        writer.AddTable("hhea", hhea);
        writer.AddTable("hmtx", new byte[10]);
        writer.AddTable("OS/2", new byte[78]);
        writer.AddTable("name", new byte[] { 0, 0, 0, 0, 0, 6 });
        writer.AddTable("cmap", new byte[4]);
        writer.AddTable("loca", new byte[10]);
        writer.AddTable("glyf", Array.Empty<byte>());
        if (colr != null) writer.AddTable("COLR", colr);
        if (cpal != null) writer.AddTable("CPAL", cpal);
        using var stream = new MemoryStream(writer.Build());
        return new OpenFontReader().Read(stream)!;
    }

    private static byte[] Colr()
    {
        var data = new byte[28];
        Put16(data, 2, 1);
        Put32(data, 4, 14);
        Put32(data, 8, 20);
        Put16(data, 12, 2);
        Put16(data, 14, 1);
        Put16(data, 18, 2);
        Put16(data, 20, 2);
        Put16(data, 22, 1);
        Put16(data, 24, 3);
        Put16(data, 26, 0xffff);
        return data;
    }

    private static byte[] Cpal()
    {
        var data = new byte[28];
        Put16(data, 2, 2);
        Put16(data, 4, 2);
        Put16(data, 6, 3);
        Put32(data, 8, 16);
        Put16(data, 14, 1);
        new byte[] { 1, 2, 3, 4, 10, 20, 30, 40, 50, 60, 70, 80 }.CopyTo(data, 16);
        return data;
    }

    private static void Put16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), value);

    private static void Put32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
}
