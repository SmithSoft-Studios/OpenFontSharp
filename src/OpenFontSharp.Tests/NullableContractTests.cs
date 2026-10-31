using System.Reflection;
using OpenFontSharp.AdditionalInfo;
using OpenFontSharp.Tests.Subsetting;
using OpenFontSharp.Tables;

namespace OpenFontSharp.Tests;

public class NullableContractTests
{
    [Theory]
    [InlineData(nameof(Glyph.GlyphPoints))]
    [InlineData(nameof(Glyph.EndPoints))]
    [InlineData(nameof(Glyph.MathGlyphInfo))]
    public void OptionalGlyphProperties_AdvertiseNullableResults(string propertyName)
    {
        var property = typeof(Glyph).GetProperty(propertyName)!;
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(property).ReadState);
    }

    [Fact]
    public void CffGlyph_HasCffDataButNoTrueTypeArrays()
    {
        var typeface = FontFiles.Load(FontFiles.GreatVibes);
        var glyph = typeface.GetGlyph(typeface.GetGlyphIndex('A'));

        Assert.True(glyph.IsCffGlyph);
        Assert.NotNull(glyph.GetCff1GlyphData());
        Assert.Null(glyph.GlyphPoints);
        Assert.Null(glyph.EndPoints);
        Assert.False(glyph.HasGlyphInstructions);
    }

    [Fact]
    public void TrueTypeGlyph_HasOutlineButNoCffOrMathData()
    {
        var typeface = FontFiles.Load(FontFiles.RobotoRegular);
        var glyph = typeface.GetGlyph(typeface.GetGlyphIndex('A'));

        Assert.NotNull(glyph.GlyphPoints);
        Assert.NotNull(glyph.EndPoints);
        Assert.False(glyph.IsCffGlyph);
        Assert.Null(glyph.GetCff1GlyphData());
        Assert.Null(glyph.MathGlyphInfo);
        Assert.Null(typeface.MathConsts);
        Assert.Null(typeface.Filename);
    }

    [Fact]
    public void CollectionPreview_HasMembersButNoMemberSpecificTables()
    {
        var preview = new OpenFontReader().ReadPreview(new MemoryStream(FontFiles.Bytes(FontFiles.RobotoCollection)));

        Assert.NotNull(preview);
        Assert.True(preview.IsFontCollection);
        Assert.Equal(2, preview.MemberCount);
        Assert.Null(preview.NameEntry);
        Assert.Null(preview.OS2Table);
        Assert.Equal(string.Empty, preview.PostScriptName);
        Assert.Equal(0, preview.WeightClass);
        Assert.NotNull(preview.GetMember(0).NameEntry);
        Assert.NotNull(preview.GetMember(1).OS2Table);
    }

    [Fact]
    public void UndeclaredNameAndLanguageData_RemainsNullRatherThanInventedDefaults()
    {
        var names = new NameEntry();
        var languages = new Languages();

        Assert.Null(names.FontName);
        Assert.Null(names.FontSubFamily);
        Assert.Null(names.PostScriptName);
        Assert.Null(names.TypographicFamilyName);
        Assert.Null(languages.SupportedLangs);
        Assert.Null(languages.DesignLangs);
        Assert.Null(languages.GSUBScriptList);
        Assert.Null(languages.GPOSScriptList);
        Assert.False(languages.ContainGlyphForUnicode('A'));
    }

    [Theory]
    [InlineData(FontFiles.RobotoRegular)]
    [InlineData(FontFiles.GreatVibes)]
    public void TrimAndRestore_PreservesMetricsAndRestoresFormatSpecificOutlines(string font)
    {
        var bytes = FontFiles.Bytes(font);
        var typeface = FontFiles.Load(bytes);
        var glyphId = typeface.GetGlyphIndex('A');
        var advance = typeface.GetAdvanceWidthFromGlyphIndex(glyphId);
        var bounds = typeface.GetGlyph(glyphId).Bounds;

        var ticket = typeface.TrimDown();
        Assert.NotNull(ticket);
        Assert.True(typeface.IsTrimmed());
        Assert.Null(typeface.TrimDown());
        var trimmed = typeface.GetGlyph(glyphId);
        Assert.Null(trimmed.GlyphPoints);
        Assert.Null(trimmed.EndPoints);
        Assert.Null(trimmed.GetCff1GlyphData());
        Assert.False(trimmed.HasGlyphInstructions);
        Assert.Equal(advance, typeface.GetAdvanceWidthFromGlyphIndex(glyphId));
        Assert.Equal(bounds, trimmed.Bounds);

        typeface.RestoreUp(ticket, new MemoryStream(bytes));
        Assert.Equal(TrimMode.Restored, typeface.GetTrimMode());
        Assert.Equal(advance, typeface.GetAdvanceWidthFromGlyphIndex(glyphId));
        var restored = typeface.GetGlyph(glyphId);
        if (typeface.HasTtfOutline)
        {
            Assert.NotNull(restored.GlyphPoints);
            Assert.NotNull(restored.EndPoints);
            Assert.Null(restored.GetCff1GlyphData());
        }
        else
        {
            Assert.NotNull(restored.GetCff1GlyphData());
            Assert.Null(restored.GlyphPoints);
        }
    }
}
