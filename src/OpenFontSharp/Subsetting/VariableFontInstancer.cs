namespace OpenFontSharp.Subsetting;

// Implements the OpenType fvar/avar, tuple variation and item variation store formats.
// All readers are limited to their enclosing table, glyph or tuple data block.
internal static class VariableFontInstancer
{
    private const int MaximumFontBytes = 128 * 1024 * 1024;
    private const int MaximumExpandedPoints = 4_194_304;
    private static InvalidDataException Bad() => new("Malformed TrueType variation data.");

    private sealed class Budget
    {
        private long remaining = 100_000_000;
        private int remainingPoints = MaximumExpandedPoints;
        internal void Points(int count)
        {
            if (count < 0 || count > remainingPoints) throw Bad();
            remainingPoints -= count;
        }
        internal void Spend(long amount)
        {
            if (amount < 0 || amount > remaining) throw Bad();
            remaining -= amount;
        }
    }

    private sealed class Reader(byte[] data, int start = 0, int? length = null)
    {
        internal readonly byte[] Data = data;
        internal int Position = start;
        internal readonly int End = CheckedEnd(data, start, length ?? data.Length - start);
        private static int CheckedEnd(byte[] data, int start, int length)
        {
            if (start < 0 || length < 0 || start > data.Length - length) throw Bad();
            return start + length;
        }
        internal int Remaining => End - Position;
        internal int U8() { if (Position >= End) throw Bad(); return Data[Position++]; }
        internal int U16() => (U8() << 8) | U8();
        internal short I16() => (short)U16();
        internal uint U32() => ((uint)U16() << 16) | (uint)U16();
        internal int Offset() { uint n = U32(); if (n > int.MaxValue) throw Bad(); return (int)n; }
        internal double F2() => I16() / 16384d;
        internal double Fixed() => unchecked((int)U32()) / 65536d;
        internal void Skip(int n) { if (n < 0 || n > Remaining) throw Bad(); Position += n; }
        internal byte[] Bytes(int n) { if (n < 0 || n > Remaining) throw Bad(); var b = Data.AsSpan(Position, n).ToArray(); Position += n; return b; }
    }

    internal static byte[] Instantiate(byte[] data, IReadOnlyDictionary<string, float> requested)
    {
        if (data.Length > MaximumFontBytes) throw Bad();
        var directory = new Reader(data);
        if (directory.U32() is not (0x10000 or 0x74727565)) throw new NotSupportedException("TrueType outlines are required.");
        int count = directory.U16(); directory.Skip(6);
        var records = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        long totalTableBytes = 0;
        for (int i = 0; i < count; i++)
        {
            string tag = System.Text.Encoding.ASCII.GetString(directory.Bytes(4));
            directory.Skip(4);
            int offset = directory.Offset(), length = directory.Offset();
            _ = new Reader(data, offset, length);
            totalTableBytes += length;
            if (totalTableBytes > MaximumFontBytes || !records.TryAdd(tag, (offset, length))) throw Bad();
        }
        int previousEnd = directory.Position;
        foreach (var record in records.Values.Where(r => r.Length != 0).OrderBy(r => r.Offset))
        {
            if (record.Offset < previousEnd) throw Bad();
            previousEnd = record.Offset + record.Length;
        }
        var tables = records.ToDictionary(r => r.Key, r => data.AsSpan(r.Value.Offset, r.Value.Length).ToArray(), StringComparer.Ordinal);
        byte[] Required(string tag) => tables.TryGetValue(tag, out var value) ? value : throw Bad();
        if (!tables.ContainsKey("fvar"))
        {
            if (requested.Count != 0) throw new ArgumentException("The font has no variation axes.", nameof(requested));
            return data;
        }
        var coordinates = Normalize(Required("fvar"), tables.GetValueOrDefault("avar"), requested);
        var budget = new Budget();
        var head = Required("head"); var maxp = Required("maxp"); var hhea = Required("hhea");
        int glyphCount = U16(maxp, 4), metricCount = U16(hhea, 34);
        if (glyphCount == 0 || metricCount == 0 || metricCount > glyphCount) throw Bad();
        int locaFormat = I16(head, 50);
        if (locaFormat is not (0 or 1)) throw Bad();
        var locaReader = new Reader(Required("loca")); var glyf = Required("glyf");
        var offsets = new int[glyphCount + 1];
        for (int i = 0; i < offsets.Length; i++)
        {
            offsets[i] = locaFormat == 1 ? locaReader.Offset() : locaReader.U16() * 2;
            if (offsets[i] > glyf.Length || (i > 0 && offsets[i] < offsets[i - 1])) throw Bad();
        }
        var glyphs = new Outline[glyphCount];
        var metricReader = new Reader(Required("hmtx"));
        var advances = new double[glyphCount]; var bearings = new double[glyphCount];
        for (int i = 0; i < glyphCount; i++)
        {
            advances[i] = i < metricCount ? metricReader.U16() : advances[i - 1];
            bearings[i] = metricReader.I16();
            glyphs[i] = Outline.Read(new Reader(glyf, offsets[i], offsets[i + 1] - offsets[i]), budget);
            budget.Spend(glyphs[i].X.Length * 16L);
        }
        var gvar = tables.TryGetValue("gvar", out var gv) ? new GlyphVariations(gv, coordinates.Length, glyphCount) : null;
        ItemStore? hvar = null;
        int advanceMap = 0, lsbMap = 0;
        byte[]? hv = tables.GetValueOrDefault("HVAR");
        if (hv is not null)
        {
            var r = new Reader(hv);
            if (r.U16() != 1 || r.U16() != 0) throw Bad();
            int storeOffset = r.Offset();
            if (storeOffset < 20) throw Bad();
            hvar = new ItemStore(hv, storeOffset, coordinates, budget);
            advanceMap = r.Offset(); lsbMap = r.Offset(); r.Offset();
        }
        for (int i = 0; i < glyphCount; i++)
        {
            var glyph = glyphs[i];
            int points = glyph.X.Length;
            double left = glyph.XMin - bearings[i];
            var x = glyph.X.Concat(new[] { left, left + advances[i], 0d, 0d }).ToArray();
            var y = glyph.Y.Concat(new double[4]).ToArray();
            var (dx, dy) = gvar is null ? (new double[x.Length], new double[x.Length])
                : gvar.Apply(i, coordinates, x, y, glyph.EndPoints, !glyph.Composite, budget);
            for (int p = 0; p < points; p++) { glyph.X[p] += dx[p]; glyph.Y[p] += dy[p]; }
            glyph.LeftPhantom = left + dx[points];
            glyph.RightPhantom = left + advances[i] + dx[points + 1];
            advances[i] += hvar is null ? dx[points + 1] - dx[points] : hvar.Delta(MapIndex(hv!, advanceMap, i));
        }
        // Resolve components only after every glyph has received its own tuple deltas.
        for (int i = 0; i < glyphCount; i++) glyphs[i].Resolve(glyphs, 0, budget);
        var hmtx = new byte[glyphCount * 4];
        for (int i = 0; i < glyphCount; i++)
        {
            if (hvar is null) advances[i] = glyphs[i].RightPhantom - glyphs[i].LeftPhantom;
            bearings[i] = hvar is not null && lsbMap != 0
                ? bearings[i] + hvar.Delta(MapIndex(hv!, lsbMap, i))
                : glyphs[i].XMin - glyphs[i].LeftPhantom;
            Put16(hmtx, i * 4, Unsigned(advances[i]));
            Put16(hmtx, i * 4 + 2, Signed(bearings[i]));
        }
        using var outputGlyf = new MemoryStream();
        var outputLoca = new byte[(glyphCount + 1) * 4];
        for (int i = 0; i < glyphCount; i++)
        {
            Put32(outputLoca, i * 4, (uint)outputGlyf.Length);
            byte[] record = glyphs[i].Write();
            if (record.Length > MaximumFontBytes - outputGlyf.Length - 1) throw Bad();
            outputGlyf.Write(record);
            if ((outputGlyf.Length & 1) != 0) outputGlyf.WriteByte(0);
        }
        Put32(outputLoca, glyphCount * 4, (uint)outputGlyf.Length);
        Put16(head, 50, 1);
        int headFlags = U16(head, 16);
        if (Enumerable.Range(0, glyphCount).Any(i => Rounded(bearings[i]) != glyphs[i].XMin)) headFlags &= ~2;
        Put16(head, 16, headFlags);
        var outlined = glyphs.Where(g => g.ResolvedX.Length != 0).ToArray();
        if (outlined.Length > 0)
        {
            Put16(head, 36, Signed(outlined.Min(g => g.XMin)));
            Put16(head, 38, Signed(outlined.Min(g => g.YMin)));
            Put16(head, 40, Signed(outlined.Max(g => g.XMax)));
            Put16(head, 42, Signed(outlined.Max(g => g.YMax)));
        }
        Put16(hhea, 34, glyphCount);
        Put16(hhea, 10, Unsigned(advances.Max()));
        Put16(hhea, 12, Signed(bearings.Min()));
        Put16(hhea, 14, Signed(Enumerable.Range(0, glyphCount).Min(i => advances[i] - bearings[i] - (glyphs[i].XMax - glyphs[i].XMin))));
        Put16(hhea, 16, Signed(Enumerable.Range(0, glyphCount).Max(i => bearings[i] + glyphs[i].XMax - glyphs[i].XMin)));
        tables["glyf"] = outputGlyf.ToArray(); tables["loca"] = outputLoca; tables["hmtx"] = hmtx;
        if (tables.TryGetValue("cvar", out var cv))
        {
            var cvt = Required("cvt ");
            if ((cvt.Length & 1) != 0) throw Bad();
            var cr = new Reader(cv);
            if (cr.U16() != 1 || cr.U16() != 0) throw Bad();
            var values = new double[cvt.Length / 2];
            var delta = TupleData(cr, 0, coordinates, [], values, values, [], false, false, budget).X;
            for (int i = 0; i < values.Length; i++) Put16(cvt, i * 2, Signed(I16(cvt, i * 2) + delta[i]));
        }
        // Layout tables may contain variation indices too. Like the subset path, retain
        // only static outline, naming, character mapping and hinting tables.
        var writer = new TtfWriter();
        long outputSize = 4096;
        foreach (string tag in new[] { "head", "maxp", "hhea", "hmtx", "glyf", "loca", "cmap", "name", "OS/2", "post", "cvt ", "fpgm", "prep", "gasp" })
            if (tables.TryGetValue(tag, out var bytes))
            {
                outputSize += (bytes.Length + 3L) & ~3L;
                if (outputSize > MaximumFontBytes) throw Bad();
                writer.AddTable(tag, bytes);
            }
        return writer.Build();
    }

    private static double[] Normalize(byte[] fvar, byte[]? avar, IReadOnlyDictionary<string, float> requested)
    {
        var r = new Reader(fvar);
        if (r.U16() != 1 || r.U16() != 0) throw Bad();
        int offset = r.U16(); r.U16();
        int count = r.U16(), size = r.U16(); r.U16(); r.U16();
        if (size < 20 || offset < 16 || count == 0 || count > (fvar.Length - offset) / size) throw Bad();
        var tags = new HashSet<string>(StringComparer.Ordinal); var coords = new double[count];
        for (int i = 0; i < count; i++)
        {
            var axis = new Reader(fvar, offset + i * size, size);
            string tag = System.Text.Encoding.ASCII.GetString(axis.Bytes(4));
            double min = axis.Fixed(), def = axis.Fixed(), max = axis.Fixed();
            if (!tags.Add(tag) || min > def || def > max) throw Bad();
            double value = requested.TryGetValue(tag, out float v) ? v : def;
            if (!double.IsFinite(value)) throw new ArgumentException($"Axis '{tag}' requires a finite coordinate.", nameof(requested));
            value = Math.Clamp(value, min, max);
            coords[i] = value == def ? 0 : value < def ? (value - def) / (def - min) : (value - def) / (max - def);
        }
        foreach (string tag in requested.Keys)
            if (!tags.Contains(tag)) throw new ArgumentException($"Unknown variation axis '{tag}'.", nameof(requested));
        if (avar is null) return coords;
        var a = new Reader(avar);
        int major = a.U16(), minor = a.U16();
        if (major != 1 || minor != 0) throw new NotSupportedException("Only avar version 1.0 is supported.");
        a.U16(); if (a.U16() != count) throw Bad();
        for (int i = 0; i < count; i++)
        {
            int n = a.U16(); if (n < 3 || n > a.Remaining / 4) throw Bad();
            double previousFrom = -2, previousTo = -2, mapped = coords[i];
            bool neg = false, zero = false, pos = false;
            for (int j = 0; j < n; j++)
            {
                double from = a.F2(), to = a.F2();
                if (from <= previousFrom || from < -1 || from > 1 || to < -1 || to > 1 || to < previousTo) throw Bad();
                neg |= from == -1 && to == -1; zero |= from == 0 && to == 0; pos |= from == 1 && to == 1;
                if (j > 0 && coords[i] >= previousFrom && coords[i] <= from)
                    mapped = previousTo + (coords[i] - previousFrom) * (to - previousTo) / (from - previousFrom);
                previousFrom = from; previousTo = to;
            }
            if (!neg || !zero || !pos) throw Bad();
            coords[i] = mapped;
        }
        return coords;
    }

    private sealed class GlyphVariations
    {
        private readonly byte[] data;
        private readonly int[] offsets;
        private readonly double[][] shared;
        internal GlyphVariations(byte[] bytes, int axes, int glyphs)
        {
            data = bytes; var r = new Reader(data);
            if (r.U16() != 1 || r.U16() != 0 || r.U16() != axes) throw Bad();
            int sharedCount = r.U16(), sharedOffset = r.Offset();
            if (r.U16() != glyphs) throw Bad();
            int flags = r.U16(), dataOffset = r.Offset();
            if ((flags & ~1) != 0) throw Bad();
            offsets = new int[glyphs + 1];
            for (int i = 0; i <= glyphs; i++)
            {
                int relative = (flags & 1) != 0 ? r.Offset() : r.U16() * 2;
                if (relative > data.Length - dataOffset || dataOffset < 0) throw Bad();
                offsets[i] = dataOffset + relative;
                if (i > 0 && offsets[i] < offsets[i - 1]) throw Bad();
            }
            if (dataOffset < r.Position || sharedOffset < 0 || sharedOffset > data.Length
                || sharedCount > 4096 || sharedCount > (data.Length - sharedOffset) / (axes * 2)) throw Bad();
            var s = new Reader(data, sharedOffset, sharedCount * axes * 2);
            shared = new double[sharedCount][];
            for (int i = 0; i < sharedCount; i++) shared[i] = ReadTuple(s, axes);
        }
        internal (double[] X, double[] Y) Apply(int glyph, double[] coords, double[] x, double[] y, int[] ends, bool iup, Budget budget)
        {
            int length = offsets[glyph + 1] - offsets[glyph];
            if (length == 0) return (new double[x.Length], new double[x.Length]);
            return TupleData(new Reader(data, offsets[glyph], length), offsets[glyph], coords, shared, x, y, ends, iup, true, budget);
        }
    }

    private sealed record TupleHeader(int Size, int Flags, double[] Peak, double[]? Start, double[]? End);

    private static (double[] X, double[] Y) TupleData(Reader r, int origin, double[] coords, double[][] shared,
        double[] x, double[] y, int[] ends, bool iup, bool twoDimensions, Budget budget)
    {
        int countFlags = r.U16(), count = countFlags & 0x0fff, dataOffset = r.U16();
        if ((countFlags & 0x7000) != 0 || dataOffset > r.End - origin) throw Bad();
        budget.Spend((long)count * (x.Length + coords.Length));
        var headers = new TupleHeader[count];
        for (int i = 0; i < count; i++)
        {
            int size = r.U16(), flags = r.U16();
            double[] peak;
            if ((flags & 0x8000) != 0) peak = ReadTuple(r, coords.Length);
            else
            {
                if (!twoDimensions || (flags & 0x0fff) >= shared.Length) throw Bad();
                peak = shared[flags & 0x0fff];
            }
            var start = (flags & 0x4000) != 0 ? ReadTuple(r, coords.Length) : null;
            var end = (flags & 0x4000) != 0 ? ReadTuple(r, coords.Length) : null;
            headers[i] = new(size, flags, peak, start, end);
        }
        if (r.Position > origin + dataOffset) throw Bad();
        r.Position = origin + dataOffset;
        int[]? sharedPoints = (countFlags & 0x8000) != 0 ? ReadPoints(r, x.Length) : null;
        var sumX = new double[x.Length]; var sumY = new double[x.Length];
        foreach (var header in headers)
        {
            var tuple = new Reader(r.Data, r.Position, header.Size);
            if (tuple.End > r.End) throw Bad();
            r.Skip(header.Size);
            int[]? points = (header.Flags & 0x2000) != 0 ? ReadPoints(tuple, x.Length) : sharedPoints;
            int n = points?.Length ?? x.Length;
            var dx = ReadDeltas(tuple, n);
            var dy = twoDimensions ? ReadDeltas(tuple, n) : new double[n];
            double scalar = TupleScalar(coords, header.Peak, header.Start, header.End);
            var expandedX = new double[x.Length]; var expandedY = new double[x.Length];
            var touched = new bool[x.Length];
            for (int p = 0; p < n; p++)
            {
                int index = points is null ? p : points[p];
                expandedX[index] = dx[p]; expandedY[index] = dy[p]; touched[index] = true;
            }
            if (iup && points is not null)
            {
                Interpolate(x, expandedX, touched, ends);
                Interpolate(y, expandedY, touched, ends);
            }
            for (int p = 0; p < x.Length; p++) { sumX[p] += scalar * expandedX[p]; sumY[p] += scalar * expandedY[p]; }
        }
        return (sumX, sumY);
    }

    private static double[] ReadTuple(Reader r, int axes)
    {
        var result = new double[axes];
        for (int i = 0; i < axes; i++) { result[i] = r.F2(); if (result[i] < -1 || result[i] > 1) throw Bad(); }
        return result;
    }

    private static double TupleScalar(double[] coords, double[] peak, double[]? start, double[]? end)
    {
        double scalar = 1;
        for (int i = 0; i < coords.Length; i++)
        {
            double c = coords[i], p = peak[i];
            if (start is not null && end is not null)
            {
                if (start[i] > p || p > end[i] || (start[i] < 0 && end[i] > 0 && p != 0)) continue;
                if (p == 0) continue;
                if (c == p) continue;
                if (c <= start[i] || c >= end[i]) { scalar = 0; continue; }
                scalar *= c < p ? (c - start[i]) / (p - start[i]) : (end[i] - c) / (end[i] - p);
            }
            else
            {
                if (p == 0) continue;
                if (c == 0 || Math.Sign(c) != Math.Sign(p) || Math.Abs(c) > Math.Abs(p)) { scalar = 0; continue; }
                scalar *= c / p;
            }
        }
        return scalar;
    }

    private static int[]? ReadPoints(Reader r, int limit)
    {
        int count = r.U8();
        if ((count & 0x80) != 0) count = ((count & 0x7f) << 8) | r.U8();
        if (count == 0) return null;
        if (count > limit) throw Bad();
        var points = new int[count]; int index = 0, point = 0;
        while (index < count)
        {
            int control = r.U8(), run = (control & 0x7f) + 1;
            if (run > count - index) throw Bad();
            for (int j = 0; j < run; j++)
            {
                int delta = (control & 0x80) != 0 ? r.U16() : r.U8();
                if (index != 0 && delta == 0) throw Bad();
                point += delta;
                if (point >= limit) throw Bad();
                points[index++] = point;
            }
        }
        return points;
    }

    private static double[] ReadDeltas(Reader r, int count)
    {
        var result = new double[count]; int i = 0;
        while (i < count)
        {
            int control = r.U8(), run = (control & 0x3f) + 1;
            if (run > count - i) throw Bad();
            for (int j = 0; j < run; j++)
                result[i++] = (control & 0x80) != 0 ? 0 : (control & 0x40) != 0 ? r.I16() : (sbyte)r.U8();
        }
        return result;
    }

    private static void Interpolate(double[] original, double[] delta, bool[] touched, int[] ends)
    {
        int first = 0;
        foreach (int end in ends)
        {
            var anchors = Enumerable.Range(first, end - first + 1).Where(p => touched[p]).ToArray();
            if (anchors.Length == 1)
            {
                for (int p = first; p <= end; p++) delta[p] = delta[anchors[0]];
            }
            else if (anchors.Length > 1)
            {
                for (int i = 0; i < anchors.Length; i++)
                {
                    int a = anchors[i], b = anchors[(i + 1) % anchors.Length];
                    for (int p = a == end ? first : a + 1; p != b; p = p == end ? first : p + 1)
                    {
                        double lo = original[a], hi = original[b], dlo = delta[a], dhi = delta[b], v = original[p];
                        if (lo > hi) { (lo, hi) = (hi, lo); (dlo, dhi) = (dhi, dlo); }
                        delta[p] = lo == hi ? (dlo == dhi ? dlo : 0) : v <= lo ? dlo : v >= hi ? dhi : dlo + (v - lo) * (dhi - dlo) / (hi - lo);
                    }
                }
            }
            first = end + 1;
        }
    }

    private static (int Outer, int Inner) MapIndex(byte[] data, int offset, int glyph)
    {
        if (offset == 0) return (0, glyph);
        var r = new Reader(data, offset);
        int format = r.U8(), entry = r.U8();
        if (format > 1 || (entry & 0xc0) != 0) throw Bad();
        int count = format == 0 ? r.U16() : r.Offset();
        int size = ((entry >> 4) & 3) + 1, bits = (entry & 15) + 1;
        if (count == 0 || count > r.Remaining / size || bits > size * 8) throw Bad();
        r.Skip(Math.Min(glyph, count - 1) * size);
        uint value = 0;
        for (int i = 0; i < size; i++) value = (value << 8) | (uint)r.U8();
        return ((int)(value >> bits), (int)(value & ((1u << bits) - 1)));
    }

    private sealed class ItemStore
    {
        private readonly byte[] data;
        private readonly int origin;
        private readonly int[] offsets;
        private readonly double[] scalars;
        private readonly Budget budget;
        private readonly Dictionary<(int Outer, int Inner), double> cache = [];
        internal ItemStore(byte[] bytes, int offset, double[] coords, Budget workBudget)
        {
            data = bytes; origin = offset; budget = workBudget;
            var r = new Reader(data, offset);
            if (r.U16() != 1) throw Bad();
            int regionOffset = r.Offset(), count = r.U16();
            if (count > r.Remaining / 4) throw Bad();
            offsets = new int[count];
            for (int i = 0; i < count; i++) offsets[i] = r.Offset();
            var regions = At(regionOffset);
            if (regions.U16() != coords.Length) throw Bad();
            int n = regions.U16();
            if (n >= 32768 || n > regions.Remaining / (coords.Length * 6)) throw Bad();
            scalars = new double[n];
            for (int i = 0; i < n; i++)
            {
                double scalar = 1;
                for (int a = 0; a < coords.Length; a++)
                {
                    double start = regions.F2(), peak = regions.F2(), end = regions.F2(), c = coords[a];
                    // Invalid ordering or a region crossing zero makes this axis non-participating.
                    if (start > peak || peak > end || (start < 0 && end > 0 && peak != 0) || peak == 0) continue;
                    if (start < -1 || end > 1) throw Bad();
                    if (c == peak) continue;
                    if (c <= start || c >= end) { scalar = 0; continue; }
                    scalar *= c < peak ? (c - start) / (peak - start) : (end - c) / (end - peak);
                }
                scalars[i] = scalar;
            }
        }
        private Reader At(int relative)
        {
            if (relative < 0 || relative > data.Length - origin) throw Bad();
            return new Reader(data, origin + relative);
        }
        internal double Delta((int Outer, int Inner) index)
        {
            if (index == (0xffff, 0xffff)) return 0;
            if (cache.TryGetValue(index, out double cached)) return cached;
            if ((uint)index.Outer >= offsets.Length) throw Bad();
            var r = At(offsets[index.Outer]);
            int rows = r.U16(), wordCount = r.U16(), count = r.U16();
            bool longs = (wordCount & 0x8000) != 0; wordCount &= 0x7fff;
            if (index.Inner < 0 || index.Inner >= rows || wordCount > count || count > r.Remaining / 2) throw Bad();
            budget.Spend(count);
            var indices = new int[count];
            for (int i = 0; i < count; i++) { indices[i] = r.U16(); if (indices[i] >= scalars.Length) throw Bad(); }
            int rowSize = wordCount * (longs ? 4 : 2) + (count - wordCount) * (longs ? 2 : 1);
            if (rowSize != 0 && rows > r.Remaining / rowSize) throw Bad();
            r.Skip(index.Inner * rowSize);
            double result = 0;
            for (int i = 0; i < count; i++)
            {
                int delta = i < wordCount ? (longs ? unchecked((int)r.U32()) : r.I16()) : longs ? r.I16() : (sbyte)r.U8();
                result += delta * scalars[indices[i]];
            }
            cache[index] = result;
            return result;
        }
    }

    private sealed class Component
    {
        internal int Flags, Glyph, Arg1, Arg2;
        internal double A = 1, B, C, D = 1, Tx, Ty;
        internal byte[] Transform = [];
    }

    private sealed class Outline
    {
        internal bool Composite;
        internal double[] X = [], Y = [], ResolvedX = [], ResolvedY = [];
        internal int[] EndPoints = [];
        private byte[] flags = [], instructions = [];
        private readonly List<Component> components = [];
        private int state;
        internal double XMin, YMin, XMax, YMax;
        internal double LeftPhantom, RightPhantom;

        internal static Outline Read(Reader r, Budget budget)
        {
            var g = new Outline();
            if (r.Remaining == 0) return g;
            int contours = r.I16();
            g.XMin = r.I16(); g.YMin = r.I16(); g.XMax = r.I16(); g.YMax = r.I16();
            if (contours < -1) throw Bad();
            g.Composite = contours < 0;
            if (g.Composite)
            {
                int f;
                do
                {
                    if (g.components.Count == ushort.MaxValue) throw Bad();
                    budget.Points(1);
                    var c = new Component { Flags = r.U16(), Glyph = r.U16() }; f = c.Flags;
                    if ((f & 1) != 0) { c.Arg1 = (f & 2) != 0 ? r.I16() : r.U16(); c.Arg2 = (f & 2) != 0 ? r.I16() : r.U16(); }
                    else { c.Arg1 = (f & 2) != 0 ? (sbyte)r.U8() : r.U8(); c.Arg2 = (f & 2) != 0 ? (sbyte)r.U8() : r.U8(); }
                    int start = r.Position;
                    if ((f & 8) != 0) c.A = c.D = r.F2();
                    else if ((f & 64) != 0) { c.A = r.F2(); c.D = r.F2(); }
                    else if ((f & 128) != 0) { c.A = r.F2(); c.B = r.F2(); c.C = r.F2(); c.D = r.F2(); }
                    c.Transform = r.Data.AsSpan(start, r.Position - start).ToArray();
                    if ((f & 0x1800) == 0x1800 || new[] { 8, 64, 128 }.Count(bit => (f & bit) != 0) > 1) throw Bad();
                    g.components.Add(c);
                } while ((f & 32) != 0);
                if ((f & 256) != 0) g.instructions = r.Bytes(r.U16());
                g.X = new double[g.components.Count]; g.Y = new double[g.components.Count];
            }
            else
            {
                g.EndPoints = new int[contours];
                for (int i = 0; i < contours; i++)
                {
                    g.EndPoints[i] = r.U16();
                    if (i > 0 && g.EndPoints[i] <= g.EndPoints[i - 1]) throw Bad();
                }
                g.instructions = r.Bytes(r.U16());
                int count = contours == 0 ? 0 : g.EndPoints[^1] + 1;
                budget.Points(count);
                g.flags = new byte[count];
                for (int i = 0; i < count;)
                {
                    byte flag = (byte)r.U8(); int repeat = (flag & 8) != 0 ? r.U8() + 1 : 1;
                    if (repeat > count - i) throw Bad();
                    for (int j = 0; j < repeat; j++) g.flags[i++] = flag;
                }
                g.X = new double[count]; g.Y = new double[count];
                int x = 0, y = 0;
                for (int i = 0; i < count; i++) { int f = g.flags[i]; x += (f & 2) != 0 ? ((f & 16) != 0 ? r.U8() : -r.U8()) : (f & 16) != 0 ? 0 : r.I16(); g.X[i] = x; }
                for (int i = 0; i < count; i++) { int f = g.flags[i]; y += (f & 4) != 0 ? ((f & 32) != 0 ? r.U8() : -r.U8()) : (f & 32) != 0 ? 0 : r.I16(); g.Y[i] = y; }
            }
            return g;
        }

        internal void Resolve(Outline[] glyphs, int depth, Budget budget)
        {
            if (state == 2) return;
            if (state == 1 || depth > 64) throw Bad();
            state = 1;
            if (!Composite)
            {
                budget.Points(X.Length);
                ResolvedX = X.Select(v => (double)Signed(v)).ToArray();
                ResolvedY = Y.Select(v => (double)Signed(v)).ToArray();
            }
            else
            {
                var xx = new List<double>(); var yy = new List<double>();
                for (int i = 0; i < components.Count; i++)
                {
                    var c = components[i];
                    if (c.Glyph >= glyphs.Length) throw Bad();
                    var child = glyphs[c.Glyph]; child.Resolve(glyphs, depth + 1, budget);
                    if ((c.Flags & 0x200) != 0)
                    {
                        LeftPhantom = child.LeftPhantom;
                        RightPhantom = child.RightPhantom;
                    }
                    if (child.ResolvedX.Length > 1_000_000 - xx.Count) throw Bad();
                    budget.Points(child.ResolvedX.Length);
                    budget.Spend(child.ResolvedX.Length * 16L);
                    double tx, ty;
                    if ((c.Flags & 2) != 0)
                    {
                        c.Arg1 = Signed(c.Arg1 + X[i]); c.Arg2 = Signed(c.Arg2 + Y[i]);
                        tx = c.Arg1; ty = c.Arg2;
                        if ((c.Flags & 0x800) != 0) (tx, ty) = (c.A * tx + c.C * ty, c.B * tx + c.D * ty);
                    }
                    else
                    {
                        if (c.Arg1 >= xx.Count || c.Arg2 >= child.ResolvedX.Length) throw Bad();
                        double px = child.ResolvedX[c.Arg2], py = child.ResolvedY[c.Arg2];
                        tx = xx[c.Arg1] - (c.A * px + c.C * py); ty = yy[c.Arg1] - (c.B * px + c.D * py);
                    }
                    c.Tx = tx; c.Ty = ty;
                    for (int p = 0; p < child.ResolvedX.Length; p++)
                    {
                        double px = child.ResolvedX[p], py = child.ResolvedY[p];
                        xx.Add(c.A * px + c.C * py + c.Tx); yy.Add(c.B * px + c.D * py + c.Ty);
                    }
                }
                ResolvedX = xx.ToArray(); ResolvedY = yy.ToArray();
            }
            if (ResolvedX.Length > 0)
            {
                XMin = Math.Floor(ResolvedX.Min()); XMax = Math.Ceiling(ResolvedX.Max());
                YMin = Math.Floor(ResolvedY.Min()); YMax = Math.Ceiling(ResolvedY.Max());
            }
            else XMin = YMin = XMax = YMax = 0;
            state = 2;
        }

        internal byte[] Write()
        {
            if (!Composite && X.Length == 0 && instructions.Length == 0) return [];
            using var stream = new MemoryStream();
            void Word(int v) { stream.WriteByte((byte)(v >> 8)); stream.WriteByte((byte)v); }
            Word(Composite ? -1 : EndPoints.Length);
            Word(Signed(XMin)); Word(Signed(YMin)); Word(Signed(XMax)); Word(Signed(YMax));
            if (Composite)
            {
                for (int i = 0; i < components.Count; i++)
                {
                    var c = components[i];
                    int f = c.Flags | 1;
                    if ((f & 2) != 0 && (f & 0x800) == 0) f |= 0x1000;
                    Word(f); Word(c.Glyph); Word(c.Arg1); Word(c.Arg2); stream.Write(c.Transform);
                }
                if ((components[^1].Flags & 256) != 0) { Word(instructions.Length); stream.Write(instructions); }
            }
            else
            {
                foreach (int end in EndPoints) Word(end);
                Word(instructions.Length); stream.Write(instructions);
                // Emit uncompressed 16-bit deltas; preserve on-curve and overlap flags.
                for (int i = 0; i < X.Length; i++) stream.WriteByte((byte)(flags[i] & 0x41));
                int previous = 0;
                for (int i = 0; i < X.Length; i++) { int current = Signed(X[i]); Word(Signed(current - previous)); previous = current; }
                previous = 0;
                for (int i = 0; i < Y.Length; i++) { int current = Signed(Y[i]); Word(Signed(current - previous)); previous = current; }
            }
            return stream.ToArray();
        }
    }

    private static int U16(byte[] data, int offset) => new Reader(data, offset, 2).U16();
    private static int I16(byte[] data, int offset) => new Reader(data, offset, 2).I16();
    private static int Rounded(double value)
    {
        double n = Math.Floor(value + 0.5);
        if (!double.IsFinite(n) || n < int.MinValue || n > int.MaxValue) throw Bad();
        return (int)n;
    }
    private static int Signed(double value) { int n = Rounded(value); if (n < short.MinValue || n > short.MaxValue) throw Bad(); return n; }
    private static int Unsigned(double value) { int n = Rounded(value); if (n < 0 || n > ushort.MaxValue) throw Bad(); return n; }
    private static void Put16(byte[] data, int offset, int value)
    {
        if (offset < 0 || offset > data.Length - 2) throw Bad();
        data[offset] = (byte)(value >> 8); data[offset + 1] = (byte)value;
    }
    private static void Put32(byte[] data, int offset, uint value)
    {
        if (offset < 0 || offset > data.Length - 4) throw Bad();
        for (int i = 0; i < 4; i++) data[offset + i] = (byte)(value >> (24 - 8 * i));
    }
}
