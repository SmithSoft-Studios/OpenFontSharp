using OpenFontSharp.Subsetting;

namespace OpenFontSharp.Tests.Subsetting;

public class VariableSubsetTests
{
    [Theory]
    [InlineData(900, 20, 40, 580)]
    [InlineData(1200, 20, 40, 580)]
    [InlineData(650, 5, 10, 520)]
    [InlineData(400, 0, 0, 500)]
    [InlineData(100, 0, 0, 500)]
    public void NormalizesClampsMapsAndInterpolates(float weight, int first, int last, int advance)
    {
        var source = Font();
        var original = source.ToArray();
        var result = FontSubsetter.Subset(source, new HashSet<ushort> { 1 },
            new Dictionary<string, float> { ["wght"] = weight });
        var output = SfntInspector.Parse(result.FontData);
        var glyph = output.GlyphRecord(1);
        SfntInspector.I16(glyph, 2).Should().Be((short)first);
        SfntInspector.I16(glyph, 6).Should().Be((short)(100 + last));
        SfntInspector.U16(output.Bytes("hmtx"), 4).Should().Be((ushort)advance);
        output.Directory.Select(t => t.Tag).Should().NotContain(new[] { "fvar", "avar", "gvar", "cvar", "HVAR" });
        source.Should().Equal(original);
        // The middle point is untouched in the tuple and must receive an IUP delta.
        int coordinatesOffset = 14 + 3;
        int middleX = SfntInspector.I16(glyph, coordinatesOffset) + SfntInspector.I16(glyph, coordinatesOffset + 2);
        middleX.Should().Be(50 + (int)Math.Floor((first + last) / 2d + 0.5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HvarOverridesPhantomAdvanceAndCvarChangesCvt(bool explicitMap)
    {
        var result = FontSubsetter.Subset(Font(hvar: true, explicitMap: explicitMap), new HashSet<ushort> { 1 },
            new Dictionary<string, float> { ["wght"] = 900 });
        var output = SfntInspector.Parse(result.FontData);
        SfntInspector.U16(output.Bytes("hmtx"), 4).Should().Be(620);
        SfntInspector.I16(output.Bytes("cvt "), 0).Should().Be(110);
    }

    [Fact]
    public void SharedPeakAndSharedPointNumbersWork()
    {
        var result = FontSubsetter.Subset(Font(shared: true), new HashSet<ushort> { 1 },
            new Dictionary<string, float> { ["wght"] = 900 });
        SfntInspector.I16(SfntInspector.Parse(result.FontData).GlyphRecord(1), 6).Should().Be(140);
    }

    [Theory]
    [InlineData("oops", 500)]
    [InlineData("wght", float.NaN)]
    [InlineData("wght", float.PositiveInfinity)]
    public void RejectsUnknownAndNonfiniteCoordinates(string tag, float value)
    {
        Action act = () => FontSubsetter.Subset(Font(), new HashSet<ushort> { 1 },
            new Dictionary<string, float> { [tag] = value });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RejectsTruncatedTupleData()
    {
        var bytes = Font();
        var table = SfntInspector.Parse(bytes).Get("gvar");
        bytes[table.Offset + table.Length - 1] = 0x7f;
        Action act = () => FontSubsetter.Subset(bytes, new HashSet<ushort> { 1 },
            new Dictionary<string, float> { ["wght"] = 900 });
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(650, 20)]
    [InlineData(525, 10)]
    [InlineData(900, 0)]
    public void IntermediateRegionUsesBothSidesOfPeak(float weight, int delta)
    {
        var font = Replace(Font(), "avar", Words(1, 0, 0, 1, 3, -16384, -16384, 0, 0, 16384, 16384));
        // All seven points, including phantoms; peak .5, support [0,1].
        byte[] deltas = { 0, 20, 0x85, 0x86 };
        var tuple = Words(1, 14, deltas.Length, 0xc000, 8192, 0, 16384).Concat(deltas).ToArray();
        font = Replace(font, "gvar", Gvar(tuple));
        var output = Instance(font, weight);
        SfntInspector.I16(output.GlyphRecord(1), 17).Should().Be((short)delta);
    }

    [Theory]
    [InlineData(650, 10)]
    [InlineData(900, 0)]
    public void NonIntermediateTupleDoesNotApplyBeyondPeak(float weight, int delta)
    {
        byte[] deltas = { 0, 20, 0x85, 0x86 };
        var tuple = Words(1, 10, deltas.Length, 0x8000, 8192).Concat(deltas).ToArray();
        SfntInspector.I16(Instance(Replace(Font(), "gvar", Gvar(tuple)), weight).GlyphRecord(1), 17)
            .Should().Be((short)delta);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(650)]
    [InlineData(900)]
    public void IntermediateRegionCrossingZeroMakesAxisNonparticipating(float weight)
    {
        byte[] deltas = { 0, 20, 0x85, 0x86 };
        var tuple = Words(1, 14, deltas.Length, 0xc000, 8192, -8192, 16384).Concat(deltas).ToArray();
        SfntInspector.I16(Instance(Replace(Font(), "gvar", Gvar(tuple)), weight).GlyphRecord(1), 17)
            .Should().Be(20);
    }

    [Fact]
    public void IntermediateRegionWithUnorderedCoordinatesMakesAxisNonparticipating()
    {
        byte[] deltas = { 0, 20, 0x85, 0x86 };
        var tuple = Words(1, 14, deltas.Length, 0xc000, 8192, 16384, 0).Concat(deltas).ToArray();
        SfntInspector.I16(Instance(Replace(Font(), "gvar", Gvar(tuple)), 400).GlyphRecord(1), 17)
            .Should().Be(20);
    }

    [Fact]
    public void MultipleTuplesAccumulateBeforeRounding()
    {
        byte[] deltas = { 0, 1, 0x85, 0x86 };
        var tuple = Words(2, 16, deltas.Length, 0x8000, 16384, deltas.Length, 0x8000, 16384)
            .Concat(deltas).Concat(deltas).ToArray();
        var font = Replace(Font(), "gvar", Gvar(tuple));
        // avar maps .5 to .25; two .25 deltas sum to .5, rounding to one.
        SfntInspector.I16(Instance(font, 650).GlyphRecord(1), 2).Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CompositeOffsetsAndBoundsIncludeOwnAndChildVariation(bool byteArguments, bool scaledOffset)
    {
        var font = Font();
        var original = SfntInspector.Parse(font);
        int flags = (byteArguments ? 2 : 3) | (scaledOffset ? 0x808 : 0);
        byte[] composite = Words(-1, 10, 0, 110, 100, flags, 1)
            .Concat(byteArguments ? new byte[] { 10, 0 } : Words(10, 0))
            .Concat(scaledOffset ? Words(8192) : Array.Empty<byte>()).ToArray();
        var glyph = original.GlyphRecord(1);
        if ((glyph.Length & 1) != 0) glyph = glyph.Concat(new byte[] { 0 }).ToArray();
        font = Replace(font, "glyf", glyph.Concat(composite).ToArray());
        font = Replace(font, "loca", Dwords(0, 0, (uint)glyph.Length, (uint)(glyph.Length + composite.Length)));
        var maxp = original.Bytes("maxp"); Put16(maxp, 4, 3); font = Replace(font, "maxp", maxp);
        var hhea = original.Bytes("hhea"); Put16(hhea, 34, 3); font = Replace(font, "hhea", hhea);
        font = Replace(font, "hmtx", Words(500, 0, 500, 0, 500, 10));
        // Composite has one component + four phantom points. Move the component 30.
        byte[] deltas = { 0, 30, 0x83, 0x84 };
        var tuple = Words(1, 10, deltas.Length, 0x8000, 16384).Concat(deltas).ToArray();
        var baseTuple = original.Bytes("gvar").AsSpan(32).ToArray();
        var variation = Words(1, 0, 1, 0).Concat(Dwords(36)).Concat(Words(3, 1))
            .Concat(Dwords(36, 0, 0, (uint)baseTuple.Length, (uint)(baseTuple.Length + tuple.Length)))
            .Concat(baseTuple).Concat(tuple).ToArray();
        font = Replace(font, "gvar", variation);
        var output = Instance(font, 900, new HashSet<ushort> { 2 });
        var record = output.GlyphRecord(2);
        SfntInspector.I16(record, 2).Should().Be(scaledOffset ? (short)30 : (short)60);
        SfntInspector.I16(record, 6).Should().Be(scaledOffset ? (short)90 : (short)180);
        SfntInspector.I16(record, 14).Should().Be(40);
        SfntInspector.ComponentsOf(record).Should().Equal(1);
    }

    [Fact]
    public void PointAttachedCompositeIgnoresComponentDeltasAndPreservesAttachment()
    {
        var font = Font(); var original = SfntInspector.Parse(font);
        var composite = Words(-1, 10, 0, 110, 100, 35, 1, 10, 0, 1, 1, 1, 0);
        var child = original.GlyphRecord(1);
        if ((child.Length & 1) != 0) child = child.Concat(new byte[] { 0 }).ToArray();
        font = Replace(font, "glyf", child.Concat(composite).ToArray());
        font = Replace(font, "loca", Dwords(0, 0, (uint)child.Length, (uint)(child.Length + composite.Length)));
        var maxp = original.Bytes("maxp"); Put16(maxp, 4, 3); font = Replace(font, "maxp", maxp);
        var hhea = original.Bytes("hhea"); Put16(hhea, 34, 3); font = Replace(font, "hhea", hhea);
        font = Replace(font, "hmtx", Words(500, 0, 500, 0, 500, 10));
        byte[] deltas = { 0x80, 0, 30, 0x83, 0x80, 0, 20, 0x83 };
        var tuple = Words(1, 10, deltas.Length, 0x8000, 16384).Concat(deltas).ToArray();
        var baseTuple = original.Bytes("gvar").AsSpan(32).ToArray();
        var variation = Words(1, 0, 1, 0).Concat(Dwords(36)).Concat(Words(3, 1))
            .Concat(Dwords(36, 0, 0, (uint)baseTuple.Length, (uint)(baseTuple.Length + tuple.Length)))
            .Concat(baseTuple).Concat(tuple).ToArray();
        var record = Instance(Replace(font, "gvar", variation), 900, new HashSet<ushort> { 2 }).GlyphRecord(2);
        SfntInspector.I16(record, 2).Should().Be(30);
        SfntInspector.I16(record, 6).Should().Be(210);
        SfntInspector.I16(record, 8).Should().Be(200);
        (SfntInspector.U16(record, 18) & 2).Should().Be(0);
        SfntInspector.U16(record, 22).Should().Be(1);
        SfntInspector.U16(record, 24).Should().Be(0);
    }

    [Theory]
    [InlineData(false, false, 580)]
    [InlineData(false, true, 580)]
    [InlineData(true, false, 620)]
    [InlineData(true, true, 620)]
    public void CompositeUseMyMetricsInheritsVariedComponentPhantomsUnlessHvarOverridesAdvance(bool hvar, bool ownPhantoms, int expectedAdvance)
    {
        var font = Font(hvar: hvar); var original = SfntInspector.Parse(font);
        var composite = Words(-1, 10, 0, 110, 100, 0x203, 1, 10, 0);
        var child = original.GlyphRecord(1);
        if ((child.Length & 1) != 0) child = child.Concat(new byte[] { 0 }).ToArray();
        font = Replace(font, "glyf", child.Concat(composite).ToArray());
        font = Replace(font, "loca", Dwords(0, 0, (uint)child.Length, (uint)(child.Length + composite.Length)));
        var maxp = original.Bytes("maxp"); Put16(maxp, 4, 3); font = Replace(font, "maxp", maxp);
        var hhea = original.Bytes("hhea"); Put16(hhea, 34, 3); font = Replace(font, "hhea", hhea);
        font = Replace(font, "hmtx", Words(500, 0, 500, 0, 500, 10));
        var baseTuple = original.Bytes("gvar").AsSpan(32).ToArray();
        byte[] deltas = { 0x80, 0x41, 0, 50, 0, 250, 0x81, 0x84 };
        var ownTuple = ownPhantoms
            ? Words(1, 10, deltas.Length, 0x8000, 16384).Concat(deltas).ToArray()
            : Array.Empty<byte>();
        var variation = Words(1, 0, 1, 0).Concat(Dwords(36)).Concat(Words(3, 1))
            .Concat(Dwords(36, 0, 0, (uint)baseTuple.Length, (uint)(baseTuple.Length + ownTuple.Length)))
            .Concat(baseTuple).Concat(ownTuple).ToArray();
        font = Replace(font, "gvar", variation);
        if (hvar)
        {
            var hv = original.Bytes("HVAR");
            Put32(hv, 8, (uint)hv.Length);
            // Repeat row 1 for the composite as well.
            font = Replace(font, "HVAR", hv.Concat(new byte[] { 0, 0, 0, 2, 0, 1 }).ToArray());
        }
        var output = Instance(font, 900, new HashSet<ushort> { 2 });
        SfntInspector.U16(output.Bytes("hmtx"), 8).Should().Be((ushort)expectedAdvance);
        SfntInspector.I16(output.Bytes("hmtx"), 10).Should().Be(30);
    }

    [Fact]
    public void ExpandsSharedAdvanceTailAndRecomputesFontMetricSummaries()
    {
        var font = Font();
        var hhea = SfntInspector.Parse(font).Bytes("hhea"); Put16(hhea, 34, 1);
        font = Replace(Replace(font, "hhea", hhea), "hmtx", Words(500, 0, 0));
        var output = Instance(font, 900);
        var metrics = output.Bytes("hmtx"); var summary = output.Bytes("hhea"); var head = output.Bytes("head");
        metrics.Length.Should().Be(8);
        SfntInspector.U16(metrics, 0).Should().Be(500);
        SfntInspector.U16(metrics, 4).Should().Be(580);
        SfntInspector.U16(summary, 34).Should().Be(2);
        SfntInspector.U16(summary, 10).Should().Be(580);
        SfntInspector.I16(summary, 12).Should().Be(0);
        SfntInspector.I16(summary, 14).Should().Be(440);
        SfntInspector.I16(summary, 16).Should().Be(140);
        SfntInspector.I16(head, 36).Should().Be(20);
        SfntInspector.I16(head, 38).Should().Be(0);
        SfntInspector.I16(head, 40).Should().Be(140);
        SfntInspector.I16(head, 42).Should().Be(100);
    }

    [Fact]
    public void LeftSideBearingSubtractsVariedLeftPhantomFromNewXmin()
    {
        var font = Font(); var head = SfntInspector.Parse(font).Bytes("head"); Put16(head, 16, 2);
        font = Replace(font, "head", head);
        byte[] packed = { 4, 3, 0, 2, 1, 1, 3, 20, 40, 10, 90, 0x83 };
        var tuple = Words(1, 10, packed.Length, 0xa000, 16384).Concat(packed).ToArray();
        var output = Instance(Replace(font, "gvar", Gvar(tuple)), 900);
        SfntInspector.I16(output.Bytes("hmtx"), 6).Should().Be(10);
        SfntInspector.U16(output.Bytes("hmtx"), 4).Should().Be(580);
        (SfntInspector.U16(output.Bytes("head"), 16) & 2).Should().Be(0);
    }

    [Fact]
    public void ReducedSubsetRetainsInstantiatedCvtAndOutlines()
    {
        var result = FontSubsetter.Subset(Font(), new HashSet<ushort>(),
            new Dictionary<string, float> { ["wght"] = 900 });
        result.RetainedGlyphCount.Should().Be(1);
        var output = SfntInspector.Parse(result.FontData);
        output.NumGlyphs.Should().Be(1);
        SfntInspector.I16(output.Bytes("cvt "), 0).Should().Be(110);
        result.GlyphIdMap.Should().ContainKey(0);
    }

    [Fact]
    public void ActualSubsetDiscardsUnrequestedOutlineAndKeepsInstantiatedRequestedGlyph()
    {
        var font = Font(); var original = SfntInspector.Parse(font);
        var glyph = original.GlyphRecord(1);
        if ((glyph.Length & 1) != 0) glyph = glyph.Concat(new byte[] { 0 }).ToArray();
        font = Replace(font, "glyf", glyph.Concat(glyph).ToArray());
        font = Replace(font, "loca", Dwords(0, 0, (uint)glyph.Length, (uint)(glyph.Length * 2)));
        var maxp = original.Bytes("maxp"); Put16(maxp, 4, 3); font = Replace(font, "maxp", maxp);
        var hhea = original.Bytes("hhea"); Put16(hhea, 34, 3); font = Replace(font, "hhea", hhea);
        font = Replace(font, "hmtx", Words(500, 0, 500, 0, 500, 0));
        var baseTuple = original.Bytes("gvar").AsSpan(32).ToArray();
        var gvar = Words(1, 0, 1, 0).Concat(Dwords(36)).Concat(Words(3, 1))
            .Concat(Dwords(36, 0, 0, (uint)baseTuple.Length, (uint)baseTuple.Length)).Concat(baseTuple).ToArray();
        font = Replace(font, "gvar", gvar);
        var result = FontSubsetter.Subset(font, new HashSet<ushort> { 1 }, new Dictionary<string, float> { ["wght"] = 900 });
        var output = SfntInspector.Parse(result.FontData);
        output.NumGlyphs.Should().Be(2);
        result.GlyphIdMap.Should().ContainKey(1).And.NotContainKey(2);
        SfntInspector.I16(output.GlyphRecord(1), 6).Should().Be(140);
        SfntInspector.U16(output.Bytes("hmtx"), 4).Should().Be(580);
        output.Has("gvar").Should().BeFalse();
    }

    [Fact]
    public void OmittedAxisUsesDefaultAndLegacyOverloadIsUnchanged()
    {
        var font = Font();
        var ids = new HashSet<ushort> { 1 };
        FontSubsetter.Subset(font, ids).FontData.Should().Equal(font);
        var result = FontSubsetter.Subset(font, ids, new Dictionary<string, float>());
        var output = SfntInspector.Parse(result.FontData);
        output.Has("fvar").Should().BeFalse();
        SfntInspector.I16(output.GlyphRecord(1), 6).Should().Be(100);
    }

    [Fact]
    public void RejectsCompositeCycles()
    {
        var font = Replace(Font(), "glyf", Words(-1, 0, 0, 0, 0, 3, 1, 0, 0));
        font = Replace(font, "loca", Dwords(0, 0, 18));
        font = Replace(font, "gvar", Gvar([]));
        Action act = () => Instance(font, 900);
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(650, 50, 15)]
    [InlineData(900, 100, 120)]
    [InlineData(900, 0, 0)]
    public void MultipleAxesMultiplyTheirScalars(float weight, float width, int delta)
    {
        var axes = Words(1, 0, 16, 2, 2, 20, 0, 8)
            .Concat(Axis("wght", 100, 400, 900)).Concat(Axis("wdth", 0, 0, 100)).ToArray();
        var font = Replace(Font(), "fvar", axes);
        font = Replace(font, "avar", Words(1, 0, 0, 2, 4, -16384, -16384, 0, 0, 8192, 4096, 16384, 16384,
            3, -16384, -16384, 0, 0, 16384, 16384));
        byte[] deltas = { 0, 120, 0x85, 0x86 };
        var tuple = Words(1, 12, deltas.Length, 0x8000, 16384, 16384).Concat(deltas).ToArray();
        var gvar = Gvar(tuple); Put16(gvar, 4, 2);
        font = Replace(font, "gvar", gvar);
        // Remove cvar's single-axis tuple by replacing it with a valid empty store.
        font = Replace(font, "cvar", Words(1, 0, 0, 8));
        var output = SfntInspector.Parse(FontSubsetter.Subset(font, new HashSet<ushort> { 1 },
            new Dictionary<string, float> { ["wght"] = weight, ["wdth"] = width }).FontData);
        SfntInspector.I16(output.GlyphRecord(1), 17).Should().Be((short)delta);
    }

    [Theory]
    [InlineData(100, 20)]
    [InlineData(250, 10)]
    [InlineData(400, 0)]
    public void NegativeNormalizedCoordinatesApplyNegativePeak(float weight, int delta)
    {
        byte[] deltas = { 0, 20, 0x85, 0x86 };
        var tuple = Words(1, 10, deltas.Length, 0x8000, -16384).Concat(deltas).ToArray();
        SfntInspector.I16(Instance(Replace(Font(), "gvar", Gvar(tuple)), weight).GlyphRecord(1), 2)
            .Should().Be((short)delta);
    }

    [Fact]
    public void PackedWordPointsAndSignedWordDeltas()
    {
        // Private points 0 and 2, encoded as 16-bit increments; signed word deltas.
        byte[] packed = { 2, 0x81, 0, 0, 0, 2, 0x41, 0xff, 0x38, 0x01, 0x2c, 0x81 };
        var tuple = Words(1, 10, packed.Length, 0xa000, 16384).Concat(packed).ToArray();
        var output = Instance(Replace(Font(), "gvar", Gvar(tuple)), 900);
        var record = output.GlyphRecord(1);
        SfntInspector.I16(record, 2).Should().Be(-200);
        SfntInspector.I16(record, 6).Should().Be(400);
        (SfntInspector.I16(record, 17) + SfntInspector.I16(record, 19)).Should().Be(100);
    }

    [Fact]
    public void HvarFormatOneMapRepeatsLastEntryAndSupportsLongWords()
    {
        var store = Words(1).Concat(Dwords(12)).Concat(Words(1)).Concat(Dwords(22))
            .Concat(Words(1, 1, 0, 16384, 16384))
            .Concat(Words(2, 0x8001, 1, 0)).Concat(Dwords(0, unchecked((uint)-40000))).ToArray();
        var hvar = Words(1, 0).Concat(Dwords(20, (uint)(20 + store.Length), 0, 0))
            .Concat(store).Concat(new byte[] { 1, 0x17, 0, 0, 0, 1, 0, 1 }).ToArray();
        var font = Replace(Replace(Font(), "HVAR", hvar), "hmtx", Words(50000, 0, 50000, 0));
        var output = Instance(font, 900);
        SfntInspector.U16(output.Bytes("hmtx"), 4).Should().Be(10000);
        SfntInspector.U16(output.Bytes("hmtx"), 0).Should().Be(10000);
    }

    [Fact]
    public void HvarNoVariationSentinelOverridesPhantomAdvanceWithZeroDelta()
    {
        var font = Font(hvar: true);
        var hvar = SfntInspector.Parse(font).Bytes("HVAR");
        int offset = hvar.Length;
        Put32(hvar, 8, (uint)offset);
        hvar = hvar.Concat(new byte[] { 0, 0x3f, 0, 1, 0xff, 0xff, 0xff, 0xff }).ToArray();
        SfntInspector.U16(Instance(Replace(font, "HVAR", hvar), 900).Bytes("hmtx"), 4).Should().Be(500);
    }

    [Fact]
    public void ShortGvarOffsetsAreDoubled()
    {
        var font = Font();
        byte[] tuple = SfntInspector.Parse(font).Bytes("gvar").AsSpan(32).ToArray();
        if ((tuple.Length & 1) != 0) tuple = tuple.Concat(new byte[] { 0 }).ToArray();
        var gvar = Words(1, 0, 1, 0).Concat(Dwords(26)).Concat(Words(2, 0))
            .Concat(Dwords(26)).Concat(Words(0, 0, tuple.Length / 2)).Concat(tuple).ToArray();
        SfntInspector.I16(Instance(Replace(font, "gvar", gvar), 900).GlyphRecord(1), 6).Should().Be(140);
    }

    [Fact]
    public void CvarPrivatePointSelectionChangesOnlySelectedCvtEntries()
    {
        var font = Replace(Font(), "cvt ", Words(100, 200));
        byte[] packed = { 1, 0, 1, 0, 25 };
        var cvar = Words(1, 0, 1, 14, packed.Length, 0xa000, 16384).Concat(packed).ToArray();
        var cvt = Instance(Replace(font, "cvar", cvar), 900).Bytes("cvt ");
        SfntInspector.I16(cvt, 0).Should().Be(100);
        SfntInspector.I16(cvt, 2).Should().Be(225);
    }

    [Fact]
    public void CvarSharedPointsApplyToEveryTupleAndRoundCombinedDeltasOnce()
    {
        var font = Replace(Font(), "cvt ", Words(100, 200));
        var cvar = Words(1, 0, 0x8002, 20, 2, 0x8000, 16384, 2, 0x8000, 16384)
            .Concat(new byte[] { 1, 0, 1, 0, 1, 0, 1 }).ToArray();
        var cvt = Instance(Replace(font, "cvar", cvar), 650).Bytes("cvt ");
        SfntInspector.I16(cvt, 0).Should().Be(100);
        SfntInspector.I16(cvt, 2).Should().Be(201);
    }

    [Fact]
    public void GvarPhantomAdvanceRoundsCombinedTupleDeltasOnce()
    {
        byte[] deltas = { 0x83, 0, 1, 0x81, 0x86 };
        var tuple = Words(2, 16, deltas.Length, 0x8000, 16384, deltas.Length, 0x8000, 16384)
            .Concat(deltas).Concat(deltas).ToArray();
        var output = Instance(Replace(Font(), "gvar", Gvar(tuple)), 650);
        SfntInspector.U16(output.Bytes("hmtx"), 4).Should().Be(501);
    }

    [Fact]
    public void HvarAdvanceRoundsCombinedRegionDeltasOnce()
    {
        var store = Words(1).Concat(Dwords(12)).Concat(Words(1)).Concat(Dwords(28))
            .Concat(Words(1, 2, 0, 16384, 16384, 0, 16384, 16384))
            .Concat(Words(2, 2, 2, 0, 1, 0, 0, 1, 1)).ToArray();
        var hvar = Words(1, 0).Concat(Dwords(20, 0, 0, 0)).Concat(store).ToArray();
        var output = Instance(Replace(Font(), "HVAR", hvar), 650);
        SfntInspector.U16(output.Bytes("hmtx"), 4).Should().Be(501);
    }

    [Fact]
    public void EveryTruncatedVariationTableIsRejectedWithoutChangingInput()
    {
        var font = Font(hvar: true);
        var original = SfntInspector.Parse(font);
        foreach (string tag in new[] { "fvar", "avar", "gvar", "HVAR", "cvar" })
        {
            var table = original.Bytes(tag);
            for (int length = 0; length < table.Length; length++)
            {
                var input = Replace(font, tag, table.AsSpan(0, length).ToArray());
                var copy = input.ToArray();
                Action act = () => Instance(input, 900);
                act.Should().Throw<InvalidDataException>($"{tag} truncated to {length} bytes must not be used");
                input.Should().Equal(copy);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingVariableFixtureProducesReadableStaticInstance(bool allGlyphs)
    {
        var source = FontFiles.Bytes(FontFiles.ShantellVariable);
        var face = FontFiles.Load(source);
        var ids = allGlyphs
            ? Enumerable.Range(0, SfntInspector.Parse(source).NumGlyphs).Select(i => (ushort)i).ToHashSet()
            : FontFiles.GlyphsFor(face, "Hello Ü");
        var result = FontSubsetter.Subset(source, ids, new Dictionary<string, float> { ["wght"] = 900 },
            includeUnicodeCmap: true);
        var output = SfntInspector.Parse(result.FontData);
        output.Has("fvar").Should().BeFalse();
        output.Has("gvar").Should().BeFalse();
        var loaded = FontFiles.Load(result.FontData);
        loaded.GetGlyphIndex('H').Should().NotBe(0);
        int originalId = face.GetGlyphIndex('H');
        int resultId = loaded.GetGlyphIndex('H');
        loaded.GetGlyph((ushort)resultId).GlyphPoints.Should().NotBeEmpty();
        output.GlyphRecord(resultId).Should().NotEqual(SfntInspector.Parse(source).GlyphRecord(originalId));
        if (allGlyphs) result.GlyphIdMap.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, 10, 110)]
    [InlineData(false, 0, 100)]
    public void IupSingleTouchedPointTranslatesContourButPhantomsDoNot(bool contourPoint, int xmin, int xmax)
    {
        byte[] packed = { 1, 0, contourPoint ? (byte)0 : (byte)4, 0, 10, 0x80 };
        var tuple = Words(1, 10, packed.Length, 0xa000, 16384).Concat(packed).ToArray();
        var output = Instance(Replace(Font(), "gvar", Gvar(tuple)), 900);
        SfntInspector.I16(output.GlyphRecord(1), 2).Should().Be((short)xmin);
        SfntInspector.I16(output.GlyphRecord(1), 6).Should().Be((short)xmax);
        SfntInspector.U16(output.Bytes("hmtx"), 4).Should().Be(contourPoint ? (ushort)500 : (ushort)510);
    }

    [Fact]
    public void IupEqualCoordinatesWithDifferentDeltasDoNotMoveUntouchedPoint()
    {
        // End points 0 and 2 have equal y coordinates but different y deltas.
        byte[] packed = { 2, 1, 0, 2, 0x81, 1, 20, 40 };
        var tuple = Words(1, 10, packed.Length, 0xa000, 16384).Concat(packed).ToArray();
        var output = Instance(Replace(Font(), "gvar", Gvar(tuple)), 900);
        var record = output.GlyphRecord(1);
        int yStart = 23;
        (SfntInspector.I16(record, yStart) + SfntInspector.I16(record, yStart + 2)).Should().Be(100);
    }

    [Fact]
    public void InstantiationRejectsNonmonotonicLocaAndInvalidSharedTupleIndex()
    {
        var font = Replace(Font(), "loca", Dwords(0, 20, 10));
        Action badLoca = () => Instance(font, 900);
        badLoca.Should().Throw<InvalidDataException>();
        var tuple = Words(1, 8, 0, 1);
        Action badTuple = () => Instance(Replace(Font(), "gvar", Gvar(tuple)), 400);
        badTuple.Should().Throw<InvalidDataException>("inactive tuples must still be structurally validated");
    }

    [Fact]
    public void RejectsOverlappingTableReferencesBeforeCopyingPayloads()
    {
        var font = Font();
        var original = SfntInspector.Parse(font);
        var alias = original.Directory.Single(t => t.Tag == "cvar");
        var target = original.Get("gvar");
        int index = original.Directory.IndexOf(alias);
        Put32(font, 12 + index * 16 + 8, (uint)target.Offset);
        Put32(font, 12 + index * 16 + 12, (uint)target.Length);
        Action act = () => Instance(font, 900);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsGlobalExpansionOfCompactRepeatedPointFlags()
    {
        const int count = 65;
        var font = Font(); var original = SfntInspector.Parse(font);
        // 65,536 coincident points encoded with only 512 flag bytes and no coordinates.
        var compact = Words(1, 0, 0, 0, 0, 65535, 0)
            .Concat(Enumerable.Range(0, 256).SelectMany(_ => new byte[] { 0x39, 255 })).ToArray();
        font = Replace(font, "glyf", Enumerable.Range(0, count).SelectMany(_ => compact).ToArray());
        font = Replace(font, "loca", Dwords(Enumerable.Range(0, count + 1).Select(i => (uint)(i * compact.Length)).ToArray()));
        var maxp = original.Bytes("maxp"); Put16(maxp, 4, count); font = Replace(font, "maxp", maxp);
        var hhea = original.Bytes("hhea"); Put16(hhea, 34, 1); font = Replace(font, "hhea", hhea);
        font = Replace(font, "hmtx", Words(new[] { 500, 0 }.Concat(Enumerable.Repeat(0, count - 1)).ToArray()));
        int start = 20 + (count + 1) * 4;
        var gvar = Words(1, 0, 1, 0).Concat(Dwords((uint)start)).Concat(Words(count, 1))
            .Concat(Dwords((uint)start)).Concat(new byte[(count + 1) * 4]).ToArray();
        Action act = () => Instance(Replace(font, "gvar", gvar), 900);
        act.Should().Throw<InvalidDataException>();
    }

    private static byte[] Axis(string tag, uint min, uint def, uint max) =>
        System.Text.Encoding.ASCII.GetBytes(tag).Concat(Dwords(min << 16, def << 16, max << 16)).Concat(Words(0, 256)).ToArray();

    private static SfntInspector Instance(byte[] font, float weight, ISet<ushort>? glyphs = null) =>
        SfntInspector.Parse(FontSubsetter.Subset(font, glyphs ?? new HashSet<ushort> { 1 },
            new Dictionary<string, float> { ["wght"] = weight }).FontData);

    private static byte[] Replace(byte[] font, string tag, byte[] table)
    {
        var original = SfntInspector.Parse(font);
        var writer = new TtfWriter();
        foreach (var t in original.Directory) writer.AddTable(t.Tag, t.Tag == tag ? table : original.Bytes(t.Tag));
        if (!original.Has(tag)) writer.AddTable(tag, table);
        return writer.Build();
    }

    private static byte[] Gvar(byte[] tuple) => Words(1, 0, 1, 0).Concat(Dwords(32))
        .Concat(Words(2, 1)).Concat(Dwords(32, 0, 0, (uint)tuple.Length)).Concat(tuple).ToArray();

    private static byte[] Font(bool hvar = false, bool explicitMap = false, bool shared = false)
    {
        var tables = new Dictionary<string, byte[]>();
        var head = new byte[54]; Put16(head, 18, 1000); Put16(head, 50, 1);
        tables["head"] = head;
        var maxp = new byte[32]; Put32(maxp, 0, 0x10000); Put16(maxp, 4, 2); Put16(maxp, 6, 3); Put16(maxp, 8, 1);
        tables["maxp"] = maxp;
        var hhea = new byte[36]; Put32(hhea, 0, 0x10000); Put16(hhea, 34, 2); tables["hhea"] = hhea;
        tables["hmtx"] = Words(500, 0, 500, 0);
        tables["glyf"] = Words(1, 0, 0, 100, 100, 2, 0)
            .Concat(new byte[] { 1, 1, 1 }).Concat(Words(0, 50, 50, 0, 100, -100)).ToArray();
        tables["loca"] = Dwords(0, 0, (uint)tables["glyf"].Length);
        tables["fvar"] = Words(1, 0, 16, 2, 1, 20, 0, 8)
            .Concat(System.Text.Encoding.ASCII.GetBytes("wght"))
            .Concat(Dwords(100u << 16, 400u << 16, 900u << 16)).Concat(Words(0, 256)).ToArray();
        tables["avar"] = Words(1, 0, 0, 1, 4, -16384, -16384, 0, 0, 8192, 4096, 16384, 16384);
        // Point numbers 0,2,4; x deltas 20,40,80; zero y deltas.
        byte[] points = { 3, 2, 0, 2, 2 };
        byte[] deltas = { 2, 20, 40, 80, 0x82 };
        var tuple = Words(shared ? 0x8001 : 1, shared ? 8 : 10,
                shared ? deltas.Length : points.Length + deltas.Length, shared ? 0 : 0xa000)
            .Concat(shared ? Array.Empty<byte>() : Words(16384))
            .Concat(points).Concat(deltas).ToArray();
        int dataOffset = shared ? 34 : 32;
        tables["gvar"] = Words(1, 0, 1, shared ? 1 : 0).Concat(Dwords(32))
            .Concat(Words(2, 1)).Concat(Dwords((uint)dataOffset, 0, 0, (uint)tuple.Length))
            .Concat(shared ? Words(16384) : Array.Empty<byte>()).Concat(tuple).ToArray();
        tables["cvt "] = Words(100);
        tables["cvar"] = Words(1, 0, 1, 14, 2, 0x8000, 16384).Concat(new byte[] { 0, 10 }).ToArray();
        if (hvar)
        {
            // One region and two rows; implicit glyph index mapping selects row 1.
            var store = Words(1).Concat(Dwords(12)).Concat(Words(1)).Concat(Dwords(22))
                .Concat(Words(1, 1, 0, 16384, 16384))
                .Concat(Words(2, 1, 1, 0, 0, 120)).ToArray();
            tables["HVAR"] = Words(1, 0).Concat(Dwords(20, explicitMap ? (uint)(20 + store.Length) : 0, 0, 0))
                .Concat(store).Concat(explicitMap ? new byte[] { 0, 0, 0, 2, 0, 1 } : Array.Empty<byte>()).ToArray();
        }
        var writer = new TtfWriter();
        foreach (var (tag, data) in tables) writer.AddTable(tag, data);
        return writer.Build();
    }

    private static byte[] Words(params int[] values) => values.SelectMany(v => new[] { (byte)(v >> 8), (byte)v }).ToArray();
    private static byte[] Dwords(params uint[] values) => values.SelectMany(v => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }).ToArray();
    private static void Put16(byte[] bytes, int offset, int value) => Words(value).CopyTo(bytes, offset);
    private static void Put32(byte[] bytes, int offset, uint value) => Dwords(value).CopyTo(bytes, offset);
}
