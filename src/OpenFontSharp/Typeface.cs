//Apache2, 2017-present, WinterDev
//Apache2, 2014-2016, Samuel Carlsson, WinterDev
using System;
using System.Collections.Generic;
using OpenFontSharp.AdditionalInfo;
using OpenFontSharp.Tables;
using OpenFontSharp.Tables.AdvancedLayout;
using OpenFontSharp.Tables.BitmapAndSvgFonts;
using OpenFontSharp.Tables.CFF;
using OpenFontSharp.Tables.Others;
using OpenFontSharp.Tables.TrueType;

namespace OpenFontSharp
{


    /// <summary>A parsed font with character mapping, glyphs, metrics, and optional OpenType tables.</summary>
    public partial class Typeface
    {

        //TODO: implement vertical metrics
        HorizontalMetrics? _loadedHMetrics;
        NameEntry? _loadedNameEntry;
        Glyph[]? _loadedGlyphs;
        TableHeader[]? _loadedHeaders;
        HorizontalMetrics _hMetrics
        {
            get => _loadedHMetrics ?? throw new NullReferenceException("Horizontal metrics have not been loaded.");
            set => _loadedHMetrics = value;
        }
        NameEntry _nameEntry
        {
            get => _loadedNameEntry ?? throw new NullReferenceException("The name table has not been loaded.");
            set => _loadedNameEntry = value;
        }
        Glyph[] _glyphs
        {
            get => _loadedGlyphs ?? throw new NullReferenceException("Glyphs have not been loaded.");
            set => _loadedGlyphs = value;
        }
        TableHeader[] _tblHeaders
        {
            get => _loadedHeaders ?? throw new NullReferenceException("Table headers have not been loaded.");
            set => _loadedHeaders = value;
        }
        Cff1FontSet? _cff1FontSet;
        bool _hasTtfOutline;
        bool _hasCffData;
        internal bool _useTypographicMertic;
#if DEBUG 
        static int s_dbugTotalId;
        public readonly int dbugId = ++s_dbugTotalId;
#endif

        internal Typeface()
        {
            //blank typefaces 
#if DEBUG
            if (dbugId == 5)
            {

            }
#endif
        }

        internal void SetTableEntryCollection(TableHeader[] headers) => _tblHeaders = headers;

        internal void SetBasicTypefaceTables(OS2Table os2Table,
             NameEntry? nameEntry,
             Head head,
             HorizontalMetrics? horizontalMetrics)
        {
            OS2Table = os2Table;
            _loadedNameEntry = nameEntry;
            Head = head;
            Bounds = head.Bounds;
            UnitsPerEm = head.UnitsPerEm;
            _loadedHMetrics = horizontalMetrics;
        }

        Head? _head;
        internal Head Head
        {
            get => _head ?? throw new NullReferenceException("The head table has not been loaded.");
            set => _head = value;
        }

        internal void SetTtfGlyphs(Glyph[] glyphs)
        {
            _glyphs = glyphs;
            _hasTtfOutline = true;
        }
        internal void SetBitmapGlyphs(Glyph[] glyphs, BitmapFontGlyphSource bitmapFontGlyphSource)
        {
            _glyphs = glyphs;
            _bitmapFontGlyphSource = bitmapFontGlyphSource;
        }
        internal void SetCffFontSet(Cff1FontSet cff1FontSet)
        {
            _cff1FontSet = cff1FontSet;
            _hasCffData = true;

            Glyph[]? exisitingGlyphs = _loadedGlyphs;

            _glyphs = cff1FontSet._fonts[0]._glyphs; //TODO: review _fonts[0]

            if (exisitingGlyphs != null)
            {
                //
#if DEBUG
                if (_glyphs.Length != exisitingGlyphs.Length)
                {
                    throw new OpenFontNotSupportedException();
                }
#endif
                for (int i = 0; i < exisitingGlyphs.Length; ++i)
                {
                    Glyph.CopyExistingGlyphInfo(exisitingGlyphs[i], _glyphs[i]);
                }
            }

        }

        /// <summary>Gets declared language coverage and available layout script lists.</summary>
        public Languages Languages { get; } = new Languages();
        /// <summary>
        /// control values in Font unit
        /// </summary>
        internal int[]? ControlValues { get; set; }
        internal byte[]? PrepProgramBuffer { get; set; }
        internal byte[]? FpgmProgramBuffer { get; set; }

        MaxProfile? _maxProfile;
        Cmap? _cmapTable;
        HorizontalHeader? _hheaTable;
        OS2Table? _os2Table;
        internal MaxProfile MaxProfile
        {
            get => _maxProfile ?? throw new NullReferenceException("The maxp table has not been loaded.");
            set => _maxProfile = value;
        }
        internal Cmap CmapTable
        {
            get => _cmapTable ?? throw new NullReferenceException("The cmap table has not been loaded.");
            set => _cmapTable = value;
        }
        internal Kern? KernTable { get; set; }
        internal Gasp? GaspTable { get; set; }
        internal HorizontalHeader HheaTable
        {
            get => _hheaTable ?? throw new NullReferenceException("The hhea table has not been loaded.");
            set => _hheaTable = value;
        }
        /// <summary>Gets or sets the loaded OS/2 metrics and classification table.</summary>
        /// <exception cref="NullReferenceException">The table has not been loaded.</exception>
        public OS2Table OS2Table
        {
            get => _os2Table ?? throw new NullReferenceException("The OS/2 table has not been loaded.");
            set => _os2Table = value;
        }
        //
        /// <summary>Gets whether a TrueType pre-program is currently loaded.</summary>
        public bool HasPrepProgramBuffer => PrepProgramBuffer != null;


        /// <summary>
        /// actual font filename (optional)
        /// </summary>
        public string? Filename { get; set; }
        /// <summary>
        /// OS2 sTypoAscender/HheaTable.Ascent, in font designed unit
        /// </summary>         
        public short Ascender => _useTypographicMertic ? OS2Table.sTypoAscender : HheaTable.Ascent;

        /// <summary>
        /// OS2 sTypoDescender, in font designed unit
        /// </summary>
        public short Descender => _useTypographicMertic ? OS2Table.sTypoDescender : HheaTable.Descent;
        /// <summary>
        /// OS2 usWinAscender
        /// </summary>
        public ushort ClipedAscender => OS2Table.usWinAscent;
        /// <summary>
        /// OS2 usWinDescender
        /// </summary>
        public ushort ClipedDescender => OS2Table.usWinDescent;

        /// <summary>
        /// OS2 Linegap
        /// </summary>
        public short LineGap => _useTypographicMertic ? OS2Table.sTypoLineGap : HheaTable.LineGap;
        //The typographic line gap for this font.
        //Remember that this is not the same as the LineGap value in the 'hhea' table, 
        //which Apple defines in a far different manner.
        //The suggested usage for sTypoLineGap is 
        //that it be used in conjunction with unitsPerEm 
        //to compute a typographically correct default line spacing.
        //
        //Typical values average 7 - 10 % of units per em.
        //The goal is to free applications from Macintosh or Windows - specific metrics
        //which are constrained by backward compatability requirements
        //(see chapter, “Recommendations for OpenType Fonts”).
        //These new metrics, when combined with the character design widths,
        //will allow applications to lay out documents in a typographically correct and portable fashion. 
        //These metrics will be exposed through Windows APIs.
        //Macintosh applications will need to access the 'sfnt' resource and 
        //parse it to extract this data from the “OS / 2” table
        //(unless Apple exposes the 'OS/2' table through a new API)
        //---------------

        /// <summary>Gets the family name, or null when the name record is absent.</summary>
        public string? Name => _nameEntry.FontName;
        /// <summary>Gets the style name, or null when the name record is absent.</summary>
        public string? FontSubFamily => _nameEntry.FontSubFamily;
        /// <summary>Gets the PostScript name, or null when the name record is absent.</summary>
        public string? PostScriptName => _nameEntry.PostScriptName;
        /// <summary>Gets the version string, or null when the name record is absent.</summary>
        public string? VersionString => _nameEntry.VersionString;
        /// <summary>Gets the unique font identifier, or null when the name record is absent.</summary>
        public string? UniqueFontIden => _nameEntry.UniqueFontIden;

        internal NameEntry NameEntry => _nameEntry;

        /// <summary>Gets the number of glyphs, including the missing-glyph entry at index zero.</summary>
        public int GlyphCount => _glyphs.Length;
        /// <summary>
        /// find glyph index by codepoint
        /// </summary>
        /// <param name="codepoint">The Unicode scalar value to map.</param>
        /// <param name="nextCodepoint">The following scalar value, or zero when absent.</param>
        /// <param name="skipNextCodepoint">True when a variation-selector sequence consumes the following scalar.</param>
        /// <returns>The glyph ID, or zero when no mapping exists.</returns>

        public ushort GetGlyphIndex(int codepoint, int nextCodepoint, out bool skipNextCodepoint)
        {
            return CmapTable.GetGlyphIndex(codepoint, nextCodepoint, out skipNextCodepoint);
        }
        /// <summary>Maps a Unicode scalar value to a glyph.</summary>
        /// <param name="codepoint">The Unicode scalar value.</param>
        /// <returns>The glyph ID, or zero when no mapping exists.</returns>
        public ushort GetGlyphIndex(int codepoint)
        {
            return CmapTable.GetGlyphIndex(codepoint, 0, out bool skipNextCodepoint);
        }
        /// <summary>Appends character codes from the font's character-map subtables.</summary>
        /// <param name="unicodes">The destination list; existing entries are retained.</param>
        public void CollectUnicode(List<uint> unicodes)
        {
            CmapTable.CollectUnicode(unicodes);
        }
        /// <summary>Appends character codes and corresponding glyph IDs for a platform's subtables.</summary>
        /// <param name="platform">The OpenType platform ID.</param>
        /// <param name="unicodes">The destination character-code list.</param>
        /// <param name="glyphIndexList">The parallel destination glyph-ID list.</param>
        public void CollectUnicode(int platform, List<uint> unicodes, List<ushort> glyphIndexList)
        {
            CmapTable.CollectUnicode(platform, unicodes, glyphIndexList);
        }
        /// <summary>Looks up a glyph by its PostScript name.</summary>
        /// <param name="glyphName">The name to look up; null selects the missing glyph.</param>
        /// <returns>The matching glyph, or glyph zero when not found.</returns>
        public Glyph GetGlyphByName(string? glyphName) => GetGlyph(GetGlyphIndexByName(glyphName));

        Dictionary<string, ushort>? _cachedGlyphDicByName;

        void UpdateCff1FontSetNamesCache()
        {
            if (_cff1FontSet != null && _cachedGlyphDicByName == null)
            {
                //create cache data
                _cachedGlyphDicByName = new Dictionary<string, ushort>();
                for (int i = 1; i < _glyphs.Length; ++i)
                {
                    Glyph glyph = _glyphs[i];

                    if (glyph._cff1GlyphData != null && glyph._cff1GlyphData.Name != null)
                    {
                        _cachedGlyphDicByName.Add(glyph._cff1GlyphData.Name, (ushort)i);
                    }
                    else
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine("Cff unknown glyphname");
#endif
                    }
                }
            }
        }
        /// <summary>Looks up a glyph ID using CFF names, post names, or the Adobe glyph list.</summary>
        /// <param name="glyphName">The PostScript glyph name, or null.</param>
        /// <returns>The glyph ID, or zero when no mapping exists.</returns>
        public ushort GetGlyphIndexByName(string? glyphName)
        {
            if (glyphName == null) return 0;

            if (_cff1FontSet != null && _cachedGlyphDicByName == null)
            {
                //we create a dictionary 
                //create cache data
                _cachedGlyphDicByName = new Dictionary<string, ushort>();
                for (int i = 1; i < _glyphs.Length; ++i)
                {
                    Glyph glyph = _glyphs[i];
                    if (glyph._cff1GlyphData?.Name is string name)
                    {
                        _cachedGlyphDicByName.Add(name, (ushort)i);
                    }
                    else
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine("Cff unknown glyphname");
#endif
                    }
                }
                return _cachedGlyphDicByName.TryGetValue(glyphName, out ushort glyphIndex) ? glyphIndex : (ushort)0;
            }
            else if (PostTable != null)
            {
                if (PostTable.Version == 2)
                {
                    return PostTable.GetGlyphIndex(glyphName);
                }
                else
                {
                    //check data from adobe glyph list 
                    //from the unicode value
                    //select glyph index   

                    //we use AdobeGlyphList
                    //from https://github.com/adobe-type-tools/agl-aglfn/blob/master/glyphlist.txt

                    //but user can provide their own map here...

                    return GetGlyphIndex(AdobeGlyphList.GetUnicodeValueByGlyphName(glyphName));
                }
            }
            return 0;
        }

        /// <summary>Enumerates CFF glyph names or version 2 post-table glyph names.</summary>
        /// <returns>Glyph ID and name pairs.</returns>
        /// <remarks>For non-CFF fonts, a loaded post table is required.</remarks>
        public IEnumerable<GlyphNameMap> GetGlyphNameIter()
        {
            if (_cachedGlyphDicByName == null && _cff1FontSet != null)
            {
                UpdateCff1FontSetNamesCache();
            }

            if (_cachedGlyphDicByName != null)
            {
                //iter from here
                foreach (var kv in _cachedGlyphDicByName)
                {
                    yield return new GlyphNameMap(kv.Value, kv.Key);
                }
            }
            else if (PostTable!.Version == 2)
            {
                foreach (var kp in PostTable.GlyphNames!)
                {
                    yield return new GlyphNameMap(kp.Key, kp.Value);
                }
            }
        }
        /// <summary>Gets a glyph by ID, falling back to glyph zero for an out-of-range ID.</summary>
        /// <param name="glyphIndex">The requested glyph ID.</param>
        /// <returns>The requested glyph or the font's missing glyph.</returns>
        public Glyph GetGlyph(ushort glyphIndex)
        {
            if (glyphIndex < _glyphs.Length)
            {
                return _glyphs[glyphIndex];
            }
            else
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("found unknown glyph:" + glyphIndex);
#endif
                return _glyphs[0]; //return empty glyph?;
            }
        }

        /// <summary>Gets a glyph's horizontal advance in font design units.</summary>
        /// <param name="glyphIndex">The glyph ID.</param>
        /// <returns>The horizontal advance width.</returns>
        public ushort GetAdvanceWidthFromGlyphIndex(ushort glyphIndex) => _hMetrics.GetAdvanceWidth(glyphIndex);
        /// <summary>Gets a glyph's left-side bearing in font design units.</summary>
        /// <param name="glyphIndex">The glyph ID.</param>
        /// <returns>The left-side bearing.</returns>
        public short GetLeftSideBearing(ushort glyphIndex) => _hMetrics.GetLeftSideBearing(glyphIndex);
        /// <summary>Gets legacy kern-table adjustment between two glyphs.</summary>
        /// <param name="leftGlyphIndex">The left glyph ID.</param>
        /// <param name="rightGlyphIndex">The right glyph ID.</param>
        /// <returns>The adjustment in font design units, or zero when no kern table exists.</returns>
        public short GetKernDistance(ushort leftGlyphIndex, ushort rightGlyphIndex)
        {
            if (KernTable == null) return 0;
            return KernTable.GetKerningDistance(leftGlyphIndex, rightGlyphIndex);
        }

        /// <summary>
        /// Gets the PostScript glyph name for a given glyph index, or null if unavailable.
        /// </summary>
        /// <param name="glyphIndex">The glyph ID.</param>
        /// <returns>The glyph name, or null when unavailable.</returns>
        public string? GetGlyphName(ushort glyphIndex)
        {
            if (_cff1FontSet != null && glyphIndex < _glyphs.Length)
            {
                return _glyphs[glyphIndex]._cff1GlyphData?.Name;
            }
            if (PostTable?.Version == 2 && PostTable.GlyphNames is { } names)
            {
                return names.TryGetValue(glyphIndex, out string? name) ? name : null;
            }
            return null;
        }

        /// <summary>
        /// Returns all kerning pairs from the kern table.
        /// GPOS pair adjustment support is added by the BasicLatinShaper (US2).
        /// </summary>
        /// <returns>Legacy kerning pairs in font design units; empty when no kern table exists.</returns>
        public IReadOnlyList<KerningPair> GetAllKerningPairs()
        {
            var pairs = new List<KerningPair>();

            if (KernTable != null)
            {
                foreach (var (left, right, value) in KernTable.EnumerateAllPairs())
                {
                    pairs.Add(new KerningPair(left, right, value, KerningSource.KernTable));
                }
            }

            return pairs;
        }

        //
        /// <summary>Gets the font-wide bounding box from the head table.</summary>
        public Bounds Bounds { get; private set; }
        /// <summary>Gets the number of font design units in one em.</summary>
        public ushort UnitsPerEm { get; private set; }
        /// <summary>Gets the suggested underline position in font design units; requires a post table.</summary>
        public short UnderlinePosition => PostTable!.UnderlinePosition; //TODO: review here
        //

        const int s_pointsPerInch = 72;//point per inch, fix?        

        /// <summary>
        /// default dpi
        /// </summary>
        public static uint DefaultDpi { get; set; } = 96;

        /// <summary>
        /// convert from point-unit value to pixel value
        /// </summary>
        /// <param name="targetPointSize"></param>
        /// <param name="resolution">dpi</param>
        /// <returns></returns>
        public static float ConvPointsToPixels(float targetPointSize, int resolution = -1)
        {
            //http://stackoverflow.com/questions/139655/convert-pixels-to-points
            //points = pixels * 72 / 96
            //------------------------------------------------
            //pixels = targetPointSize * 96 /72
            //pixels = targetPointSize * resolution / pointPerInch

            if (resolution < 0)
            {
                //use current DefaultDPI
                resolution = (int)DefaultDpi;
            }

            return targetPointSize * resolution / s_pointsPerInch;
        }
        /// <summary>
        /// calculate scale to target pixel size based on current typeface's UnitsPerEm
        /// </summary>
        /// <param name="targetPixelSize">target font size in point unit</param>
        /// <returns></returns>
        public float CalculateScaleToPixel(float targetPixelSize)
        {
            //1. return targetPixelSize / UnitsPerEm
            return targetPixelSize / this.UnitsPerEm;
        }
        /// <summary>
        ///  calculate scale to target pixel size based on current typeface's UnitsPerEm
        /// </summary>
        /// <param name="targetPointSize">target font size in point unit</param>
        /// <param name="resolution">dpi</param>
        /// <returns></returns>
        public float CalculateScaleToPixelFromPointSize(float targetPointSize, int resolution = -1)
        {
            //1. var sizeInPixels = ConvPointsToPixels(sizeInPointUnit);
            //2. return sizeInPixels / UnitsPerEm

            if (resolution < 0)
            {
                //use current DefaultDPI
                resolution = (int)DefaultDpi;
            }
            return (targetPointSize * resolution / s_pointsPerInch) / this.UnitsPerEm;
        }


        /// <summary>Whether this font has TrueType outlines (glyf table) as opposed to CFF.</summary>
        public bool HasTtfOutline => _hasTtfOutline;

        /// <summary>
        /// Gets the original font file data if it was stored during loading.
        /// Returns null if the data was not retained.
        /// </summary>
        public byte[]? GetOriginalFontData() => _originalFontData;
        internal byte[]? _originalFontData;

        internal BASE? BaseTable { get; set; }
        internal GDEF? GDEFTable { get; set; }

        /// <summary>Gets the color layer table, or null when absent or trimmed.</summary>
        public COLR? COLRTable { get; private set; }
        /// <summary>Gets the color palette table, or null when absent or trimmed.</summary>
        public CPAL? CPALTable { get; private set; }

        internal bool HasColorAndPal { get; private set; }
        internal void SetColorAndPalTable(COLR? colr, CPAL? cpal)
        {
            COLRTable = colr;
            CPALTable = cpal;
            HasColorAndPal = colr != null;
        }

        /// <summary>Gets glyph positioning data, or null when the font has no GPOS table.</summary>
        public GPOS? GPOSTable { get; internal set; }
        /// <summary>Gets glyph substitution data, or null when the font has no GSUB table.</summary>
        public GSUB? GSUBTable { get; internal set; }

        internal void LoadOpenFontLayoutInfo(GDEF? gdefTable, GSUB? gsubTable, GPOS? gposTable, BASE? baseTable, COLR? colrTable, CPAL? cpalTable)
        {

            //***
            this.GDEFTable = gdefTable;
            this.GSUBTable = gsubTable;
            this.GPOSTable = gposTable;
            this.BaseTable = baseTable;
            this.COLRTable = colrTable;
            this.CPALTable = cpalTable;
            //---------------------------
            //fill glyph definition            
            if (gdefTable != null)
            {
                gdefTable.FillGlyphData(_glyphs);
            }
        }

        internal PostTable? PostTable { get; set; }
        internal bool _evalCffGlyphBounds;
        /// <summary>Gets whether the font uses CFF outlines, including when its outlines are trimmed.</summary>
        public bool IsCffFont => _hasCffData;

        //Math Table

        MathGlyphInfo[]? _mathGlyphInfos;
        internal MathTable? _mathTable;
        //
        /// <summary>Gets mathematical layout constants, or null when no MATH table is loaded.</summary>
        public MathConstants? MathConsts => _mathTable?._mathConstTable;
        internal void LoadMathGlyphInfos(MathGlyphInfo[] mathGlyphInfos)
        {
            _mathGlyphInfos = mathGlyphInfos;
            if (mathGlyphInfos != null)
            {
                //fill to original glyph?
                for (int glyphIndex = 0; glyphIndex < _glyphs.Length; ++glyphIndex)
                {
                    _glyphs[glyphIndex].MathGlyphInfo = mathGlyphInfos[glyphIndex];
                }
            }
        }
        /// <summary>Gets mathematical layout information for a glyph; requires a loaded MATH table.</summary>
        /// <param name="glyphIndex">The glyph ID.</param>
        /// <returns>The mathematical layout information at the glyph's index.</returns>
        /// <exception cref="IndexOutOfRangeException">The glyph ID is outside the MATH glyph array.</exception>
        public MathGlyphInfo GetMathGlyphInfo(ushort glyphIndex) => _mathGlyphInfos![glyphIndex];

        //-------------------------
        //svg and bitmap font
        SvgTable? _svgTable;

        internal bool HasSvgTable { get; private set; }
        internal void SetSvgTable(SvgTable? svgTable)
        {
            HasSvgTable = (_svgTable = svgTable) != null;
        }
        /// <summary>Appends a glyph's SVG document when an SVG table is loaded.</summary>
        /// <param name="glyphIndex">The glyph ID.</param>
        /// <param name="output">The destination builder, left unchanged when no SVG table is loaded.</param>
        public void ReadSvgContent(ushort glyphIndex, System.Text.StringBuilder output) => _svgTable?.ReadSvgContent(glyphIndex, output);


        internal BitmapFontGlyphSource? _bitmapFontGlyphSource;
        /// <summary>Gets whether a bitmap glyph source is available.</summary>
        public bool IsBitmapFont => _bitmapFontGlyphSource != null;
        /// <summary>Copies a bitmap glyph's encoded image bytes into a stream.</summary>
        /// <param name="glyph">A glyph from this bitmap typeface.</param>
        /// <param name="output">The destination stream.</param>
        /// <remarks>Requires an untrimmed bitmap font.</remarks>
        public void ReadBitmapContent(Glyph glyph, System.IO.Stream output)
        {
            _bitmapFontGlyphSource!.CopyBitmapContent(glyph, output);
        }

        /// <summary>
        /// undate lang info
        /// </summary>
        /// <param name="metaTable"></param>
        internal void UpdateLangs(Meta? metaTable) => Languages.Update(OS2Table, metaTable, CmapTable, this.GSUBTable, this.GPOSTable);



        internal ushort _whitespaceWidth; //common used value

        internal void UpdateFrequentlyUsedValues()
        {
            //whitespace
            ushort whitespace_glyphIndex = this.GetGlyphIndex(' ');
            if (whitespace_glyphIndex > 0)
            {
                _whitespaceWidth = this.GetAdvanceWidthFromGlyphIndex(whitespace_glyphIndex);
            }
        }

#if DEBUG
        public override string ToString() => Name;
#endif

    }

    /// <summary>A mutable positioned glyph sequence used by OpenType layout lookups.</summary>
    public interface IGlyphPositions
    {
        /// <summary>Gets the number of positioned glyphs.</summary>
        int Count { get; }

        /// <summary>Gets the GDEF class of a positioned glyph.</summary>
        /// <param name="index">The sequence index.</param>
        /// <returns>The glyph classification.</returns>
        GlyphClassKind GetGlyphClassKind(int index);
        /// <summary>Adds an offset adjustment in font design units.</summary>
        /// <param name="index">The sequence index.</param>
        /// <param name="appendOffsetX">The horizontal adjustment.</param>
        /// <param name="appendOffsetY">The vertical adjustment.</param>
        void AppendGlyphOffset(int index, short appendOffsetX, short appendOffsetY);
        /// <summary>Adds advance adjustments in font design units.</summary>
        /// <param name="index">The sequence index.</param>
        /// <param name="appendAdvX">The horizontal advance adjustment.</param>
        /// <param name="appendAdvY">The vertical advance adjustment.</param>
        void AppendGlyphAdvance(int index, short appendAdvX, short appendAdvY);

        /// <summary>Gets a positioned glyph and its horizontal advance.</summary>
        /// <param name="index">The sequence index.</param>
        /// <param name="advW">The advance in font design units.</param>
        /// <returns>The glyph ID.</returns>
        ushort GetGlyph(int index, out short advW);
        /// <summary>Gets a glyph's input location, offsets, and horizontal advance.</summary>
        /// <param name="index">The sequence index.</param>
        /// <param name="inputOffset">The source input offset.</param>
        /// <param name="offsetX">The horizontal offset in font design units.</param>
        /// <param name="offsetY">The vertical offset in font design units.</param>
        /// <param name="advW">The horizontal advance in font design units.</param>
        /// <returns>The glyph ID.</returns>
        ushort GetGlyph(int index, out ushort inputOffset, out short offsetX, out short offsetY, out short advW);
        //
        /// <summary>Gets a positioned glyph's offset in font design units.</summary>
        /// <param name="index">The sequence index.</param>
        /// <param name="offsetX">The horizontal offset.</param>
        /// <param name="offsetY">The vertical offset.</param>
        void GetOffset(int index, out short offsetX, out short offsetY);
    }


    /// <summary>Enumerates UTF-16 text as code points, combining valid surrogate pairs.</summary>
    public static class StringUtils
    {
        /// <summary>Appends code points from a UTF-16 character range without clearing the destination.</summary>
        /// <param name="codepoints">The destination list.</param>
        /// <param name="str">The source UTF-16 characters.</param>
        /// <param name="startAt">The first character index.</param>
        /// <param name="len">The number of characters, or -1 for the array length; the range must fit the array.</param>
        public static void FillWithCodepoints(List<int> codepoints, char[] str, int startAt = 0, int len = -1)
        {

            if (len == -1) len = str.Length;
            // this is important!
            // -----------------------
            //  from @samhocevar's PR: (https://github.com/LayoutFarm/Typography/pull/56/commits/b71c7cf863531ebf5caa478354d3249bde40b96e)
            // In many places, "char" is not a valid type to handle characters, because it
            // only supports 16 bits.In order to handle the full range of Unicode characters,
            // we need to use "int".
            // This allows characters such as 🙌 or 𐐷 or to be treated as single codepoints even
            // though they are encoded as two "char"s in a C# string.
            for (int i = 0; i < len; ++i)
            {
                char ch = str[startAt + i];
                int codepoint = ch;
                if (char.IsHighSurrogate(ch) && i + 1 < len)
                {
                    char nextCh = str[startAt + i + 1];
                    if (char.IsLowSurrogate(nextCh))
                    {
                        ++i;
                        codepoint = char.ConvertToUtf32(ch, nextCh);
                    }
                }
                codepoints.Add(codepoint);
            }
        }
        /// <summary>Enumerates code points, yielding unmatched surrogates as their UTF-16 values.</summary>
        /// <param name="str">The source UTF-16 characters.</param>
        /// <param name="startAt">The first character index.</param>
        /// <param name="len">The number of characters, or -1 for the array length; the range must fit the array.</param>
        /// <returns>The decoded code points.</returns>
        public static IEnumerable<int> GetCodepoints(char[] str, int startAt = 0, int len = -1)
        {
            if (len == -1) len = str.Length;
            // this is important!
            // -----------------------
            //  from @samhocevar's PR: (https://github.com/LayoutFarm/Typography/pull/56/commits/b71c7cf863531ebf5caa478354d3249bde40b96e)
            // In many places, "char" is not a valid type to handle characters, because it
            // only supports 16 bits.In order to handle the full range of Unicode characters,
            // we need to use "int".
            // This allows characters such as 🙌 or 𐐷 or to be treated as single codepoints even
            // though they are encoded as two "char"s in a C# string.
            for (int i = 0; i < len; ++i)
            {
                char ch = str[startAt + i];
                int codepoint = ch;
                if (char.IsHighSurrogate(ch) && i + 1 < len)
                {
                    char nextCh = str[startAt + i + 1];
                    if (char.IsLowSurrogate(nextCh))
                    {
                        ++i;
                        codepoint = char.ConvertToUtf32(ch, nextCh);
                    }
                }
                yield return codepoint;
            }
        }
    }




    /// <summary>A glyph ID paired with its PostScript name.</summary>
    public readonly struct GlyphNameMap
    {
        /// <summary>The glyph ID.</summary>
        public readonly ushort glyphIndex;
        /// <summary>The PostScript glyph name.</summary>
        public readonly string? glyphName;
        /// <summary>Creates a glyph-name mapping.</summary>
        /// <param name="glyphIndex">The glyph ID.</param>
        /// <param name="glyphName">The PostScript name, or null when not declared.</param>
        public GlyphNameMap(ushort glyphIndex, string? glyphName)
        {
            this.glyphIndex = glyphIndex;
            this.glyphName = glyphName;
        }
    }


}

