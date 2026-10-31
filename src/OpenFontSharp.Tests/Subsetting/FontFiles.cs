using System.IO.Compression;
using OpenFontSharp.WebFont;

namespace OpenFontSharp.Tests.Subsetting;

/// <summary>Paths to the test fonts and helpers to load them.</summary>
internal static class FontFiles
{
    public const string RobotoRegular = "TTF/Roboto/Roboto-Regular.ttf";
    public const string RobotoBold = "TTF/Roboto/Roboto-Bold.ttf";
    public const string ShantellStatic = "TTF/Shantell_Sans/static/ShantellSans-Medium.ttf";
    public const string ShantellVariable = "TTF/Shantell_Sans/ShantellSans-VariableFont_BNCE,INFM,SPAC,wght.ttf";
    public const string GreatVibes = "OTF/great-vibes/GreatVibes-Regular.otf";
    public const string RobotoCollection = "TTC/Roboto-RegularBold.ttc";
    public const string RobotoWoff = "WOFF/Roboto-Regular.woff";
    public const string GreatVibesWoff = "WOFF/GreatVibes-Regular.woff";
    public const string RobotoWoff2 = "WOFF2/Roboto-Regular.woff2";

    /// <summary>Text covering plain, accented (composite) and punctuation glyphs.</summary>
    public const string SampleText = "Hello, World! The quick brown fox: 0123456789 àéîõü ÅÇÑ";

    static FontFiles()
    {
        // The reader needs decompressors supplied by the host for web fonts.
        WoffDefaultZlibDecompressFunc.DecompressHandler = (compressed, output) =>
        {
            using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            zlib.ReadExactly(output);
            return true;
        };
        Woff2DefaultBrotliDecompressFunc.DecompressHandler = (compressed, output) =>
        {
            using var brotli = new BrotliStream(new MemoryStream(compressed), CompressionMode.Decompress);
            brotli.CopyTo(output);
            return true;
        };
    }

    public static string PathOf(string relative) => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Resources", "Fonts", relative);

    public static byte[] Bytes(string relative) => File.ReadAllBytes(PathOf(relative));

    public static Typeface Load(byte[] data, int offset = 0)
        => new OpenFontReader().Read(new MemoryStream(data), offset)
           ?? throw new InvalidOperationException("The reader could not read the font.");

    public static Typeface Load(string relative) => Load(Bytes(relative));

    /// <summary>Glyph ids for every character of <paramref name="text"/> that the font maps.</summary>
    public static HashSet<ushort> GlyphsFor(Typeface typeface, string text)
    {
        var glyphs = new HashSet<ushort>();
        foreach (char c in text)
        {
            ushort glyph = typeface.GetGlyphIndex(c);
            if (glyph != 0)
                glyphs.Add(glyph);
        }
        return glyphs;
    }
}
