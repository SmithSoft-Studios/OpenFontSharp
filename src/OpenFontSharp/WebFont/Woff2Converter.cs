using System.Buffers.Binary;
using System.Text;
using OpenFontSharp.Subsetting;

namespace OpenFontSharp.WebFont;

/// <summary>Reconstructs standalone OpenType fonts from the W3C WOFF2 representation.</summary>
public static class Woff2Converter
{
    // Bounds apply before allocation, including output from an untrusted host decompressor.
    private const int MaxFontSize = 128 * 1024 * 1024;
    private const int MaxTransformedSize = 64 * 1024 * 1024;
    private const int MaxExpandedPoints = 4 * 1024 * 1024;
    private static readonly string[] KnownTags = [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post",
        "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT",
        "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea",
        "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH",
        "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar",
        "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
        "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop",
        "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill"
    ];

    /// <summary>Converts a WOFF2 byte array into a standalone, checksummed sfnt font.</summary>
    /// <param name="woff2">A complete WOFF2 file containing one TrueType or CFF font.</param>
    /// <param name="brotli">
    /// The host Brotli decompressor. When omitted, uses
    /// <see cref="Woff2DefaultBrotliDecompressFunc.DecompressHandler"/>.
    /// </param>
    /// <returns>Standalone sfnt bytes with reconstructed outlines and horizontal metrics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="woff2"/> is null.</exception>
    /// <exception cref="InvalidDataException">The container, transform, or decompressed data is invalid.</exception>
    /// <exception cref="NotSupportedException">The font is a collection, or no Brotli hook is installed.</exception>
    /// <remarks>
    /// Font input, declared original sfnt sizes, and reconstructed output are limited to 128 MiB;
    /// the decompressed transformed data is limited to 64 MiB. Metadata and private
    /// data are bounds-checked but are not copied into the sfnt. Reconstructed glyph
    /// encodings may differ from the original; the declared loca format is retained.
    /// Transformed outlines must have
    /// an acyclic component graph with depth at most 16, at most 65535 expanded points
    /// and contours per glyph, and at most 4194304 expanded points across the font.
    /// The host callback receives a bounded, write-only, non-seekable output stream.
    /// </remarks>
    public static byte[] ToSfnt(byte[] woff2, BrotliDecompressStreamFunc? brotli = null)
    {
        ArgumentNullException.ThrowIfNull(woff2);
        Require(woff2.Length >= 48 && woff2.Length <= MaxFontSize, "WOFF2 must contain a complete header and fit the 128 MiB input limit.");
        var input = new Reader(woff2);
        Require(input.U32() == 0x774F4632, "Invalid WOFF2 signature.");
        uint flavor = input.U32();
        if (flavor == 0x74746366)
            throw new NotSupportedException("WOFF2 font collections (TTC) are not supported.");
        Require(flavor is TtfWriter.TrueTypeVersion or TtfWriter.CffVersion, "Invalid WOFF2 sfnt flavor.");
        Require(input.U32() == woff2.Length, "Invalid WOFF2 file length.");
        int count = input.U16();
        Require(count is > 0 and <= 4095, "Invalid WOFF2 table count.");
        input.Skip(2); // WOFF2 section 3.2 requires decoders to ignore a nonzero reserved field.
        Size(input.U32(), MaxFontSize); // Advisory totalSfntSize: bound it, but do not require equality.
        int compressedSize = Size(input.U32(), MaxFontSize);
        input.Skip(4); // majorVersion and minorVersion
        uint metaOffset = input.U32(), metaLength = input.U32(), metaOriginal = input.U32();
        uint privateOffset = input.U32(), privateLength = input.U32();

        var entries = new List<Entry>(count);
        var tags = new HashSet<string>(StringComparer.Ordinal);
        long originalSize = 12L + count * 16;
        int transformedSize = 0;
        for (int i = 0; i < count; i++)
        {
            int flags = input.U8(), index = flags & 63, version = flags >> 6;
            string tag = index == 63 ? Encoding.Latin1.GetString(input.Bytes(4)) : KnownTags[index];
            Require(tag.All(c => c is >= ' ' and <= '~') && tags.Add(tag), "Invalid or duplicate WOFF2 table tag.");
            int originalLength = Size(input.Base128(), MaxFontSize);
            bool outline = tag is "glyf" or "loca";
            Require(outline ? version is 0 or 3 : tag == "hmtx" ? version is 0 or 1 : version == 0,
                "Invalid WOFF2 table transform version.");
            bool transformed = outline ? version == 0 : version != 0;
            int storedLength = transformed ? Size(input.Base128(), MaxTransformedSize) : originalLength;
            Require(tag != "loca" || !transformed || storedLength == 0, "Transformed loca must have zero length.");
            originalSize += ((long)originalLength + 3) & ~3L;
            Require(originalSize <= MaxFontSize && storedLength <= MaxTransformedSize - transformedSize,
                "WOFF2 table sizes exceed resource limits.");
            transformedSize += storedLength;
            entries.Add(new Entry(tag, originalLength, storedLength, transformed));
        }
        Entry? glyf = entries.Find(e => e.Tag == "glyf");
        Entry? loca = entries.Find(e => e.Tag == "loca");
        Require((glyf == null) == (loca == null), "WOFF2 glyf and loca must occur together.");
        Require(glyf == null || loca!.Transformed == glyf.Transformed, "WOFF2 glyf and loca transforms must match.");
        Require(glyf == null || entries.IndexOf(glyf) < entries.IndexOf(loca!), "WOFF2 loca must follow glyf in the directory.");
        int compressedStart = input.Position;
        Require(compressedSize > 0 && compressedSize <= input.Remaining, "Invalid WOFF2 compressed data length.");
        long end = (long)compressedStart + compressedSize;
        end = CheckExtraBlock(metaOffset, metaLength, end, woff2.Length);
        Require((metaOffset == 0) == (metaOriginal == 0), "Invalid WOFF2 metadata lengths.");
        end = CheckExtraBlock(privateOffset, privateLength, end, woff2.Length);
        // Optional blocks start on four-byte boundaries; only final alignment padding may remain.
        Require(woff2.Length >= end && woff2.Length - end <= 3, "Unexpected WOFF2 trailing data.");

        brotli ??= Woff2DefaultBrotliDecompressFunc.DecompressHandler;
        if (brotli == null)
            throw new NotSupportedException("WOFF2 requires a Brotli decompression hook. Supply brotli or set Woff2DefaultBrotliDecompressFunc.DecompressHandler.");
        using var decompressed = new LimitedStream(transformedSize);
        try
        {
            Require(brotli(input.Bytes(compressedSize), decompressed), "WOFF2 Brotli decompression failed.");
        }
        catch (Exception ex) when (ex is not InvalidDataException && ex is not OutOfMemoryException)
        {
            throw new InvalidDataException("WOFF2 Brotli decompression failed.", ex);
        }
        Require(!decompressed.Overflowed && decompressed.Length == transformedSize,
            "WOFF2 decompressed length does not match the table directory.");
        var payload = new Reader(decompressed.ToArray());
        var tables = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (Entry entry in entries) tables.Add(entry.Tag, payload.Bytes(entry.StoredLength));

        short[]? xMins = null;
        if (glyf is { Transformed: true })
        {
            Require(flavor == TtfWriter.TrueTypeVersion, "CFF fonts cannot contain transformed glyf.");
            xMins = ReconstructGlyf(tables, loca!.OriginalLength);
        }
        Entry? hmtx = entries.Find(e => e.Tag == "hmtx");
        if (hmtx is { Transformed: true })
        {
            xMins ??= ReadXMins(tables);
            tables["hmtx"] = ReconstructHmtx(tables, xMins, hmtx.OriginalLength);
        }
        long outputSize = 12L + count * 16 + tables.Values.Sum(t => ((long)t.Length + 3) & ~3L);
        Require(outputSize <= MaxFontSize, "Reconstructed font exceeds the 128 MiB limit.");
        var writer = new TtfWriter(flavor);
        foreach (var table in tables) writer.AddTable(table.Key, table.Value);
        return writer.Build();
    }

    private static long CheckExtraBlock(uint offset, uint length, long previousEnd, int fileLength)
    {
        Require((offset == 0) == (length == 0), "Invalid WOFF2 optional block.");
        if (offset == 0) return previousEnd;
        Require((offset & 3) == 0 && offset == ((previousEnd + 3) & ~3L) &&
            (long)offset + length <= fileLength, "Invalid WOFF2 optional block bounds.");
        return (long)offset + length;
    }

    private static short[] ReconstructGlyf(Dictionary<string, byte[]> tables, int originalLocaLength, bool optimizeFlags = false)
    {
        var source = new Reader(tables["glyf"]);
        Require(source.U16() == 0, "Invalid transformed glyf version.");
        int options = source.U16(), count = source.U16(), indexFormat = source.U16();
        Require((options & ~1) == 0 && indexFormat <= 1, "Invalid transformed glyf flags.");
        byte[] head = Table(tables, "head", 54), maxp = Table(tables, "maxp", 6);
        Require(U16(maxp, 4) == count && U16(head, 50) == indexFormat,
            "Inconsistent transformed glyf glyph count or loca format.");
        Require(originalLocaLength == (count + 1) * (indexFormat == 0 ? 2 : 4), "Invalid loca original length.");
        var lengths = new int[7];
        long total = 36L + ((options & 1) != 0 ? (count + 7) / 8 : 0);
        for (int i = 0; i < 7; i++) { lengths[i] = Size(source.U32(), MaxTransformedSize); total += lengths[i]; }
        Require(total == tables["glyf"].Length && lengths[0] == count * 2, "Invalid transformed glyf substream sizes.");
        Reader contours = source.Slice(lengths[0]), points = source.Slice(lengths[1]),
            flags = source.Slice(lengths[2]), glyphs = source.Slice(lengths[3]),
            composites = source.Slice(lengths[4]), boxes = source.Slice(lengths[5]),
            instructions = source.Slice(lengths[6]);
        byte[] bboxBitmap = boxes.Bytes(((count + 31) / 32) * 4);
        byte[] overlapBitmap = (options & 1) != 0 ? source.Bytes((count + 7) / 8) : [];
        using var output = new LimitedStream(MaxFontSize);
        var locations = new uint[count + 1];
        var xMins = new short[count];
        var children = new List<int>?[count];
        var pointCounts = new int[count];
        var contourCounts = new int[count];
        for (int g = 0; g < count; g++)
        {
            locations[g] = (uint)output.Length;
            int contourCount = contours.I16();
            bool explicitBox = Bit(bboxBitmap, g);
            Require(contourCount >= -1, "Invalid transformed glyph contour count.");
            if (contourCount == 0)
            {
                Require(!explicitBox, "An empty glyph cannot have a bounding box.");
                continue;
            }
            int xMin, yMin, xMax, yMax;
            if (contourCount == -1)
            {
                Require(explicitBox, "A composite glyph must have an explicit bounding box.");
                xMin = boxes.I16(); yMin = boxes.I16(); xMax = boxes.I16(); yMax = boxes.I16();
                WriteWords(output, -1, xMin, yMin, xMax, yMax);
                bool hasInstructions = false;
                int componentFlags;
                var components = children[g] = [];
                do
                {
                    int start = composites.Position;
                    componentFlags = composites.U16();
                    int component = composites.U16();
                    Require(component < count, "Invalid composite glyph index.");
                    components.Add(component);
                    int scaleFlags = componentFlags & (8 | 64 | 128);
                    Require(scaleFlags is 0 or 8 or 64 or 128, "Invalid composite glyph scale flags.");
                    composites.Skip((componentFlags & 1) != 0 ? 4 : 2);
                    composites.Skip(scaleFlags == 8 ? 2 : scaleFlags == 64 ? 4 : scaleFlags == 128 ? 8 : 0);
                    output.Write(composites.Range(start, composites.Position - start));
                    hasInstructions |= (componentFlags & 256) != 0;
                } while ((componentFlags & 32) != 0);
                if (hasInstructions)
                {
                    int length = glyphs.UInt255();
                    WriteWords(output, length);
                    output.Write(instructions.Bytes(length));
                }
            }
            else
            {
                var ends = new int[contourCount];
                int pointCount = 0;
                for (int i = 0; i < contourCount; i++)
                {
                    int n = points.UInt255();
                    Require(n > 0 && n <= ushort.MaxValue - pointCount, "Invalid contour point count.");
                    pointCount += n;
                    ends[i] = pointCount - 1;
                }
                pointCounts[g] = pointCount;
                contourCounts[g] = contourCount;
                var xs = new int[pointCount];
                var ys = new int[pointCount];
                var pointFlags = new byte[pointCount];
                int x = 0, y = 0;
                xMin = yMin = short.MaxValue; xMax = yMax = short.MinValue;
                for (int p = 0; p < pointCount; p++)
                {
                    int flag = flags.U8();
                    (int dx, int dy) = Triplet(flag & 127, glyphs);
                    x += dx; y += dy;
                    Require(x is >= short.MinValue and <= short.MaxValue && y is >= short.MinValue and <= short.MaxValue,
                        "Glyph coordinates exceed signed 16-bit range.");
                    xs[p] = x; ys[p] = y;
                    pointFlags[p] = (byte)((flag & 128) == 0 ? 1 : 0);
                    xMin = Math.Min(xMin, x); yMin = Math.Min(yMin, y);
                    xMax = Math.Max(xMax, x); yMax = Math.Max(yMax, y);
                }
                if (explicitBox)
                {
                    xMin = boxes.I16(); yMin = boxes.I16(); xMax = boxes.I16(); yMax = boxes.I16();
                }
                if (overlapBitmap.Length != 0 && Bit(overlapBitmap, g)) pointFlags[0] |= 0x40;
                int instructionLength = glyphs.UInt255();
                WriteWords(output, contourCount, xMin, yMin, xMax, yMax);
                foreach (int endpoint in ends) WriteWords(output, endpoint);
                WriteWords(output, instructionLength);
                output.Write(instructions.Bytes(instructionLength));
                x = y = 0;
                for (int p = 0; p < pointCount; p++)
                {
                    // Normalize before selecting byte/word encodings: sfnt coordinates wrap to int16.
                    int dx = unchecked((short)(xs[p] - x)), dy = unchecked((short)(ys[p] - y));
                    x = xs[p]; y = ys[p];
                    xs[p] = dx; ys[p] = dy;
                    pointFlags[p] |= (byte)(CoordinateFlag(dx, 2, 16) | CoordinateFlag(dy, 4, 32));
                }
                if (optimizeFlags) OptimizePointFlags(pointFlags, xs, ys);
                for (int p = 0; p < pointCount;)
                {
                    byte flag = pointFlags[p];
                    int run = 1;
                    while (run < 256 && p + run < pointCount && pointFlags[p + run] == flag) run++;
                    output.WriteByte((byte)(flag | (run > 1 ? 8 : 0)));
                    if (run > 1) output.WriteByte((byte)(run - 1));
                    p += run;
                }
                for (int p = 0; p < pointCount; p++) WriteCoordinate(output, xs[p], pointFlags[p], 2, 16);
                for (int p = 0; p < pointCount; p++) WriteCoordinate(output, ys[p], pointFlags[p], 4, 32);
            }
            Require(xMin <= xMax && yMin <= yMax, "Invalid glyph bounding box.");
            xMins[g] = (short)xMin;
            if ((output.Length & 1) != 0) output.WriteByte(0);
        }
        locations[count] = (uint)output.Length;
        foreach (Reader stream in new[] { source, contours, points, flags, glyphs, composites, boxes, instructions })
            Require(stream.Remaining == 0, "Unconsumed transformed glyf substream data.");
        ValidateComponents(children, pointCounts, contourCounts);
        if (indexFormat == 0 && output.Length > 131070)
        {
            // Locally minimal coordinates need not minimize complete flag runs.
            // Retry with globally minimal point encoding rather than change indexFormat.
            Require(!optimizeFlags, "Reconstructed glyph offsets cannot fit the declared short loca format.");
            return ReconstructGlyf(tables, originalLocaLength, optimizeFlags: true);
        }
        var loca = new byte[(count + 1) * (indexFormat == 0 ? 2 : 4)];
        for (int i = 0; i <= count; i++)
        {
            if (indexFormat == 0) BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(i * 2), (ushort)(locations[i] / 2));
            else BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), locations[i]);
        }
        tables["glyf"] = output.ToArray();
        tables["loca"] = loca;
        return xMins;
    }

    private static void ValidateComponents(List<int>?[] children, int[] points, int[] contours)
    {
        var states = new byte[children.Length];
        var depths = new int[children.Length];
        long expandedPoints = 0;
        void Visit(int glyph, int stackDepth)
        {
            Require(stackDepth <= 16, "Composite glyph nesting exceeds the OpenType limit of 16.");
            Require(states[glyph] != 1, "Cyclic composite glyph reference.");
            if (states[glyph] == 2) return;
            states[glyph] = 1;
            if (children[glyph] is { } components)
            {
                foreach (int child in components)
                {
                    Visit(child, stackDepth + 1);
                    Require(points[child] <= ushort.MaxValue - points[glyph] &&
                        contours[child] <= ushort.MaxValue - contours[glyph],
                        "Composite glyph expansion exceeds the UInt16 point or contour limit.");
                    points[glyph] += points[child];
                    contours[glyph] += contours[child];
                    depths[glyph] = Math.Max(depths[glyph], depths[child] + 1);
                }
                Require(depths[glyph] <= 16, "Composite glyph nesting exceeds the OpenType limit of 16.");
            }
            states[glyph] = 2;
            expandedPoints += points[glyph];
            Require(expandedPoints <= MaxExpandedPoints, "Expanded glyph points exceed the font resource limit.");
        }
        for (int glyph = 0; glyph < children.Length; glyph++) Visit(glyph, 0);
    }

    private static int CoordinateFlag(int delta, int shortFlag, int sameFlag)
        => delta == 0 ? sameFlag : delta is >= -255 and <= 255 ? shortFlag | (delta > 0 ? sameFlag : 0) : 0;

    private static void WriteCoordinate(Stream output, int delta, byte flag, int shortBit, int sameBit)
    {
        if ((flag & shortBit) != 0) output.WriteByte((byte)Math.Abs(delta));
        else if ((flag & sameBit) == 0) WriteWords(output, delta);
    }

    private static void OptimizePointFlags(byte[] flags, int[] xs, int[] ys)
    {
        // A zero delta can cost zero, one, or two bytes. Spending a byte on it
        // can merge flag runs and reduce the complete record. Find the minimum
        // over all legal flags and run lengths (1..256) using sliding minima.
        const int window = 257;
        var starts = new int[128 * window];
        var costs = new int[128 * window];
        var heads = new int[128];
        var tails = new int[128];
        var prefixes = new int[128];
        var previousCosts = new int[128];
        var previousValid = new bool[128];
        var best = new int[flags.Length + 1];
        var choices = new byte[flags.Length + 1];
        var lengths = new ushort[flags.Length + 1];
        // Only the first flag determines OVERLAP_SIMPLE; later flags can share it in a run.
        bool overlap = (flags[0] & 64) != 0;
        for (int i = 1; i <= flags.Length; i++)
        {
            best[i] = int.MaxValue;
            for (int flag = 0; flag < 128; flag++)
            {
                if ((flag & 8) != 0) continue;
                int xCost = CoordinateCost(xs[i - 1], flag, 2, 16);
                int yCost = CoordinateCost(ys[i - 1], flag, 4, 32);
                bool valid = (flag & 1) == (flags[i - 1] & 1) &&
                    (i == 1 ? (flag & 64) == (flags[0] & 64) : overlap || (flag & 64) == 0) &&
                    xCost >= 0 && yCost >= 0;
                if (!valid)
                {
                    heads[flag] = tails[flag] = prefixes[flag] = 0;
                    previousValid[flag] = false;
                    continue;
                }
                int cost = xCost + yCost, offset = flag * window;
                if (previousValid[flag])
                {
                    int start = i - 2;
                    int key = best[start] - (prefixes[flag] - previousCosts[flag]);
                    while (heads[flag] != tails[flag])
                    {
                        int back = (tails[flag] + window - 1) % window;
                        if (costs[offset + back] < key) break;
                        tails[flag] = back;
                    }
                    starts[offset + tails[flag]] = start;
                    costs[offset + tails[flag]] = key;
                    tails[flag] = (tails[flag] + 1) % window;
                }
                prefixes[flag] += cost;
                while (heads[flag] != tails[flag] && starts[offset + heads[flag]] < i - 256)
                    heads[flag] = (heads[flag] + 1) % window;
                int candidate = best[i - 1] + 1 + cost, run = 1;
                if (heads[flag] != tails[flag])
                {
                    int repeated = costs[offset + heads[flag]] + prefixes[flag] + 2;
                    if (repeated < candidate)
                    {
                        candidate = repeated;
                        run = i - starts[offset + heads[flag]];
                    }
                }
                if (candidate < best[i])
                {
                    best[i] = candidate;
                    choices[i] = (byte)flag;
                    lengths[i] = (ushort)run;
                }
                previousValid[flag] = true;
                previousCosts[flag] = cost;
            }
        }
        for (int end = flags.Length; end > 0; end -= lengths[end])
            flags.AsSpan(end - lengths[end], lengths[end]).Fill(choices[end]);
    }

    private static int CoordinateCost(int delta, int flag, int shortBit, int sameBit)
    {
        if ((flag & shortBit) == 0) return (flag & sameBit) == 0 ? 2 : delta == 0 ? 0 : -1;
        return (flag & sameBit) != 0 ? delta is >= 0 and <= 255 ? 1 : -1 : delta is >= -255 and <= 0 ? 1 : -1;
    }

    private static (int X, int Y) Triplet(int flag, Reader stream)
    {
        static int Signed(int value, int bits) => (bits & 1) != 0 ? value : -value;
        if (flag < 10) return (0, Signed(((flag & 14) << 7) + stream.U8(), flag));
        if (flag < 20) return (Signed((((flag - 10) & 14) << 7) + stream.U8(), flag), 0);
        int x, y;
        if (flag < 84)
        {
            int b = stream.U8(), n = flag - 20;
            x = 1 + (n & 0x30) + (b >> 4);
            y = 1 + ((n & 0x0C) << 2) + (b & 15);
        }
        else if (flag < 120)
        {
            int n = flag - 84;
            x = 1 + ((n / 12) << 8) + stream.U8();
            y = 1 + (((n % 12) >> 2) << 8) + stream.U8();
        }
        else if (flag < 124)
        {
            int b = stream.U8(), middle = stream.U8();
            x = (b << 4) + (middle >> 4);
            y = ((middle & 15) << 8) + stream.U8();
        }
        else { x = stream.U16(); y = stream.U16(); }
        return (Signed(x, flag), Signed(y, flag >> 1));
    }

    private static short[] ReadXMins(Dictionary<string, byte[]> tables)
    {
        byte[] head = Table(tables, "head", 54), maxp = Table(tables, "maxp", 6),
            glyf = Table(tables, "glyf", 0), loca = Table(tables, "loca", 0);
        int count = U16(maxp, 4), format = U16(head, 50);
        Require(format <= 1 && loca.Length == (count + 1) * (format == 0 ? 2 : 4), "Invalid null-transform loca.");
        var reader = new Reader(loca);
        uint Offset() => format == 0 ? (uint)(reader.U16() * 2) : reader.U32();
        uint start = Offset();
        Require(start == 0, "Invalid first loca offset.");
        var mins = new short[count];
        for (int i = 0; i < count; i++)
        {
            uint end = Offset();
            Require(end >= start && end <= glyf.Length && (end == start || end - start >= 10), "Invalid loca glyph bounds.");
            if (end != start) mins[i] = BinaryPrimitives.ReadInt16BigEndian(glyf.AsSpan((int)start + 2));
            start = end;
        }
        return mins;
    }

    private static byte[] ReconstructHmtx(Dictionary<string, byte[]> tables, short[] xMins, int originalLength)
    {
        byte[] hhea = Table(tables, "hhea", 36), maxp = Table(tables, "maxp", 6);
        int count = U16(maxp, 4), metrics = U16(hhea, 34);
        Require(count == xMins.Length && metrics > 0 && metrics <= count &&
            originalLength == metrics * 4 + (count - metrics) * 2, "Invalid transformed hmtx metric counts.");
        var source = new Reader(tables["hmtx"]);
        int flags = source.U8();
        Require((flags & ~3) == 0 && flags != 0, "Invalid transformed hmtx flags.");
        var advances = new ushort[metrics];
        for (int i = 0; i < metrics; i++) advances[i] = source.U16();
        var bearings = new short[count];
        for (int i = 0; i < count; i++)
            bearings[i] = (flags & (i < metrics ? 1 : 2)) == 0 ? source.I16() : xMins[i];
        Require(source.Remaining == 0, "Invalid transformed hmtx stream length.");
        using var output = new MemoryStream(originalLength);
        for (int i = 0; i < count; i++)
        {
            if (i < metrics) WriteWords(output, advances[i]);
            WriteWords(output, bearings[i]);
        }
        return output.ToArray();
    }

    private static byte[] Table(Dictionary<string, byte[]> tables, string tag, int minLength)
    {
        Require(tables.TryGetValue(tag, out byte[]? data) && data.Length >= minLength, $"Missing or truncated {tag} table.");
        return data!;
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));
    private static bool Bit(byte[] bytes, int index) => (bytes[index >> 3] & (0x80 >> (index & 7))) != 0;
    private static int Size(uint value, int maximum)
    {
        Require(value <= maximum, "WOFF2 size exceeds resource limits.");
        return (int)value;
    }

    private static void WriteWords(Stream output, params int[] values)
    {
        foreach (int value in values)
        {
            output.WriteByte((byte)(value >> 8));
            output.WriteByte((byte)value);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed record Entry(string Tag, int OriginalLength, int StoredLength, bool Transformed);

    private sealed class Reader
    {
        private readonly byte[] _bytes;
        private readonly int _end;
        public int Position { get; private set; }
        public int Remaining => _end - Position;
        public Reader(byte[] bytes) : this(bytes, 0, bytes.Length) { }
        private Reader(byte[] bytes, int start, int length) { _bytes = bytes; Position = start; _end = start + length; }
        public void Skip(int length) { Require(length >= 0 && length <= Remaining, "Truncated WOFF2 stream."); Position += length; }
        public byte U8() { Require(Remaining > 0, "Truncated WOFF2 stream."); return _bytes[Position++]; }
        public ushort U16() { Require(Remaining >= 2, "Truncated WOFF2 stream."); ushort n = BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(Position)); Position += 2; return n; }
        public short I16() => unchecked((short)U16());
        public uint U32() { Require(Remaining >= 4, "Truncated WOFF2 stream."); uint n = BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(Position)); Position += 4; return n; }
        public byte[] Bytes(int length) { int start = Position; Skip(length); return _bytes.AsSpan(start, length).ToArray(); }
        public ReadOnlySpan<byte> Range(int start, int length) => _bytes.AsSpan(start, length);
        public Reader Slice(int length) { int start = Position; Skip(length); return new Reader(_bytes, start, length); }
        public int UInt255()
        {
            int code = U8();
            return code == 253 ? U16() : code == 254 ? U8() + 506 : code == 255 ? U8() + 253 : code;
        }
        public uint Base128()
        {
            uint value = 0;
            for (int i = 0; i < 5; i++)
            {
                byte b = U8();
                Require(!(i == 0 && b == 128) && (value & 0xFE000000) == 0, "Invalid UIntBase128 encoding.");
                value = (value << 7) | (uint)(b & 127);
                if ((b & 128) == 0) return value;
            }
            throw new InvalidDataException("UIntBase128 encoding is too long.");
        }
    }

    // A wrapper, not a MemoryStream subclass: SetLength, seeking, or exposing the backing
    // buffer must not allow a host callback to circumvent the decompression limit.
    private sealed class LimitedStream(int limit) : Stream
    {
        private readonly MemoryStream _stream = new();
        public bool Overflowed { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _stream.Length;
        public override long Position { get => _stream.Position; set => throw new NotSupportedException(); }
        private void Check(int count)
        {
            if (count < 0 || count > limit - _stream.Length)
            {
                Overflowed = true;
                throw new InvalidDataException("WOFF2 decompressed or reconstructed output exceeds its size limit.");
            }
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); _stream.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); _stream.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); _stream.WriteByte(value); }
        public byte[] ToArray() => _stream.ToArray();
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _stream.Dispose(); base.Dispose(disposing); }
    }
}
