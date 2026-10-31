# OpenFontSharp
OpenFontSharp is a lightweight, pure C# library for reading font metadata, character mappings, glyph outlines, and OpenType tables. It also provides measurement, basic shaping, and font-subsetting utilities for document applications. It is based on [Typography.OpenFont](https://github.com/LayoutFarm/Typography).

## Installation

The current library targets **.NET 10** and runs on Windows, Linux, and macOS without requiring installed system fonts.

```shell
dotnet add package OpenFontSharp
```

The NuGet package includes XML API documentation for IntelliSense. See [compatibility notes](docs/COMPATIBILITY.md) for nullable contracts and upgrading, and [release instructions](docs/RELEASING.md) for publishing.

## Features and use cases

| Feature | API | Typical use case |
|---|---|---|
| Names, style, and language previews | `OpenFontReader.ReadPreview` | A font picker or inventory that does not need glyph outlines |
| Character-to-glyph mapping and outline access | `Typeface`, `Glyph`, `IGlyphTranslator` | Inspecting font coverage or feeding outlines to your own drawing backend |
| Consolidated embedding metrics | `FontLoader`, `FontInfo` | Building PDF font descriptors and glyph-width arrays |
| Unshaped advance measurement | `TextMeasurer.MeasureWidth(Typeface, ...)` | Estimating simple text widths in points or pixels |
| Basic glyph substitution and legacy kerning | `BasicLatinShaper` | Simple Latin text in a document pipeline |
| Glyph subsetting and ID remapping | `FontSubsetter` | Reducing embedded font size for glyph-addressed PDF fonts |
| ToUnicode CMap generation | `ToUnicodeCMapBuilder` | Mapping PDF character IDs back to BMP Unicode characters |
| Trim and restore | `TypefaceExtensions.TrimDown` / `RestoreUp` | Keeping metrics while temporarily unloading outlines |

This is not a PDF writer, a complete text-layout engine, or a ready-made rasterizer. It exposes drawing callbacks and parsed font tables, but your application supplies rendering, line breaking, bidirectional layout, and complex-script shaping.

### Read a font and measure simple text

```csharp
using OpenFontSharp;
using OpenFontSharp.Metrics;

using var stream = File.OpenRead("Roboto-Regular.ttf");
var typeface = new OpenFontReader().Read(stream)
    ?? throw new InvalidOperationException("The font could not be decoded.");

Console.WriteLine($"{typeface.Name ?? "(unnamed)"}: {typeface.GlyphCount} glyphs");
ushort glyphId = typeface.GetGlyphIndex('A'); // Zero means .notdef (missing glyph).
ushort advance = typeface.GetAdvanceWidthFromGlyphIndex(glyphId);
double widthInPoints = TextMeasurer.MeasureWidth(typeface, "Hello", 12);
Console.WriteLine($"A advance: {advance} design units; Hello: {widthInPoints:F2} pt");

FontInfo info = FontLoader.FromTypeface(typeface, File.ReadAllBytes("Roboto-Regular.ttf"));
Console.WriteLine($"Units per em: {info.UnitsPerEm}; ascent: {info.Ascent}");
```

The reader closes the supplied stream. Use a new readable, seekable stream for each read. Metrics and bounds are in **font design units**; multiply by `fontSize / UnitsPerEm` to convert to the units of your chosen font size.

`TextMeasurer.MeasureWidth(Typeface, ...)` maps Unicode runes but does not apply kerning or shaping. Prefer this overload for real fonts: the legacy `FontInfo` overload indexes its width array directly by UTF-16 character value, not by a `cmap` lookup. `FontInfo.GlyphWidths` produced by `FontLoader` is indexed by glyph ID, so that overload is not a general-purpose measurement path for loaded fonts.

### Preview a font collection and select a member

```csharp
using OpenFontSharp;

var reader = new OpenFontReader();
var preview = reader.ReadPreview(File.OpenRead("Fonts.ttc"))
    ?? throw new InvalidOperationException("The collection could not be decoded.");
if (!preview.IsFontCollection)
    throw new InvalidOperationException("Expected a font collection.");

for (int i = 0; i < preview.MemberCount; i++)
    Console.WriteLine($"{i}: {preview.GetMember(i).Name}");

int memberOffset = preview.GetMember(0).ActualStreamOffset;
var member = reader.Read(File.OpenRead("Fonts.ttc"), memberOffset)
    ?? throw new InvalidOperationException("The member could not be decoded.");
```

`Read` cannot select an entire collection at offset zero: use `ReadPreview` to obtain a member offset. A collection-level preview has no `NameEntry` or `OS2Table`; inspect its members instead. `ReadFlags` values are currently reserved and do not restrict the tables loaded; use `ReadPreview` when you need a lightweight read.

### Basic shaping

```csharp
using OpenFontSharp.Shaping;

var shaped = BasicLatinShaper.Shape(typeface, "office");
if (shaped.RequiresComplexShaping)
    throw new NotSupportedException("Use a full shaping engine for this text.");

double shapedWidth = shaped.Advances.Sum(a => (double)a) * 12 / typeface.UnitsPerEm;
```

The basic shaper applies available GSUB lookups through its glyph-list adapter and legacy `kern` adjustments. It does **not** currently apply GPOS pair adjustments, despite exposing the parsed GPOS tables. It works on UTF-16 characters rather than Unicode runes and is not suitable for supplementary-plane text, bidi layout, or complex scripts. The complex-shaping flag is a warning, not a guarantee that every unflagged string is fully shaped. Shaping does not return source clusters, so ligatures need an application-provided Unicode mapping when embedding them.

### Optional outlines and trimming

```csharp
var glyph = typeface.GetGlyph(typeface.GetGlyphIndex('A'));
if (glyph.GlyphPoints is { } points && glyph.EndPoints is { } ends)
{
    // Implement IGlyphTranslator to draw or collect TrueType contour commands:
    // translator.Read(points, ends, typeface.CalculateScaleToPixel(24));
}
else if (glyph.GetCff1GlyphData() is { } cff)
{
    Console.WriteLine($"CFF glyph: {cff.Name}");
}

var ticket = typeface.TrimDown();
if (ticket is not null)
{
    // Glyph IDs, bounds, and advance metrics remain usable; outlines are unloaded.
    typeface.RestoreUp(ticket, File.OpenRead("Roboto-Regular.ttf"));
    if (typeface.IsTrimmed())
        throw new InvalidOperationException("Restoration did not match the original font.");
}
```

Do not substitute empty arrays for missing outlines: null distinguishes CFF, bitmap, or trimmed glyphs from loaded TrueType glyphs. Color, SVG, and bitmap data can also be unavailable or unloaded. Check the actual data and trim state before using format-specific operations.

### Web-font containers

WOFF 1 reading requires a host-provided zlib decoder. For example, configure this once during application startup:

```csharp
using System.IO.Compression;
using OpenFontSharp.WebFont;

WoffDefaultZlibDecompressFunc.DecompressHandler = (compressed, output) =>
{
    using var input = new MemoryStream(compressed);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    zlib.ReadExactly(output);
    return true;
};
```

Then use `OpenFontReader` as with a standalone font. WOFF2 also has a host Brotli-decompression hook, but transformed TrueType outline decoding is incomplete; do not treat WOFF2 as fully supported for outlines or subsetting. Prefer converting WOFF2 to TTF/OTF outside this library. The byte-based WOFF1 subsetter uses built-in .NET zlib decompression and does not need the reader hook.

## Font subsetting
`OpenFontSharp.Subsetting.FontSubsetter` keeps only the glyphs you use (plus `.notdef` and any composite components), renumbers them contiguously and returns the subset font with an old-to-new glyph id map. Glyph outlines are copied verbatim, never re-encoded.

```csharp
var result = FontSubsetter.Subset(fontBytes, usedGlyphIds);        // or Subset(typeface, usedGlyphIds)
var member = FontSubsetter.Subset(ttcBytes, fontIndex, usedGlyphIds); // one font of a collection
```

The output is always a plain TrueType (`0x00010000`) or CFF (`OTTO`) font. When every glyph is requested the font is returned whole and `GlyphIdMap` is empty. An empty map means glyph IDs are unchanged, not that the output has no glyphs. Certain recognized sfnt inputs without usable outline tables are also returned whole; check the result rather than assuming a size reduction. A format that cannot be subset throws `NotSupportedException`; the subsetter never invents missing outlines.

| Input | Test font | Result |
|---|---|---|
| TrueType (`.ttf`) | Roboto, Shantell Sans (static) | Supported |
| TrueType variable font | Shantell Sans (variable) | Supported: subset to the default instance. `fvar`, `gvar`, `HVAR` and the other variation tables are dropped |
| OpenType with CFF outlines (`.otf`, `CFF `) | Great Vibes | Supported for name-keyed CFF. Subroutines are kept whole, not subset |
| CID-keyed CFF | hand-built CFF table | Throws `NotSupportedException` |
| CFF glyphs using the deprecated `seac` accent operator | hand-built CFF table | Throws `NotSupportedException` |
| OpenType with CFF2 outlines (variable `.otf`) | Great Vibes with the table relabelled `CFF2` | Throws `NotSupportedException` |
| TrueType/OpenType collection (`.ttc`), as bytes | Roboto Regular + Bold | Throws `NotSupportedException` unless you pass a font index; supported with `Subset(bytes, fontIndex, glyphs)` |
| Collection member read with `OpenFontReader.Read(stream, offset)` | Roboto Regular + Bold | Supported |
| WOFF 1.0 (`.woff`) | Roboto, Great Vibes | Supported: decoded, then subset as TTF/OTF |
| WOFF 2.0 (`.woff2`) | Roboto | Throws `NotSupportedException` (WOFF2's transformed `glyf`/`loca` decoding is not implemented) |
| Unrecognised data | n/a | Throws `NotSupportedException` |

What a subset keeps: `head`, `hhea`, `maxp`, `hmtx`, `OS/2`, `name`, hinting (`cvt `, `fpgm`, `prep`, `gasp`) and the outline tables (`glyf`/`loca` or `CFF `). `post` becomes version 3 (no glyph names, though CFF glyph names survive in the charset), and `cmap` is reduced to an empty table, since PDF Identity-H embedding does not use it. Layout and kerning tables (`GSUB`, `GPOS`, `GDEF`, `kern`) are dropped.

**These subsets are for glyph-addressed embedding, not drop-in fonts for ordinary Unicode text rendering.** Map and shape text against the original font first, then remap glyph IDs for the subset. `FontSubsetter` itself resolves composite dependencies from the original bytes; `GlyphCollector` only collects the initial IDs from the parsed font.

### Subset simple BMP text and create a PDF ToUnicode map

```csharp
using OpenFontSharp;
using OpenFontSharp.Metrics;
using OpenFontSharp.Subsetting;

byte[] fontBytes = File.ReadAllBytes("Roboto-Regular.ttf");
var font = new OpenFontReader().Read(new MemoryStream(fontBytes))
    ?? throw new InvalidOperationException("The font could not be decoded.");

// This example deliberately uses simple BMP text without shaping or ligatures.
const string text = "Hello";
var usedGlyphIds = text.Select(c => font.GetGlyphIndex(c)).ToHashSet();
if (usedGlyphIds.Contains(0))
    throw new InvalidOperationException("The font does not cover the sample text.");

var subset = FontSubsetter.Subset(fontBytes, usedGlyphIds);
ushort Remap(ushort oldId) =>
    subset.GlyphIdMap.Count == 0 ? oldId : subset.GlyphIdMap[oldId];

var cidToUnicode = new Dictionary<int, int>();
foreach (char c in text)
    cidToUnicode.TryAdd(Remap(font.GetGlyphIndex(c)), c);

File.WriteAllBytes("Hello-subset.ttf", subset.FontData);
File.WriteAllBytes("Hello-ToUnicode.txt", ToUnicodeCMapBuilder.Build(cidToUnicode));
```

Your PDF writer must use the same remapped IDs as CIDs (or supply a matching CID-to-GID mapping), create the font dictionaries and width entries, and embed both streams. Multiple characters can map to one glyph; choose the intended extraction mapping in your application. The current CMap builder accepts one integer per CID and formats it as hexadecimal; use BMP scalar values only, not supplementary-plane values or multi-character ligature mappings. Creating correct UTF-16 sequences for those cases requires a richer mapping implementation outside this helper.

Respect the font's embedding permissions and license. Subsetting retains OS/2 licensing bits; it does not authorize embedding.

## Development and quality checks

```shell
dotnet build src/OpenFontSharp/OpenFontSharp.csproj -c Release --no-incremental
dotnet test SmithSoftStudios.OpenFontSharp.sln -c Release
dotnet pack src/OpenFontSharp/OpenFontSharp.csproj -c Release --no-build
```

`LibraryQualityTests` performs a fresh Release library build with XML documentation enabled and treats `CS8618` and `CS1591` as errors. Nullable contract tests cover absent tables, CFF versus TrueType outlines, collection previews, and trim/restore behavior. The existing subsetting tests verify output structures and composite dependencies across supported formats.

# License
The project is based on multiple open-sourced projects (listed below) all using permissive licenses.

A license for a whole project is [MIT](https://opensource.org/license/mit/)

But if you copy source code directly, please check each source file's header for the licensing info if available.

**Font**

Apache2, 2014-2016, Samuel Carlsson, Big thanks for https://github.com/vidstige/NRasterizer

MIT, 2015, Michael Popoloski, https://github.com/MikePopoloski/SharpFont

The FreeType Project LICENSE (3-clauses BSD style),2003-2016, David Turner, Robert Wilhelm, and Werner Lemberg and others, https://www.freetype.org/

Apache2, 2018, Apache/PDFBox Authors, https://github.com/apache/pdfbox

Apache2, 2020, Adobe Font Development Kit for OpenType (AFDKO), https://github.com/adobe-type-tools/afdko
