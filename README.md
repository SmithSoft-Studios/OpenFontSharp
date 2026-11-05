# OpenFontSharp
OpenFontSharp is a lightweight, pure C# library for reading font metadata, character mappings, glyph outlines, and OpenType tables. It also provides measurement, basic shaping, and font-subsetting utilities for document applications. It is based on [Typography.OpenFont](https://github.com/LayoutFarm/Typography).

**Website:** [openfontsharp.smithsoft.co.za](https://openfontsharp.smithsoft.co.za/)

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
| Basic glyph substitution and optional GPOS positioning | `BasicLatinShaper` | Simple Latin text in a document pipeline |
| Glyph subsetting and ID remapping | `FontSubsetter` | Reducing embedded font size for glyph-addressed PDF fonts |
| ToUnicode CMap generation | `ToUnicodeCMapBuilder` | Mapping two-byte PDF character IDs to Unicode scalar values, including supplementary planes |
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

Truncated or malformed font data is reported as `InvalidDataException`. The reader bounds collection and cmap counts before allocation; SVG documents are limited to 64 MiB. Unsupported GSUB operations report `OpenFontNotSupportedException`.

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

By default, the basic shaper applies available GSUB lookups through its glyph-list adapter and legacy `kern` adjustments; this preserves the existing shaping behavior. To opt in to GPOS pair kerning and Latin mark placement, pass `new ShapingOptions { EnableGposPositioning = true }` to the overload. It applies GPOS PairPos formats 1 and 2 referenced by the `kern` feature, and MarkBasePos lookups referenced by `mark`. When a supported GPOS `kern` lookup is present, it takes precedence over the legacy `kern` table. `ShapingResult.XOffsets` and `YOffsets` contain the per-glyph placement adjustments in design units.

The basic shaper works on UTF-16 characters rather than Unicode runes and is not suitable for supplementary-plane text, bidirectional layout, complex scripts, or Indic reordering. The complex-shaping flag is a warning, not a guarantee that every unflagged string is fully shaped. Shaping does not return source clusters, so ligatures need an application-provided Unicode mapping when embedding them.

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

### Colour glyph data (no rendering)

`GetColorLayers(glyphId)` returns read-only COLR v0 layers in back-to-front painting order, including compatible v0 records in a COLR v1 table. Each `ColorLayer` contains a `GlyphId` and `PaletteIndex`; index `0xFFFF` means the application's foreground colour. Missing or trimmed layers return an empty list.

`PaletteCount` and `GetPalette(index)` expose CPAL v0/v1 palettes as read-only `PaletteColor` entries (`Blue`, `Green`, `Red`, `Alpha`, in BGRA order with sRGB colour components). Invalid palette indices, including when palettes are absent or trimmed, throw `ArgumentOutOfRangeException`. CPAL v1 palette labels and type metadata are not exposed. COLR versions other than 0/1 throw a version-specific `OpenFontNotSupportedException`.

COLR v1 fonts load their supported outlines, metrics and cmap normally. `ColorTableVersion` is the loaded COLR version (`0` or `1`), or `null` when absent or trimmed. Version 1 paint graphs (`BaseGlyphList`, `LayerList`, `ClipList`, `VarIndexMap`, `VarStore`) are not interpreted or rendered; without compatible v0 records, `GetColorLayers` returns an empty list. Ordinary base glyph outlines may themselves be empty; this API does not flatten painted emoji into outlines. Reduced subsets preserve retained ordinary outlines and metrics but drop `COLR` and `CPAL`, as for v0; retaining every glyph returns the original font unchanged, including its colour tables.

Bitmap-only fonts use the first strike through `GetGlyph` and `ReadBitmapContent`; outlines still take priority in hybrid fonts. See [bitmap compatibility and limits](docs/COMPATIBILITY.md#colour-and-bitmap-tables) for supported formats and malformed-data handling. This exposes encoded data and metrics, not a rasteriser.

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

Then use `OpenFontReader` as with a standalone font. For WOFF2 reading, conversion, and subsetting, install the host Brotli hook once during startup:

```csharp
Woff2DefaultBrotliDecompressFunc.DecompressHandler = (compressed, output) =>
{
    using var input = new MemoryStream(compressed);
    using var brotli = new BrotliStream(input, CompressionMode.Decompress);
    brotli.CopyTo(output);
    return true;
};
```

`Woff2Converter.ToSfnt(woff2Bytes)` returns standalone TTF/OTF bytes, reconstructing transformed `glyf`, `loca`, and `hmtx` tables per the [W3C WOFF2 Recommendation](https://www.w3.org/TR/WOFF2/). An optional second argument supplies a per-call Brotli decoder instead of the default hook. No Brotli package is required by the library; without a hook it throws `NotSupportedException`. WOFF2 collections are not supported. The byte-based WOFF1 subsetter uses built-in .NET zlib decompression and does not need the reader hook.

## Font subsetting
`OpenFontSharp.Subsetting.FontSubsetter` keeps only the glyphs you use (plus `.notdef` and any composite components), renumbers them contiguously and returns the subset font with an old-to-new glyph id map. After container decoding, TrueType and CFF1 outlines are copied verbatim; CFF2 charstrings are reduced to their default instance and emitted as static CFF1, unless axis coordinates are supplied for TrueType variable-font instantiation.

```csharp
var result = FontSubsetter.Subset(fontBytes, usedGlyphIds);        // or Subset(typeface, usedGlyphIds)
var member = FontSubsetter.Subset(ttcBytes, fontIndex, usedGlyphIds); // one font of a collection
```

The output is always a plain TrueType (`0x00010000`) or CFF (`OTTO`) font. Without explicit instantiation, when every glyph is requested glyph IDs are unchanged and `GlyphIdMap` is empty (CFF2 is still converted to static CFF1 outlines). An empty map means glyph IDs are unchanged, not that the output has no glyphs. Certain recognized sfnt inputs without usable outline tables are also returned whole; check the result rather than assuming a size reduction. A format that cannot be subset throws `NotSupportedException`; the subsetter never invents missing outlines.

| Input | Test font | Result |
|---|---|---|
| TrueType (`.ttf`) | Roboto, Shantell Sans (static) | Supported |
| TrueType variable font | Shantell Sans (default and non-default); synthetic axis-coordinate tests | Supported: default instance, or explicit axis coordinates via the dictionary overload. Static output drops variation tables |
| OpenType with CFF outlines (`.otf`, `CFF `) | Great Vibes | Supported for name-keyed CFF. Subroutines are kept whole, not subset |
| CID-keyed CFF | hand-built CFF table | Supported: CIDs, used Font DICTs and FDSelect formats 0/3 are preserved; subroutines are kept whole |
| CFF glyphs using the deprecated `seac` accent operator | hand-built CFF table | Supported: the base and accent glyphs are retained automatically (resolved through StandardEncoding and the charset); unresolvable components throw `NotSupportedException` |
| OpenType with CFF2 outlines (variable `.otf`) | Synthetic CFF2 with `blend` / `vsindex` | Supported: reduced to the default instance and emitted as static CFF1 (`CFF `) in an `OTTO` font; `CFF2`, `fvar`, `avar`, `HVAR`, `MVAR` and `STAT` are dropped |
| TrueType/OpenType collection (`.ttc`), as bytes | Roboto Regular + Bold | Throws `NotSupportedException` unless you pass a font index; supported with `Subset(bytes, fontIndex, glyphs)` |
| Collection member read with `OpenFontReader.Read(stream, offset)` | Roboto Regular + Bold | Supported |
| WOFF 1.0 (`.woff`) | Roboto, Great Vibes | Supported: decoded, then subset as TTF/OTF |
| WOFF 2.0 (`.woff2`) | Synthetic simple/composite glyphs and metrics; Roboto; in-test Great Vibes wrapper | Supported: host Brotli hook required, decoded then subset as TTF/OTF; collections excluded |
| Unicode cmap output (opt-in) | Synthetic BMP/supplementary mappings; Great Vibes; Roboto collection | Supported: `includeUnicodeCmap: true` writes formats 4 and 12 with remapped glyph IDs; off by default |
| Colour / bitmap tables | Synthetic reader tests | Reading only; colour/bitmap-aware subsetting is not implemented. Outline subsets drop these tables; bitmap-only inputs are returned whole |
| Unrecognised data | n/a | Throws `NotSupportedException` |

What a subset keeps: `head`, `hhea`, `maxp`, `hmtx`, `OS/2`, `name`, hinting (`cvt `, `fpgm`, `prep`, `gasp`) and the outline tables (`glyf`/`loca` or `CFF `). `post` becomes version 3 (no glyph names, though CFF glyph names survive in the charset), and by default `cmap` is reduced to an empty table, since PDF Identity-H embedding does not use it. Layout and kerning tables (`GSUB`, `GPOS`, `GDEF`, `kern`) are dropped.

**Default subsets are for glyph-addressed embedding, not drop-in fonts for ordinary Unicode text rendering.** Map and shape text against the original font first, then remap glyph IDs for the subset. `FontSubsetter` itself resolves composite dependencies from the original bytes; `GlyphCollector` only collects the initial IDs from the parsed font.

To retain Unicode lookup, use `FontSubsetter.Subset(fontBytes, usedGlyphIds, includeUnicodeCmap: true)` (also available for a `Typeface` or a collection member). All Unicode scalar mappings to retained glyphs, including aliases and composite dependencies, are renumbered from the source cmap. The output contains Windows format 4 (BMP) and format 12 (full Unicode, including BMP) subtables. Layout/kerning tables and Unicode variation sequences are still omitted, so this is not a full shaping-preserving font subset. Fonts returned whole keep their original cmap. See [cmap compatibility limits](docs/COMPATIBILITY.md#subset-unicode-cmap).

### Instantiate a variable TrueType font

```csharp
var instance = FontSubsetter.Subset(fontBytes, usedGlyphIds,
    new Dictionary<string, float> { ["wght"] = 700 });
```

Axis tags and values are in user coordinates, as declared by `fvar`. Omitted axes use their defaults; values outside an axis range are clamped. Normalization uses `fvar` and `avar`. The static output applies `gvar` outline deltas (including IUP), `HVAR` advance deltas or the `gvar` phantom-point fallback, and `cvar` control-value deltas. Instantiation also occurs when all glyphs are retained; an empty ID map still only means that glyph IDs are unchanged. Existing overloads retain their default-instance behavior. See [variable-font compatibility](docs/COMPATIBILITY.md#truetype-variable-instances) for scope and verification.

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

var cidToUnicode = new Dictionary<int, int[]>();
foreach (char c in text)
    cidToUnicode.TryAdd(Remap(font.GetGlyphIndex(c)), new[] { (int)c });

File.WriteAllBytes("Hello-subset.ttf", subset.FontData);
File.WriteAllBytes("Hello-ToUnicode.txt", ToUnicodeCMapBuilder.Build(cidToUnicode));
```

Your PDF writer must use the same remapped IDs as CIDs (or supply a matching CID-to-GID mapping), create the font dictionaries and width entries, and embed both streams. Multiple characters can map to one glyph; choose the intended extraction mapping in your application. The CMap builder accepts source IDs from 0 through 65535 and either one Unicode scalar or a non-empty sequence of Unicode scalars per source ID. For example, a ligature CID can map to `new[] { 0x66, 0x66, 0x69 }` (`ffi`). Destinations use UTF-16BE bytes without a BOM: U+1F600 becomes `<D83DDE00>`, not `<1F600>`, and sequences can mix BMP and supplementary scalars. Surrogate code points and out-of-range values are rejected. Consecutive mappings use `bfrange` only when the UTF-16BE destination bytes can be incremented without overflow or crossing a surrogate boundary; all CMap blocks contain at most 100 entries. The BMP example above does not demonstrate supplementary-text shaping; the basic shaper's separate limitations still apply.

Respect the font's embedding permissions and license. Subsetting retains OS/2 licensing bits; it does not authorize embedding.

## Development and quality checks

```shell
dotnet build src/OpenFontSharp/OpenFontSharp.csproj -c Release --no-incremental
dotnet test SmithSoftStudios.OpenFontSharp.sln -c Debug
dotnet test SmithSoftStudios.OpenFontSharp.sln -c Release
dotnet pack src/OpenFontSharp/OpenFontSharp.csproj -c Release --no-build
```

`LibraryQualityTests` performs a fresh Release library build with XML documentation enabled and treats `CS8618` and `CS1591` as errors. Nullable contract tests cover absent tables, CFF versus TrueType outlines, collection previews, and trim/restore behavior. The existing subsetting tests verify output structures and composite dependencies across supported formats.

See the [test coverage matrix](docs/TEST-COVERAGE.md) for per-feature tests, real-font fixtures, independent references, and known evidence gaps.

# License

OpenFontSharp contains SmithSoft original code (MIT) and code imported from other open-source projects, each under the licence in its file header (Apache-2.0, MIT, BSD, FreeType). See [LICENSE.md](LICENSE.md) for how the code is divided, [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the credits, required notices and statement of changes, and the [licenses](licenses) folder for the texts. If you copy source code from this project, check each file's header for its licence.

Imported code is based on [Typography.OpenFont](https://github.com/LayoutFarm/Typography) (WinterDev and contributors), with NRasterizer (Samuel Carlsson), SharpFont (Michael Popoloski), FreeType, Apache PDFBox and the Adobe Glyph List.
