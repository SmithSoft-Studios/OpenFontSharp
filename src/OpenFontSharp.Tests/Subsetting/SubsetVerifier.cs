using System.Globalization;
using OpenFontSharp.Subsetting;
using OpenFontSharp.Tables.CFF;

namespace OpenFontSharp.Tests.Subsetting;

/// <summary>
/// Checks that a subset is faithful: every retained glyph has the same outline, bounds
/// and metrics as in the original, and the file is structurally valid. Sizes are never
/// evidence — the earlier subsetter shrank fonts by blanking every glyph.
/// </summary>
internal static class SubsetVerifier
{
    public static Typeface VerifyFaithful(Typeface original, SubsetResult result)
    {
        result.GlyphIdMap.Should().NotBeEmpty("a subset was expected, not the whole font");
        result.GlyphIdMap.Should().ContainKey(0).WhoseValue.Should().Be(0, ".notdef must stay glyph 0");
        result.GlyphIdMap.Values.Order().Should().Equal(
            Enumerable.Range(0, result.RetainedGlyphCount).Select(i => (ushort)i), "new ids are contiguous");

        VerifyStructure(result);

        var subset = FontFiles.Load(result.FontData);
        subset.GlyphCount.Should().Be(result.RetainedGlyphCount);
        subset.UnitsPerEm.Should().Be(original.UnitsPerEm);
        subset.IsCffFont.Should().Be(original.IsCffFont);

        foreach (var (oldId, newId) in result.GlyphIdMap)
        {
            var before = original.GetGlyph(oldId);
            var after = subset.GetGlyph(newId);
            string because = $"glyph {oldId} (subset id {newId}) must keep its outline";

            var beforeOutline = Outline.Of(before);
            var afterOutline = Outline.Of(after);
            afterOutline.Contours.Should().Be(beforeOutline.Contours, because);
            afterOutline.Points.Should().Be(beforeOutline.Points, because);
            afterOutline.Commands.Should().Equal(beforeOutline.Commands, because);

            after.Bounds.XMin.Should().Be(before.Bounds.XMin, because);
            after.Bounds.YMin.Should().Be(before.Bounds.YMin, because);
            after.Bounds.XMax.Should().Be(before.Bounds.XMax, because);
            after.Bounds.YMax.Should().Be(before.Bounds.YMax, because);

            subset.GetAdvanceWidthFromGlyphIndex(newId).Should().Be(original.GetAdvanceWidthFromGlyphIndex(oldId), because);
            subset.GetLeftSideBearing(newId).Should().Be(original.GetLeftSideBearing(oldId), because);
        }

        return subset;
    }

    /// <summary>Structural checks that do not depend on the library's own reader.</summary>
    public static void VerifyStructure(SubsetResult result)
    {
        var font = SfntInspector.Parse(result.FontData);

        // Directory sorted, tables aligned and inside the file, checksums valid.
        font.Directory.Select(t => t.Tag).Should().BeInAscendingOrder(StringComparer.Ordinal);
        foreach (var table in font.Directory)
        {
            (table.Offset % 4).Should().Be(0, $"table '{table.Tag}' must be 4-byte aligned");
            (table.Offset + table.Length).Should().BeLessThanOrEqualTo(result.FontData.Length);

            var bytes = font.Bytes(table.Tag);
            if (table.Tag == "head")
                bytes[8] = bytes[9] = bytes[10] = bytes[11] = 0;
            SfntInspector.Checksum(bytes).Should().Be(table.Checksum, $"table '{table.Tag}' checksum must be valid");
        }
        SfntInspector.Checksum(result.FontData).Should().Be(0xB1B0AFBA, "head.checkSumAdjustment must balance the file");

        // head, maxp, hhea, hmtx agree on the glyph count.
        SfntInspector.U32(font.Bytes("head"), 12).Should().Be(0x5F0F3CF5, "head magic number");
        font.NumGlyphs.Should().Be(result.RetainedGlyphCount);
        font.NumberOfHMetrics.Should().BeInRange(1, font.NumGlyphs);
        font.Get("hmtx").Length.Should().Be((font.NumberOfHMetrics * 4) + ((font.NumGlyphs - font.NumberOfHMetrics) * 2));

        // No variation data may outlive the subset: the variation tables index glyphs.
        foreach (var tag in new[] { "fvar", "gvar", "HVAR", "VVAR", "avar", "MVAR", "cvar", "STAT", "GSUB", "GPOS", "GDEF", "kern", "hdmx", "vmtx" })
            font.Has(tag).Should().BeFalse($"'{tag}' refers to original glyph ids and must be dropped");

        // cmap may only point at glyphs that exist.
        font.CmapGlyphIds().Should().OnlyContain(g => g < font.NumGlyphs);

        if (font.SfntVersion == 0x4F54544F)
        {
            font.Has("CFF ").Should().BeTrue();
            font.Has("glyf").Should().BeFalse();
            return;
        }

        // glyf/loca consistency.
        font.SfntVersion.Should().Be(0x00010000u);
        var loca = font.Loca();
        loca.Should().BeInAscendingOrder("loca offsets never decrease");
        loca[^1].Should().BeLessThanOrEqualTo((uint)font.Get("glyf").Length);
        loca.Should().OnlyContain(o => o % 2 == 0, "glyph records start on even offsets");

        for (int glyph = 0; glyph < font.NumGlyphs; glyph++)
        {
            var record = font.GlyphRecord(glyph);
            foreach (var component in SfntInspector.ComponentsOf(record))
                ((int)component).Should().BeLessThan(font.NumGlyphs, $"glyph {glyph} references a component that exists");
        }
    }

    /// <summary>A glyph outline as the sequence of drawing commands the library produces.</summary>
    private sealed class Outline : IGlyphTranslator
    {
        public List<string> Commands { get; } = [];
        public int Contours { get; private set; }
        public int Points { get; private set; }

        public static Outline Of(Glyph glyph)
        {
            var outline = new Outline();
            if (glyph.IsCffGlyph)
            {
                var data = glyph.GetCff1GlyphData();
                Assert.NotNull(data);
                new CffEvaluationEngine().Run(outline, data);
            }
            else
                outline.Read(glyph.GlyphPoints, glyph.EndPoints);
            return outline;
        }

        public void BeginRead(int contourCount) { }
        public void EndRead() { }
        public void MoveTo(float x0, float y0) { Contours++; Add("M", x0, y0); }
        public void LineTo(float x1, float y1) => Add("L", x1, y1);
        public void Curve3(float x1, float y1, float x2, float y2) => Add("Q", x1, y1, x2, y2);
        public void Curve4(float x1, float y1, float x2, float y2, float x3, float y3) => Add("C", x1, y1, x2, y2, x3, y3);
        public void CloseContour() => Commands.Add("Z");

        private void Add(string op, params float[] coordinates)
        {
            Points += coordinates.Length / 2;
            Commands.Add(op + string.Join(" ", coordinates.Select(c => c.ToString("R", CultureInfo.InvariantCulture))));
        }
    }
}
