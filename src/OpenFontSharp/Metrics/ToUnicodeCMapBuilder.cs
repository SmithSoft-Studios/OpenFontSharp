using System.Globalization;
using System.Text;

namespace OpenFontSharp.Metrics;

/// <summary>
/// Generates a /ToUnicode CMap stream for PDF font objects.
/// Maps CID values (glyph indices) to Unicode codepoints,
/// enabling text extraction and copy-paste from PDF viewers.
/// </summary>
public static class ToUnicodeCMapBuilder
{
    private const int MaxBlockSize = 100;

    /// <summary>
    /// Builds a ToUnicode CMap stream for the given glyph-to-Unicode scalar mappings.
    /// </summary>
    /// <param name="glyphToUnicode">Dictionary mapping two-byte source character IDs to Unicode scalar values.</param>
    /// <returns>The CMap content as a byte array (ASCII-encoded PostScript).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="glyphToUnicode"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A source ID is outside 0 through 65535, or a destination is not a Unicode scalar value.</exception>
    public static byte[] Build(IDictionary<int, int> glyphToUnicode)
    {
        ArgumentNullException.ThrowIfNull(glyphToUnicode);
        return BuildCore(glyphToUnicode.Select(entry =>
            new KeyValuePair<int, IEnumerable<int>>(entry.Key, new[] { entry.Value })));
    }

    /// <summary>
    /// Builds a ToUnicode CMap stream for glyph-to-Unicode scalar sequence mappings.
    /// </summary>
    /// <typeparam name="TSequence">A sequence type containing Unicode scalar values.</typeparam>
    /// <param name="glyphToUnicode">Dictionary mapping two-byte source character IDs to Unicode scalar sequences.</param>
    /// <returns>The CMap content as a byte array (ASCII-encoded PostScript).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="glyphToUnicode"/> is null.</exception>
    /// <exception cref="ArgumentException">A destination sequence is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A source ID is outside 0 through 65535, or a destination is not a Unicode scalar value.</exception>
    public static byte[] Build<TSequence>(IDictionary<int, TSequence> glyphToUnicode)
        where TSequence : IEnumerable<int>
    {
        ArgumentNullException.ThrowIfNull(glyphToUnicode);
        return BuildCore(glyphToUnicode.Select(entry =>
            new KeyValuePair<int, IEnumerable<int>>(entry.Key, entry.Value)));
    }

    private static byte[] BuildCore(IEnumerable<KeyValuePair<int, IEnumerable<int>>> glyphToUnicode)
    {
        var entries = new List<Mapping>();
        foreach (var entry in glyphToUnicode)
        {
            if (entry.Key is < 0 or > ushort.MaxValue)
                throw new ArgumentOutOfRangeException("glyphToUnicode", entry.Key,
                    "Source character IDs must fit the declared two-byte codespace.");
            if (entry.Value is null)
                throw new ArgumentException("Destination sequences must not be null or empty.", "glyphToUnicode");

            var destination = new List<byte>();
            var scalarCount = 0;
            foreach (var scalar in entry.Value)
            {
                if (!Rune.IsValid(scalar))
                    throw new ArgumentOutOfRangeException("glyphToUnicode", scalar,
                        "Destinations must be Unicode scalar values, not surrogate code points.");

                destination.AddRange(Encoding.BigEndianUnicode.GetBytes(char.ConvertFromUtf32(scalar)));
                scalarCount++;
            }

            if (scalarCount == 0)
                throw new ArgumentException("Destination sequences must not be null or empty.", "glyphToUnicode");

            entries.Add(new Mapping(entry.Key, destination.ToArray(), scalarCount == 1));
        }

        entries.Sort((left, right) => left.SourceId.CompareTo(right.SourceId));
        var sb = new StringBuilder();

        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine("/CIDSystemInfo");
        sb.AppendLine("<< /Registry (Adobe)");
        sb.AppendLine("/Ordering (UCS)");
        sb.AppendLine("/Supplement 0");
        sb.AppendLine(">> def");
        sb.AppendLine("/CMapName /Adobe-Identity-UCS def");
        sb.AppendLine("/CMapType 2 def");
        sb.AppendLine("1 begincodespacerange");
        sb.AppendLine("<0000> <FFFF>");
        sb.AppendLine("endcodespacerange");

        var index = 0;
        while (index < entries.Count)
        {
            var rangeLength = GetRangeLength(entries, index);
            if (rangeLength > 1)
            {
                sb.AppendLine("1 beginbfrange");
                var first = entries[index];
                var last = entries[index + rangeLength - 1];
                sb.AppendLine($"<{first.SourceId.ToString("X4", CultureInfo.InvariantCulture)}> <{last.SourceId.ToString("X4", CultureInfo.InvariantCulture)}> <{Convert.ToHexString(first.Destination)}>");
                sb.AppendLine("endbfrange");
                index += rangeLength;
                continue;
            }

            var blockStart = index++;
            while (index < entries.Count
                && index - blockStart < MaxBlockSize
                && GetRangeLength(entries, index) < 2)
            {
                index++;
            }

            var blockSize = index - blockStart;
            sb.AppendLine($"{blockSize.ToString(CultureInfo.InvariantCulture)} beginbfchar");
            for (var entryIndex = blockStart; entryIndex < index; entryIndex++)
            {
                var entry = entries[entryIndex];
                sb.AppendLine($"<{entry.SourceId.ToString("X4", CultureInfo.InvariantCulture)}> <{Convert.ToHexString(entry.Destination)}>");
            }
            sb.AppendLine("endbfchar");
        }

        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static int GetRangeLength(IReadOnlyList<Mapping> entries, int startIndex)
    {
        var length = 1;
        while (length < MaxBlockSize && startIndex + length < entries.Count)
        {
            var previous = entries[startIndex + length - 1];
            var current = entries[startIndex + length];
            if (previous.SourceId == ushort.MaxValue
                || current.SourceId != previous.SourceId + 1
                || !CanIncrement(previous, current))
            {
                break;
            }

            length++;
        }
        return length;
    }

    private static bool CanIncrement(Mapping previous, Mapping current)
    {
        if (!previous.IsSingleScalar
            || !current.IsSingleScalar
            || previous.Destination.Length != current.Destination.Length
            || previous.Destination[^1] == byte.MaxValue)
        {
            return false;
        }

        for (var i = 0; i < previous.Destination.Length - 1; i++)
        {
            if (previous.Destination[i] != current.Destination[i])
                return false;
        }

        return current.Destination[^1] == previous.Destination[^1] + 1;
    }

    private sealed record Mapping(int SourceId, byte[] Destination, bool IsSingleScalar);
}
