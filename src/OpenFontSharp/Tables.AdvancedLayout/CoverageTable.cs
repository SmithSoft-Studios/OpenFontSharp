//Apache2, 2016-present, WinterDev

namespace OpenFontSharp.Tables.AdvancedLayout
{
    //https://docs.microsoft.com/en-us/typography/opentype/spec/chapter2

    /// <summary>
    /// Maps glyph identifiers to their zero-based coverage indices.
    /// </summary>
    public abstract class CoverageTable
    {
        /// <summary>
        /// Finds the coverage index of a glyph.
        /// </summary>
        /// <param name="glyphIndex">The glyph identifier to find.</param>
        /// <returns>The zero-based coverage index, or -1 when the glyph is not covered.</returns>
        public abstract int FindPosition(ushort glyphIndex);

        /// <summary>
        /// Enumerates the glyph identifiers represented by this coverage table.
        /// </summary>
        /// <returns>Covered glyph identifiers in table order.</returns>
        public abstract IEnumerable<ushort> GetExpandedValueIter();

#if DEBUG

#endif
        /// <summary>
        /// Reads a coverage table in either supported OpenType format.
        /// </summary>
        /// <param name="reader">The reader for the OpenType data.</param>
        /// <param name="beginAt">The offset of the coverage table.</param>
        /// <returns>The parsed coverage table.</returns>
        /// <exception cref="OpenFontNotSupportedException">The table uses an unsupported format.</exception>
        public static CoverageTable CreateFrom(BinaryReader reader, long beginAt)
        {
            reader.BaseStream.Seek(beginAt, SeekOrigin.Begin);
            ushort format = reader.ReadUInt16();
            switch (format)
            {
                default: throw new OpenFontNotSupportedException();
                case 1: return CoverageFmt1.CreateFrom(reader);
                case 2: return CoverageFmt2.CreateFrom(reader);
            }
        }
        /// <summary>
        /// Reads coverage tables at offsets relative to a common base position.
        /// </summary>
        /// <param name="initPos">The base offset for each table.</param>
        /// <param name="offsets">Offsets of the coverage tables.</param>
        /// <param name="reader">The reader for the OpenType data.</param>
        /// <returns>The parsed coverage tables in offset order.</returns>
        public static CoverageTable[] CreateMultipleCoverageTables(long initPos, ushort[] offsets, BinaryReader reader)
        {
            CoverageTable[] results = new CoverageTable[offsets.Length];
            for (int i = 0; i < results.Length; ++i)
            {
                results[i] = CoverageTable.CreateFrom(reader, initPos + offsets[i]);
            }
            return results;
        }
    }

    /// <summary>
    /// Coverage format 1, which stores an ordered list of glyph identifiers.
    /// </summary>
    public class CoverageFmt1 : CoverageTable
    {
        /// <summary>
        /// Reads a format 1 coverage table from the current stream position.
        /// </summary>
        /// <param name="reader">The reader positioned after the format field.</param>
        /// <returns>The parsed format 1 coverage table.</returns>
        public static CoverageFmt1 CreateFrom(BinaryReader reader)
        {
            // CoverageFormat1 table: Individual glyph indices
            // Type      Name                     Description
            // uint16    CoverageFormat           Format identifier-format = 1
            // uint16    GlyphCount               Number of glyphs in the GlyphArray
            // uint16    GlyphArray[GlyphCount]   Array of glyph IDs — in numerical order

            ushort glyphCount = reader.ReadUInt16();
            ushort[] glyphs = Utils.ReadUInt16Array(reader, glyphCount);
            return new CoverageFmt1() { _orderedGlyphIdList = glyphs };
        }

        /// <inheritdoc />
        public override int FindPosition(ushort glyphIndex)
        {
            // "The glyph indices must be in numerical order for binary searching of the list"
            // (https://www.microsoft.com/typography/otspec/chapter2.htm#coverageFormat1)
            int n = Array.BinarySearch(_orderedGlyphIdList, glyphIndex);
            return n < 0 ? -1 : n;
        }
        /// <inheritdoc />
        public override IEnumerable<ushort> GetExpandedValueIter() { return _orderedGlyphIdList; }

#if DEBUG

        public override string ToString()
        {
            List<string> stringList = new List<string>();
            foreach (ushort g in _orderedGlyphIdList)
            {
                stringList.Add(g.ToString());
            }
            return "CoverageFmt1: " + string.Join(",", stringList.ToArray());
        }
#endif

        internal ushort[] _orderedGlyphIdList = Array.Empty<ushort>();
    }

    /// <summary>
    /// Coverage format 2, which stores ordered glyph ranges and their coverage indices.
    /// </summary>
    public class CoverageFmt2 : CoverageTable
    {
        /// <inheritdoc />
        public override int FindPosition(ushort glyphIndex)
        {
            // Ranges must be in glyph ID order, and they must be distinct, with no overlapping.
            // [...] quick calculation of the Coverage Index for any glyph in any range using the
            // formula: Coverage Index (glyphID) = startCoverageIndex + glyphID - startGlyphID.
            // (https://www.microsoft.com/typography/otspec/chapter2.htm#coverageFormat2)
            int n = Array.BinarySearch(_endIndices, glyphIndex);
            n = n < 0 ? ~n : n;
            if (n >= RangeCount || glyphIndex < _startIndices[n])
            {
                return -1;
            }
            return _coverageIndices[n] + glyphIndex - _startIndices[n];
        }

        /// <inheritdoc />
        public override IEnumerable<ushort> GetExpandedValueIter()
        {
            for (int i = 0; i < RangeCount; ++i)
            {
                for (ushort n = _startIndices[i]; n <= _endIndices[i]; ++n)
                {
                    yield return n;
                }
            }
        }
        /// <summary>
        /// Reads a format 2 coverage table from the current stream position.
        /// </summary>
        /// <param name="reader">The reader positioned after the format field.</param>
        /// <returns>The parsed format 2 coverage table.</returns>
        public static CoverageFmt2 CreateFrom(BinaryReader reader)
        {
            // CoverageFormat2 table: Range of glyphs
            // Type      Name                     Description
            // uint16    CoverageFormat           Format identifier-format = 2
            // uint16    RangeCount               Number of RangeRecords
            // struct    RangeRecord[RangeCount]  Array of glyph ranges — ordered by StartGlyphID.
            //
            // RangeRecord
            // Type      Name                Description
            // uint16    StartGlyphID        First glyph ID in the range
            // uint16    EndGlyphID          Last glyph ID in the range
            // uint16    StartCoverageIndex  Coverage Index of first glyph ID in range

            ushort rangeCount = reader.ReadUInt16();
            ushort[] startIndices = new ushort[rangeCount];
            ushort[] endIndices = new ushort[rangeCount];
            ushort[] coverageIndices = new ushort[rangeCount];
            for (int i = 0; i < rangeCount; ++i)
            {
                startIndices[i] = reader.ReadUInt16();
                endIndices[i] = reader.ReadUInt16();
                coverageIndices[i] = reader.ReadUInt16();
            }

            return new CoverageFmt2()
            {
                _startIndices = startIndices,
                _endIndices = endIndices,
                _coverageIndices = coverageIndices
            };
        }

#if DEBUG

        public override string ToString()
        {
            List<string> stringList = new List<string>();
            for (int i = 0; i < RangeCount; ++i)
            {
                stringList.Add(string.Format("{0}-{1}", _startIndices[i], _endIndices[i]));
            }
            return "CoverageFmt2: " + string.Join(",", stringList.ToArray());
        }
#endif

        internal ushort[] _startIndices = Array.Empty<ushort>();
        internal ushort[] _endIndices = Array.Empty<ushort>();
        internal ushort[] _coverageIndices = Array.Empty<ushort>();

        private int RangeCount => _startIndices.Length;
    }

}
