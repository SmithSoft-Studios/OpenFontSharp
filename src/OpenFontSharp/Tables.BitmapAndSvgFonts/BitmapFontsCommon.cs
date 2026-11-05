//MIT, 2019-present, WinterDev

using System.Buffers.Binary;

namespace OpenFontSharp.Tables.BitmapAndSvgFonts
{
    // Microsoft OpenType EBLC/CBLC and EBDT/CBDT specifications:
    // https://learn.microsoft.com/en-us/typography/opentype/spec/eblc
    // https://learn.microsoft.com/en-us/typography/opentype/spec/ebdt
    // https://learn.microsoft.com/en-us/typography/opentype/spec/cblc
    // https://learn.microsoft.com/en-us/typography/opentype/spec/cbdt
    sealed class BitmapGlyphRecord
    {
        internal ushort GlyphId;
        internal uint Offset;
        internal uint Length;
        internal ushort ImageFormat;
        internal byte BitDepth;
        internal byte[]? SharedMetrics;
    }

    abstract class BitmapLocationTable : TableEntry
    {
        internal BitmapGlyphRecord[][] Strikes { get; private set; } = [];
        internal ushort HighestGlyphId { get; private set; }
        protected abstract ushort MajorVersion { get; }

        protected override void ReadContentFrom(BinaryReader reader)
        {
            byte[] data = BitmapTableData.Read(reader, Header.Length, 64 * 1024 * 1024);
            var input = new BitmapTableData(data);
            if (input.U16() != MajorVersion || input.U16() != 0)
                throw new OpenFontNotSupportedException($"Unsupported {Name} version.");
            uint count = input.U32();
            if (count == 0 || count > 1024)
                throw new InvalidDataException("A bitmap font must contain between 1 and 1024 strikes.");
            input.Require((long)count * 48);
            Strikes = new BitmapGlyphRecord[count][];
            int totalGlyphs = 0;
            int indexedSlots = 0;
            for (int strike = 0; strike < count; strike++)
            {
                input.Position = 8 + strike * 48;
                uint arrayOffset = input.U32();
                uint indexSize = input.U32();
                uint subtableCount = input.U32();
                input.Position += 28; // colorRef and horizontal/vertical line metrics
                ushort first = input.U16();
                ushort last = input.U16();
                HighestGlyphId = Math.Max(HighestGlyphId, last);
                input.Position += 2; // ppemX and ppemY
                byte depth = input.U8();
                byte flags = input.U8();
                if (first > last || subtableCount > 65536 ||
                    (ulong)arrayOffset + indexSize > (ulong)data.Length ||
                    arrayOffset < 8 + count * 48 || (ulong)subtableCount * 8 > indexSize)
                    throw new InvalidDataException("Invalid bitmap strike range or index array.");
                if (depth != 1 && depth != 2 && depth != 4 && depth != 8 &&
                    !(MajorVersion == 3 && depth == 32))
                    throw new OpenFontNotSupportedException($"Unsupported bitmap bit depth {depth}.");
                if (flags == 0 || (flags & ~3) != 0)
                    throw new InvalidDataException("Invalid bitmap strike layout flags.");
                if ((flags & 2) != 0)
                    throw new OpenFontNotSupportedException("Vertical bitmap strike layouts (flag 0x02) are not supported.");

                var glyphs = new List<BitmapGlyphRecord>();
                int previousLast = -1;
                long indexEnd = (long)arrayOffset + indexSize;
                for (int subtable = 0; subtable < subtableCount; subtable++)
                {
                    input.Position = (long)arrayOffset + subtable * 8;
                    ushort rangeFirst = input.U16();
                    ushort rangeLast = input.U16();
                    uint offset = input.U32();
                    if (rangeFirst < first || rangeLast > last || rangeFirst > rangeLast ||
                        rangeFirst <= previousLast || offset < subtableCount * 8 ||
                        (ulong)offset + 8 > indexSize)
                        throw new InvalidDataException("Invalid bitmap index subtable range or offset.");
                    previousLast = rangeLast;
                    indexedSlots += rangeLast - rangeFirst + 1;
                    if (indexedSlots > 1048576)
                        throw new InvalidDataException("Bitmap strikes exceed the limit of 1048576 indexed glyph slots.");
                    input.Position = (long)arrayOffset + offset;
                    input.Limit = indexEnd;
                    ReadIndex(input, rangeFirst, rangeLast, depth, glyphs);
                    input.Limit = data.Length;
                }
                totalGlyphs += glyphs.Count;
                if (totalGlyphs > 1048576)
                    throw new InvalidDataException("Bitmap strikes exceed the limit of 1048576 glyph records.");
                Strikes[strike] = glyphs.ToArray();
            }
        }

        static void ReadIndex(BitmapTableData input, ushort first, ushort last, byte depth,
            List<BitmapGlyphRecord> glyphs)
        {
            ushort format = input.U16();
            ushort imageFormat = input.U16();
            uint imageOffset = input.U32();
            if (imageFormat is not (1 or 2 or 5 or 6 or 7 or 17 or 18 or 19))
                throw new OpenFontNotSupportedException($"Unsupported bitmap image format {imageFormat}.");
            int rangeCount = last - first + 1;
            void Add(int id, ulong offset, ulong length, byte[]? metrics = null)
            {
                if (offset > uint.MaxValue || length > uint.MaxValue ||
                    offset + length > uint.MaxValue || offset < 4)
                    throw new InvalidDataException("Bitmap image offset or length is out of range.");
                if (length == 0) return; // identical consecutive offsets denote absent glyphs
                glyphs.Add(new BitmapGlyphRecord
                {
                    GlyphId = (ushort)id, Offset = (uint)offset, Length = (uint)length,
                    ImageFormat = imageFormat, BitDepth = depth, SharedMetrics = metrics
                });
            }
            if (format is 1 or 3)
            {
                input.Require((rangeCount + 1L) * (format == 1 ? 4 : 2));
                uint previous = format == 1 ? input.U32() : input.U16();
                for (int i = 0; i < rangeCount; i++)
                {
                    uint next = format == 1 ? input.U32() : input.U16();
                    if (next < previous) throw new InvalidDataException("Bitmap offsets must be nondecreasing.");
                    Add(first + i, (ulong)imageOffset + previous, next - previous);
                    previous = next;
                }
            }
            else if (format is 2 or 5)
            {
                uint size = input.U32();
                byte[] metrics = input.Bytes(8);
                uint count = format == 5 ? input.U32() : (uint)rangeCount;
                if (size == 0 || count > rangeCount)
                    throw new InvalidDataException("Invalid constant-size bitmap glyph count or image size.");
                if (format == 5) input.Require(count * 2L);
                int previous = first - 1;
                for (int i = 0; i < count; i++)
                {
                    int id = format == 5 ? input.U16() : first + i;
                    if (id <= previous || id < first || id > last)
                        throw new InvalidDataException("Sparse bitmap glyph IDs must be ordered within their range.");
                    previous = id;
                    Add(id, (ulong)imageOffset + (ulong)i * size, size, metrics);
                }
            }
            else if (format == 4)
            {
                uint count = input.U32();
                if (count > rangeCount)
                    throw new InvalidDataException("Invalid sparse bitmap glyph count.");
                input.Require((count + 1L) * 4);
                int id = input.U16();
                uint previousOffset = input.U16();
                int previousId = first - 1;
                for (int i = 0; i < count; i++)
                {
                    int nextId = input.U16();
                    uint nextOffset = input.U16();
                    if (id <= previousId || id < first || id > last || nextOffset < previousOffset)
                        throw new InvalidDataException("Invalid sparse bitmap glyph IDs or offsets.");
                    Add(id, (ulong)imageOffset + previousOffset, nextOffset - previousOffset);
                    previousId = id;
                    id = nextId; // the terminal pair's glyph ID is not a glyph
                    previousOffset = nextOffset;
                }
            }
            else
                throw new OpenFontNotSupportedException($"Unsupported bitmap index subtable format {format}.");
        }

        public Glyph[] BuildGlyphList()
        {
            // The public glyph array has one entry per glyph ID, not one per image.
            BitmapGlyphRecord[] records = Strikes.Length == 0 ? [] : Strikes[0];
            int count = records.Length == 0 ? 1 : records.Max(r => (int)r.GlyphId) + 1;
            var glyphs = new Glyph[count];
            for (int i = 0; i < count; i++) glyphs[i] = new Glyph((ushort)i, 0, 0, 0);
            foreach (var record in records)
                glyphs[record.GlyphId] = new Glyph(record.GlyphId, record.Offset, record.Length, record.ImageFormat);
            return glyphs;
        }
    }

    // All reads are restricted to the table (or index region), never adjacent SFNT data.
    sealed class BitmapTableData
    {
        readonly byte[] _data;
        internal long Position;
        internal long Limit;
        internal BitmapTableData(byte[] data) { _data = data; Limit = data.Length; }

        internal static byte[] Read(BinaryReader reader, uint length, int maximum)
        {
            if (length > maximum || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Bitmap table is truncated or exceeds its allocation limit.");
            byte[] data = reader.ReadBytes((int)length);
            if (data.Length != length) throw new InvalidDataException("Truncated bitmap table.");
            return data;
        }
        internal void Require(long size)
        {
            if (size < 0 || Position < 0 || Position > Limit || size > Limit - Position)
                throw new InvalidDataException("Truncated bitmap table or index subtable.");
        }
        internal byte U8() { Require(1); return _data[(int)Position++]; }
        internal ushort U16()
        {
            Require(2);
            ushort result = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan((int)Position, 2));
            Position += 2;
            return result;
        }
        internal uint U32()
        {
            Require(4);
            uint result = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan((int)Position, 4));
            Position += 4;
            return result;
        }
        internal byte[] Bytes(int count)
        {
            Require(count);
            byte[] result = _data.AsSpan((int)Position, count).ToArray();
            Position += count;
            return result;
        }
    }
}
