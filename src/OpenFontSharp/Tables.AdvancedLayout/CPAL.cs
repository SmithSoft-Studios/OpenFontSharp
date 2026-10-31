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

        // Palette Table Header
        // Read the CPAL table
        // https://docs.microsoft.com/en-us/typography/opentype/spec/cpal
        /// <inheritdoc />
        protected override void ReadContentFrom(BinaryReader reader)
        {
            long beginAt = reader.BaseStream.Position;

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
            ushort numPaletteEntries = reader.ReadUInt16(); // XXX: unused?
            ushort numPalettes = reader.ReadUInt16();
            ColorCount = reader.ReadUInt16();           //numColorRecords
            uint offsetFirstColorRecord = reader.ReadUInt32();   //Offset from the beginning of CPAL table to the first ColorRecord.
            Palettes = Utils.ReadUInt16Array(reader, numPalettes); //colorRecordIndices, Index of each palette’s first color record in the combined color record array.

#if DEBUG
            if (version == 1)
            {
                //Offset32 	offsetPaletteTypeArray 	            Offset from the beginning of CPAL table to the Palette Type Array. Set to 0 if no array is provided.
                //Offset32 	offsetPaletteLabelArray 	        Offset from the beginning of CPAL table to the Palette Labels Array. Set to 0 if no array is provided.
                //Offset32 	offsetPaletteEntryLabelArray 	    Offset from the beginning of CPAL table to the Palette Entry Label Array. Set to 0 if no array is provided.
            }
#endif

            //move to color records
            reader.BaseStream.Seek(beginAt + offsetFirstColorRecord, SeekOrigin.Begin);
            _colorBGRABuffer = reader.ReadBytes(4 * ColorCount);
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
        public void GetColor(int colorIndex, out byte r, out byte g, out byte b, out byte a)
        {
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
