using System.Buffers.Binary;

namespace OpenFontSharp.Tests;

public class BitmapTableTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    public void Reader_LoadsBitmapIndicesAndMetrics(bool color, int indexFormat)
    {
        var (location, data) = BitmapTables(color, indexFormat);
        using var font = Font(color, location, data);
        var face = new OpenFontReader().Read(font)!;
        Assert.True(face.IsBitmapFont);
        Assert.Equal(7, face.GetGlyph(7).GlyphIndex);
        var glyph = face.GetGlyph(3);
        Assert.Equal(3, glyph.GlyphIndex);
        Assert.Equal(-2, glyph.Bounds.XMin);
        Assert.Equal(1, glyph.Bounds.YMin);
        Assert.Equal(6, glyph.Bounds.XMax);
        Assert.Equal(3, glyph.Bounds.YMax);
        Assert.Equal((ushort)9, BitmapAdvance(glyph));
        using var output = new MemoryStream();
        face.ReadBitmapContent(glyph, output);
        Assert.Equal(new byte[] { 0xA5, 0x5A }, output.ToArray());
        if (indexFormat != 2)
        {
            using var missing = new MemoryStream();
            face.ReadBitmapContent(face.GetGlyph(4), missing);
            Assert.Empty(missing.ToArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_SeeksEachStrikeArrayAndUsesFirstStrike(bool color)
    {
        var (location, data) = BitmapTables(color, 1);
        int indexLength = location.Length - 56;
        var multi = new byte[116 + indexLength * 2];
        location.AsSpan(0, 56).CopyTo(multi);
        Put32(multi, 4, 2);
        location.AsSpan(8, 48).CopyTo(multi.AsSpan(56));
        location.AsSpan(56).CopyTo(multi.AsSpan(116));
        location.AsSpan(56).CopyTo(multi.AsSpan(116 + indexLength));
        Put32(multi, 8, 116);
        Put32(multi, 56, (uint)(116 + indexLength));
        Put32(multi, 116 + indexLength + 12, (uint)data.Length);
        multi[100] = 24;
        byte[] combinedData = [.. data, .. data.AsSpan(4)];
        combinedData[data.Length + 3] = 9;
        using var font = Font(color, multi, combinedData);
        var face = new OpenFontReader().Read(font)!;
        Assert.Equal(3, face.GetGlyph(3).Bounds.YMax);
        Assert.Equal(7, face.GetGlyph(7).GlyphIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_DoesNotExposeImagesPresentOnlyInLaterStrikes(bool color)
    {
        var (location, data) = BitmapTables(color, 1);
        int indexLength = location.Length - 56;
        int secondArray = 116 + indexLength;
        var multi = new byte[secondArray + indexLength];
        location.AsSpan(0, 56).CopyTo(multi);
        location.AsSpan(8, 48).CopyTo(multi.AsSpan(56));
        location.AsSpan(56).CopyTo(multi.AsSpan(116));
        location.AsSpan(56).CopyTo(multi.AsSpan(secondArray));
        Put32(multi, 4, 2);
        Put32(multi, 8, 116);
        Put32(multi, 56, (uint)secondArray);
        Put16(multi, 96, 4); Put16(multi, 98, 4);
        Put16(multi, secondArray, 4); Put16(multi, secondArray + 2, 4);
        Put32(multi, secondArray + 12, (uint)data.Length);
        byte[] combinedData = [.. data, .. data.AsSpan(4)];
        using var font = Font(color, multi, combinedData);
        var face = new OpenFontReader().Read(font)!;
        var missing = face.GetGlyph(4);
        Assert.Equal(4, missing.GlyphIndex);
        Assert.Equal(Bounds.Zero, missing.Bounds);
        using var output = new MemoryStream();
        face.ReadBitmapContent(missing, output);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void Reader_RejectsVerticalLayoutsSpecifically(bool color, byte flags)
    {
        var (location, data) = BitmapTables(color, 1);
        location[55] = flags;
        using var font = Font(color, location, data);
        var exception = Assert.Throws<OpenFontNotSupportedException>(() => new OpenFontReader().Read(font));
        Assert.Contains("vertical", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void Reader_UsesSubtableRangesRatherThanStrikeRange(bool color, int format)
    {
        var (original, data) = BitmapTables(color, format);
        int imageSize = color ? 11 : 7;
        int subtableSize = format == 1 ? 16 : 12;
        byte[] location = new byte[72 + subtableSize * 2];
        original.AsSpan(0, 56).CopyTo(location);
        Put32(location, 12, (uint)(location.Length - 56));
        Put32(location, 16, 2);
        for (int i = 0; i < 2; i++)
        {
            int entry = 56 + i * 8, subtable = 72 + i * subtableSize;
            ushort id = (ushort)(i == 0 ? 3 : 7);
            Put16(location, entry, id); Put16(location, entry + 2, id);
            Put32(location, entry + 4, (uint)(16 + i * subtableSize));
            Put16(location, subtable, (ushort)format);
            Put16(location, subtable + 2, (ushort)(color ? 17 : 1));
            Put32(location, subtable + 4, (uint)(4 + i * imageSize));
            if (format == 1) Put32(location, subtable + 12, (uint)imageSize);
            else Put16(location, subtable + 10, (ushort)imageSize);
        }
        using var font = Font(color, location, data);
        var face = new OpenFontReader().Read(font)!;
        Assert.Equal(3, face.GetGlyph(3).Bounds.YMax);
        Assert.Equal(3, face.GetGlyph(7).Bounds.YMax);
    }

    [Theory]
    [InlineData(false, "strikes")]
    [InlineData(true, "strikes")]
    [InlineData(false, "array")]
    [InlineData(true, "array")]
    [InlineData(false, "subtables")]
    [InlineData(true, "subtables")]
    [InlineData(false, "subtable")]
    [InlineData(true, "subtable")]
    [InlineData(false, "descending")]
    [InlineData(true, "descending")]
    [InlineData(false, "data")]
    [InlineData(true, "data")]
    [InlineData(false, "truncated")]
    [InlineData(true, "truncated")]
    [InlineData(false, "sparse-count")]
    [InlineData(true, "sparse-count")]
    public void Reader_RejectsMalformedBitmapData(bool color, string mutation)
    {
        var (location, data) = BitmapTables(color, mutation == "sparse-count" ? 4 : 1);
        switch (mutation)
        {
            case "strikes": Put32(location, 4, uint.MaxValue); break;
            case "array": Put32(location, 8, uint.MaxValue); break;
            case "subtables": Put32(location, 16, uint.MaxValue); break;
            case "subtable": Put32(location, 60, uint.MaxValue); break;
            case "descending": Put32(location, 76, uint.MaxValue); break;
            case "data": Put32(location, 68, uint.MaxValue); break;
            case "truncated": data = data[..^1]; break;
            case "sparse-count": Put32(location, 72, uint.MaxValue); break;
        }
        using var font = Font(color, location, data);
        Assert.IsType<InvalidDataException>(Record.Exception(() => new OpenFontReader().Read(font)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Reader_RejectsUnknownFormatsSpecifically(bool color, bool image)
    {
        var (location, data) = BitmapTables(color, 1);
        Put16(location, image ? 66 : 64, 99);
        using var font = Font(color, location, data);
        var exception = Assert.Throws<OpenFontNotSupportedException>(() => new OpenFontReader().Read(font));
        Assert.Contains("99", exception.Message);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(false, 6)]
    [InlineData(false, 7)]
    [InlineData(true, 18)]
    public void Reader_LoadsOtherSupportedImageMetrics(bool color, int imageFormat)
    {
        var (location, data) = BitmapTables(color, 1, imageFormat);
        using var font = Font(color, location, data);
        var face = new OpenFontReader().Read(font)!;
        var glyph = face.GetGlyph(7);
        Assert.Equal(-2, glyph.Bounds.XMin);
        Assert.Equal(1, glyph.Bounds.YMin);
        Assert.Equal(6, glyph.Bounds.XMax);
        Assert.Equal((ushort)9, BitmapAdvance(glyph));
        using var output = new MemoryStream();
        face.ReadBitmapContent(glyph, output);
        Assert.Equal(new byte[] { 0xA5, 0x5A }, output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_RejectsEveryTableTruncation(bool color)
    {
        var (location, data) = BitmapTables(color, 1);
        for (int length = 0; length < location.Length; length++)
        {
            using var font = Font(color, location[..length], data);
            Assert.IsType<InvalidDataException>(Record.Exception(() => new OpenFontReader().Read(font)));
        }
        for (int length = 0; length < data.Length; length++)
        {
            using var font = Font(color, location, data[..length]);
            Assert.IsType<InvalidDataException>(Record.Exception(() => new OpenFontReader().Read(font)));
        }
    }

    [Theory]
    [InlineData(false, "zero-strikes")]
    [InlineData(true, "zero-strikes")]
    [InlineData(false, "sparse5-count")]
    [InlineData(true, "sparse5-count")]
    [InlineData(false, "duplicate")]
    [InlineData(true, "duplicate")]
    [InlineData(false, "maxp")]
    [InlineData(true, "maxp")]
    [InlineData(true, "png-length")]
    [InlineData(true, "png-cross-image")]
    [InlineData(false, "missing-shared")]
    [InlineData(true, "missing-shared")]
    [InlineData(false, "reserved-flags")]
    [InlineData(true, "reserved-flags")]
    public void Reader_RejectsAdditionalInvalidRecords(bool color, string mutation)
    {
        var (location, data) = BitmapTables(color, mutation is "sparse5-count" or "duplicate" ? 5 : 1);
        switch (mutation)
        {
            case "zero-strikes": Put32(location, 4, 0); break;
            case "sparse5-count": Put32(location, 84, uint.MaxValue); break;
            case "duplicate": Put16(location, 90, 3); break;
            case "maxp": Put16(location, 50, ushort.MaxValue); break;
            case "png-length": Put32(data, 9, uint.MaxValue); break;
            case "png-cross-image": Put32(data, 9, 3); break;
            case "missing-shared": Put16(location, 66, (ushort)(color ? 19 : 5)); break;
            case "reserved-flags": location[55] = 128; break;
        }
        using var font = Font(color, location, data);
        Assert.IsType<InvalidDataException>(Record.Exception(() => new OpenFontReader().Read(font)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_RestoresMatchingBitmapDataAfterTrim(bool color)
    {
        var (location, data) = BitmapTables(color, 5);
        using var font = Font(color, location, data);
        byte[] bytes = font.ToArray();
        var face = new OpenFontReader().Read(font)!;
        var ticket = face.TrimDown()!;
        using var restored = new MemoryStream(bytes);
        face.RestoreUp(ticket, restored);
        Assert.False(face.IsTrimmed());
        Assert.Equal((ushort)9, BitmapAdvance(face.GetGlyph(7)));
        using var output = new MemoryStream();
        face.ReadBitmapContent(face.GetGlyph(7), output);
        Assert.Equal(new byte[] { 0xA5, 0x5A }, output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_RejectsMissingCompanion(bool color)
    {
        var (location, data) = BitmapTables(color, 1);
        using var font = Font(color, location, data, omitData: true);
        Assert.Throws<InvalidDataException>(() => new OpenFontReader().Read(font));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_RejectsZeroMaxpGlyphCount(bool color)
    {
        var (location, data) = BitmapTables(color, 1);
        using var font = Font(color, location, data, glyphCount: 0);
        Assert.Throws<InvalidDataException>(() => new OpenFontReader().Read(font));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_RejectsOversizedTableBeforeAllocation(bool color)
    {
        var (location, data) = BitmapTables(color, 1);
        using var font = Font(color, location, data);
        byte[] bytes = font.GetBuffer();
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int entry = 12 + i * 16;
            string tag = System.Text.Encoding.ASCII.GetString(bytes, entry, 4);
            if (tag == (color ? "CBLC" : "EBLC"))
                Put32(bytes, entry + 12, uint.MaxValue);
        }
        Assert.Throws<InvalidDataException>(() => new OpenFontReader().Read(font));
    }

    private static (byte[] Location, byte[] Data) BitmapTables(bool color, int indexFormat, int? requestedImage = null)
    {
        bool shared = indexFormat is 2 or 5;
        int imageFormat = requestedImage ?? (color ? (shared ? 19 : 17) : (shared ? 5 : 1));
        int metricSize = shared ? 0 : (imageFormat is 6 or 7 or 18 ? 8 : 5);
        byte[] metrics = [2, 8, 254, 3, 9, 0, 0, 10];
        using var images = new MemoryStream();
        images.Write(color ? new byte[] { 0, 3, 0, 0 } : new byte[] { 0, 2, 0, 0 });
        int imageSize = metricSize + (color ? 4 : 0) + 2;
        for (int i = 0; i < (indexFormat == 2 ? 5 : 2); i++)
        {
            if (!shared) images.Write(metrics.AsSpan(0, metricSize));
            if (color) images.Write(new byte[] { 0, 0, 0, 2 });
            images.Write(new byte[] { 0xA5, 0x5A });
        }
        using var indexes = new MemoryStream();
        Write16(indexes, (ushort)indexFormat);
        Write16(indexes, (ushort)imageFormat);
        Write32(indexes, 4);
        switch (indexFormat)
        {
            case 1:
            case 3:
                foreach (int value in new[] { 0, imageSize, imageSize, imageSize, imageSize, imageSize * 2 })
                    if (indexFormat == 1) Write32(indexes, (uint)value);
                    else Write16(indexes, (ushort)value);
                break;
            case 2:
            case 5:
                Write32(indexes, (uint)imageSize);
                indexes.Write(metrics);
                if (indexFormat == 5) { Write32(indexes, 2); Write16(indexes, 3); Write16(indexes, 7); }
                break;
            case 4:
                Write32(indexes, 2);
                Write16(indexes, 3); Write16(indexes, 0);
                Write16(indexes, 7); Write16(indexes, (ushort)imageSize);
                Write16(indexes, 0); Write16(indexes, (ushort)(imageSize * 2));
                break;
        }
        byte[] location = new byte[64 + indexes.Length];
        Put16(location, 0, (ushort)(color ? 3 : 2)); Put32(location, 4, 1);
        Put32(location, 8, 56); Put32(location, 12, (uint)(8 + indexes.Length)); Put32(location, 16, 1);
        Put16(location, 48, 3); Put16(location, 50, 7);
        location[52] = 12; location[53] = 12;
        location[54] = (byte)(color ? 32 : 1); location[55] = 1;
        Put16(location, 56, 3); Put16(location, 58, 7); Put32(location, 60, 8);
        indexes.ToArray().CopyTo(location, 64);
        return (location, images.ToArray());
    }

    // Reuse only mandatory non-bitmap metadata from the licensed fixture.
    // All bitmap bytes and their enclosing SFNT directory are synthetic.
    private static MemoryStream Font(bool color, byte[] location, byte[] data, bool omitData = false,
        ushort? glyphCount = null)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources",
            "Fonts", "TTF", "Roboto", "Roboto-Regular.ttf");
        byte[] original = File.ReadAllBytes(path);
        var tables = new SortedDictionary<string, byte[]>();
        int count = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int entry = 12 + i * 16;
            string tag = System.Text.Encoding.ASCII.GetString(original, entry, 4);
            if (tag is not ("OS/2" or "head" or "hhea" or "hmtx" or "maxp" or "name" or "cmap")) continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(entry + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(entry + 12));
            tables.Add(tag, original.AsSpan(offset, length).ToArray());
        }
        tables.Add(color ? "CBLC" : "EBLC", location);
        if (!omitData) tables.Add(color ? "CBDT" : "EBDT", data);
        if (glyphCount.HasValue) Put16(tables["maxp"], 4, glyphCount.Value);
        var output = new MemoryStream();
        output.Write(new byte[12 + tables.Count * 16]);
        byte[] header = output.GetBuffer();
        Put32(header, 0, 0x10000); Put16(header, 4, (ushort)tables.Count);
        int n = 0;
        foreach (var (tag, bytes) in tables)
        {
            int entry = 12 + n++ * 16;
            System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(header, entry);
            Put32(header, entry + 8, (uint)output.Position); Put32(header, entry + 12, (uint)bytes.Length);
            output.Write(bytes);
            header = output.GetBuffer();
        }
        output.Position = 0;
        return output;
    }

    private static void Put16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
    private static ushort BitmapAdvance(Glyph glyph) =>
        Assert.IsType<ushort>(typeof(Glyph).GetProperty("BitmapGlyphAdvanceWidth",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(glyph));
    private static void Put32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
    private static void Write16(Stream stream, ushort value)
    {
        byte[] bytes = new byte[2]; Put16(bytes, 0, value); stream.Write(bytes);
    }
    private static void Write32(Stream stream, uint value)
    {
        byte[] bytes = new byte[4]; Put32(bytes, 0, value); stream.Write(bytes);
    }
}
