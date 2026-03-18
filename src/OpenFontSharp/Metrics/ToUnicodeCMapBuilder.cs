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
    /// <param name="glyphToUnicode">Dictionary mapping glyph IDs to Unicode codepoints.</param>
    /// <returns>The CMap content as a byte array (ASCII-encoded PostScript).</returns>
    public static byte[] Build(IDictionary<int, int> glyphToUnicode)
    {
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
        var entries = glyphToUnicode.OrderBy(kvp => kvp.Key).ToList();
        var index = 0;

        while (index < entries.Count)
        {
            var batchSize = Math.Min(100, entries.Count - index);
            sb.AppendLine(batchSize.ToString(CultureInfo.InvariantCulture) + " beginbfchar");

            for (int i = 0; i < batchSize; i++)
            {
                var entry = entries[index + i];
                var glyphHex = entry.Key.ToString("X4");
                var unicodeHex = entry.Value.ToString("X4");
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
