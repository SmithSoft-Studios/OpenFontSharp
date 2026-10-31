using OpenFontSharp.Subsetting;

namespace OpenFontSharp.Tests.Subsetting;

/// <summary>
/// Composite glyphs (most accented letters) are drawn from other glyphs. Subsetting a
/// composite must bring its components along and renumber the references to them.
/// </summary>
public class SubsetCompositeGlyphTests
{
    [Theory]
    [InlineData(FontFiles.RobotoRegular, 'é')]
    [InlineData(FontFiles.RobotoRegular, 'Å')]
    [InlineData(FontFiles.ShantellStatic, 'Ñ')]
    [InlineData(FontFiles.ShantellVariable, 'Ü')]
    public void Composite_PullsInItsComponents(string file, char accented)
    {
        var data = FontFiles.Bytes(file);
        var original = FontFiles.Load(data);
        var source = SfntInspector.Parse(data);
        ushort glyph = original.GetGlyphIndex(accented);

        var components = AllComponents(source, glyph);
        components.Should().NotBeEmpty($"'{accented}' is a composite glyph in this font");

        // Only the accented glyph is requested; its components must come along.
        var result = FontSubsetter.Subset(data, new HashSet<ushort> { glyph });

        result.GlyphIdMap.Keys.Should().Contain(components);
        result.RetainedGlyphCount.Should().Be(components.Count + 2, ".notdef, the composite and its components");

        // References inside the composite are renumbered to the subset's glyph ids.
        var subset = SfntInspector.Parse(result.FontData);
        var expected = SfntInspector.ComponentsOf(source.GlyphRecord(glyph)).Select(c => result.GlyphIdMap[c]);
        SfntInspector.ComponentsOf(subset.GlyphRecord(result.GlyphIdMap[glyph])).Should().Equal(expected);

        SubsetVerifier.VerifyFaithful(original, result);
    }

    /// <summary>Components of a glyph, followed transitively through nested composites.</summary>
    private static HashSet<ushort> AllComponents(SfntInspector font, ushort glyph)
    {
        var found = new HashSet<ushort>();
        var pending = new Stack<ushort>(SfntInspector.ComponentsOf(font.GlyphRecord(glyph)));
        while (pending.Count > 0)
        {
            var component = pending.Pop();
            if (found.Add(component))
            {
                foreach (var nested in SfntInspector.ComponentsOf(font.GlyphRecord(component)))
                    pending.Push(nested);
            }
        }
        return found;
    }
}
