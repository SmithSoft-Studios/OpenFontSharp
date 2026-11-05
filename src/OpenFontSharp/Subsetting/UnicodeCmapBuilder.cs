using System.Buffers.Binary;

namespace OpenFontSharp.Subsetting;

internal static class UnicodeCmapBuilder
{
    internal static byte[] Build(byte[] source, IReadOnlyDictionary<ushort, ushort> glyphIdMap)
    {
        var mappings = ReadMappings(source, glyphIdMap);
        var groups = new List<(int Start, int End, ushort Glyph)>();
        foreach (var (codePoint, glyph) in mappings)
        {
            if (groups.Count > 0 && codePoint == groups[^1].End + 1 &&
                glyph == groups[^1].Glyph + codePoint - groups[^1].Start)
            {
                var previous = groups[^1];
                groups[^1] = (previous.Start, codePoint, previous.Glyph);
            }
            else
                groups.Add((codePoint, codePoint, glyph));
        }

        var bmp = BuildBmpSegments(groups);
        int segmentCount = bmp.Count + 1;
        int format4Length = 16 + 8 * segmentCount +
            2 * bmp.Where(g => g.UseArray).Sum(g => g.End - g.Start + 1);
        if (format4Length > ushort.MaxValue)
            throw new NotSupportedException("The retained Unicode mappings exceed the cmap format 4 length limit.");

        var output = new byte[20 + format4Length + 16 + 12 * groups.Count];
        Write16(output, 2, 2);
        Write16(output, 4, 3);
        Write16(output, 6, 1);
        Write32(output, 8, 20);
        Write16(output, 12, 3);
        Write16(output, 14, 10);
        Write32(output, 16, (uint)(20 + format4Length));

        int sub = 20;
        Write16(output, sub, 4);
        Write16(output, sub + 2, (ushort)format4Length);
        Write16(output, sub + 6, (ushort)(2 * segmentCount));
        int power = 1, selector = 0;
        while (power * 2 <= segmentCount)
        {
            power *= 2;
            selector++;
        }
        Write16(output, sub + 8, (ushort)(2 * power));
        Write16(output, sub + 10, (ushort)selector);
        Write16(output, sub + 12, (ushort)(2 * segmentCount - 2 * power));
        int glyphArray = sub + 16 + 8 * segmentCount;
        for (int i = 0; i < segmentCount; i++)
        {
            int start = i < bmp.Count ? bmp[i].Start : 0xFFFF;
            int end = i < bmp.Count ? bmp[i].End : 0xFFFF;
            bool useArray = i < bmp.Count && bmp[i].UseArray;
            int delta = i < bmp.Count ? bmp[i].Glyph - start : 1;
            Write16(output, sub + 14 + 2 * i, (ushort)end);
            Write16(output, sub + 16 + 2 * segmentCount + 2 * i, (ushort)start);
            Write16(output, sub + 16 + 4 * segmentCount + 2 * i, useArray ? (ushort)0 : unchecked((ushort)delta));
            if (useArray)
            {
                int rangeWord = sub + 16 + 6 * segmentCount + 2 * i;
                Write16(output, rangeWord, (ushort)(glyphArray - rangeWord));
                for (int c = start; c <= end; c++)
                {
                    Write16(output, glyphArray, mappings.GetValueOrDefault(c));
                    glyphArray += 2;
                }
            }
        }

        sub += format4Length;
        Write16(output, sub, 12);
        Write32(output, sub + 4, (uint)(16 + 12 * groups.Count));
        Write32(output, sub + 12, (uint)groups.Count);
        for (int i = 0; i < groups.Count; i++)
        {
            Write32(output, sub + 16 + 12 * i, (uint)groups[i].Start);
            Write32(output, sub + 20 + 12 * i, (uint)groups[i].End);
            Write32(output, sub + 24 + 12 * i, groups[i].Glyph);
        }
        return output;
    }

    private static List<(int Start, int End, ushort Glyph, bool UseArray)> BuildBmpSegments(
        List<(int Start, int End, ushort Glyph)> groups)
    {
        var bmp = groups.Where(g => g.Start < 0xFFFF)
            .Select(g => (g.Start, End: Math.Min(g.End, 0xFFFE), g.Glyph)).ToList();
        var costs = new int[bmp.Count + 1];
        var starts = new int[bmp.Count];
        var useArrays = new bool[bmp.Count];
        int bestArrayCost = int.MaxValue, bestArrayStart = 0;
        // A delta segment costs 8 bytes; an array segment costs 8 plus 2 per code point.
        // Track the cheapest array start so choosing a compact partition is linear.
        for (int i = 0; i < bmp.Count; i++)
        {
            int candidate = costs[i] - 2 * bmp[i].Start;
            if (candidate < bestArrayCost)
            {
                bestArrayCost = candidate;
                bestArrayStart = i;
            }
            int arrayCost = bestArrayCost + 8 + 2 * (bmp[i].End + 1);
            useArrays[i] = arrayCost < costs[i] + 8;
            costs[i + 1] = useArrays[i] ? arrayCost : costs[i] + 8;
            starts[i] = useArrays[i] ? bestArrayStart : i;
        }
        var segments = new List<(int Start, int End, ushort Glyph, bool UseArray)>();
        for (int end = bmp.Count - 1; end >= 0;)
        {
            int start = starts[end];
            segments.Add((bmp[start].Start, bmp[end].End, bmp[start].Glyph, useArrays[end]));
            end = start - 1;
        }
        segments.Reverse();
        return segments;
    }

    private static SortedDictionary<int, ushort> ReadMappings(
        byte[] source, IReadOnlyDictionary<ushort, ushort> glyphIdMap)
    {
        var mappings = new SortedDictionary<int, ushort>();
        if (source.Length == 0)
            return mappings;

        Require(source.Length >= 4 && Read16(source, 0) == 0);
        int count = Read16(source, 2);
        Require(count <= (source.Length - 4) / 8);
        int selected = -1, priority = -1;
        for (int i = 0; i < count; i++)
        {
            int record = 4 + 8 * i;
            int platform = Read16(source, record), encoding = Read16(source, record + 2);
            // Choose one Unicode encoding, preferring full repertoire over BMP.
            int candidate = (platform, encoding) switch
            {
                (3, 10) => 6,
                (0, 6) => 5,
                (0, 4) => 4,
                (3, 1) => 3,
                (0, >= 0 and <= 3) => 2,
                _ => -1
            };
            if (candidate <= priority)
                continue;
            uint offset = Read32(source, record + 4);
            Require(offset <= source.Length - 2);
            selected = (int)offset;
            priority = candidate;
        }
        if (selected < 0)
            return mappings;

        ReadOnlySpan<byte> table = source.AsSpan(selected);
        int format = Read16(table, 0);
        bool longLength = format is 10 or 12 or 13;
        Require(table.Length >= (longLength ? 8 : 4));
        uint length = longLength ? Read32(table, 4) : Read16(table, 2);
        Require(length <= table.Length && length >= (longLength ? 8 : 4));
        table = table[..(int)length];

        void Add(int codePoint, uint glyph)
        {
            if (codePoint is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) &&
                glyph is > 0 and <= ushort.MaxValue &&
                glyphIdMap.TryGetValue((ushort)glyph, out ushort mapped) && mapped != 0)
                mappings[codePoint] = mapped;
        }

        switch (format)
        {
            case 0:
                Require(table.Length >= 262);
                for (int c = 0; c < 256; c++)
                    Add(c, table[6 + c]);
                break;
            case 4:
                Require(table.Length >= 16);
                int segments = Read16(table, 6) / 2;
                Require(segments > 0 && Read16(table, 6) % 2 == 0 && segments <= (table.Length - 16) / 8);
                int starts = 16 + 2 * segments, deltas = starts + 2 * segments, ranges = deltas + 2 * segments;
                int previousEnd = -1;
                for (int i = 0; i < segments; i++)
                {
                    int start = Read16(table, starts + 2 * i), end = Read16(table, 14 + 2 * i);
                    Require(start > previousEnd && start <= end);
                    previousEnd = end;
                    int delta = Read16(table, deltas + 2 * i), range = Read16(table, ranges + 2 * i);
                    for (int c = start; c <= end; c++)
                    {
                        int glyph;
                        if (range == 0)
                            glyph = (c + delta) & 0xFFFF;
                        else
                        {
                            int address = ranges + 2 * i + range + 2 * (c - start);
                            Require(range % 2 == 0 && address >= ranges + 2 * segments && address <= table.Length - 2);
                            glyph = Read16(table, address);
                            if (glyph != 0)
                                glyph = (glyph + delta) & 0xFFFF;
                        }
                        Add(c, (uint)glyph);
                    }
                }
                break;
            case 6:
            case 10:
                int header = format == 6 ? 10 : 20;
                Require(table.Length >= header);
                uint first = format == 6 ? Read16(table, 6) : Read32(table, 12);
                uint entries = format == 6 ? Read16(table, 8) : Read32(table, 16);
                Require(first <= 0x10FFFF && entries <= 0x110000 - first && entries <= (table.Length - header) / 2);
                for (int i = 0; i < entries; i++)
                    Add((int)first + i, Read16(table, header + 2 * i));
                break;
            case 12:
            case 13:
                Require(table.Length >= 16);
                uint groups = Read32(table, 12);
                Require(groups <= (table.Length - 16) / 12);
                long previous = -1;
                for (int i = 0; i < groups; i++)
                {
                    int record = 16 + 12 * i;
                    uint start = Read32(table, record), end = Read32(table, record + 4);
                    uint glyph = Read32(table, record + 8);
                    Require(start > previous && start <= end && end <= 0x10FFFF);
                    Require(glyph <= ushort.MaxValue && (format == 13 || end - start <= ushort.MaxValue - glyph));
                    previous = end;
                    for (uint c = start; c <= end; c++)
                        Add((int)c, format == 13 ? glyph : glyph + c - start);
                }
                break;
            default:
                throw new NotSupportedException($"Unicode cmap format {format} cannot be retained in a subset.");
        }
        return mappings;
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidDataException("The source Unicode cmap is malformed.");
    }

    private static ushort Read16(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);

    private static uint Read32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);

    private static void Write16(Span<byte> bytes, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16BigEndian(bytes[offset..], value);

    private static void Write32(Span<byte> bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(bytes[offset..], value);
}
