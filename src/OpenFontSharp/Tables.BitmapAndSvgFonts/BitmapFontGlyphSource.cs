//MIT, 2019-present, WinterDev

namespace OpenFontSharp.Tables.BitmapAndSvgFonts
{
    class BitmapFontGlyphSource
    {
        readonly BitmapLocationTable _locations;
        CBDT? _loadedCbdt;
        CBDT Data => _loadedCbdt ?? throw new InvalidDataException("Bitmap data is not loaded.");
        internal bool IsMonochrome => _locations is EBLC;

        public BitmapFontGlyphSource(CBLC cblc) => _locations = cblc;
        internal BitmapFontGlyphSource(EBLC eblc) => _locations = eblc;
        public void LoadCBDT(CBDT cbdt) => _loadedCbdt = cbdt;

        public void UnloadCBDT()
        {
            _loadedCbdt?.RemoveOldMemoryStreamAndReaders();
            _loadedCbdt = null;
        }

        public void CopyBitmapContent(Glyph glyph, Stream outputStream) => Data.CopyBitmapContent(glyph, outputStream);

        public Glyph[] BuildGlyphList() => BuildGlyphListCore(null);

        internal Glyph[] BuildGlyphList(int glyphCount)
        {
            if (glyphCount <= 0 || glyphCount > ushort.MaxValue)
                throw new InvalidDataException("Bitmap maxp.numGlyphs must be between 1 and 65535.");
            return BuildGlyphListCore(glyphCount);
        }

        Glyph[] BuildGlyphListCore(int? glyphCount)
        {
            Glyph[] glyphs = _locations.BuildGlyphList();
            if (glyphCount.HasValue)
            {
                if (_locations.HighestGlyphId >= glyphCount.Value)
                    throw new InvalidDataException("Bitmap glyph ID exceeds maxp.numGlyphs.");
                int oldLength = glyphs.Length;
                Array.Resize(ref glyphs, glyphCount.Value);
                for (int i = oldLength; i < glyphs.Length; i++)
                    glyphs[i] = new Glyph((ushort)i, 0, 0, 0);
            }
            for (int strike = 0; strike < _locations.Strikes.Length; strike++)
            {
                foreach (var record in _locations.Strikes[strike])
                {
                    if (glyphCount.HasValue && record.GlyphId >= glyphCount.Value)
                        throw new InvalidDataException("Bitmap glyph ID exceeds maxp.numGlyphs.");
                    var glyph = strike == 0 ? glyphs[record.GlyphId] :
                        new Glyph(record.GlyphId, record.Offset, record.Length, record.ImageFormat);
                    Data.FillGlyphInfo(glyph, record, retainImage: strike == 0);
                }
            }
            return glyphs;
        }
    }
}
