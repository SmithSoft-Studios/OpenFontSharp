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
    /// <summary>
    /// Builds a ToUnicode CMap stream for the given glyph-to-unicode mappings.
    /// </summary>
    /// <param name="glyphToUnicode">Dictionary mapping two-byte source character IDs to Unicode scalar values.</param>
    /// <returns>The CMap content as a byte array (ASCII-encoded PostScript).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="glyphToUnicode"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A source ID is outside 0 through 65535, or a destination is not a Unicode scalar value.</exception>
    public static byte[] Build(IDictionary<int, int> glyphToUnicode)
    {
        ArgumentNullException.ThrowIfNull(glyphToUnicode);
        var entries = glyphToUnicode.OrderBy(kvp => kvp.Key).ToList();
        foreach (var entry in entries)
        {
            if (entry.Key is < 0 or > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(glyphToUnicode), entry.Key,
                    "Source character IDs must fit the declared two-byte codespace.");
            if (!Rune.IsValid(entry.Value))
                throw new ArgumentOutOfRangeException(nameof(glyphToUnicode), entry.Value,
                    "Destinations must be Unicode scalar values, not surrogate code points.");
        }
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

        // Write mappings in groups of up to 100 (PDF spec limit per beginbfchar)
        var index = 0;

        while (index < entries.Count)
        {
            var batchSize = Math.Min(100, entries.Count - index);
            sb.AppendLine(batchSize.ToString(CultureInfo.InvariantCulture) + " beginbfchar");

            for (int i = 0; i < batchSize; i++)
            {
                var entry = entries[index + i];
                var glyphHex = entry.Key.ToString("X4", CultureInfo.InvariantCulture);
                var unicodeHex = Convert.ToHexString(
                    Encoding.BigEndianUnicode.GetBytes(char.ConvertFromUtf32(entry.Value)));
                sb.AppendLine($"<{glyphHex}> <{unicodeHex}>");
            }

            sb.AppendLine("endbfchar");
            index += batchSize;
        }

        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
