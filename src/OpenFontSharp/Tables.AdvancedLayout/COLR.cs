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

        /// <summary>Gets the loaded COLR version. Version 1 paint graphs are not interpreted.</summary>
        public ushort Version { get; private set; }

        readonly Dictionary<ushort, IReadOnlyList<ColorLayer>> _layers = new();
        ColorLayer[] _records = Array.Empty<ColorLayer>();

        // Read the COLR table
        // https://docs.microsoft.com/en-us/typography/opentype/spec/colr
        /// <inheritdoc />
        protected override void ReadContentFrom(BinaryReader reader)
        {
            var bounds = new BoundedTableReader(reader, TableLength, Name);
            bounds.RequireRange(0, 14);

            //Type      Name                    Description
            //uint16    version                 Table version number(starts at 0).
            //uint16    numBaseGlyphRecords     Number of Base Glyph Records.
            //Offset32  baseGlyphRecordsOffset  Offset(from beginning of COLR table) to Base Glyph records.
            //Offset32  layerRecordsOffset      Offset(from beginning of COLR table) to Layer Records.
            //uint16    numLayerRecords         Number of Layer Records.


            Version = reader.ReadUInt16();
            if (Version > 1)
                throw new OpenFontNotSupportedException($"COLR version {Version} is not supported; only versions 0 and 1 are supported.");
            uint headerLength = Version == 1 ? 34u : 14u;
            bounds.RequireRange(0, headerLength);
            ushort numBaseGlyphRecords = reader.ReadUInt16();
            uint baseGlyphRecordsOffset = reader.ReadUInt32();
            uint layerRecordsOffset = reader.ReadUInt32();
            ushort numLayerRecords = reader.ReadUInt16();

            if (Version == 1)
            {
                // Only validate the extension references; never traverse unsupported paint graphs.
                uint[] minimumLengths = { 4, 4, 5, 4, 8 };
                foreach (uint minimumLength in minimumLengths)
                {
                    uint offset = reader.ReadUInt32();
                    if (offset == 0) continue;
                    bounds.RequireRange(offset, minimumLength);
                    if (offset < headerLength)
                        throw new InvalidDataException("COLR version 1 data overlaps the header.");
                }
            }

            bounds.RequireRange(baseGlyphRecordsOffset, (ulong)numBaseGlyphRecords * 6);
            bounds.RequireRange(layerRecordsOffset, (ulong)numLayerRecords * 4);
            if ((numBaseGlyphRecords != 0 && baseGlyphRecordsOffset < headerLength) ||
                (numLayerRecords != 0 && layerRecordsOffset < headerLength))
                throw new InvalidDataException("COLR records overlap the header.");

            // All record counts are uint16, capping each allocation at 65,535 entries.
            GlyphLayers = new ushort[numLayerRecords];
            GlyphPalettes = new ushort[numLayerRecords];
            _records = new ColorLayer[numLayerRecords];
            _layers.Clear();
            LayerIndices.Clear();
            LayerCounts.Clear();

            bounds.Seek(baseGlyphRecordsOffset, (ulong)numBaseGlyphRecords * 6);
            int previousGlyph = -1;
            for (int i = 0; i < numBaseGlyphRecords; ++i)
            {
                ushort gid = reader.ReadUInt16();
                ushort first = reader.ReadUInt16();
                ushort count = reader.ReadUInt16();
                if (gid <= previousGlyph || first + count > numLayerRecords)
                    throw new InvalidDataException("COLR base glyphs must be sorted and reference valid layer ranges.");
                previousGlyph = gid;
                LayerIndices[gid] = first;
                LayerCounts[gid] = count;
                _layers.Add(gid, new System.Collections.ObjectModel.ReadOnlyCollection<ColorLayer>(
                    new ArraySegment<ColorLayer>(_records, first, count)));
            }

            bounds.Seek(layerRecordsOffset, (ulong)numLayerRecords * 4);
            for (int i = 0; i < GlyphLayers.Length; ++i)
            {
                GlyphLayers[i] = reader.ReadUInt16();
                GlyphPalettes[i] = reader.ReadUInt16();
                _records[i] = new ColorLayer(GlyphLayers[i], GlyphPalettes[i]);
            }
        }

        internal IReadOnlyList<ColorLayer> GetLayers(ushort glyphId) =>
            _layers.TryGetValue(glyphId, out var layers) ? layers : Array.Empty<ColorLayer>();

        internal void Validate(int glyphCount, CPAL? palette)
        {
            foreach (ushort glyphId in _layers.Keys)
                if (glyphId >= glyphCount)
                    throw new InvalidDataException("COLR base glyph ID exceeds maxp.numGlyphs.");
            foreach (ColorLayer layer in _records)
            {
                if (layer.GlyphId >= glyphCount)
                    throw new InvalidDataException("COLR layer glyph ID exceeds maxp.numGlyphs.");
                if (layer.PaletteIndex != 0xffff &&
                    (palette == null || layer.PaletteIndex >= palette.NumPaletteEntries))
                    throw new InvalidDataException("COLR layer palette index exceeds the CPAL palette.");
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
