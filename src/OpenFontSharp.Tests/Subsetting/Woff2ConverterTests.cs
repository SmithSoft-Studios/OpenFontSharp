using System.Buffers.Binary;
using System.IO.Compression;
using OpenFontSharp.Subsetting;
using OpenFontSharp.WebFont;
using Xunit;

namespace OpenFontSharp.Tests.Subsetting;

[CollectionDefinition("WOFF2 converter hooks", DisableParallelization = true)]
public sealed class Woff2ConverterCollection;

[Collection("WOFF2 converter hooks")]
public class Woff2ConverterTests
{
    private sealed record Table(string Tag, byte[] Original, byte[]? Transformed = null, int Version = 0);

    private static readonly BrotliDecompressStreamFunc Copy = (input, output) =>
    {
        output.Write(input);
        return true;
    };

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void ReconstructsSimpleCompositeEmptyInstructionsOverlapAndMetrics(bool longLoca, int hmtxFlags)
    {
        var (tables, expected) = TransformedFont(longLoca, hmtxFlags);
        byte[] source = Container(tables);
        byte[] result = Woff2Converter.ToSfnt(source, Copy);
        Assert.Equal(expected, result);
        Assert.Equal(result, Woff2Converter.ToSfnt(source, Copy));
        var font = SfntInspector.Parse(result);
        Assert.Equal(new uint[] { 0, 0, 22, 44 }, font.Loca());
        Assert.Equal(new ushort[] { 0, 500, 500 }, Enumerable.Range(0, 3)
            .Select(i => SfntInspector.U16(font.Bytes("hmtx"), i < 2 ? i * 4 : 4)));
        Assert.Equal(new byte[] { 0x77, 0x17 }, font.GlyphRecord(1).AsSpan(16, 2).ToArray());
        Assert.Equal(new ushort[] { 1 }, SfntInspector.ComponentsOf(font.GlyphRecord(2)));
        Assert.Equal(0xB1B0AFBAu, SfntInspector.Checksum(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopiesNullTransformsAndCustomTagsIncludingCff(bool cff)
    {
        Table[] tables = cff
            ? [new("CFF ", [1, 0, 4, 4]), new("zzzz", [9, 8, 7])]
            : NullFont();
        uint flavor = cff ? TtfWriter.CffVersion : TtfWriter.TrueTypeVersion;
        Assert.Equal(Sfnt(tables, flavor), Woff2Converter.ToSfnt(Container(tables, flavor), Copy));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReconstructsHmtxWithNullTransformedGlyf(int flags)
    {
        var tables = NullFont().ToList();
        byte[] hhea = new byte[36];
        Put16(hhea, 34, 1);
        tables.Add(new("hhea", hhea));
        byte[] transformed = flags == 1 ? [1, 1, 244, 0, 0] : flags == 2 ? [2, 1, 244, 0, 10] : [3, 1, 244];
        tables.Add(new("hmtx", [1, 244, 0, 10, 0, 0], transformed, 1));
        Assert.Equal(Sfnt(tables), Woff2Converter.ToSfnt(Container(tables), Copy));
    }

    [Fact]
    public void AllTripletFlagEncodingsAndOnCurveBitsAreDecoded()
    {
        var flags = new List<byte>();
        var triplets = new List<byte>();
        var points = new List<(short X, short Y, bool On)>();
        // Reset each point to the origin with an independent full-width triplet.
        for (int flag = 0; flag < 128; flag++)
        {
            byte[] data = [1, 2, 3, 4];
            int count = flag < 84 ? 1 : flag < 120 ? 2 : flag < 124 ? 3 : 4;
            (int x, int y) = ExpectedTriplet(flag, data);
            flags.Add((byte)(flag | (flag % 2 == 0 ? 128 : 0)));
            triplets.AddRange(data.Take(count));
            points.Add(((short)x, (short)y, flag % 2 != 0));
            flags.Add((byte)(124 | (x <= 0 ? 1 : 0) | (y <= 0 ? 2 : 0)));
            triplets.AddRange([(byte)(Math.Abs(x) >> 8), (byte)Math.Abs(x), (byte)(Math.Abs(y) >> 8), (byte)Math.Abs(y)]);
            points.Add((0, 0, true));
        }
        triplets.AddRange(Encoded255(points.Count)); // instructions length 256
        var streams = new byte[][] {
            Words(1), Encoded255(points.Count), flags.ToArray(), triplets.ToArray(), [],
            new byte[4], Enumerable.Repeat((byte)0xAA, points.Count).ToArray()
        };
        byte[] glyph = Simple(points, Enumerable.Repeat((byte)0xAA, points.Count).ToArray(), false);
        var tables = BaseTables(1, false);
        tables.Add(new("glyf", glyph, GlyfTransform(1, false, streams)));
        tables.Add(new("loca", Words(0, glyph.Length / 2), [], 0));
        Assert.Equal(Sfnt(tables), Woff2Converter.ToSfnt(Container(tables), Copy));
    }

    [Fact]
    public void PromotesShortLocaWhenEquivalentPointEncodingNeedsLargerOffsets()
    {
        const int count = 53000;
        var points = new List<(short X, short Y, bool On)> { (0, short.MinValue, true) };
        var triplets = new List<byte> { 0, 0, 0x80, 0 };
        int x = 0, y = short.MinValue;
        for (int i = 1; i < count; i++)
        {
            int dx = i % 2;
            x += dx; y++;
            points.Add(((short)x, (short)y, true));
            triplets.AddRange([0, (byte)dx, 0, 1]);
        }
        triplets.Add(0);
        byte[] original = Simple(points, [], false, forceShortZeros: true);
        byte[] canonical = Simple(points, [], false);
        Assert.True(original.Length < 131070 && canonical.Length > 131070);
        var input = BaseTables(1, false);
        byte[][] streams = [
            Words(1), Encoded255(count),
            [125, .. Enumerable.Repeat((byte)127, count - 1)],
            triplets.ToArray(), [], new byte[4], []
        ];
        input.Add(new("glyf", original, GlyfTransform(1, false, streams)));
        input.Add(new("loca", Words(0, original.Length / 2), []));
        var expected = BaseTables(1, true);
        expected.Add(new("glyf", canonical));
        expected.Add(new("loca", DWords(0, (uint)canonical.Length)));
        Assert.Equal(Sfnt(expected), Woff2Converter.ToSfnt(Container(input), Copy));
    }

    [Fact]
    public void FullWidthTripletMagnitudesRoundTripSignedCoordinateExtremes()
    {
        var points = new List<(short X, short Y, bool On)> {
            (short.MinValue, short.MinValue, true),
            (short.MaxValue, short.MaxValue, true),
            (short.MinValue, short.MinValue, true), (0, 0, true)
        };
        byte[] glyph = Simple(points, [], false);
        byte[][] streams = [
            Words(1), [4], [124, 127, 124, 127],
            [.. Words(32768, 32768, 65535, 65535, 65535, 65535, 32768, 32768), 0],
            [], new byte[4], []
        ];
        var tables = BaseTables(1, false);
        tables.Add(new("glyf", glyph, GlyfTransform(1, false, streams)));
        tables.Add(new("loca", Words(0, glyph.Length / 2), []));
        byte[] actual = Woff2Converter.ToSfnt(Container(tables), Copy);
        Assert.Equal(Sfnt(tables), actual);
        Assert.Equal(points, DecodeSimplePoints(SfntInspector.Parse(actual).GlyphRecord(0)));
    }

    [Theory]
    [InlineData(253, 0)]
    [InlineData(254, 0)]
    [InlineData(255, 0)]
    public void AcceptsAll255UInt16Forms(int code, int ignored)
    {
        _ = ignored;
        int count = code == 253 ? 253 : code == 254 ? 506 : 253;
        byte[] encoded = code == 253 ? [253, 0, 253] : [(byte)code, 0];
        var points = Enumerable.Repeat(((short)0, (short)0, true), count).ToList();
        byte[] glyph = Simple(points, [], false);
        byte[][] streams = [Words(1), encoded, new byte[count], new byte[count + 1], [], new byte[4], []];
        var tables = BaseTables(1, false);
        tables.Add(new("glyf", glyph, GlyfTransform(1, false, streams)));
        tables.Add(new("loca", Words(0, glyph.Length / 2), []));
        Assert.Equal(Sfnt(tables), Woff2Converter.ToSfnt(Container(tables), Copy));
    }

    [Fact]
    public void ExplicitHookAndDefaultHookUseRealBrotli()
    {
        Table[] tables = [new("CFF ", [1, 0, 4, 4])];
        byte[] source = Container(tables, TtfWriter.CffVersion, compress: true);
        BrotliDecompressStreamFunc hook = (input, output) =>
        {
            using var stream = new BrotliStream(new MemoryStream(input), CompressionMode.Decompress);
            stream.CopyTo(output);
            return true;
        };
        Assert.Equal(Sfnt(tables, TtfWriter.CffVersion), Woff2Converter.ToSfnt(source, hook));
        var current = Woff2DefaultBrotliDecompressFunc.DecompressHandler;
        try
        {
            Woff2DefaultBrotliDecompressFunc.DecompressHandler = hook;
            Assert.Equal(Sfnt(tables, TtfWriter.CffVersion), Woff2Converter.ToSfnt(source));
        }
        finally { Woff2DefaultBrotliDecompressFunc.DecompressHandler = current; }
    }

    [Fact]
    public void RealBrotliReconstructsSyntheticTransformedOutlinesAndMetrics()
    {
        var (tables, expected) = TransformedFont(false, 3);
        byte[] source = Container(tables, compress: true);
        byte[] actual = Woff2Converter.ToSfnt(source, (input, output) =>
        {
            using var stream = new BrotliStream(new MemoryStream(input), CompressionMode.Decompress);
            stream.CopyTo(output);
            return true;
        });
        Assert.Equal(expected, actual);
        Assert.Equal(new ushort[] { 1 }, SfntInspector.ComponentsOf(SfntInspector.Parse(actual).GlyphRecord(2)));
    }

    [Fact]
    public void MissingHookIsExplicitAndFailedOrThrowingHooksAreInvalidData()
    {
        byte[] source = Container([new("CFF ", [1])], TtfWriter.CffVersion);
        var current = Woff2DefaultBrotliDecompressFunc.DecompressHandler;
        try
        {
            Woff2DefaultBrotliDecompressFunc.DecompressHandler = null;
            Assert.Throws<NotSupportedException>(() => Woff2Converter.ToSfnt(source));
        }
        finally { Woff2DefaultBrotliDecompressFunc.DecompressHandler = current; }
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(source, (_, _) => false));
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(source, (_, _) => throw new IOException("bad Brotli")));
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(source, (_, _) => true));
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(source, (input, output) =>
        {
            output.Write(new byte[input.Length + 1]);
            return true;
        }));
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(source, (input, output) =>
        {
            try { output.Write(new byte[input.Length + 1]); } catch (InvalidDataException) { }
            output.Write(input);
            return true;
        }));
    }

    [Fact]
    public void CompositeArgumentsAndAllScaleFormsAreCopiedWithoutRounding()
    {
        byte[] components = [
            .. Words(0x012B, 0, 7, -9, 0x2001), // words, one scale, more, instructions
            .. Words(0x0062, 0), 2, 3, .. Words(0x2002, 0x3003), // bytes, x/y scales, more
            .. Words(0x0083, 0, -4, 5, 0x2004, 0x1005, -0x1006, 0x3007) // 2x2
        ];
        byte[] record = [.. Words(-1, -10, -20, 30, 40), .. components, .. Words(3), 1, 2, 3, 0];
        byte[][] streams = [
            Words(0, -1), [], [], [3], components,
            [0x40, 0, 0, 0, .. Words(-10, -20, 30, 40)], [1, 2, 3]
        ];
        var tables = BaseTables(2, false);
        tables.Add(new("glyf", record, GlyfTransform(2, false, streams)));
        tables.Add(new("loca", Words(0, 0, record.Length / 2), []));
        Assert.Equal(Sfnt(tables), Woff2Converter.ToSfnt(Container(tables), Copy));
    }

    [Fact]
    public void AcceptsKnownTagIndicesAndOptionalAlignedBlocks()
    {
        Table[] tables = [new("CFF ", [1, 2, 3])];
        byte[] custom = Container(tables, TtfWriter.CffVersion);
        byte[] known = [.. custom[..48], 13, .. custom[53..]];
        Put32(known, 8, (uint)known.Length);
        Assert.Equal(Sfnt(tables, TtfWriter.CffVersion), Woff2Converter.ToSfnt(known, Copy));
        int metaStart = (known.Length + 3) & ~3;
        int privateStart = (metaStart + 3 + 3) & ~3;
        byte[] optional = new byte[privateStart + 2];
        known.CopyTo(optional, 0);
        Put32(optional, 8, (uint)optional.Length);
        Put32(optional, 28, (uint)metaStart);
        Put32(optional, 32, 3);
        Put32(optional, 36, 100);
        Put32(optional, 40, (uint)privateStart);
        Put32(optional, 44, 2);
        Assert.Equal(Sfnt(tables, TtfWriter.CffVersion), Woff2Converter.ToSfnt(optional, Copy));
        Put32(optional, 40, (uint)metaStart);
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(optional, Copy));
    }

    [Fact]
    public void RejectsBadBboxContoursEndpointsAndUnusedStreamBytes()
    {
        byte[] glyph = Simple([(1, 2, true)], [], false);
        byte[][] valid = [Words(1), [1], [127], [0, 1, 0, 2, 0], [], new byte[4], []];
        foreach (int kind in Enumerable.Range(0, 10))
        {
            byte[][] streams = valid.Select(s => (byte[])s.Clone()).ToArray();
            switch (kind)
            {
                case 0: streams[0] = Words(-2); break;
                case 1: streams[1] = [0]; break;
                case 2: streams[1] = [253, 255, 255]; streams[2] = []; break;
                case 3: streams[2] = []; break;
                case 4: streams[3] = [0, 1, 0]; break;
                case 5: streams[3] = [0, 1, 0, 2, 1]; break;
                case 6: streams[5] = [0x80, 0, 0, 0]; break;
                case 7: streams[0] = Words(-1); streams[4] = Words(2, 0, 0); break;
                case 8: streams[0] = Words(0); streams[5] = [0x80, 0, 0, 0]; break;
                case 9: streams[6] = [0]; break;
            }
            var tables = BaseTables(1, false);
            tables.Add(new("glyf", glyph, GlyfTransform(1, false, streams)));
            tables.Add(new("loca", Words(0, glyph.Length / 2), []));
            Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(Container(tables), Copy));
        }
    }

    [Fact]
    public void RejectsMalformedHeadersDirectoriesAndTruncation()
    {
        byte[] good = Container([new("CFF ", [1, 2])], TtfWriter.CffVersion);
        foreach (int length in Enumerable.Range(0, good.Length))
            Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(good[..length], Copy));
        foreach ((int offset, uint value) in new (int, uint)[] {
            (0, 0), (8, 1), (12, 0), (16, uint.MaxValue), (20, uint.MaxValue),
            (28, 1), (40, 1)
        })
        {
            byte[] bad = (byte[])good.Clone();
            Put32(bad, offset, value);
            Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(bad, Copy));
        }
        byte[] ttc = (byte[])good.Clone();
        Put32(ttc, 4, 0x74746366);
        Assert.Throws<NotSupportedException>(() => Woff2Converter.ToSfnt(ttc, Copy));
        byte[] excessiveTables = (byte[])good.Clone();
        Put16(excessiveTables, 12, 0xFFFF);
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(excessiveTables, Copy));
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(
            Container([new("CFF ", [1]), new("CFF ", [2])], TtfWriter.CffVersion), Copy));
        byte[] badTransform = (byte[])good.Clone();
        badTransform[48] |= 0x40;
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(badTransform, Copy));
        // An overlong/noncanonical UIntBase128.
        byte[] base128 = [.. good[..49], 0x80, .. good[49..]];
        Put32(base128, 8, (uint)base128.Length);
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(base128, Copy));
    }

    [Fact]
    public void RejectsMalformedTransformsAndUnconsumedSubstreams()
    {
        var (tables, _) = TransformedFont(false, 3);
        int glyf = tables.FindIndex(t => t.Tag == "glyf");
        foreach (int offset in new[] { 0, 2, 4, 6, 8, 12, 16, 20, 24, 28, 32 })
        {
            var bad = tables.ToList();
            byte[] data = (byte[])bad[glyf].Transformed!.Clone();
            data[offset] = 0x7F;
            bad[glyf] = bad[glyf] with { Transformed = data };
            Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(Container(bad), Copy));
        }
        foreach (int version in new[] { 1, 2 })
        {
            var bad = tables.ToList();
            bad[glyf] = bad[glyf] with { Version = version };
            Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(Container(bad), Copy));
        }
        var badHmtx = tables.ToList();
        int hmtx = badHmtx.FindIndex(t => t.Tag == "hmtx");
        foreach (byte flags in new byte[] { 0, 4, 255 })
        {
            badHmtx[hmtx] = tables[hmtx] with { Transformed = [flags, 0, 0, 1, 244] };
            Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(Container(badHmtx), Copy));
        }
        var missing = tables.Where(t => t.Tag != "loca").ToList();
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(Container(missing), Copy));
        var mismatched = tables.ToList();
        int loca = mismatched.FindIndex(t => t.Tag == "loca");
        mismatched[loca] = mismatched[loca] with { Transformed = null, Version = 3 };
        Assert.Throws<InvalidDataException>(() => Woff2Converter.ToSfnt(Container(mismatched), Copy));
    }

    private static (List<Table> Tables, byte[] Expected) TransformedFont(bool longLoca, int hmtxFlags)
    {
        byte[] simple = Simple([(10, 20, true), (15, 17, true)], [0xAA, 0xBB], true);
        byte[] composite = [.. Words(-1, 10, 17, 15, 20), .. Words(0x0103, 1, 0, 0), .. Words(1), 0xCC];
        if (composite.Length % 2 != 0) composite = [.. composite, 0];
        byte[] glyf = [.. simple, .. composite];
        byte[] loca = longLoca ? DWords(0, 0, (uint)simple.Length, (uint)glyf.Length)
            : Words(0, 0, simple.Length / 2, glyf.Length / 2);
        byte[][] streams = [
            Words(0, 1, -1), [2], [127, 125],
            [0, 10, 0, 20, 0, 5, 0, 3, 2, 1],
            Words(0x0103, 1, 0, 0),
            [0x20, 0, 0, 0, .. Words(10, 17, 15, 20)],
            [0xAA, 0xBB, 0xCC]
        ];
        var tables = BaseTables(3, longLoca);
        byte[] hhea = new byte[36];
        Put16(hhea, 34, 2);
        tables.Add(new("hhea", hhea));
        var hmtx = new List<byte> { (byte)hmtxFlags, 0, 0, 1, 244 };
        if ((hmtxFlags & 1) == 0) hmtx.AddRange(Words(0, 10));
        if ((hmtxFlags & 2) == 0) hmtx.AddRange(Words(10));
        tables.Add(new("hmtx", Words(0, 0, 500, 10, 10), hmtx.ToArray(), 1));
        tables.Add(new("glyf", glyf, GlyfTransform(3, longLoca, streams, [0x40])));
        tables.Add(new("loca", loca, []));
        return (tables, Sfnt(tables));
    }

    private static List<Table> BaseTables(int count, bool longLoca)
    {
        byte[] head = new byte[54];
        Put32(head, 0, 0x00010000);
        Put32(head, 12, 0x5F0F3CF5);
        Put16(head, 18, 1000);
        Put16(head, 50, longLoca ? 1 : 0);
        byte[] maxp = new byte[32];
        Put32(maxp, 0, 0x00010000);
        Put16(maxp, 4, count);
        return [new("head", head), new("maxp", maxp)];
    }

    private static Table[] NullFont()
    {
        var tables = BaseTables(2, false);
        byte[] glyph = Simple([(10, 20, true)], [], false);
        tables.Add(new("glyf", glyph, Version: 3));
        tables.Add(new("loca", Words(0, glyph.Length / 2, glyph.Length / 2), Version: 3));
        tables.Add(new("zzzz", [7, 8, 9]));
        return tables.ToArray();
    }

    private static byte[] Simple(List<(short X, short Y, bool On)> points, byte[] instructions, bool overlap, bool forceShortZeros = false)
    {
        var result = new List<byte>();
        result.AddRange(Words(1, points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y)));
        result.AddRange(Words(points.Count - 1, instructions.Length));
        result.AddRange(instructions);
        var flags = new List<byte>();
        var xData = new List<byte>();
        var yData = new List<byte>();
        int x = 0, y = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            int f = (point.On ? 1 : 0) | (overlap && i == 0 ? 0x40 : 0);
            int dx = point.X - x, dy = point.Y - y;
            f |= EncodeAxis(dx, xData, 2, 16, forceShortZeros);
            f |= EncodeAxis(dy, yData, 4, 32, forceShortZeros);
            flags.Add((byte)f);
            x = point.X; y = point.Y;
        }
        for (int i = 0; i < flags.Count;)
        {
            int last = i + 1;
            while (last < flags.Count && last - i < 256 && flags[last] == flags[i]) last++;
            result.Add((byte)(flags[i] | (last - i > 1 ? 8 : 0)));
            if (last - i > 1) result.Add((byte)(last - i - 1));
            i = last;
        }
        result.AddRange(xData);
        result.AddRange(yData);
        if (result.Count % 2 != 0) result.Add(0);
        return result.ToArray();
    }

    private static int EncodeAxis(int delta, List<byte> bytes, int shortBit, int positiveBit, bool forceShortZeros)
    {
        if (delta == 0)
        {
            if (!forceShortZeros) return positiveBit;
            bytes.Add(0);
            return shortBit | positiveBit;
        }
        if (Math.Abs(delta) > 255) { bytes.AddRange(Words(delta)); return 0; }
        bytes.Add((byte)Math.Abs(delta));
        return shortBit | (delta > 0 ? positiveBit : 0);
    }

    private static List<(short X, short Y, bool On)> DecodeSimplePoints(byte[] glyph)
    {
        int contours = SfntInspector.U16(glyph, 0);
        int count = SfntInspector.U16(glyph, 10 + (contours - 1) * 2) + 1;
        int position = 10 + contours * 2;
        position += 2 + SfntInspector.U16(glyph, position);
        var flags = new List<byte>();
        while (flags.Count < count)
        {
            byte flag = glyph[position++];
            int run = (flag & 8) != 0 ? 1 + glyph[position++] : 1;
            flags.AddRange(Enumerable.Repeat(flag, run));
        }
        short[] ReadAxis(int shortBit, int sameBit)
        {
            short current = 0;
            var values = new short[count];
            for (int i = 0; i < count; i++)
            {
                int delta;
                if ((flags[i] & shortBit) != 0)
                    delta = ((flags[i] & sameBit) != 0 ? 1 : -1) * glyph[position++];
                else if ((flags[i] & sameBit) != 0) delta = 0;
                else { delta = SfntInspector.I16(glyph, position); position += 2; }
                current = unchecked((short)(current + delta));
                values[i] = current;
            }
            return values;
        }
        short[] xs = ReadAxis(2, 16), ys = ReadAxis(4, 32);
        return Enumerable.Range(0, count).Select(i => (xs[i], ys[i], (flags[i] & 1) != 0)).ToList();
    }

    private static (int X, int Y) ExpectedTriplet(int flag, byte[] b)
    {
        int x, y;
        if (flag < 10) { x = 0; y = (flag / 2) * 256 + b[0]; }
        else if (flag < 20) { x = ((flag - 10) / 2) * 256 + b[0]; y = 0; }
        else if (flag < 84) { int n = (flag - 20) / 4; x = 1 + (n / 4) * 16 + b[0] / 16; y = 1 + (n % 4) * 16 + b[0] % 16; }
        else if (flag < 120) { int n = (flag - 84) / 4; x = 1 + (n / 3) * 256 + b[0]; y = 1 + (n % 3) * 256 + b[1]; }
        else if (flag < 124) { x = b[0] * 16 + b[1] / 16; y = (b[1] % 16) * 256 + b[2]; }
        else { x = b[0] * 256 + b[1]; y = b[2] * 256 + b[3]; }
        if (flag < 10) return (0, (flag & 1) != 0 ? y : -y);
        if (flag < 20) return ((flag & 1) != 0 ? x : -x, 0);
        return ((flag & 1) != 0 ? x : -x, (flag & 2) != 0 ? y : -y);
    }

    private static byte[] GlyfTransform(int count, bool longLoca, byte[][] streams, byte[]? overlap = null)
        => [.. Words(0, overlap == null ? 0 : 1, count, longLoca ? 1 : 0),
            .. DWords(streams.Select(s => (uint)s.Length).ToArray()), .. streams.SelectMany(s => s), .. overlap ?? []];

    private static byte[] Sfnt(IEnumerable<Table> tables, uint flavor = TtfWriter.TrueTypeVersion)
    {
        var writer = new TtfWriter(flavor);
        foreach (var table in tables) writer.AddTable(table.Tag, table.Original);
        return writer.Build();
    }

    private static byte[] Container(IEnumerable<Table> source, uint flavor = TtfWriter.TrueTypeVersion, bool compress = false)
    {
        var tables = source.ToArray();
        var directory = new List<byte>();
        foreach (var table in tables)
        {
            directory.Add((byte)(63 | (table.Version << 6)));
            directory.AddRange(table.Tag.Select(c => (byte)c));
            directory.AddRange(Base128((uint)table.Original.Length));
            if (table.Transformed != null) directory.AddRange(Base128((uint)table.Transformed.Length));
        }
        byte[] payload = tables.SelectMany(t => t.Transformed ?? t.Original).ToArray();
        if (compress)
        {
            using var memory = new MemoryStream();
            using (var brotli = new BrotliStream(memory, CompressionLevel.SmallestSize, leaveOpen: true)) brotli.Write(payload);
            payload = memory.ToArray();
        }
        byte[] header = new byte[48];
        Put32(header, 0, 0x774F4632);
        Put32(header, 4, flavor);
        Put32(header, 8, (uint)(48 + directory.Count + payload.Length));
        Put16(header, 12, tables.Length);
        Put32(header, 16, (uint)(12 + tables.Length * 16 + tables.Sum(t => (t.Original.Length + 3) & ~3)));
        Put32(header, 20, (uint)payload.Length);
        return [.. header, .. directory, .. payload];
    }

    private static byte[] Base128(uint value)
    {
        var bytes = new List<byte> { (byte)(value & 127) };
        while ((value >>= 7) != 0) bytes.Insert(0, (byte)(128 | (value & 127)));
        return bytes.ToArray();
    }

    private static byte[] Encoded255(int value) => value < 253 ? [(byte)value] : [253, (byte)(value >> 8), (byte)value];
    private static byte[] Words(params int[] values) => values.SelectMany(v => new byte[] { (byte)(v >> 8), (byte)v }).ToArray();
    private static byte[] DWords(params uint[] values) => values.SelectMany(v => new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }).ToArray();
    private static void Put16(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), (ushort)value);
    private static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
}
