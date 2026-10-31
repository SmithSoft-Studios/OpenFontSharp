using System.IO.Compression;

namespace OpenFontSharp.Subsetting;

/// <summary>The container format of a font file, identified by its first four bytes.</summary>
public enum FontContainerFormat
{
    /// <summary>Not a font this library recognises.</summary>
    Unknown,

    /// <summary>A single sfnt font with TrueType outlines (0x00010000 or 'true').</summary>
    TrueType,

    /// <summary>A single sfnt font with CFF or CFF2 outlines ('OTTO').</summary>
    OpenTypeCff,

    /// <summary>A TrueType/OpenType collection ('ttcf').</summary>
    Collection,

    /// <summary>WOFF 1.0 ('wOFF').</summary>
    Woff,

    /// <summary>WOFF 2.0 ('wOF2').</summary>
    Woff2,
}

/// <summary>
/// Turns the container formats a font can arrive in (collections, WOFF) into a plain
/// standalone sfnt font that the subsetter can work on.
/// </summary>
public static class SfntContainers
{
    /// <summary>Identifies the container format from the file signature.</summary>
    public static FontContainerFormat Detect(byte[] data, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (offset < 0 || offset + 4 > data.Length)
            return FontContainerFormat.Unknown;

        return RawTtfTables.ReadU32(data, offset) switch
        {
            0x00010000 or 0x74727565 => FontContainerFormat.TrueType,    // 'true'
            0x4F54544F => FontContainerFormat.OpenTypeCff,              // 'OTTO'
            0x74746366 => FontContainerFormat.Collection,               // 'ttcf'
            0x774F4646 => FontContainerFormat.Woff,                     // 'wOFF'
            0x774F4632 => FontContainerFormat.Woff2,                    // 'wOF2'
            _ => FontContainerFormat.Unknown,
        };
    }

    /// <summary>Number of fonts in a collection, or 0 when the data is not a collection.</summary>
    public static int CollectionFontCount(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Detect(data) == FontContainerFormat.Collection && data.Length >= 12
            ? (int)RawTtfTables.ReadU32(data, 8)
            : 0;
    }

    /// <summary>
    /// Copies one font out of a collection into a standalone sfnt file.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is not in the collection.</exception>
    /// <exception cref="InvalidDataException">The collection is malformed.</exception>
    public static byte[] ExtractFromCollection(byte[] data, int fontIndex)
    {
        ArgumentNullException.ThrowIfNull(data);

        int count = CollectionFontCount(data);
        if (count == 0)
            throw new InvalidDataException("The data is not a font collection ('ttcf').");
        if (fontIndex < 0 || fontIndex >= count)
            throw new ArgumentOutOfRangeException(nameof(fontIndex), fontIndex, $"The collection holds {count} fonts.");

        int headerEntry = 12 + (fontIndex * 4);
        if (headerEntry + 4 > data.Length)
            throw new InvalidDataException("The collection header is truncated.");

        return ExtractSfntAt(data, (int)RawTtfTables.ReadU32(data, headerEntry));
    }

    /// <summary>
    /// Copies the sfnt font whose table directory starts at <paramref name="directoryOffset"/>
    /// into a standalone file. Table offsets are taken as absolute within
    /// <paramref name="data"/>, as they are in a collection.
    /// </summary>
    public static byte[] ExtractSfntAt(byte[] data, int directoryOffset)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (directoryOffset < 0 || directoryOffset + 12 > data.Length)
            throw new InvalidDataException("The font directory lies outside the data.");

        var format = Detect(data, directoryOffset);
        if (format is not (FontContainerFormat.TrueType or FontContainerFormat.OpenTypeCff))
            throw new InvalidDataException("No sfnt font directory at the given offset.");

        uint sfntVersion = RawTtfTables.ReadU32(data, directoryOffset);
        int numTables = RawTtfTables.ReadU16(data, directoryOffset + 4);
        if (directoryOffset + 12 + (numTables * 16) > data.Length)
            throw new InvalidDataException("The font directory is truncated.");

        var writer = new TtfWriter(sfntVersion);
        for (int i = 0; i < numTables; i++)
        {
            int record = directoryOffset + 12 + (i * 16);
            string tag = System.Text.Encoding.ASCII.GetString(data, record, 4);
            long offset = RawTtfTables.ReadU32(data, record + 8);
            long length = RawTtfTables.ReadU32(data, record + 12);

            if (offset + length > data.Length)
                throw new InvalidDataException($"Table '{tag}' lies outside the data.");

            var table = new byte[length];
            Array.Copy(data, offset, table, 0, length);
            writer.AddTable(tag, table);
        }

        return writer.Build();
    }

    /// <summary>
    /// Decodes a WOFF 1.0 file back to the sfnt font it wraps.
    /// </summary>
    /// <exception cref="InvalidDataException">The WOFF file is malformed.</exception>
    public static byte[] DecodeWoff(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (Detect(data) != FontContainerFormat.Woff || data.Length < 44)
            throw new InvalidDataException("The data is not a WOFF 1.0 file.");

        uint flavor = RawTtfTables.ReadU32(data, 4);
        int numTables = RawTtfTables.ReadU16(data, 12);
        if (44 + (numTables * 20) > data.Length)
            throw new InvalidDataException("The WOFF table directory is truncated.");

        var writer = new TtfWriter(flavor);
        for (int i = 0; i < numTables; i++)
        {
            int entry = 44 + (i * 20);
            string tag = System.Text.Encoding.ASCII.GetString(data, entry, 4);
            long offset = RawTtfTables.ReadU32(data, entry + 4);
            long compLength = RawTtfTables.ReadU32(data, entry + 8);
            long origLength = RawTtfTables.ReadU32(data, entry + 12);

            if (offset + compLength > data.Length || compLength > origLength)
                throw new InvalidDataException($"WOFF table '{tag}' is malformed.");

            var table = new byte[origLength];
            if (compLength == origLength)
            {
                // Stored uncompressed.
                Array.Copy(data, offset, table, 0, origLength);
            }
            else
            {
                using var input = new MemoryStream(data, (int)offset, (int)compLength, writable: false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                zlib.ReadExactly(table);
            }

            writer.AddTable(tag, table);
        }

        return writer.Build();
    }
}
