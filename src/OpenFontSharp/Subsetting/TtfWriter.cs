namespace OpenFontSharp.Subsetting;

/// <summary>
/// Minimal TrueType font binary writer. Serializes a set of named tables
/// into a valid TTF file with correct offset table, table directory,
/// checksums, and 4-byte alignment.
/// </summary>
public class TtfWriter
{
    private readonly List<(string Tag, byte[] Data)> _tables = [];

    /// <summary>
    /// Adds a table to be included in the output font.
    /// Tables are sorted by tag alphabetically in the output directory.
    /// </summary>
    /// <param name="tag">4-character table tag (e.g., "head", "glyf").</param>
    /// <param name="data">Raw table data bytes.</param>
    public void AddTable(string tag, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(data);
        if (tag.Length != 4)
            throw new ArgumentException("Table tag must be exactly 4 characters.", nameof(tag));

        _tables.Add((tag, data));
    }

    /// <summary>
    /// Builds the complete TTF binary from the added tables.
    /// </summary>
    /// <returns>Valid TTF font file bytes.</returns>
    public byte[] Build()
    {
        if (_tables.Count == 0)
            throw new InvalidOperationException("Cannot build a font with no tables.");

        // Sort tables by tag (required by TTF spec for binary search)
        var sorted = _tables.OrderBy(t => t.Tag, StringComparer.Ordinal).ToList();
        int numTables = sorted.Count;

        // Calculate search parameters for binary search
        int searchRange = HighestPowerOf2(numTables) * 16;
        int entrySelector = Log2(HighestPowerOf2(numTables));
        int rangeShift = numTables * 16 - searchRange;

        // Calculate offsets
        int headerSize = 12; // Offset table
        int directorySize = numTables * 16; // Table directory entries
        int dataStart = Align4(headerSize + directorySize);

        // Pre-calculate table offsets and aligned sizes
        var tableOffsets = new int[numTables];
        int currentOffset = dataStart;
        for (int i = 0; i < numTables; i++)
        {
            tableOffsets[i] = currentOffset;
            currentOffset += Align4(sorted[i].Data.Length);
        }

        int totalSize = currentOffset;
        var output = new byte[totalSize];

        using var ms = new MemoryStream(output);
        using var writer = new BinaryWriter(ms);

        // Write offset table (12 bytes)
        writer.Write((byte)0x00); writer.Write((byte)0x01); // sfVersion major
        writer.Write((byte)0x00); writer.Write((byte)0x00); // sfVersion minor
        WriteBE16(writer, (ushort)numTables);
        WriteBE16(writer, (ushort)searchRange);
        WriteBE16(writer, (ushort)entrySelector);
        WriteBE16(writer, (ushort)rangeShift);

        // Write table directory entries (16 bytes each)
        for (int i = 0; i < numTables; i++)
        {
            var (tag, data) = sorted[i];

            // Tag (4 bytes ASCII)
            writer.Write((byte)tag[0]);
            writer.Write((byte)tag[1]);
            writer.Write((byte)tag[2]);
            writer.Write((byte)tag[3]);

            // Checksum (4 bytes)
            WriteBE32(writer, CalculateChecksum(data));

            // Offset (4 bytes)
            WriteBE32(writer, (uint)tableOffsets[i]);

            // Length (4 bytes) — actual length, not padded
            WriteBE32(writer, (uint)data.Length);
        }

        // Pad to data start
        while (ms.Position < dataStart)
            writer.Write((byte)0);

        // Write table data (4-byte aligned)
        for (int i = 0; i < numTables; i++)
        {
            var data = sorted[i].Data;
            writer.Write(data);

            // Pad to 4-byte alignment
            int padding = Align4(data.Length) - data.Length;
            for (int p = 0; p < padding; p++)
                writer.Write((byte)0);
        }

        return output;
    }

    private static uint CalculateChecksum(byte[] data)
    {
        uint sum = 0;
        int length = data.Length;
        int i = 0;
        while (i + 3 < length)
        {
            sum += (uint)((data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]);
            i += 4;
        }
        // Handle remaining bytes (up to 3)
        if (i < length)
        {
            uint last = 0;
            int shift = 24;
            while (i < length)
            {
                last |= (uint)(data[i] << shift);
                shift -= 8;
                i++;
            }
            sum += last;
        }
        return sum;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static int HighestPowerOf2(int n)
    {
        int p = 1;
        while (p * 2 <= n) p *= 2;
        return p;
    }

    private static int Log2(int n)
    {
        int log = 0;
        while (n > 1) { n >>= 1; log++; }
        return log;
    }

    private static void WriteBE16(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteBE32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }
}
