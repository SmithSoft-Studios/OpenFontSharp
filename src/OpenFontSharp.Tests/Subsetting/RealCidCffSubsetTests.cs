using System.Globalization;
using OpenFontSharp.Subsetting;
using OpenFontSharp.Tables.CFF;

namespace OpenFontSharp.Tests.Subsetting;

public class RealCidCffSubsetTests
{
    [Fact]
    public void SourceHanCidFont_InvalidFormat0FontDictReferenceThrowsInvalidData()
    {
        const int fdSelectOffset = 335;
        var source = SfntInspector.Parse(FontFiles.Bytes(FontFiles.CidCff));
        byte[] cff = source.Bytes("CFF ");
        cff[fdSelectOffset].Should().Be(0);
        cff[fdSelectOffset + 1] = byte.MaxValue;

        var writer = new TtfWriter(source.SfntVersion);
        foreach (var table in source.Directory)
            writer.AddTable(table.Tag, table.Tag == "CFF " ? cff : source.Bytes(table.Tag));

        var act = () => FontFiles.Load(writer.Build());
        act.Should().Throw<InvalidDataException>().WithMessage("*FDSelect*Font DICT*");
    }

    [Fact]
    public void SourceHanCidFont_SubsetsOutlinesCidsWidthsAndGlyphCountAgainstFontToolsReference()
    {
        var source = FontFiles.Load(FontFiles.CidCff);
        source.GlyphCount.Should().Be(3);
        source.GetGlyphIndex('A').Should().Be(1);
        source.GetGlyphIndex('B').Should().Be(2);
        source.GetAdvanceWidthFromGlyphIndex(1).Should().Be(608);
        source.GetAdvanceWidthFromGlyphIndex(2).Should().Be(657);

        var result = FontSubsetter.Subset(FontFiles.Bytes(FontFiles.CidCff), new HashSet<ushort> { 1 });
        result.GlyphIdMap.Should().Equal(new Dictionary<ushort, ushort> { [0] = 0, [1] = 1 });
        var subset = SubsetVerifier.VerifyFaithful(source, result);

        subset.GlyphCount.Should().Be(2);
        subset.GetGlyph(0).GetCff1GlyphData()!.SIDName.Should().Be(0);
        subset.GetGlyph(1).GetCff1GlyphData()!.SIDName.Should().Be(34);
        subset.GetAdvanceWidthFromGlyphIndex(0).Should().Be(1000);
        subset.GetAdvanceWidthFromGlyphIndex(1).Should().Be(608);
        subset.GetGlyphIndex('B').Should().Be(0);
        Draw(subset, 0).Should().Equal(
            "moveTo 107 873", "lineTo 107 -112", "lineTo 895 -112", "lineTo 895 873", "close",
            "moveTo 814 824", "lineTo 501 421", "lineTo 187 824", "close",
            "moveTo 845 -23", "lineTo 532 380", "lineTo 845 783", "close",
            "moveTo 814 -63", "lineTo 187 -63", "lineTo 501 340", "close",
            "moveTo 156 -23", "lineTo 156 783", "lineTo 469 380", "close");
        Draw(subset, 1).Should().Equal(
            "moveTo 4 0", "lineTo 97 0", "lineTo 168 224", "lineTo 436 224",
            "lineTo 506 0", "lineTo 604 0", "lineTo 355 733", "lineTo 252 733", "close",
            "moveTo 191 297", "lineTo 227 410", "curveTo 253 493 277 572 300 658",
            "lineTo 304 658", "curveTo 328 573 351 493 378 410", "lineTo 413 297", "close");
    }

    private static string[] Draw(Typeface typeface, ushort glyphId)
    {
        var recorder = new OutlineRecorder();
        var data = typeface.GetGlyph(glyphId).GetCff1GlyphData();
        Assert.NotNull(data);
        new CffEvaluationEngine().Run(recorder, data);
        if (recorder.Commands.FirstOrDefault() == "close")
            recorder.Commands.RemoveAt(0);
        return recorder.Commands.ToArray();
    }

    private sealed class OutlineRecorder : IGlyphTranslator
    {
        public List<string> Commands { get; } = [];
        public void BeginRead(int contourCount) { }
        public void EndRead() { }
        public void MoveTo(float x, float y) => Add("moveTo", x, y);
        public void LineTo(float x, float y) => Add("lineTo", x, y);
        public void Curve3(float x1, float y1, float x2, float y2) => Add("curve3", x1, y1, x2, y2);
        public void Curve4(float x1, float y1, float x2, float y2, float x3, float y3) =>
            Add("curveTo", x1, y1, x2, y2, x3, y3);
        public void CloseContour() => Commands.Add("close");

        private void Add(string operation, params float[] values) =>
            Commands.Add(operation + " " + string.Join(" ", values.Select(value =>
                value.ToString("0.################", CultureInfo.InvariantCulture))));
    }
}
