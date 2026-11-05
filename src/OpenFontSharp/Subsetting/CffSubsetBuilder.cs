namespace OpenFontSharp.Subsetting;

/// <summary>
/// Subsets a 'CFF ' table (CFF version 1) by rebuilding the CharStrings INDEX and charset
/// for the retained glyphs.
/// </summary>
/// <remarks>
/// <para>
/// CharStrings are copied verbatim. Global and local subroutines are kept whole, so
/// every subroutine call in a retained charstring still resolves to the same code (and
/// the subroutine bias, which depends on the subroutine count, is unchanged). This
/// keeps the subset simple and exact at the cost of some size.
/// </para>
/// <para>
/// Not supported, and reported with <see cref="NotSupportedException"/>: fonts with
/// more than one Top DICT, Type 1 charstrings,
/// synthetic fonts, the predefined Expert charsets, and glyphs composed with the
/// deprecated <c>seac</c> form of <c>endchar</c>, whose components the subset would
/// otherwise silently lose.
/// </para>
/// </remarks>
public static class CffSubsetBuilder
{
    // Top DICT operators (two-byte operators are 1200 + second byte).
    private const int OpUniqueId = 13;
    private const int OpXuid = 14;
    private const int OpCharset = 15;
    private const int OpEncoding = 16;
    private const int OpCharStrings = 17;
    private const int OpPrivate = 18;
    private const int OpSubrs = 19;
    private const int OpCharstringType = 1206;
    private const int OpSyntheticBase = 1220;
    private const int OpRos = 1230;
    private const int OpFdArray = 1236;
    private const int OpFdSelect = 1237;

    /// <summary>
    /// Builds the subset 'CFF ' table. <paramref name="retainedGlyphs"/> must be sorted,
    /// start with glyph 0, and only hold ids below the font's glyph count.
    /// </summary>
    /// <exception cref="NotSupportedException">The CFF data uses a feature this subsetter does not handle.</exception>
    public static byte[] Build(byte[] cff, IReadOnlyList<ushort> retainedGlyphs)
    {
        ArgumentNullException.ThrowIfNull(cff);
        ArgumentNullException.ThrowIfNull(retainedGlyphs);

        try
        {
            return BuildCore(cff, retainedGlyphs);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidDataException)
        {
            throw new NotSupportedException("The CFF table is malformed, so the font cannot be subset.", ex);
        }
    }

    private static byte[] BuildCore(byte[] cff, IReadOnlyList<ushort> retainedGlyphs)
    {
        if (cff.Length < 4 || cff[0] != 1)
            throw new NotSupportedException($"Only CFF version 1 can be subset (found major version {(cff.Length > 0 ? cff[0] : 0)}).");

        int hdrSize = cff[2];
        var nameIndex = Index.Read(cff, hdrSize);
        var topDictIndex = Index.Read(cff, nameIndex.End);
        var stringIndex = Index.Read(cff, topDictIndex.End);
        var globalSubrs = Index.Read(cff, stringIndex.End);

        if (topDictIndex.Count != 1)
            throw new NotSupportedException("CFF tables holding more than one font (FontSet) cannot be subset.");

        var topDict = Dict.Parse(topDictIndex.Item(cff, 0));

        bool cidKeyed = topDict.Has(OpRos) || topDict.Has(OpFdArray) || topDict.Has(OpFdSelect);
        if (cidKeyed && (!topDict.Has(OpRos) || !topDict.Has(OpFdArray)))
            throw new InvalidDataException("A CID-keyed CFF Top DICT must have ROS and FDArray entries.");
        if (cidKeyed && (!topDict.Has(OpCharset) || topDict.Int(OpCharset, 0) <= 2))
            throw new InvalidDataException("A CID-keyed CFF font must have a custom charset.");
        if (topDict.Has(OpSyntheticBase))
            throw new NotSupportedException("Synthetic CFF fonts cannot be subset.");
        if (topDict.Has(OpCharstringType) && topDict.Int(OpCharstringType, 0) != 2)
            throw new NotSupportedException("Only Type 2 charstrings can be subset.");
        if (!topDict.Has(OpCharStrings) || (!cidKeyed && !topDict.Has(OpPrivate)))
            throw new InvalidDataException("The Top DICT has no CharStrings or Private entry.");

        var charStrings = Index.Read(cff, topDict.Int(OpCharStrings, 0));
        int glyphCount = charStrings.Count;

        var sids = ReadCharset(cff, topDict.Has(OpCharset) ? topDict.Int(OpCharset, 0) : 0, glyphCount);
        if (cidKeyed)
            return BuildCidKeyed(cff, topDict, nameIndex, stringIndex, globalSubrs, charStrings, sids, retainedGlyphs);

        int privateSize = topDict.Int(OpPrivate, 0);
        int privateOffset = topDict.Int(OpPrivate, 1);
        var privateDict = Dict.Parse(cff.AsSpan(privateOffset, privateSize));

        Index? localSubrs = privateDict.Has(OpSubrs)
            ? Index.Read(cff, privateOffset + privateDict.Int(OpSubrs, 0))
            : null;

        var scanner = new CharStringScanner(cff, globalSubrs, localSubrs);
        foreach (var glyph in retainedGlyphs)
        {
            if (scanner.UsesSeac(charStrings.Item(cff, glyph)))
            {
                throw new NotSupportedException(
                    $"Glyph {glyph} is built with the deprecated 'seac' accent operator, whose " +
                    "components this subsetter does not follow.");
            }
        }

        // Pieces copied verbatim.
        var nameBytes = cff.AsSpan(nameIndex.Start, nameIndex.End - nameIndex.Start).ToArray();
        var stringBytes = cff.AsSpan(stringIndex.Start, stringIndex.End - stringIndex.Start).ToArray();
        var globalSubrBytes = cff.AsSpan(globalSubrs.Start, globalSubrs.End - globalSubrs.Start).ToArray();
        var localSubrBytes = localSubrs is { } local
            ? cff.AsSpan(local.Start, local.End - local.Start).ToArray()
            : [];

        // Rebuilt pieces.
        var charsetBytes = BuildCharset(sids, retainedGlyphs);
        var charStringsBytes = Index.Write(retainedGlyphs.Select(g => charStrings.Item(cff, g).ToArray()).ToList());

        // The Private DICT points at its local subroutines, placed right after it.
        var privateEntries = privateDict.Without(OpSubrs);
        var privateBytes = localSubrs is null
            ? privateEntries
            : [.. privateEntries, .. Dict.Offset(privateEntries.Length + Dict.OffsetEntryLength), OpSubrs];

        // The Top DICT's offset entries are fixed width, so its size is known before
        // the offsets themselves are.
        var topEntries = topDict.Without(OpCharset, OpEncoding, OpCharStrings, OpPrivate, OpUniqueId, OpXuid);
        byte[] Top(int charset, int charStrings, int privateOffset) =>
        [
            .. topEntries,
            .. Dict.Offset(charset), OpCharset,
            .. Dict.Offset(charStrings), OpCharStrings,
            .. Dict.Offset(privateBytes.Length), .. Dict.Offset(privateOffset), OpPrivate,
        ];

        int topDictLength = Top(0, 0, 0).Length;
        int topIndexLength = Index.WrittenLength(topDictLength);

        int charsetOffset = 4 + nameBytes.Length + topIndexLength + stringBytes.Length + globalSubrBytes.Length;
        int charStringsOffset = charsetOffset + charsetBytes.Length;
        int newPrivateOffset = charStringsOffset + charStringsBytes.Length;

        byte[] newTopDict = Top(charsetOffset, charStringsOffset, newPrivateOffset);

        using var output = new MemoryStream();
        output.Write([1, 0, 4, 4]); // version 1.0, header size 4, absolute offsets 4 bytes
        output.Write(nameBytes);
        output.Write(Index.Write([newTopDict]));
        output.Write(stringBytes);
        output.Write(globalSubrBytes);
        output.Write(charsetBytes);
        output.Write(charStringsBytes);
        output.Write(privateBytes);
        output.Write(localSubrBytes);
        return output.ToArray();
    }

    private static byte[] BuildCidKeyed(
        byte[] cff,
        Dict topDict,
        Index nameIndex,
        Index stringIndex,
        Index globalSubrs,
        Index charStrings,
        ushort[] cids,
        IReadOnlyList<ushort> retainedGlyphs)
    {
        var fdArray = Index.Read(cff, topDict.Int(OpFdArray, 0));
        if (fdArray.Count == 0)
            throw new InvalidDataException("A CID-keyed CFF font has an empty FDArray.");

        int[] sourceSelectors;
        int sourceFdSelectFormat = 3;
        if (topDict.Has(OpFdSelect))
        {
            int sourceFdSelectOffset = topDict.Int(OpFdSelect, 0);
            sourceFdSelectFormat = ReadFdSelectFormat(cff, sourceFdSelectOffset);
            sourceSelectors = ReadFdSelect(cff, sourceFdSelectOffset, charStrings.Count, fdArray.Count);
        }
        else
        {
            if (fdArray.Count != 1)
                throw new InvalidDataException("A CID-keyed CFF font with multiple FDs must have an FDSelect.");
            sourceSelectors = new int[charStrings.Count];
        }

        var sourceFontDicts = new CidFontDict[fdArray.Count];
        for (int fd = 0; fd < fdArray.Count; fd++)
        {
            var dict = Dict.Parse(fdArray.Item(cff, fd));
            byte[] privateBytes = [];
            byte[] localSubrBytes = [];
            Index? localSubrs = null;
            bool hasPrivate = dict.Has(OpPrivate);

            if (hasPrivate)
            {
                int privateSize = dict.Int(OpPrivate, 0);
                int privateOffset = dict.Int(OpPrivate, 1);
                var privateDict = Dict.Parse(cff.AsSpan(privateOffset, privateSize));
                localSubrs = privateDict.Has(OpSubrs)
                    ? Index.Read(cff, privateOffset + privateDict.Int(OpSubrs, 0))
                    : null;
                localSubrBytes = localSubrs is { } local
                    ? cff.AsSpan(local.Start, local.End - local.Start).ToArray()
                    : [];

                var privateEntries = privateDict.Without(OpSubrs);
                privateBytes = localSubrs is null
                    ? privateEntries
                    : [.. privateEntries, .. Dict.Offset(privateEntries.Length + Dict.OffsetEntryLength), OpSubrs];
            }

            sourceFontDicts[fd] = new CidFontDict(dict, hasPrivate, privateBytes, localSubrBytes, localSubrs);
        }

        foreach (var glyph in retainedGlyphs)
        {
            int fd = sourceSelectors[glyph];
            var scanner = new CharStringScanner(cff, globalSubrs, sourceFontDicts[fd].LocalSubrs);
            if (scanner.UsesSeac(charStrings.Item(cff, glyph)))
            {
                throw new NotSupportedException(
                    $"Glyph {glyph} is built with the deprecated 'seac' accent operator, whose " +
                    "components this subsetter does not follow.");
            }
        }

        var usedFds = retainedGlyphs.Select(glyph => sourceSelectors[glyph]).Distinct().Order().ToArray();
        if (usedFds.Length > byte.MaxValue + 1)
            throw new NotSupportedException("CID-keyed CFF fonts cannot use more than 256 retained FDs.");
        var fdMap = usedFds.Select((oldFd, newFd) => (oldFd, newFd)).ToDictionary(pair => pair.oldFd, pair => pair.newFd);
        var selectors = retainedGlyphs.Select(glyph => fdMap[sourceSelectors[glyph]]).ToArray();

        var nameBytes = cff.AsSpan(nameIndex.Start, nameIndex.End - nameIndex.Start).ToArray();
        var stringBytes = cff.AsSpan(stringIndex.Start, stringIndex.End - stringIndex.Start).ToArray();
        var globalSubrBytes = cff.AsSpan(globalSubrs.Start, globalSubrs.End - globalSubrs.Start).ToArray();
        var charsetBytes = BuildCharset(cids, retainedGlyphs);
        var charStringsBytes = Index.Write(retainedGlyphs.Select(glyph => charStrings.Item(cff, glyph).ToArray()).ToList());
        var fdSelectBytes = usedFds.Length > 1
            ? BuildFdSelect(selectors, sourceFdSelectFormat)
            : [];

        var topEntries = topDict.Without(
            OpCharset, OpEncoding, OpCharStrings, OpPrivate, OpUniqueId, OpXuid, OpFdArray, OpFdSelect);
        byte[] Top(int charsetOffset, int charStringsOffset, int fdArrayOffset, int fdSelectOffset)
        {
            using var dict = new MemoryStream();
            dict.Write(topEntries);
            dict.Write(Dict.Offset(charsetOffset));
            dict.WriteByte(OpCharset);
            dict.Write(Dict.Offset(charStringsOffset));
            dict.WriteByte(OpCharStrings);
            dict.Write(Dict.Offset(fdArrayOffset));
            dict.WriteByte(12);
            dict.WriteByte(36);
            if (usedFds.Length > 1)
            {
                dict.Write(Dict.Offset(fdSelectOffset));
                dict.WriteByte(12);
                dict.WriteByte(37);
            }
            return dict.ToArray();
        }

        int topDictLength = Top(0, 0, 0, 0).Length;
        int topIndexLength = Index.WrittenLength(topDictLength);
        int prefixLength = 4 + nameBytes.Length + topIndexLength + stringBytes.Length + globalSubrBytes.Length;
        int charsetOffset = prefixLength;
        int fdSelectOffset = fdSelectBytes.Length == 0 ? 0 : charsetOffset + charsetBytes.Length;
        int charStringsOffset = charsetOffset + charsetBytes.Length + fdSelectBytes.Length;
        int fdArrayOffset = charStringsOffset + charStringsBytes.Length;

        byte[] FontDictBytes(int fd, int privateOffset)
        {
            var source = sourceFontDicts[fd];
            var entries = source.Dict.Without(OpPrivate);
            return source.HasPrivate
                ? [.. entries, .. Dict.Offset(source.PrivateBytes.Length), .. Dict.Offset(privateOffset), OpPrivate]
                : entries;
        }

        var provisionalFdArray = Index.Write(usedFds.Select(fd => FontDictBytes(fd, 0)).ToList());
        int privateOffsetCursor = fdArrayOffset + provisionalFdArray.Length;
        var privateOffsets = new Dictionary<int, int>();
        foreach (int fd in usedFds)
        {
            var source = sourceFontDicts[fd];
            if (!source.HasPrivate)
                continue;

            privateOffsets[fd] = privateOffsetCursor;
            privateOffsetCursor += source.PrivateBytes.Length + source.LocalSubrBytes.Length;
        }

        var fdArrayBytes = Index.Write(usedFds
            .Select(fd => FontDictBytes(fd, privateOffsets.GetValueOrDefault(fd)))
            .ToList());
        byte[] newTopDict = Top(charsetOffset, charStringsOffset, fdArrayOffset, fdSelectOffset);

        using var output = new MemoryStream();
        output.Write([1, 0, 4, 4]);
        output.Write(nameBytes);
        output.Write(Index.Write([newTopDict]));
        output.Write(stringBytes);
        output.Write(globalSubrBytes);
        output.Write(charsetBytes);
        output.Write(fdSelectBytes);
        output.Write(charStringsBytes);
        output.Write(fdArrayBytes);
        foreach (int fd in usedFds)
        {
            var source = sourceFontDicts[fd];
            if (!source.HasPrivate)
                continue;
            output.Write(source.PrivateBytes);
            output.Write(source.LocalSubrBytes);
        }
        return output.ToArray();
    }

    private static int ReadFdSelectFormat(byte[] cff, int offset)
    {
        if ((uint)offset >= (uint)cff.Length)
            throw new InvalidDataException("The FDSelect offset is outside the CFF table.");
        return cff[offset];
    }

    private static int[] ReadFdSelect(byte[] cff, int offset, int glyphCount, int fdCount)
    {
        int format = ReadFdSelectFormat(cff, offset);
        var selectors = new int[glyphCount];
        if (format == 0)
        {
            if (offset + 1 + glyphCount > cff.Length)
                throw new InvalidDataException("The FDSelect format 0 array is truncated.");
            for (int glyph = 0; glyph < glyphCount; glyph++)
            {
                selectors[glyph] = cff[offset + 1 + glyph];
                if (selectors[glyph] >= fdCount)
                    throw new InvalidDataException($"FDSelect references missing FD {selectors[glyph]}.");
            }
            return selectors;
        }

        if (format != 3)
            throw new NotSupportedException($"FDSelect format {format} cannot be subset.");
        if (offset + 3 > cff.Length)
            throw new InvalidDataException("The FDSelect format 3 header is truncated.");
        int rangeCount = ReadU16(cff, offset + 1);
        if (rangeCount == 0 || offset + 3L + (rangeCount * 3L) + 2 > cff.Length)
            throw new InvalidDataException("The FDSelect format 3 ranges are truncated or empty.");

        int cursor = offset + 3;
        int first = ReadU16(cff, cursor);
        if (first != 0)
            throw new InvalidDataException("The first FDSelect format 3 range must begin at glyph 0.");
        for (int range = 0; range < rangeCount; range++)
        {
            int start = ReadU16(cff, cursor);
            int fd = cff[cursor + 2];
            int end = ReadU16(cff, cursor + 3);
            if (end <= start || fd >= fdCount)
                throw new InvalidDataException("The FDSelect format 3 ranges are invalid.");
            for (int glyph = start; glyph < end && glyph < glyphCount; glyph++)
                selectors[glyph] = fd;
            cursor += 3;
        }
        if (ReadU16(cff, cursor) != glyphCount)
            throw new InvalidDataException("The FDSelect format 3 sentinel does not match the glyph count.");
        return selectors;
    }

    private static byte[] BuildFdSelect(IReadOnlyList<int> selectors, int format)
    {
        if (format == 0)
        {
            if (selectors.Any(fd => fd > byte.MaxValue))
                throw new NotSupportedException("CID-keyed CFF FD indices must fit in one byte.");
            return [0, .. selectors.Select(fd => (byte)fd)];
        }

        if (format != 3)
            throw new NotSupportedException($"FDSelect format {format} cannot be subset.");

        var ranges = new List<(ushort FirstGlyph, byte Fd)> { (0, (byte)selectors[0]) };
        for (int glyph = 1; glyph < selectors.Count; glyph++)
        {
            if (selectors[glyph] != selectors[glyph - 1])
                ranges.Add(((ushort)glyph, (byte)selectors[glyph]));
        }
        if (ranges.Count > ushort.MaxValue)
            throw new NotSupportedException("The rebuilt FDSelect has too many format 3 ranges.");

        using var output = new MemoryStream();
        output.WriteByte(3);
        WriteU16(output, (ushort)ranges.Count);
        foreach (var (firstGlyph, fd) in ranges)
        {
            WriteU16(output, firstGlyph);
            output.WriteByte(fd);
        }
        WriteU16(output, (ushort)selectors.Count);
        return output.ToArray();
    }

    private static void WriteU16(Stream output, ushort value)
    {
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    /// <summary>Reads the charset into a SID or CID per glyph id.</summary>
    private static ushort[] ReadCharset(byte[] cff, int offset, int glyphCount)
    {
        var sids = new ushort[glyphCount];

        if (offset == 0)
        {
            // Predefined ISOAdobe charset: glyph id equals SID.
            if (glyphCount > 229)
                throw new InvalidDataException("The ISOAdobe charset covers at most 229 glyphs.");
            for (int i = 0; i < glyphCount; i++)
                sids[i] = (ushort)i;
            return sids;
        }

        if (offset is 1 or 2)
            throw new NotSupportedException("CFF fonts using the predefined Expert charsets cannot be subset.");

        int format = cff[offset];
        int pos = offset + 1;
        int glyph = 1;

        switch (format)
        {
            case 0:
                for (; glyph < glyphCount; glyph++, pos += 2)
                    sids[glyph] = ReadU16(cff, pos);
                break;

            case 1:
            case 2:
                while (glyph < glyphCount)
                {
                    int first = ReadU16(cff, pos);
                    int left = format == 1 ? cff[pos + 2] : ReadU16(cff, pos + 2);
                    pos += format == 1 ? 3 : 4;
                    for (int i = 0; i <= left && glyph < glyphCount; i++, glyph++)
                        sids[glyph] = (ushort)(first + i);
                }
                break;

            default:
                throw new InvalidDataException($"Unknown charset format {format}.");
        }

        return sids;
    }

    /// <summary>Writes a format 0 charset with each retained glyph's original SID or CID.</summary>
    private static byte[] BuildCharset(ushort[] sids, IReadOnlyList<ushort> retainedGlyphs)
    {
        var charset = new byte[1 + ((retainedGlyphs.Count - 1) * 2)];
        for (int i = 1; i < retainedGlyphs.Count; i++)
        {
            ushort sid = sids[retainedGlyphs[i]];
            charset[(i * 2) - 1] = (byte)(sid >> 8);
            charset[i * 2] = (byte)sid;
        }
        return charset;
    }

    private static ushort ReadU16(byte[] data, int offset) => (ushort)((data[offset] << 8) | data[offset + 1]);

    private sealed record CidFontDict(Dict Dict, bool HasPrivate, byte[] PrivateBytes, byte[] LocalSubrBytes, Index? LocalSubrs);

    /// <summary>A CFF INDEX located in the table.</summary>
    private readonly record struct Index(int Start, int End, int Count, int OffSize, int OffsetsStart, int DataStart)
    {
        public static Index Read(byte[] data, int start)
        {
            int count = ReadU16(data, start);
            if (count == 0)
                return new Index(start, start + 2, 0, 0, 0, 0);

            int offSize = data[start + 2];
            if (offSize is < 1 or > 4)
                throw new InvalidDataException($"Invalid INDEX offSize {offSize}.");

            int offsetsStart = start + 3;
            int dataStart = offsetsStart + ((count + 1) * offSize) - 1;
            var index = new Index(start, 0, count, offSize, offsetsStart, dataStart);
            return index with { End = dataStart + index.OffsetAt(data, count) };
        }

        public ReadOnlySpan<byte> Item(byte[] data, int i)
        {
            if ((uint)i >= (uint)Count)
                throw new InvalidDataException($"INDEX item {i} is out of range.");

            int from = DataStart + OffsetAt(data, i);
            int to = DataStart + OffsetAt(data, i + 1);
            return data.AsSpan(from, to - from);
        }

        private int OffsetAt(byte[] data, int i)
        {
            int value = 0;
            int pos = OffsetsStart + (i * OffSize);
            for (int b = 0; b < OffSize; b++)
                value = (value << 8) | data[pos + b];
            return value;
        }

        public static int WrittenLength(int dataLength, int count = 1)
            => 3 + ((count + 1) * OffSizeFor(dataLength + 1)) + dataLength;

        public static byte[] Write(IReadOnlyList<byte[]> items)
        {
            if (items.Count == 0)
                return [0, 0];

            int dataLength = items.Sum(i => i.Length);
            int offSize = OffSizeFor(dataLength + 1);

            using var ms = new MemoryStream();
            ms.WriteByte((byte)(items.Count >> 8));
            ms.WriteByte((byte)items.Count);
            ms.WriteByte((byte)offSize);

            int offset = 1;
            for (int i = 0; i <= items.Count; i++)
            {
                for (int b = offSize - 1; b >= 0; b--)
                    ms.WriteByte((byte)(offset >> (b * 8)));
                if (i < items.Count)
                    offset += items[i].Length;
            }

            foreach (var item in items)
                ms.Write(item);

            return ms.ToArray();
        }

        private static int OffSizeFor(int maxOffset)
            => maxOffset <= 0xFF ? 1 : maxOffset <= 0xFFFF ? 2 : maxOffset <= 0xFFFFFF ? 3 : 4;
    }

    /// <summary>A parsed DICT that keeps each entry's original bytes for verbatim copying.</summary>
    private sealed class Dict
    {
        /// <summary>Length of an offset operand written by <see cref="Offset"/>.</summary>
        public const int OffsetEntryLength = 5 + 1; // 5-byte integer + one-byte operator

        private readonly List<(int Op, List<double> Operands, byte[] Raw)> _entries = [];

        public static Dict Parse(ReadOnlySpan<byte> data)
        {
            var dict = new Dict();
            var operands = new List<double>();
            int entryStart = 0;
            int pos = 0;

            while (pos < data.Length)
            {
                int b0 = data[pos];
                if (b0 <= 21)
                {
                    int op = b0;
                    pos++;
                    if (b0 == 12)
                        op = 1200 + data[pos++];

                    dict._entries.Add((op, operands, data[entryStart..pos].ToArray()));
                    operands = [];
                    entryStart = pos;
                }
                else if (b0 == 28)
                {
                    operands.Add((short)((data[pos + 1] << 8) | data[pos + 2]));
                    pos += 3;
                }
                else if (b0 == 29)
                {
                    operands.Add((data[pos + 1] << 24) | (data[pos + 2] << 16) | (data[pos + 3] << 8) | data[pos + 4]);
                    pos += 5;
                }
                else if (b0 == 30)
                {
                    // Real number: nibbles until one is 0xF. Its value is never an
                    // offset, so only its length matters here.
                    pos++;
                    while ((data[pos] & 0x0F) != 0x0F && (data[pos] & 0xF0) != 0xF0)
                        pos++;
                    pos++;
                    operands.Add(0);
                }
                else if (b0 is >= 32 and <= 246)
                {
                    operands.Add(b0 - 139);
                    pos++;
                }
                else if (b0 is >= 247 and <= 250)
                {
                    operands.Add(((b0 - 247) * 256) + data[pos + 1] + 108);
                    pos += 2;
                }
                else if (b0 is >= 251 and <= 254)
                {
                    operands.Add((-(b0 - 251) * 256) - data[pos + 1] - 108);
                    pos += 2;
                }
                else
                {
                    throw new InvalidDataException($"Invalid DICT byte {b0}.");
                }
            }

            return dict;
        }

        public bool Has(int op) => _entries.Any(e => e.Op == op);

        public int Int(int op, int operandIndex)
        {
            var entry = _entries.First(e => e.Op == op);
            if (operandIndex >= entry.Operands.Count)
                throw new InvalidDataException($"DICT operator {op} is missing an operand.");
            return (int)entry.Operands[operandIndex];
        }

        /// <summary>The DICT's bytes with the given operators removed.</summary>
        public byte[] Without(params int[] ops)
            => _entries.Where(e => !ops.Contains(e.Op)).SelectMany(e => e.Raw).ToArray();

        /// <summary>An integer operand in the fixed five-byte form, so its size never depends on its value.</summary>
        public static byte[] Offset(int value)
            => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    /// <summary>
    /// Walks Type 2 charstrings, following subroutine calls, just far enough to spot the
    /// deprecated <c>seac</c> form of <c>endchar</c> (four accent arguments).
    /// </summary>
    private sealed class CharStringScanner(byte[] cff, Index globalSubrs, Index? localSubrs)
    {
        private const int MaxDepth = 10;

        private readonly List<int> _stack = [];
        private int _stems;
        private bool _seac;
        private bool _ended;

        public bool UsesSeac(ReadOnlySpan<byte> charString)
        {
            _stack.Clear();
            _stems = 0;
            _seac = _ended = false;
            Run(charString, 0);
            return _seac;
        }

        private void Run(ReadOnlySpan<byte> code, int depth)
        {
            if (depth > MaxDepth)
                throw new InvalidDataException("Charstring subroutines nest too deeply.");

            int pos = 0;
            while (pos < code.Length && !_ended)
            {
                int b0 = code[pos];

                if (b0 == 28)
                {
                    _stack.Add((short)((code[pos + 1] << 8) | code[pos + 2]));
                    pos += 3;
                    continue;
                }
                if (b0 is >= 32 and <= 246)
                {
                    _stack.Add(b0 - 139);
                    pos++;
                    continue;
                }
                if (b0 is >= 247 and <= 250)
                {
                    _stack.Add(((b0 - 247) * 256) + code[pos + 1] + 108);
                    pos += 2;
                    continue;
                }
                if (b0 is >= 251 and <= 254)
                {
                    _stack.Add((-(b0 - 251) * 256) - code[pos + 1] - 108);
                    pos += 2;
                    continue;
                }
                if (b0 == 255)
                {
                    // 16.16 fixed; the integer part is enough for counting.
                    _stack.Add((short)((code[pos + 1] << 8) | code[pos + 2]));
                    pos += 5;
                    continue;
                }

                pos++;
                switch (b0)
                {
                    case 1:  // hstem
                    case 3:  // vstem
                    case 18: // hstemhm
                    case 23: // vstemhm
                        _stems += _stack.Count / 2;
                        _stack.Clear();
                        break;

                    case 19: // hintmask
                    case 20: // cntrmask
                        _stems += _stack.Count / 2; // implicit vstem arguments
                        _stack.Clear();
                        pos += (_stems + 7) / 8;
                        break;

                    case 10: // callsubr
                    case 29: // callgsubr
                        CallSubr(b0 == 29, depth);
                        break;

                    case 11: // return
                        return;

                    case 14: // endchar
                        // endchar takes an optional width, then optionally four seac
                        // arguments (adx ady bchar achar).
                        if (_stack.Count >= 4)
                            _seac = true;
                        _ended = true;
                        return;

                    case 12: // escape: two-byte operators
                        pos++;
                        _stack.Clear();
                        break;

                    default:
                        _stack.Clear();
                        break;
                }
            }
        }

        private void CallSubr(bool global, int depth)
        {
            if (_stack.Count == 0)
                throw new InvalidDataException("Subroutine call without a subroutine number.");

            int number = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);

            var subrs = global ? globalSubrs : localSubrs ?? throw new InvalidDataException("callsubr without local subroutines.");
            int count = subrs.Count;
            int bias = count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

            Run(subrs.Item(cff, number + bias), depth + 1);
        }
    }
}
