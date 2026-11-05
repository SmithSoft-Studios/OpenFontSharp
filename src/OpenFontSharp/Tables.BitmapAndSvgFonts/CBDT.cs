//MIT, 2019-present, WinterDev

namespace OpenFontSharp.Tables.BitmapAndSvgFonts
{
    class CBDT : TableEntry, IDisposable
    {
        public const string _N = "CBDT";
        public override string Name => _N;
        protected virtual ushort MajorVersion => 3;
        byte[] _data = [];
        readonly Dictionary<ushort, (int Offset, int Length)> _images = new();

        public void Dispose() => RemoveOldMemoryStreamAndReaders();
        public void RemoveOldMemoryStreamAndReaders()
        {
            _data = [];
            _images.Clear();
        }

        protected override void ReadContentFrom(BinaryReader reader)
        {
            RemoveOldMemoryStreamAndReaders();
            _data = BitmapTableData.Read(reader, Header.Length, 256 * 1024 * 1024);
            var input = new BitmapTableData(_data);
            if (input.U16() != MajorVersion || input.U16() != 0)
                throw new OpenFontNotSupportedException($"Unsupported {Name} version.");
        }

        internal void FillGlyphInfo(Glyph glyph, BitmapGlyphRecord record, bool retainImage = true)
        {
            var input = new BitmapTableData(_data) { Position = record.Offset };
            if ((ulong)record.Offset + record.Length > (ulong)_data.Length)
                throw new InvalidDataException("Bitmap glyph image is outside its data table.");
            input.Limit = (long)record.Offset + record.Length;
            byte[] metrics;
            switch (record.ImageFormat)
            {
                case 1:
                case 2:
                case 17:
                    metrics = input.Bytes(5);
                    break;
                case 6:
                case 7:
                case 18:
                    metrics = input.Bytes(8);
                    break;
                case 5:
                case 19:
                    metrics = record.SharedMetrics ??
                        throw new InvalidDataException("Bitmap image format requires shared index metrics.");
                    break;
                default:
                    // Composite EBDT formats 8/9 and reserved formats are not rendered.
                    throw new OpenFontNotSupportedException($"Unsupported bitmap image format {record.ImageFormat}.");
            }
            if (record.ImageFormat >= 17 && MajorVersion != 3)
                throw new OpenFontNotSupportedException($"Bitmap image format {record.ImageFormat} requires CBDT.");
            int height = metrics[0], width = metrics[1];
            int bearingX = (sbyte)metrics[2], bearingY = (sbyte)metrics[3];
            glyph.BitmapGlyphAdvanceWidth = metrics[4];
            glyph.Bounds = new Bounds((short)bearingX, (short)(bearingY - height),
                (short)(bearingX + width), (short)bearingY);
            long length;
            if (record.ImageFormat >= 17)
                length = input.U32();
            else if (record.ImageFormat is 1 or 6)
                length = ((width * record.BitDepth + 7) / 8) * height;
            else
                length = (width * height * record.BitDepth + 7) / 8;
            input.Require(length);
            if (retainImage) _images[glyph.GlyphIndex] = ((int)input.Position, (int)length);
        }

        public void FillGlyphInfo(Glyph glyph)
        {
            if (!_images.ContainsKey(glyph.GlyphIndex))
                throw new InvalidDataException("Bitmap glyph metrics require the location table record.");
        }

        public void CopyBitmapContent(Glyph glyph, Stream outputStream)
        {
            if (_images.TryGetValue(glyph.GlyphIndex, out var image))
                outputStream.Write(_data, image.Offset, image.Length);
            else if (glyph.BitmapFormat != 0)
                throw new InvalidDataException("Bitmap image data has not been validated.");
        }
    }
}
