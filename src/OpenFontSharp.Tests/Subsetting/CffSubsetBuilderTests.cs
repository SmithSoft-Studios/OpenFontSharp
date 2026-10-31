using OpenFontSharp.Subsetting;

namespace OpenFontSharp.Tests.Subsetting;

/// <summary>
/// CFF features the subsetter refuses, exercised on minimal hand-built CFF tables
/// because no shipped font uses them.
/// </summary>
public class CffSubsetBuilderTests
{
    private const byte EndChar = 14;

    [Fact]
    public void SeacComposite_ThrowsNotSupported()
    {
        // "0 0 65 66 endchar": the deprecated seac form, which builds the glyph from
        // the StandardEncoding glyphs 'A' and 'B' that the subset would not contain.
        var cff = MinimalCff(seacGlyph: [Int(0), Int(0), Int(65), Int(66), EndChar]);

        var act = () => CffSubsetBuilder.Build(cff, [0, 1]);

        act.Should().Throw<NotSupportedException>().WithMessage("*seac*");
    }

    [Fact]
    public void SeacComposite_NotRetained_DoesNotThrow()
    {
        var cff = MinimalCff(seacGlyph: [Int(0), Int(0), Int(65), Int(66), EndChar]);

        var act = () => CffSubsetBuilder.Build(cff, [0]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EndCharWithWidthOnly_IsNotSeac()
    {
        var cff = MinimalCff(seacGlyph: [Int(50), EndChar]);

        var act = () => CffSubsetBuilder.Build(cff, [0, 1]);

        act.Should().NotThrow();
    }

    [Fact]
    public void CidKeyedFont_ThrowsNotSupported()
    {
        var cff = MinimalCff(seacGlyph: [EndChar], cidKeyed: true);

        var act = () => CffSubsetBuilder.Build(cff, [0, 1]);

        act.Should().Throw<NotSupportedException>().WithMessage("*CID*");
    }

    private static byte Int(int value) => (byte)(value + 139); // one-byte operand, -107..107

    /// <summary>A two-glyph name-keyed CFF table (ISOAdobe charset, empty Private DICT).</summary>
    private static byte[] MinimalCff(byte[] seacGlyph, bool cidKeyed = false)
    {
        byte[] header = [1, 0, 4, 1];
        byte[] nameIndex = [0, 1, 1, 1, 2, (byte)'T'];
        byte[] ros = cidKeyed ? [Int(0), Int(0), Int(0), 12, 30] : [];
        byte[] TopDict(int charStrings, int priv) => [.. ros, .. Long(charStrings), 17, Int(0), .. Long(priv), 18];
        int topIndexLength = 5 + TopDict(0, 0).Length;
        byte[] emptyIndex = [0, 0];

        int charStringsOffset = header.Length + nameIndex.Length + topIndexLength + emptyIndex.Length + emptyIndex.Length;
        byte[] charStrings = [0, 2, 1, 1, 2, (byte)(2 + seacGlyph.Length), EndChar, .. seacGlyph];
        int privateOffset = charStringsOffset + charStrings.Length;

        byte[] topDict = TopDict(charStringsOffset, privateOffset);
        byte[] topIndex = [0, 1, 1, 1, (byte)(topDict.Length + 1), .. topDict];

        return [.. header, .. nameIndex, .. topIndex, .. emptyIndex, .. emptyIndex, .. charStrings];
    }

    private static byte[] Long(int value) => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
