//MIT, 2020-present, WinterDev

using OpenFontSharp.Tables;

namespace OpenFontSharp;

/// <summary>Records the tables and outline formats removed when a typeface is trimmed.</summary>
public class RestoreTicket
{
  internal RestoreTicket(string? typefaceName, TableHeader[] headers)
  {
    TypefaceName = typefaceName;
    Headers = headers;
  }
  internal string? TypefaceName { get; }

  internal TableHeader[] Headers;
  internal bool HasTtf;
  internal bool HasCff;
  internal bool HasSvg;
  internal bool HasBitmapSource;

  internal bool ControlValues;
  internal bool PrepProgramBuffer;
  internal bool FpgmProgramBuffer;
  internal bool CPALTable;
  internal bool COLRTable;
  internal bool GaspTable;
}

/// <summary>The current outline-retention state of a typeface.</summary>
public enum TrimMode
{
  /// <summary>
  /// No trim, full glyph instruction
  /// </summary>
  No, //default
  /// <summary>
  /// only essential info for glyph layout
  /// </summary>
  EssentailLayoutInfo,
  /// <summary>
  /// restore again
  /// </summary>
  Restored,
}

public static partial class TypefaceExtensions
{
  /// <summary>Replaces glyphs with layout-only copies and unloads outline-related data.</summary>
  /// <param name="typeface">The font to trim.</param>
  /// <returns>A ticket for restoring the font, or null if it was already trimmed.</returns>
  public static RestoreTicket? TrimDown(this Typeface typeface) => typeface.TrimDownAndRemoveGlyphBuildingDetail();
  /// <summary>Gets the current outline-retention state.</summary>
  /// <param name="typeface">The font to inspect.</param>
  /// <returns>The font's trim state.</returns>
  public static TrimMode GetTrimMode(this Typeface typeface) => typeface._typefaceTrimMode;
  /// <summary>Checks whether glyph building data has been unloaded.</summary>
  /// <param name="typeface">The font to inspect.</param>
  /// <returns>True when only essential layout data remains.</returns>
  public static bool IsTrimmed(this Typeface typeface) => typeface._typefaceTrimMode == TrimMode.EssentailLayoutInfo;

  /// <summary>Attempts to reload trimmed data from the original font; a header mismatch leaves the font trimmed.</summary>
  /// <param name="typeface">The trimmed font. Untrimmed fonts are left unchanged.</param>
  /// <param name="ticket">The ticket returned by the first trim operation.</param>
  /// <param name="openFontReader">The reader to use for restoration.</param>
  /// <param name="fontStream">The original readable, seekable font stream, closed by the reader.</param>
  public static void RestoreUp(this Typeface typeface, RestoreTicket ticket, OpenFontReader openFontReader, Stream fontStream)
  {
    if (typeface.IsTrimmed())
    {
      openFontReader.Read(typeface, ticket, fontStream);
    }
  }
  /// <summary>Attempts to reload trimmed data using a new reader; a header mismatch leaves the font trimmed.</summary>
  /// <param name="typeface">The trimmed font. Untrimmed fonts are left unchanged.</param>
  /// <param name="ticket">The ticket returned by the first trim operation.</param>
  /// <param name="fontStream">The original readable, seekable font stream, closed by the reader.</param>
  public static void RestoreUp(this Typeface typeface, RestoreTicket ticket, Stream fontStream)
  {
    //use default opent font reader
    RestoreUp(typeface, ticket, new OpenFontReader(), fontStream);
  } 
}


//-------------------------
//This is our extension***,
//NOT in OpenFont spec
//-------------------------
//user can reload a new clone of glyphs with fewer detail
//or restore a new clone of glyphs with full detail 

partial class Typeface
{
  internal TrimMode _typefaceTrimMode;


  internal RestoreTicket? TrimDownAndRemoveGlyphBuildingDetail()
  {
    switch (_typefaceTrimMode)
    {
      default: throw new OpenFontNotSupportedException();
      case TrimMode.EssentailLayoutInfo: return null;//same mode
      case TrimMode.Restored:
      case TrimMode.No:
      {
        RestoreTicket ticket = new RestoreTicket(Name, _tblHeaders);

        //FROM:GlyphLoadingMode.Full => TO: GlyphLoadingMode.EssentailLayoutInfo 

        ticket.HasTtf = _hasTtfOutline;

        //cache glyph name before unload 
        if (_cff1FontSet != null)
        {
          ticket.HasCff = true;
          UpdateCff1FontSetNamesCache();//***
          _cff1FontSet = null;
        }

        //1.Ttf and Otf => clone each glyphs in NO building
        Glyph[] newClones = new Glyph[_glyphs.Length];
        for (int i = 0; i < newClones.Length; ++i)
        {
          newClones[i] = Glyph.Clone_NO_BuildingInstructions(_glyphs[i]);
        }
        _glyphs = newClones;

        //and since glyph has no building instructions in this mode
        //so  ... 

        ticket.ControlValues = ControlValues != null;
        ControlValues = null;

        ticket.PrepProgramBuffer = PrepProgramBuffer != null;
        PrepProgramBuffer = null;

        ticket.FpgmProgramBuffer = FpgmProgramBuffer != null;
        FpgmProgramBuffer = null;

        ticket.CPALTable = CPALTable != null;
        CPALTable = null;

        ticket.COLRTable = COLRTable != null;
        COLRTable = null;

        ticket.GaspTable = GaspTable != null;
        GaspTable = null;

        // 
        //3. Svg=> remove SvgTable
        if (_svgTable != null)
        {
          ticket.HasSvg = true;
          _svgTable.UnloadSvgData();
          _svgTable = null;
        }

        //4. Bitmap Font => remove embeded bitmap data
        if (_bitmapFontGlyphSource != null)
        {
          ticket.HasBitmapSource = true;
          _bitmapFontGlyphSource.UnloadCBDT();
        }


        _typefaceTrimMode = TrimMode.EssentailLayoutInfo;

        return ticket;
      }
    }
  }


  internal bool CompareOriginalHeadersWithNewlyLoadOne(TableHeader[] others)
  {
    if (_tblHeaders != null && others != null &&
        _tblHeaders.Length == others.Length)
    {
      for (int i = 0; i < _tblHeaders.Length; ++i)
      {
        TableHeader a = _tblHeaders[i];
        TableHeader b = others[i];

        if (a.Tag != b.Tag ||
            a.Offset != b.Offset ||
            a.Length != b.Length ||
            a.CheckSum != b.CheckSum)
        {
#if DEBUG
          System.Diagnostics.Debugger.Break();
#endif

          return false;
        }
      }
      //pass all
      return true;
    }
    return false;
  }
}