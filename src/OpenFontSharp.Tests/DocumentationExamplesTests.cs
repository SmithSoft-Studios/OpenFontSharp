using System.Text;
using OpenFontSharp.Metrics;
using OpenFontSharp.Shaping;
using OpenFontSharp.Subsetting;
using OpenFontSharp.Tests.Subsetting;

namespace OpenFontSharp.Tests;

public class DocumentationExamplesTests
{
    [Fact]
    public void ReadmeMeasurementAndShapingExamples_UseDesignUnitScaling()
    {
        var bytes = FontFiles.Bytes(FontFiles.RobotoRegular);
        var typeface = FontFiles.Load(bytes);
        var info = FontLoader.FromTypeface(typeface, bytes);
        var expected = "Hello".Sum(c => (double)typeface.GetAdvanceWidthFromGlyphIndex(typeface.GetGlyphIndex(c)))
            * 12 / typeface.UnitsPerEm;

        Assert.Equal(expected, TextMeasurer.MeasureWidth(typeface, "Hello", 12));
        Assert.Equal(typeface.UnitsPerEm, info.UnitsPerEm);
        Assert.Equal(typeface.GlyphCount, info.GlyphWidths.Length);
        Assert.Equal(typeface.GetAdvanceWidthFromGlyphIndex(typeface.GetGlyphIndex('A')),
            info.GlyphWidths[typeface.GetGlyphIndex('A')]);
        var shaped = BasicLatinShaper.Shape(typeface, "office");
        Assert.False(shaped.RequiresComplexShaping);
        Assert.Equal(shaped.GlyphIds.Length, shaped.Advances.Length);
        Assert.True(shaped.Advances.Sum(a => (double)a) * 12 / typeface.UnitsPerEm > 0);
    }

    [Fact]
    public void ReadmeSubsetExample_MapsPdfCidsToRemappedGlyphsAndBmpUnicode()
    {
        var fontBytes = FontFiles.Bytes(FontFiles.RobotoRegular);
        var font = FontFiles.Load(fontBytes);
        const string text = "Hello";
        var usedGlyphIds = text.Select(c => font.GetGlyphIndex(c)).ToHashSet();
        Assert.DoesNotContain((ushort)0, usedGlyphIds);

        var subset = FontSubsetter.Subset(fontBytes, usedGlyphIds);
        ushort Remap(ushort oldId) =>
            subset.GlyphIdMap.Count == 0 ? oldId : subset.GlyphIdMap[oldId];

        var cidToUnicode = new Dictionary<int, int>();
        foreach (char c in text)
            cidToUnicode.TryAdd(Remap(font.GetGlyphIndex(c)), c);

        var cmap = Encoding.ASCII.GetString(ToUnicodeCMapBuilder.Build(cidToUnicode));
        var parsedSubset = FontFiles.Load(subset.FontData);
        Assert.True(subset.RetainedGlyphCount < subset.OriginalGlyphCount);
        foreach (var entry in cidToUnicode)
        {
            Assert.Contains($"<{entry.Key:X4}> <{entry.Value:X4}>", cmap);
            Assert.Equal(font.GetAdvanceWidthFromGlyphIndex(font.GetGlyphIndex(entry.Value)),
                parsedSubset.GetAdvanceWidthFromGlyphIndex((ushort)entry.Key));
        }
        Assert.Equal((ushort)0, parsedSubset.GetGlyphIndex('H'));
    }
}
