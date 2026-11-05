using OpenFontSharp.Subsetting;

namespace OpenFontSharp.Tests.Subsetting;

public class RealVariableFontReferenceTests
{
    public static IEnumerable<object[]> Instances()
    {
        yield return new object[]
        {
            "wght 700", FontFiles.ShantellWeight700Reference,
            new Dictionary<string, float> { ["wght"] = 700 }, 0
        };
        yield return new object[]
        {
            "mixed axes", FontFiles.ShantellMixedAxesReference,
            new Dictionary<string, float> { ["wght"] = 500, ["BNCE"] = -50, ["INFM"] = 75, ["SPAC"] = 25 }, 1
        };
        yield return new object[]
        {
            "axis maximums", FontFiles.ShantellMaximumAxesReference,
            new Dictionary<string, float> { ["wght"] = 800, ["BNCE"] = 100, ["INFM"] = 100, ["SPAC"] = 100 }, 0
        };
    }

    [Theory]
    [MemberData(nameof(Instances))]
    public void ShantellAxisInstanceMatchesFontToolsReference(
        string instanceName, string referencePath, Dictionary<string, float> coordinates, int expectedLargestDifference)
    {
        byte[] sourceBytes = FontFiles.Bytes(FontFiles.ShantellVariable);
        var source = FontFiles.Load(sourceBytes);
        ushort[] sourceGlyphs = new[] { 'A', 'V' }.Select(character => source.GetGlyphIndex(character)).ToArray();
        var result = FontSubsetter.Subset(sourceBytes, sourceGlyphs.ToHashSet(), coordinates);
        var actual = FontFiles.Load(result.FontData);
        var reference = FontFiles.Load(referencePath);

        result.RetainedGlyphCount.Should().Be(3, instanceName);
        actual.GlyphCount.Should().Be(reference.GlyphCount, instanceName);
        actual.GlyphCount.Should().Be(result.RetainedGlyphCount, instanceName);
        result.GlyphIdMap.Should().HaveCount(3, instanceName);

        float largestDifference = 0;
        string largestDifferenceLocation = "none";
        foreach (char character in new[] { 'A', 'V' })
        {
            ushort sourceGlyph = source.GetGlyphIndex(character);
            ushort actualGlyph = result.GlyphIdMap[sourceGlyph];
            ushort referenceGlyph = reference.GetGlyphIndex(character);
            var actualOutline = actual.GetGlyph(actualGlyph);
            var referenceOutline = reference.GetGlyph(referenceGlyph);

            actualOutline.EndPoints.Should().Equal(referenceOutline.EndPoints, instanceName);
            actualOutline.GlyphPoints.Should().HaveCount(referenceOutline.GlyphPoints!.Length, instanceName);
            actual.GetAdvanceWidthFromGlyphIndex(actualGlyph)
                .Should().Be(reference.GetAdvanceWidthFromGlyphIndex(referenceGlyph), instanceName);
            for (int point = 0; point < actualOutline.GlyphPoints!.Length; point++)
            {
                float xDifference = Math.Abs(actualOutline.GlyphPoints[point].X - referenceOutline.GlyphPoints![point].X);
                if (xDifference > largestDifference)
                {
                    largestDifference = xDifference;
                    largestDifferenceLocation = $"{character} point {point} X";
                }

                float yDifference = Math.Abs(actualOutline.GlyphPoints[point].Y - referenceOutline.GlyphPoints![point].Y);
                if (yDifference > largestDifference)
                {
                    largestDifference = yDifference;
                    largestDifferenceLocation = $"{character} point {point} Y";
                }
            }
        }

        largestDifference.Should().Be(expectedLargestDifference,
            $"{instanceName} should match FontTools in font units; largest difference was at {largestDifferenceLocation}");
    }
}
