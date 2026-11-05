namespace OpenFontSharp.Tables.CFF;

internal static class Cff2DefaultInstanceConverter
{
    private const int OpPrivate = 18;
    private const int OpSubrs = 19;
    private const int OpCharStrings = 17;
    private const int OpVariationStore = 24;
    private const int OpFdArray = 1236;
    private const int OpFdSelect = 1237;

    public static byte[] ConvertToCff1(byte[] cff2)
    {
        ArgumentNullException.ThrowIfNull(cff2);
        if (cff2.Length < 5 || cff2[0] != 2 || cff2[2] < 5)
            throw new InvalidDataException("The CFF2 table header is invalid.");

        int headerSize = cff2[2];
        int topDictLength = ReadU16(cff2, 3);
        EnsureRange(cff2, headerSize, topDictLength);

        var topDict = ReadDict(cff2.AsSpan(headerSize, topDictLength));
        int charStringsOffset = RequiredOffset(topDict, OpCharStrings);
        int fdArrayOffset = RequiredOffset(topDict, OpFdArray);
        int fdSelectOffset = OptionalOffset(topDict, OpFdSelect);
        int variationStoreOffset = OptionalOffset(topDict, OpVariationStore);

        var globalSubrs = ReadIndex(cff2, headerSize + topDictLength).Items;
        var charStrings = ReadIndex(cff2, charStringsOffset).Items;
        if (charStrings.Length == 0 || charStrings.Length > ushort.MaxValue)
            throw new InvalidDataException($"The CFF2 CharStrings INDEX at offset {charStringsOffset} has {charStrings.Length} entries.");

        var fontDictItems = ReadIndex(cff2, fdArrayOffset).Items;
        if (fontDictItems.Length == 0)
            throw new InvalidDataException("The CFF2 FDArray is empty.");

        var fontDicts = new List<FontDict>(fontDictItems.Length);
        foreach (byte[] item in fontDictItems)
        {
            var dict = ReadDict(item);
            int privateSize = 0;
            int privateOffset = 0;
            if (dict.TryGetValue(OpPrivate, out double[]? privateEntry))
            {
                if (privateEntry.Length != 2)
                    throw new InvalidDataException("A CFF2 Private entry must have two operands.");
                privateSize = ToOffset(privateEntry[0]);
                privateOffset = ToOffset(privateEntry[1]);
            }

            var fontDict = new FontDict(privateSize, privateOffset);
            if (privateSize > 0)
            {
                EnsureRange(cff2, privateOffset, privateSize);
                var privateDict = ReadDict(cff2.AsSpan(privateOffset, privateSize));
                if (privateDict.TryGetValue(OpSubrs, out double[]? subrsEntry))
                    fontDict.LocalSubr = ReadIndex(cff2, checked(privateOffset + ToOffset(subrsEntry[0]))).Items.ToList();
            }
            fontDicts.Add(fontDict);
        }

        var font = new Cff1Font
        {
            _globalSubrRawBufferList = globalSubrs.ToList(),
            _cidFontDict = fontDicts
        };
        int[] selectors = fdSelectOffset == 0
            ? new int[charStrings.Length]
            : ReadFdSelect(cff2, fdSelectOffset, charStrings.Length);
        if (fdSelectOffset == 0 && fontDicts.Count != 1)
            throw new InvalidDataException("A CFF2 font with multiple Font DICTs must have an FDSelect.");
        float[][] variationScalars = ReadVariationScalars(cff2, variationStoreOffset);

        var parser = new Type2CharStringParser();
        parser.SetCurrentCff2Font(font, variationScalars);
        var staticCharStrings = new List<byte[]>(charStrings.Length);
        for (int glyph = 0; glyph < charStrings.Length; glyph++)
        {
            int fontDictIndex = selectors[glyph];
            if ((uint)fontDictIndex >= (uint)fontDicts.Count)
                throw new InvalidDataException("A CFF2 FDSelect entry is outside the FDArray.");
            parser.SetCidFontDict(fontDicts[fontDictIndex]);
            var instructions = parser.ParseType2CharString(charStrings[glyph]);
            staticCharStrings.Add(EncodeCharString(instructions.InnerInsts));
        }

        return BuildCff1(staticCharStrings);
    }

    private static byte[] EncodeCharString(IReadOnlyList<Type2Instruction> instructions)
    {
        var maskData = new HashSet<int>();
        for (int i = 0; i < instructions.Count; i++)
        {
            if ((OperatorName)instructions[i].Op is not (OperatorName.hintmask_bits or OperatorName.cntrmask_bits))
                continue;

            int byteCount = instructions[i].Value;
            int chunks = (byteCount + 3) / 4;
            if (byteCount <= 4 || chunks > i)
                throw new OpenFontNotSupportedException("The CFF2 hint mask is malformed.");
            for (int j = i - chunks; j < i; j++)
            {
                if ((OperatorName)instructions[j].Op != OperatorName.LoadInt)
                    throw new OpenFontNotSupportedException("The CFF2 hint mask is malformed.");
                maskData.Add(j);
            }
        }

        using var output = new MemoryStream();
        for (int i = 0; i < instructions.Count; i++)
        {
            Type2Instruction instruction = instructions[i];
            OperatorName op = (OperatorName)instruction.Op;
            if (maskData.Contains(i))
                continue;

            switch (op)
            {
                case OperatorName.LoadInt:
                    WriteNumber(output, instruction.Value);
                    break;
                case OperatorName.LoadFloat:
                    output.WriteByte(255);
                    WriteI32(output, instruction.Value);
                    break;
                case OperatorName.hintmask_bits:
                case OperatorName.cntrmask_bits:
                    {
                        int byteCount = instruction.Value;
                        int first = i - ((byteCount + 3) / 4);
                        int opByte = op == OperatorName.hintmask_bits ? 19 : 20;
                        output.WriteByte((byte)opByte);
                        for (int j = first; j < i; j++)
                        {
                            int value = instructions[j].Value;
                            for (int b = 0; b < 4 && ((j - first) * 4 + b) < byteCount; b++)
                                output.WriteByte((byte)(value >> (24 - b * 8)));
                        }
                    }
                    break;
                case OperatorName.hintmask1:
                case OperatorName.hintmask2:
                case OperatorName.hintmask3:
                case OperatorName.hintmask4:
                case OperatorName.cntrmask1:
                case OperatorName.cntrmask2:
                case OperatorName.cntrmask3:
                case OperatorName.cntrmask4:
                    {
                        bool counter = op is OperatorName.cntrmask1 or OperatorName.cntrmask2
                            or OperatorName.cntrmask3 or OperatorName.cntrmask4;
                        int byteCount = op is OperatorName.hintmask1 or OperatorName.cntrmask1 ? 1
                            : op is OperatorName.hintmask2 or OperatorName.cntrmask2 ? 2
                            : op is OperatorName.hintmask3 or OperatorName.cntrmask3 ? 3 : 4;
                        output.WriteByte((byte)(counter ? 20 : 19));
                        for (int b = 0; b < byteCount; b++)
                            output.WriteByte((byte)(instruction.Value >> (24 - b * 8)));
                    }
                    break;
                case OperatorName.GlyphWidth:
                    throw new OpenFontNotSupportedException("CFF2 charstrings cannot specify a width.");
                case OperatorName.Unknown:
                    throw new OpenFontNotSupportedException("The CFF2 charstring contains an unsupported operator.");
                default:
                    WriteOperator(output, op);
                    break;
            }
        }

        output.WriteByte(14);
        return output.ToArray();
    }

    private static void WriteOperator(Stream output, OperatorName op)
    {
        byte first;
        byte second = 0;
        bool escaped = false;
        switch (op)
        {
            case OperatorName.hstem: first = 1; break;
            case OperatorName.vstem: first = 3; break;
            case OperatorName.vmoveto: first = 4; break;
            case OperatorName.rlineto: first = 5; break;
            case OperatorName.hlineto: first = 6; break;
            case OperatorName.vlineto: first = 7; break;
            case OperatorName.rrcurveto: first = 8; break;
            case OperatorName._return: first = 11; break;
            case OperatorName.endchar: first = 14; break;
            case OperatorName.hstemhm: first = 18; break;
            case OperatorName.rmoveto: first = 21; break;
            case OperatorName.hmoveto: first = 22; break;
            case OperatorName.vstemhm: first = 23; break;
            case OperatorName.rcurveline: first = 24; break;
            case OperatorName.rlinecurve: first = 25; break;
            case OperatorName.vvcurveto: first = 26; break;
            case OperatorName.hhcurveto: first = 27; break;
            case OperatorName.vhcurveto: first = 30; break;
            case OperatorName.hvcurveto: first = 31; break;
            case OperatorName.and: first = 12; second = 3; escaped = true; break;
            case OperatorName.or: first = 12; second = 4; escaped = true; break;
            case OperatorName.not: first = 12; second = 5; escaped = true; break;
            case OperatorName.abs: first = 12; second = 9; escaped = true; break;
            case OperatorName.add: first = 12; second = 10; escaped = true; break;
            case OperatorName.sub: first = 12; second = 11; escaped = true; break;
            case OperatorName.div: first = 12; second = 12; escaped = true; break;
            case OperatorName.neg: first = 12; second = 14; escaped = true; break;
            case OperatorName.eq: first = 12; second = 15; escaped = true; break;
            case OperatorName.drop: first = 12; second = 18; escaped = true; break;
            case OperatorName.put: first = 12; second = 20; escaped = true; break;
            case OperatorName.get: first = 12; second = 21; escaped = true; break;
            case OperatorName.ifelse: first = 12; second = 22; escaped = true; break;
            case OperatorName.random: first = 12; second = 23; escaped = true; break;
            case OperatorName.mul: first = 12; second = 24; escaped = true; break;
            case OperatorName.sqrt: first = 12; second = 26; escaped = true; break;
            case OperatorName.dup: first = 12; second = 27; escaped = true; break;
            case OperatorName.exch: first = 12; second = 28; escaped = true; break;
            case OperatorName.index: first = 12; second = 29; escaped = true; break;
            case OperatorName.roll: first = 12; second = 30; escaped = true; break;
            case OperatorName.hflex: first = 12; second = 34; escaped = true; break;
            case OperatorName.flex: first = 12; second = 35; escaped = true; break;
            case OperatorName.hflex1: first = 12; second = 36; escaped = true; break;
            case OperatorName.flex1: first = 12; second = 37; escaped = true; break;
            default:
                throw new OpenFontNotSupportedException($"The CFF2 charstring operator '{op}' cannot be normalized.");
        }

        output.WriteByte(first);
        if (escaped)
            output.WriteByte(second);
    }

    private static byte[] BuildCff1(IReadOnlyList<byte[]> charStrings)
    {
        if (charStrings.Count > 64999)
            throw new OpenFontNotSupportedException("The CFF2 font has too many glyphs for static CFF output.");

        var customStrings = Enumerable.Range(1, charStrings.Count - 1)
            .Select(glyph => System.Text.Encoding.ASCII.GetBytes($"glyph{glyph}"))
            .ToArray();
        byte[] nameIndex = WriteCff1Index([System.Text.Encoding.ASCII.GetBytes("OpenFontSharp")]);
        byte[] stringIndex = WriteCff1Index(customStrings);
        byte[] globalSubrs = [0, 0];
        byte[] charset = new byte[1 + ((charStrings.Count - 1) * 2)];
        for (int glyph = 1; glyph < charStrings.Count; glyph++)
        {
            int sid = 390 + glyph;
            charset[1 + ((glyph - 1) * 2)] = (byte)(sid >> 8);
            charset[2 + ((glyph - 1) * 2)] = (byte)sid;
        }

        byte[] charStringsIndex = WriteCff1Index(charStrings);
        byte[] TopDict(int charsetOffset, int charStringsOffset, int privateOffset) =>
        [
            .. Long(charsetOffset), 15,
            .. Long(charStringsOffset), 17,
            .. Long(0), .. Long(privateOffset), 18
        ];

        int topDictLength = TopDict(0, 0, 0).Length;
        int topIndexLength = Cff1IndexLength(topDictLength);
        int charsetOffset = 4 + nameIndex.Length + topIndexLength + stringIndex.Length + globalSubrs.Length;
        int charStringsOffset = charsetOffset + charset.Length;
        int privateOffset = charStringsOffset + charStringsIndex.Length;
        byte[] topDict = TopDict(charsetOffset, charStringsOffset, privateOffset);
        byte[] topIndex = WriteCff1Index([topDict]);

        using var output = new MemoryStream();
        output.Write([1, 0, 4, 4]);
        output.Write(nameIndex);
        output.Write(topIndex);
        output.Write(stringIndex);
        output.Write(globalSubrs);
        output.Write(charset);
        output.Write(charStringsIndex);
        return output.ToArray();
    }

    private static float[][] ReadVariationScalars(byte[] data, int offset)
    {
        if (offset == 0)
            return [[]];

        int length = ReadU16(data, offset);
        int store = checked(offset + 2);
        EnsureRange(data, store, length);
        int storeEnd = checked(store + length);
        if (ReadU16(data, store) != 1)
            throw new InvalidDataException("The CFF2 ItemVariationStore format is unsupported.");

        int regionList = checked(store + (int)ReadU32(data, store + 2));
        if (regionList < store + 12 || regionList > storeEnd - 4)
            throw new InvalidDataException("The CFF2 variation region list offset is invalid.");
        int dataCount = ReadU16(data, store + 6);
        if (store + 8 > storeEnd || dataCount > (storeEnd - (store + 8)) / 4)
            throw new InvalidDataException("The CFF2 variation data offsets are truncated.");
        var dataOffsets = new uint[dataCount];
        for (int i = 0; i < dataCount; i++)
            dataOffsets[i] = ReadU32(data, store + 8 + (i * 4));

        int axisCount = ReadU16(data, regionList);
        int regionCount = ReadU16(data, regionList + 2);
        int regionListLength = checked(4 + (axisCount * regionCount * 6));
        if (regionListLength > storeEnd - regionList)
            throw new InvalidDataException("The CFF2 variation region list is truncated.");
        var neutralRegions = new bool[regionCount];
        int coordOffset = regionList + 4;
        for (int region = 0; region < regionCount; region++)
        {
            bool neutral = true;
            for (int axis = 0; axis < axisCount; axis++)
            {
                int peak = ReadI16(data, coordOffset + 2);
                if (peak != 0)
                    neutral = false;
                coordOffset += 6;
            }
            neutralRegions[region] = neutral;
        }

        var scalars = new float[dataCount][];
        for (int i = 0; i < dataCount; i++)
        {
            int variationData = checked(store + (int)dataOffsets[i]);
            if (variationData < store + 12 || variationData > storeEnd - 6)
                throw new InvalidDataException("A CFF2 variation data offset is invalid.");
            int regionIndexCount = ReadU16(data, variationData + 4);
            if (regionIndexCount > (storeEnd - (variationData + 6)) / 2)
                throw new InvalidDataException("A CFF2 variation region index list is truncated.");
            scalars[i] = new float[regionIndexCount];
            for (int region = 0; region < regionIndexCount; region++)
            {
                int regionIndex = ReadU16(data, variationData + 6 + (region * 2));
                if ((uint)regionIndex >= (uint)neutralRegions.Length)
                    throw new InvalidDataException("A CFF2 variation region index is out of range.");
                scalars[i][region] = neutralRegions[regionIndex] ? 1 : 0;
            }
        }
        return scalars.Length == 0 ? [[]] : scalars;
    }

    private static int[] ReadFdSelect(byte[] data, int offset, int glyphCount)
    {
        EnsureRange(data, offset, 1);
        byte format = data[offset++];
        var selectors = new int[glyphCount];
        if (format == 0)
        {
            EnsureRange(data, offset, glyphCount);
            for (int glyph = 0; glyph < glyphCount; glyph++)
                selectors[glyph] = data[offset + glyph];
            return selectors;
        }

        int rangeCount;
        int cursor;
        int entrySize;
        if (format == 3)
        {
            EnsureRange(data, offset, 2);
            rangeCount = ReadU16(data, offset);
            cursor = offset + 2;
            entrySize = 3;
        }
        else if (format == 4)
        {
            EnsureRange(data, offset, 4);
            rangeCount = checked((int)ReadU32(data, offset));
            cursor = offset + 4;
            entrySize = 6;
        }
        else
            throw new OpenFontNotSupportedException($"CFF2 FDSelect format {format} is unsupported.");

        EnsureRange(data, cursor, checked((rangeCount * entrySize) + (format == 3 ? 2 : 4)));
        int previous = 0;
        for (int range = 0; range < rangeCount; range++)
        {
            int first = format == 3 ? ReadU16(data, cursor) : checked((int)ReadU32(data, cursor));
            int fd = format == 3 ? data[cursor + 2] : ReadU16(data, cursor + 4);
            int next = format == 3 ? ReadU16(data, cursor + 3) : checked((int)ReadU32(data, cursor + 6));
            if (first != previous || next < first || next > glyphCount)
                throw new InvalidDataException("The CFF2 FDSelect ranges are invalid.");
            Array.Fill(selectors, fd, first, next - first);
            previous = next;
            cursor += entrySize;
        }
        int sentinel = format == 3 ? ReadU16(data, cursor) : checked((int)ReadU32(data, cursor));
        if (sentinel != glyphCount || previous != glyphCount)
            throw new InvalidDataException("The CFF2 FDSelect does not cover every glyph.");
        return selectors;
    }

    private static Dictionary<int, double[]> ReadDict(ReadOnlySpan<byte> dict)
    {
        var entries = new Dictionary<int, double[]>();
        var operands = new List<double>();
        for (int cursor = 0; cursor < dict.Length;)
        {
            byte value = dict[cursor++];
            if (value == 12 || value <= 21 || value is >= 22 and <= 25)
            {
                int op = value == 12
                    ? 1200 + ReadByte(dict, ref cursor)
                    : value;
                entries[op] = operands.ToArray();
                operands.Clear();
            }
            else if (value == 28)
            {
                EnsureRange(dict, cursor, 2);
                operands.Add((short)((dict[cursor] << 8) | dict[cursor + 1]));
                cursor += 2;
            }
            else if (value == 29)
            {
                EnsureRange(dict, cursor, 4);
                operands.Add(ReadI32(dict, cursor));
                cursor += 4;
            }
            else if (value == 30)
                operands.Add(ReadReal(dict, ref cursor));
            else if (value == 255)
            {
                EnsureRange(dict, cursor, 4);
                operands.Add(ReadI32(dict, cursor) / 65536.0);
                cursor += 4;
            }
            else if (value is >= 32 and <= 254)
                operands.Add(ReadDictInteger(dict, ref cursor, value));
            else
                throw new InvalidDataException($"The CFF2 DICT contains invalid byte {value} at offset {cursor - 1}.");
        }
        return entries;
    }

    private static int RequiredOffset(Dictionary<int, double[]> dict, int op)
    {
        if (!dict.TryGetValue(op, out double[]? operands) || operands.Length != 1)
            throw new InvalidDataException($"The CFF2 DICT is missing required operator {op}.");
        return ToOffset(operands[0]);
    }

    private static int OptionalOffset(Dictionary<int, double[]> dict, int op)
    {
        if (!dict.TryGetValue(op, out double[]? operands))
            return 0;
        if (operands.Length != 1)
            throw new InvalidDataException($"The CFF2 DICT operator {op} has an invalid operand count.");
        return ToOffset(operands[0]);
    }

    private static int ToOffset(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > int.MaxValue || value != Math.Truncate(value))
            throw new InvalidDataException("A CFF2 offset is invalid.");
        return (int)value;
    }

    private static double ReadDictInteger(ReadOnlySpan<byte> data, ref int cursor, byte first)
    {
        if (first <= 246)
            return first - 139;
        EnsureRange(data, cursor, 1);
        byte second = data[cursor++];
        return first <= 250
            ? ((first - 247) * 256) + second + 108
            : -((first - 251) * 256) - second - 108;
    }

    private static double ReadReal(ReadOnlySpan<byte> data, ref int cursor)
    {
        var text = new System.Text.StringBuilder();
        bool done = false;
        while (!done)
        {
            EnsureRange(data, cursor, 1);
            byte value = data[cursor++];
            for (int nibbleIndex = 0; nibbleIndex < 2; nibbleIndex++)
            {
                int nibble = nibbleIndex == 0 ? value >> 4 : value & 0xF;
                switch (nibble)
                {
                    case <= 9: text.Append((char)('0' + nibble)); break;
                    case 0xA: text.Append('.'); break;
                    case 0xB: text.Append('E'); break;
                    case 0xC: text.Append("E-"); break;
                    case 0xD: break;
                    case 0xE: text.Append('-'); break;
                    case 0xF: done = true; break;
                }
                if (done)
                    break;
            }
        }
        if (!double.TryParse(text.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double result))
            throw new InvalidDataException("A CFF2 real operand is invalid.");
        return result;
    }

    private static Cff2Index ReadIndex(byte[] data, int offset)
    {
        EnsureRange(data, offset, 4);
        uint count = ReadU32(data, offset);
        if (count == 0)
            return new Cff2Index([], offset + 4);
        if (count > int.MaxValue)
            throw new InvalidDataException("A CFF2 INDEX has too many entries.");

        EnsureRange(data, offset + 4, 1);
        int offSize = data[offset + 4];
        if (offSize is < 1 or > 4)
            throw new InvalidDataException("A CFF2 INDEX has an invalid offset size.");
        int countInt = (int)count;
        int offsetsStart = offset + 5;
        int offsetCount = checked(countInt + 1);
        EnsureRange(data, offsetsStart, checked(offsetCount * offSize));
        int dataStart = checked(offsetsStart + (offsetCount * offSize));
        var offsets = new int[offsetCount];
        for (int i = 0; i < offsetCount; i++)
        {
            uint itemOffset = 0;
            for (int b = 0; b < offSize; b++)
                itemOffset = (itemOffset << 8) | data[offsetsStart + (i * offSize) + b];
            if (itemOffset == 0 || itemOffset > int.MaxValue)
                throw new InvalidDataException("A CFF2 INDEX contains an invalid offset.");
            offsets[i] = (int)itemOffset - 1;
        }
        if (offsets[0] != 0 || offsets[^1] < 0)
            throw new InvalidDataException("A CFF2 INDEX has an invalid offset array.");
        EnsureRange(data, dataStart, offsets[^1]);
        var items = new byte[countInt][];
        for (int i = 0; i < countInt; i++)
        {
            if (offsets[i + 1] < offsets[i])
                throw new InvalidDataException("A CFF2 INDEX offset array is not ordered.");
            items[i] = data.AsSpan(dataStart + offsets[i], offsets[i + 1] - offsets[i]).ToArray();
        }
        return new Cff2Index(items, dataStart + offsets[^1]);
    }

    private static byte[] WriteCff1Index(IReadOnlyList<byte[]> items)
    {
        if (items.Count == 0)
            return [0, 0];
        if (items.Count > ushort.MaxValue)
            throw new OpenFontNotSupportedException("The static CFF INDEX has too many entries.");

        int dataLength = items.Sum(item => item.Length);
        int offSize = dataLength + 1 <= 255 ? 1 : dataLength + 1 <= 65535 ? 2 : dataLength + 1 <= 0xFFFFFF ? 3 : 4;
        using var output = new MemoryStream();
        WriteU16(output, (ushort)items.Count);
        output.WriteByte((byte)offSize);
        int nextOffset = 1;
        for (int i = 0; i <= items.Count; i++)
        {
            WriteOffset(output, nextOffset, offSize);
            if (i < items.Count)
                nextOffset = checked(nextOffset + items[i].Length);
        }
        foreach (byte[] item in items)
            output.Write(item);
        return output.ToArray();
    }

    private static int Cff1IndexLength(int itemLength)
    {
        int offSize = itemLength + 1 <= 255 ? 1 : itemLength + 1 <= 65535 ? 2 : 3;
        return itemLength + 3 + (2 * offSize);
    }

    private static void WriteNumber(Stream output, int value)
    {
        if (value is >= -107 and <= 107)
            output.WriteByte((byte)(value + 139));
        else if (value is >= 108 and <= 1131)
        {
            int adjusted = value - 108;
            output.WriteByte((byte)(247 + adjusted / 256));
            output.WriteByte((byte)(adjusted % 256));
        }
        else if (value is >= -1131 and <= -108)
        {
            int adjusted = -value - 108;
            output.WriteByte((byte)(251 + adjusted / 256));
            output.WriteByte((byte)(adjusted % 256));
        }
        else if (value is >= short.MinValue and <= short.MaxValue)
        {
            output.WriteByte(28);
            WriteI16(output, (short)value);
        }
        else
        {
            output.WriteByte(29);
            WriteI32(output, value);
        }
    }

    private static byte[] Long(int value) => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static void WriteOffset(Stream output, int value, int size)
    {
        for (int b = size - 1; b >= 0; b--)
            output.WriteByte((byte)(value >> (b * 8)));
    }

    private static void WriteU16(Stream output, ushort value)
    {
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static void WriteI16(Stream output, short value) => WriteU16(output, unchecked((ushort)value));

    private static void WriteI32(Stream output, int value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int cursor)
    {
        EnsureRange(data, cursor, 1);
        return data[cursor++];
    }

    private static int ReadU16(byte[] data, int offset)
    {
        EnsureRange(data, offset, 2);
        return (data[offset] << 8) | data[offset + 1];
    }

    private static int ReadU16(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, 2);
        return (data[offset] << 8) | data[offset + 1];
    }

    private static short ReadI16(byte[] data, int offset) => unchecked((short)ReadU16(data, offset));

    private static uint ReadU32(byte[] data, int offset)
    {
        EnsureRange(data, offset, 4);
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    private static int ReadI32(ReadOnlySpan<byte> data, int offset)
        => unchecked((int)(((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8) | data[offset + 3]));

    private static void EnsureRange(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException("The CFF2 table contains an out-of-range offset.");
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException("The CFF2 table contains an out-of-range offset.");
    }

    private sealed record Cff2Index(byte[][] Items, int End);
}
