using OpenFontSharp.Subsetting;

namespace OpenFontSharp.Tests.Subsetting;

/// <summary>
/// Establishes which font formats the subsetter supports. Each supported format must
/// produce a faithful subset; each unsupported one must throw
/// <see cref="NotSupportedException"/> rather than return a font with missing outlines.
/// The README's support table records these results.
/// </summary>
public class SubsetFormatSupportTests
{
    [Theory]
    [InlineData(FontFiles.RobotoRegular)]
    [InlineData(FontFiles.RobotoBold)]
    [InlineData(FontFiles.ShantellStatic)]
    public void TrueType_IsSubsetFaithfully(string file)
    {
        var original = FontFiles.Load(file);

        var result = FontSubsetter.Subset(FontFiles.Bytes(file), FontFiles.GlyphsFor(original, FontFiles.SampleText));

        SubsetVerifier.VerifyFaithful(original, result);
        result.RetainedGlyphCount.Should().BeLessThan(original.GlyphCount);
    }

    [Fact]
    public void TrueType_TypefaceOverload_MatchesBytesOverload()
    {
        var original = FontFiles.Load(FontFiles.RobotoRegular);
        var glyphs = FontFiles.GlyphsFor(original, FontFiles.SampleText);

        var fromTypeface = FontSubsetter.Subset(original, glyphs);
        var fromBytes = FontSubsetter.Subset(FontFiles.Bytes(FontFiles.RobotoRegular), glyphs);

        fromTypeface.GlyphIdMap.Should().Equal(fromBytes.GlyphIdMap);
        fromTypeface.FontData.Should().Equal(fromBytes.FontData);
    }

    [Fact]
    public void VariableTrueType_IsSubsetToItsDefaultInstance()
    {
        var original = FontFiles.Load(FontFiles.ShantellVariable);
        SfntInspector.Parse(FontFiles.Bytes(FontFiles.ShantellVariable)).Has("gvar").Should().BeTrue("the fixture is a variable font");

        var result = FontSubsetter.Subset(FontFiles.Bytes(FontFiles.ShantellVariable), FontFiles.GlyphsFor(original, FontFiles.SampleText));

        // VerifyFaithful also checks that fvar/gvar/HVAR/avar are gone: the outlines kept
        // are the glyf (default instance) outlines, so the subset is a static font.
        SubsetVerifier.VerifyFaithful(original, result);
    }

    [Fact]
    public void OpenTypeCff_IsSubsetFaithfully()
    {
        var original = FontFiles.Load(FontFiles.GreatVibes);

        var result = FontSubsetter.Subset(FontFiles.Bytes(FontFiles.GreatVibes), FontFiles.GlyphsFor(original, FontFiles.SampleText));

        SubsetVerifier.VerifyFaithful(original, result);
        SfntInspector.Parse(result.FontData).SfntVersion.Should().Be(0x4F54544Fu, "a CFF font stays 'OTTO'");
        result.RetainedGlyphCount.Should().BeLessThan(original.GlyphCount);
    }

    [Fact]
    public void OpenTypeCff_KeepsGlyphNames()
    {
        // The rebuilt charset must name each retained glyph as the original did.
        var original = FontFiles.Load(FontFiles.GreatVibes);
        var glyphs = FontFiles.GlyphsFor(original, "Great Vibes");

        var result = FontSubsetter.Subset(original, glyphs);
        var subset = FontFiles.Load(result.FontData);

        foreach (var (oldId, newId) in result.GlyphIdMap)
            subset.GetGlyph(newId).GetCff1GlyphData().Name.Should().Be(original.GetGlyph(oldId).GetCff1GlyphData().Name);
    }

    [Fact]
    public void Woff_Cff_IsDecodedAndSubsetFaithfully()
    {
        var original = FontFiles.Load(FontFiles.GreatVibes);

        var result = FontSubsetter.Subset(FontFiles.Bytes(FontFiles.GreatVibesWoff), FontFiles.GlyphsFor(original, FontFiles.SampleText));

        SubsetVerifier.VerifyFaithful(original, result);
    }

    [Fact]
    public void OpenTypeCff2_ThrowsNotSupported()
    {
        // No CFF2 fixture ships with the tests; relabelling the CFF table is enough to
        // exercise the format check, which happens before any CFF2 data is read.
        var data = FontFiles.Bytes(FontFiles.GreatVibes);
        var font = SfntInspector.Parse(data);
        int record = 12 + (font.Directory.FindIndex(t => t.Tag == "CFF ") * 16);
        "CFF2"u8.CopyTo(data.AsSpan(record));

        var act = () => FontSubsetter.Subset(data, new HashSet<ushort> { 1, 2, 3 });

        act.Should().Throw<NotSupportedException>().WithMessage("*CFF2*");
    }

    [Fact]
    public void Collection_WithoutFontIndex_ThrowsNotSupported()
    {
        var act = () => FontSubsetter.Subset(FontFiles.Bytes(FontFiles.RobotoCollection), new HashSet<ushort> { 1, 2, 3 });

        act.Should().Throw<NotSupportedException>().WithMessage("*collection*fontIndex*");
    }

    [Theory]
    [InlineData(0, FontFiles.RobotoRegular)]
    [InlineData(1, FontFiles.RobotoBold)]
    public void Collection_WithFontIndex_IsSubsetFaithfully(int fontIndex, string sameFontAsTtf)
    {
        var original = FontFiles.Load(sameFontAsTtf);

        var result = FontSubsetter.Subset(
            FontFiles.Bytes(FontFiles.RobotoCollection), fontIndex, FontFiles.GlyphsFor(original, FontFiles.SampleText));

        SubsetVerifier.VerifyFaithful(original, result);
    }

    [Fact]
    public void Collection_FontIndexOutOfRange_Throws()
    {
        var act = () => FontSubsetter.Subset(FontFiles.Bytes(FontFiles.RobotoCollection), 2, new HashSet<ushort> { 1 });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FontIndex_ForNonCollection_ThrowsNotSupported()
    {
        var act = () => FontSubsetter.Subset(FontFiles.Bytes(FontFiles.RobotoRegular), 0, new HashSet<ushort> { 1 });

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void CollectionMember_ReadAsTypeface_IsSubsetFaithfully()
    {
        // Reading a collection member used to keep the bytes from the member's offset
        // onward, whose table offsets point at the wrong data. Subsetting that returned
        // a broken font with no error.
        var collection = FontFiles.Bytes(FontFiles.RobotoCollection);
        int boldOffset = (int)SfntInspector.U32(collection, 16);
        var member = FontFiles.Load(collection, boldOffset);
        var original = FontFiles.Load(FontFiles.RobotoBold);

        var result = FontSubsetter.Subset(member, FontFiles.GlyphsFor(member, FontFiles.SampleText));

        SubsetVerifier.VerifyFaithful(original, result);
    }

    [Fact]
    public void Woff_TrueType_IsDecodedAndSubsetFaithfully()
    {
        var original = FontFiles.Load(FontFiles.RobotoRegular);

        var result = FontSubsetter.Subset(FontFiles.Bytes(FontFiles.RobotoWoff), FontFiles.GlyphsFor(original, FontFiles.SampleText));

        SubsetVerifier.VerifyFaithful(original, result);
    }

    [Fact]
    public void Woff_ReadAsTypeface_IsSubsetFaithfully()
    {
        var woff = FontFiles.Load(FontFiles.RobotoWoff);
        var original = FontFiles.Load(FontFiles.RobotoRegular);

        var result = FontSubsetter.Subset(woff, FontFiles.GlyphsFor(woff, FontFiles.SampleText));

        SubsetVerifier.VerifyFaithful(original, result);
    }

    [Fact]
    public void Woff_WhenNothingIsDropped_ReturnsTheDecodedFont()
    {
        // Returning the font whole must still return something a PDF can embed: the
        // decoded sfnt, not the compressed WOFF.
        var original = FontFiles.Load(FontFiles.RobotoRegular);
        var everyGlyph = Enumerable.Range(0, original.GlyphCount).Select(i => (ushort)i).ToHashSet();

        var result = FontSubsetter.Subset(FontFiles.Bytes(FontFiles.RobotoWoff), everyGlyph);

        result.GlyphIdMap.Should().BeEmpty();
        SfntInspector.Parse(result.FontData).SfntVersion.Should().Be(0x00010000u);
        FontFiles.Load(result.FontData).GlyphCount.Should().Be(original.GlyphCount);
    }

    [Fact]
    public void Woff2_ThrowsNotSupported()
    {
        var act = () => FontSubsetter.Subset(FontFiles.Bytes(FontFiles.RobotoWoff2), new HashSet<ushort> { 1, 2, 3 });

        act.Should().Throw<NotSupportedException>().WithMessage("*WOFF2*");
    }

    [Fact]
    public void Woff2_ReadAsTypeface_ThrowsNotSupported()
    {
        var typeface = FontFiles.Load(FontFiles.RobotoWoff2);

        var act = () => FontSubsetter.Subset(typeface, new HashSet<ushort> { 1, 2, 3 });

        act.Should().Throw<NotSupportedException>().WithMessage("*WOFF2*");
    }

    [Fact]
    public void UnrecognisedData_ThrowsNotSupported()
    {
        var act = () => FontSubsetter.Subset("not a font at all"u8.ToArray(), new HashSet<ushort> { 1 });

        act.Should().Throw<NotSupportedException>();
    }
}
