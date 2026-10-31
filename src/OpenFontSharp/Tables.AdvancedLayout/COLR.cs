//Apache2, 2017-present Sam Hocevar <sam@hocevar.net>, WinterDev

namespace OpenFontSharp.Tables.AdvancedLayout
{
    /// <summary>
    /// Represents the OpenType color-layer table.
    /// </summary>
    public class COLR : TableEntry
    {
        /// <summary>The OpenType tag identifying this table.</summary>
        public const string _N = "COLR";
        /// <inheritdoc />
        public override string Name => _N;

        // Read the COLR table
        // https://docs.microsoft.com/en-us/typography/opentype/spec/colr
        /// <inheritdoc />
        protected override void ReadContentFrom(BinaryReader reader)
        {
            long offset = reader.BaseStream.Position;

            //Type      Name                    Description
            //uint16    version                 Table version number(starts at 0).
            //uint16    numBaseGlyphRecords     Number of Base Glyph Records.
            //Offset32  baseGlyphRecordsOffset  Offset(from beginning of COLR table) to Base Glyph records.
            //Offset32  layerRecordsOffset      Offset(from beginning of COLR table) to Layer Records.
            //uint16    numLayerRecords         Number of Layer Records.


            ushort version = reader.ReadUInt16();
            ushort numBaseGlyphRecords = reader.ReadUInt16();
            uint baseGlyphRecordsOffset = reader.ReadUInt32();
            uint layerRecordsOffset = reader.ReadUInt32();
            ushort numLayerRecords = reader.ReadUInt16();

            GlyphLayers = new ushort[numLayerRecords];
            GlyphPalettes = new ushort[numLayerRecords];

            reader.BaseStream.Seek(offset + baseGlyphRecordsOffset, SeekOrigin.Begin);
            for (int i = 0; i < numBaseGlyphRecords; ++i)
            {
                ushort gid = reader.ReadUInt16();
                LayerIndices[gid] = reader.ReadUInt16();
                LayerCounts[gid] = reader.ReadUInt16();
            }

            reader.BaseStream.Seek(offset + layerRecordsOffset, SeekOrigin.Begin);
            for (int i = 0; i < GlyphLayers.Length; ++i)
            {
                GlyphLayers[i] = reader.ReadUInt16();
                GlyphPalettes[i] = reader.ReadUInt16();
            }
        }

        /// <summary>Gets the glyph identifiers used as color layers.</summary>
        public ushort[] GlyphLayers { get; private set; } = Array.Empty<ushort>();
        /// <summary>Gets the palette entry indices corresponding to <see cref="GlyphLayers"/>.</summary>
        public ushort[] GlyphPalettes { get; private set; } = Array.Empty<ushort>();
        /// <summary>Maps base glyph identifiers to the first layer index.</summary>
        public readonly Dictionary<ushort, ushort> LayerIndices = new Dictionary<ushort, ushort>();
        /// <summary>Maps base glyph identifiers to their number of layers.</summary>
        public readonly Dictionary<ushort, ushort> LayerCounts = new Dictionary<ushort, ushort>();
    }
}
