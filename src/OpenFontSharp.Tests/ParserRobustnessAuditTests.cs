using System.Buffers.Binary;
using System.Reflection;
using OpenFontSharp.Subsetting;
using OpenFontSharp.Tests.Subsetting;

namespace OpenFontSharp.Tests;

public class ParserRobustnessAuditTests
{
    [Fact]
    public void Format12CmapRejectsReservedFieldsAndOversizedGroupCounts()
    {
        byte[] original = FontFiles.Bytes(FontFiles.RobotoRegular);
        byte[] source = FontSubsetter.Subset(
            original, new HashSet<ushort> { 0, 1, 2, 3 }, includeUnicodeCmap: true).FontData;
        int subtable = FindCmapFormat12(source);

        byte[] reserved = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt16BigEndian(reserved.AsSpan(subtable + 2), 1);
        Assert.Throws<InvalidDataException>(() => FontFiles.Load(reserved));

        byte[] oversized = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(oversized.AsSpan(subtable + 12), 100_000);
        Assert.Throws<InvalidDataException>(() => FontFiles.Load(oversized));
    }

    [Fact]
    public void MissingRequiredTableAndTruncatedPreviewThrowInvalidDataException()
    {
        byte[] source = FontFiles.Bytes(FontFiles.RobotoRegular);
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(4));
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            if (source.AsSpan(record, 4).SequenceEqual("head"u8))
            {
                source[record] = (byte)'x';
                break;
            }
        }

        Assert.Throws<InvalidDataException>(() => FontFiles.Load(source));
        Assert.Throws<InvalidDataException>(() => new OpenFontReader().ReadPreview(new MemoryStream([0])));
    }

    [Fact]
    public void FontCollectionCountIsCappedBeforeAllocation()
    {
        byte[] collection = FontFiles.Bytes(FontFiles.RobotoCollection);
        BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(8), uint.MaxValue);

        Assert.Throws<InvalidDataException>(() =>
            new OpenFontReader().ReadPreview(new MemoryStream(collection)));
    }

    [Fact]
    public void TruncatedSvgDocumentThrowsInvalidDataException()
    {
        ReadSvgTable(CreateSvgTable(1))
            .Should().BeOfType<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<InvalidDataException>();
    }

    [Fact]
    public void OversizedSvgDocumentIsRejectedBeforeAllocation()
    {
        ReadSvgTable(CreateSvgTable(64 * 1024 * 1024 + 1))
            .Should().BeOfType<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<InvalidDataException>();
    }

    private static Exception? ReadSvgTable(byte[] table)
    {
        var type = typeof(Typeface).Assembly.GetType("OpenFontSharp.Tables.BitmapAndSvgFonts.SvgTable")!;
        var instance = Activator.CreateInstance(type, nonPublic: true)!;
        var read = type.GetMethod("ReadContentFrom", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var headerType = typeof(Typeface).Assembly.GetType("OpenFontSharp.Tables.TableHeader")!;
        var header = Activator.CreateInstance(headerType, ["SVG ", 0u, 0u, (uint)table.Length])!;
        type.BaseType!.GetProperty("Header", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, header);
        using var stream = new MemoryStream(table);
        var readerType = typeof(Typeface).Assembly.GetType("OpenFontSharp.ByteOrderSwappingBinaryReader")!;
        using var reader = (BinaryReader)Activator.CreateInstance(readerType, stream)!;

        return Record.Exception(() => read.Invoke(instance, [reader]));
    }

    private static byte[] CreateSvgTable(uint documentLength)
    {
        byte[] table = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(2), 10);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(16), 14);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(20), documentLength);
        return table;
    }

    [Fact]
    public void RealFontTruncationsAndTableByteFlipsFailCleanly()
    {
        byte[] source = FontFiles.Bytes(FontFiles.RobotoRegular);
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(4));
        int cases = 0;

        for (int i = 0; i < 64; i++)
        {
            int length = source.Length * i / 64;
            AssertCleanFailure(source.AsSpan(0, length).ToArray(), $"truncation at {length}");
            cases++;
        }

        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(record + 8)));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(record + 12));
            if (length == 0)
                continue;

            byte[] corrupted = (byte[])source.Clone();
            corrupted[offset] ^= 0x80;
            AssertCleanFailure(corrupted, $"byte flip in table {i}");
            cases++;
        }

        cases.Should().Be(64 + tableCount);
    }

    private static void AssertCleanFailure(byte[] data, string scenario)
    {
        try
        {
            _ = FontFiles.Load(data);
        }
        catch (Exception exception)
        {
            (exception is OpenFontNotSupportedException or NotSupportedException or InvalidDataException)
                .Should().BeTrue($"{scenario} threw {exception.GetType().Name}");
        }
    }

    private static int FindCmapFormat12(byte[] font)
    {
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        int cmapOffset = -1;
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            if (font.AsSpan(record, 4).SequenceEqual("cmap"u8))
            {
                cmapOffset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8)));
                break;
            }
        }

        cmapOffset.Should().BeGreaterThanOrEqualTo(0);
        int encodingCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(cmapOffset + 2));
        for (int i = 0; i < encodingCount; i++)
        {
            int record = cmapOffset + 4 + i * 8;
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 4)));
            int subtable = cmapOffset + offset;
            if (BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(subtable)) == 12)
                return subtable;
        }

        throw new InvalidDataException("The Roboto fixture has no format 12 cmap.");
    }
}
