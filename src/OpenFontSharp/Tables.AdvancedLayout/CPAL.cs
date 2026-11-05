//Apache2, 2017-present Sam Hocevar <sam@hocevar.net>, WinterDev

namespace OpenFontSharp.Tables.AdvancedLayout
{
    /// <summary>
    /// Represents the OpenType color palette table.
    /// </summary>
    public class CPAL : TableEntry
    {
        /// <summary>The OpenType tag identifying this table.</summary>
        public const string _N = "CPAL";
        /// <inheritdoc />
        public override string Name => _N;
        //

        byte[] _colorBGRABuffer = Array.Empty<byte>();
        PaletteColor[] _colors = Array.Empty<PaletteColor>();
        ushort[] _paletteIndices = Array.Empty<ushort>();
        internal ushort NumPaletteEntries { get; private set; }
        internal int PaletteCount => _paletteIndices.Length;

        // Palette Table Header
        // Read the CPAL table
        // https://docs.microsoft.com/en-us/typography/opentype/spec/cpal
        /// <inheritdoc />
        protected override void ReadContentFrom(BinaryReader reader)
        {
            var bounds = new BoundedTableReader(reader, TableLength, Name);
            bounds.RequireRange(0, 12);

            //The CPAL table begins with a header that starts with a version number.
            //Currently, only versions 0 and 1 are defined.

            //CPAL version 0

            //The CPAL header version 0 is organized as follows:
            //CPAL version 0
            //Type 	    Name 	                            Description
            //uint16 	version 	                        Table version number (=0).
            //uint16 	numPaletteEntries 	                Number of palette entries in each palette.
            //uint16 	numPalettes 	                    Number of palettes in the table.
            //uint16 	numColorRecords 	                Total number of color records, combined for all palettes.
            //Offset32 	offsetFirstColorRecord 	            Offset from the beginning of CPAL table to the first ColorRecord.
            //uint16 	colorRecordIndices[numPalettes] 	Index of each palette’s first color record in the combined color record array.

            //CPAL version 1

            //The CPAL header version 1 adds three additional fields to the end of the table header and is organized as follows:
            //CPAL version 1
            //Type 	    Name 	                            Description
            //uint16 	version 	                        Table version number (=1).
            //uint16 	numPaletteEntries 	                Number of palette entries in each palette.
            //uint16 	numPalettes 	                    Number of palettes in the table.
            //uint16 	numColorRecords 	                Total number of color records, combined for all palettes.
            //Offset32 	offsetFirstColorRecord 	            Offset from the beginning of CPAL table to the first ColorRecord.
            //uint16 	colorRecordIndices[numPalettes] 	Index of each palette’s first color record in the combined color record array.
            //Offset32 	offsetPaletteTypeArray 	            Offset from the beginning of CPAL table to the Palette Type Array. Set to 0 if no array is provided.
            //Offset32 	offsetPaletteLabelArray 	        Offset from the beginning of CPAL table to the Palette Labels Array. Set to 0 if no array is provided.
            //Offset32 	offsetPaletteEntryLabelArray 	    Offset from the beginning of CPAL table to the Palette Entry Label Array. Set to 0 if no array is provided.

            ushort version = reader.ReadUInt16();
            if (version > 1)
                throw new OpenFontNotSupportedException($"CPAL version {version} is not supported; only versions 0 and 1 are supported.");
            NumPaletteEntries = reader.ReadUInt16();
            ushort numPalettes = reader.ReadUInt16();
            ColorCount = reader.ReadUInt16();           //numColorRecords
            uint offsetFirstColorRecord = reader.ReadUInt32();   //Offset from the beginning of CPAL table to the first ColorRecord.
            uint headerLength = (uint)(12 + 2 * numPalettes + (version == 1 ? 12 : 0));
            bounds.RequireRange(0, headerLength);
            bounds.RequireRange(offsetFirstColorRecord, (ulong)ColorCount * 4);
            if (NumPaletteEntries == 0 || numPalettes == 0 || ColorCount == 0 ||
                offsetFirstColorRecord < headerLength)
                throw new InvalidDataException("CPAL must contain palettes and colour records after its header.");
            // Counts are uint16; do not expand shared records into numPalettes * numPaletteEntries.
            Palettes = Utils.ReadUInt16Array(reader, numPalettes); //colorRecordIndices, Index of each palette’s first color record in the combined color record array.
            foreach (ushort first in Palettes)
                if (first + NumPaletteEntries > ColorCount)
                    throw new InvalidDataException("CPAL palette range exceeds the colour record array.");
            _paletteIndices = (ushort[])Palettes.Clone();

            if (version == 1)
            {
                uint types = reader.ReadUInt32();
                uint labels = reader.ReadUInt32();
                uint entryLabels = reader.ReadUInt32();
                if (types != 0) bounds.RequireRange(types, (ulong)numPalettes * 4);
                if (labels != 0) bounds.RequireRange(labels, (ulong)numPalettes * 2);
                if (entryLabels != 0) bounds.RequireRange(entryLabels, (ulong)NumPaletteEntries * 2);
            }

            //move to color records
            bounds.Seek(offsetFirstColorRecord, (ulong)ColorCount * 4);
            _colorBGRABuffer = reader.ReadBytes(4 * ColorCount);
            if (_colorBGRABuffer.Length != 4 * ColorCount)
                throw new InvalidDataException("CPAL colour records are truncated.");
            _colors = new PaletteColor[ColorCount];
            for (int i = 0; i < _colors.Length; i++)
                _colors[i] = new PaletteColor(_colorBGRABuffer[4 * i], _colorBGRABuffer[4 * i + 1],
                    _colorBGRABuffer[4 * i + 2], _colorBGRABuffer[4 * i + 3]);
        }

        internal IReadOnlyList<PaletteColor> GetPalette(int index)
        {
            if ((uint)index >= (uint)_paletteIndices.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new System.Collections.ObjectModel.ReadOnlyCollection<PaletteColor>(
                new ArraySegment<PaletteColor>(_colors, _paletteIndices[index], NumPaletteEntries));
        }

        /// <summary>Gets the starting color-record index for each palette.</summary>
        public ushort[] Palettes { get; private set; } = Array.Empty<ushort>();
        /// <summary>Gets the number of color records in the table.</summary>
        public ushort ColorCount { get; private set; }

        /// <summary>Reads one color from the palette and returns its RGBA components.</summary>
        /// <param name="colorIndex">The color-record index.</param>
        /// <param name="r">Receives the red component.</param>
        /// <param name="g">Receives the green component.</param>
        /// <param name="b">Receives the blue component.</param>
        /// <param name="a">Receives the alpha component.</param>
        /// <exception cref="ArgumentOutOfRangeException">The colour index is outside the loaded records.</exception>
        public void GetColor(int colorIndex, out byte r, out byte g, out byte b, out byte a)
        {
            if ((uint)colorIndex >= ColorCount)
                throw new ArgumentOutOfRangeException(nameof(colorIndex));
            //Each color record has BGRA values. The color space for these values is sRGB.
            //Type    Name    Description
            //uint8   blue    Blue value(B0).
            //uint8   green   Green value(B1).
            //uint8   red     Red value(B2).
            //uint8   alpha   Alpha value(B3).

            byte[] colorBGRABuffer = _colorBGRABuffer;
            int startAt = colorIndex * 4;//bgra
            b = colorBGRABuffer[startAt];
            g = colorBGRABuffer[startAt + 1];
            r = colorBGRABuffer[startAt + 2];
            a = colorBGRABuffer[startAt + 3];
        }
    }
}
