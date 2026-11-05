# Nullable contracts and compatibility

## Issue #9: nullable and XML documentation cleanup

The cleanup preserves public CLR member signatures and valid-font behavior. It enables XML documentation generation so the NuGet package includes API descriptions rather than relying only on source comments.

**Source-compatibility notice:** nullable reference annotations are caller-visible metadata. Code compiled with nullable analysis can receive new warnings when it dereferences a result that was always capable of being null, even though the runtime return value has not changed. Projects treating those warnings as errors must update their guards. These annotations do not change reference types into `Nullable<T>`, rename members, or remove overloads.

The initial Release library build had 422 compiler warnings, including 306 `CS8618` warnings. Enabling XML generation additionally exposed 591 `CS1591` warnings and malformed/incomplete documentation warnings. The regression test was first run against that baseline and failed. The library now generates XML documentation on normal builds; the quality test independently rebuilds it with `CS8618` and `CS1591` treated as errors.

### Verification results

- The Release library build has zero nullable warnings and zero XML documentation warnings, including `CS8618` and `CS1591`.
- All 62 solution tests pass, including the initially failing quality test and optional-data/trim/restore checks.
- A reflection comparison against the pulled `main` assembly matched all 1,681 public/protected API surface entries, including signatures, generic constraints, parameter names, and default values. Nullable reference metadata is deliberately excluded from that CLR-signature comparison.
- The package installs into a clean consumer project, contains the XML API documentation, reads a font, creates a smaller subset, and reads that subset back.

The remaining 14 compiler warnings predate this cleanup and are not suppressed:

| Diagnostic | Count | Explanation |
|---|---:|---|
| `CS0219` | 2 | Unused `useMatrix` and `hasScale` locals in the legacy WOFF2 outline decoder (not used by the standalone converter) |
| `CS0414` | 1 | The assigned but unused `Glyph._onlyLayoutEssMode` marker |
| `CS0649` | 11 | Unassigned fields in legacy bitmap glyph-format/component records |

Implementing incomplete decoders or removing legacy state is outside this contract/documentation cleanup.

## Optional data is not an empty value

| Surface | When null is meaningful | Caller guidance |
|---|---|---|
| `OpenFontReader.Read` / web-font preview decoding | A collection was supplied without selecting a member, or decoding could not complete | Check the returned value; select collection offsets from `ReadPreview` |
| `Typeface.Filename` | No application filename was assigned | Assign it yourself or show an explicit placeholder |
| `Typeface` / `NameEntry` name strings | The corresponding name record is absent | Check for null before using it; do not assume every font declares every name |
| `PreviewFontInfo.NameEntry` / `OS2Table` | The preview represents a whole collection | Check `IsFontCollection` and inspect individual members |
| `Glyph.GlyphPoints` / `EndPoints` | The glyph is CFF, bitmap, or layout-only after trimming | Use both arrays only when present; null is different from an empty TrueType outline |
| `Glyph.GetCff1GlyphData` / CFF names | The glyph is not a loaded CFF glyph, or its name is absent | Check the result; do not invent TrueType data for CFF glyphs |
| `Glyph.MathGlyphInfo` / `Typeface.MathConsts` | Mathematical layout data is absent | Check availability before accessing MATH data |
| `Typeface.GSUBTable` / `GPOSTable` | The font lacks the corresponding layout table | Treat the feature as unavailable; a missing table is not a parsed empty table |
| `Typeface.COLRTable` / `CPALTable` | Color tables are absent or unloaded by trimming | Check actual table values and trim state, not only cached availability flags |
| `Languages` language arrays / script lists | Metadata or layout script lists were not declared | Distinguish undeclared coverage from an explicitly empty declaration |
| Versioned/optional low-level table records and CFF metadata | A format, version, flag, or zero offset omits the data | Follow each member's XML documentation and the owning table's format |
| Web-font decompression delegates | The host has not configured a decoder | Configure the appropriate callback before reading compressed web-font data |
| `TrimDown` result | The typeface is already trimmed | Keep the first non-null ticket; do not replace it with the result of a second trim |

Collection previews retain their existing convenience defaults: for example, `PostScriptName` is an empty string and `WeightClass` is zero on the collection itself. This differs deliberately from raw optional name-table records.

## Safe migration example

```csharp
using OpenFontSharp;

var font = new OpenFontReader().Read(File.OpenRead("Font.ttf"))
    ?? throw new InvalidOperationException("The font could not be read.");
var glyph = font.GetGlyph(font.GetGlyphIndex('A'));
if (glyph.GlyphPoints is { } points && glyph.EndPoints is { } ends)
{
    Console.WriteLine($"{points.Length} points in {ends.Length} contours");
}
else if (glyph.GetCff1GlyphData() is { } cff)
{
    Console.WriteLine(cff.Name ?? "(unnamed CFF glyph)");
}
```

Avoid adding `!` at all call sites just to silence the new warnings. Check the format and presence of data, and keep existing error handling for unreadable fonts.

## Required parser and interpreter state

Some internal members are populated only during table loading or interpreter setup. Nullable backing storage and guarded access express this lifecycle instead of initializing the members to fabricated tables, strings, or arrays. Format-specific operations retain their preconditions and failure paths; guards preserve the prior `NullReferenceException` category for missing required state while providing a diagnostic message.

Constructing a low-level table directly does not load it. Likewise, `GetMathGlyphInfo` requires a loaded MATH table, `UnderlinePosition` and non-CFF glyph-name enumeration require a post table, and bitmap reading requires loaded bitmap data. Nullable annotations do not make those operations valid for every format.

Trimming keeps glyph IDs, bounds, and advance metrics but removes outline and related data. `RestoreUp` still attempts restoration only when trimmed; a header mismatch leaves the typeface trimmed. Check the resulting state and use the original font stream and the first restoration ticket.

## WOFF2 conversion and subsetting

`Woff2Converter.ToSfnt` converts standalone WOFF2 fonts to plain sfnt bytes, including transformed TrueType `glyf`/`loca` and `hmtx` tables. `FontSubsetter.Subset` and `OpenFontReader` use this converter. Supply `Woff2DefaultBrotliDecompressFunc.DecompressHandler` at startup, or pass a decoder directly to the converter. Without a decoder, conversion throws `NotSupportedException`; malformed data throws `InvalidDataException`. Input, declared sfnt size, and reconstructed output are limited to 128 MiB; the decompressed table-data stream is limited to 64 MiB. The converter bounds decompression and reconstruction rather than trusting file-declared allocation sizes. WOFF2 collections remain unsupported.

Transformed composite graphs are checked for cycles and limited to 16 nesting levels, 65,535 expanded points/contours per glyph, and 4,194,304 expanded points across the font.

Verification includes synthetic glyph streams, composite glyphs, horizontal metrics, malformed input, faithful subsetting of the existing Roboto WOFF2 fixture against its TTF counterpart, and a Brotli-compressed WOFF2 wrapper built in the test from the existing Great Vibes OTF. This is not a claim of exhaustive real-world font coverage.

## Colour and bitmap tables

The read-only `Typeface.GetColorLayers(ushort)` API exposes COLR v0 layer glyph IDs and palette entry indices in painting order. Palette index `0xFFFF` denotes the foreground colour. `PaletteCount` and `GetPalette(int)` expose CPAL v0/v1 colour records in BGRA order with sRGB colour components. Absent or trimmed layers give an empty list; absent, trimmed, or out-of-range palette access throws `ArgumentOutOfRangeException`. Returned lists and their value-type entries cannot be modified, and changing the legacy low-level table arrays does not change these views.

COLR versions other than 0 and CPAL versions other than 0/1 throw `OpenFontNotSupportedException` naming the table and version. CPAL v1 optional metadata is bounds-checked but not exposed. Colour record and base/layer counts are bounded by their 16-bit fields (65,535); shared palette and layer ranges are not expanded into copies. Invalid table-local offsets, truncated records, invalid layer ranges, unordered base glyphs, and out-of-range glyph/palette references throw `InvalidDataException`.

Bitmap-only EBLC/EBDT (version 2.0) and CBLC/CBDT (version 3.0) fonts support index subtable formats 1–5, including sparse ranges and shared metrics. Horizontal strikes are supported; vertical layouts throw a specific `OpenFontNotSupportedException`, and reserved layout flags throw `InvalidDataException`. The existing glyph API selects the first strike; every strike is validated. Hybrid fonts continue to use outlines instead of bitmap strikes. EBDT image formats 1, 2, 5, 6, 7 and CBDT image formats 17, 18, 19 expose metrics and encoded image bytes through `GetGlyph` / `ReadBitmapContent`. EBDT bytes remain packed pixels; CBDT 17–19 bytes are the embedded PNG payload, not decoded pixels.

Unsupported bitmap index/image formats (including composite EBDT image formats 8/9), versions, or bit depths throw a specific `OpenFontNotSupportedException`. Invalid ranges, truncated images, offsets outside their own tables, and excessive counts throw `InvalidDataException`, not array-index exceptions. Location tables are capped at 64 MiB, data tables at 256 MiB, strikes at 1,024, index subtables per strike at 65,536, and indexed glyph slots across strikes at 1,048,576. Per-strike glyph IDs are 16-bit and must fit `maxp.numGlyphs`.

Colour/bitmap rendering and colour/bitmap-aware subsetting remain out of scope. Outline subsets drop these tables; bitmap-only fonts are returned whole by the subsetter. Synthetic tests verify reading and malformed-data handling, not visual rendering or real-world colour font interoperability.

## Subset Unicode cmap

The existing subsetting overloads still write an empty cmap for glyph-addressed embedding. Opt in with `includeUnicodeCmap: true` on the byte, collection-member, or `Typeface` overload to write Windows Unicode BMP format 4 and full-repertoire format 12 subtables. Format 12 includes the BMP mappings as well as supplementary scalars; format 4 reserves U+FFFF for its terminal missing-glyph segment (a source mapping for this noncharacter survives in format 12). Mappings to removed glyphs or glyph zero, and surrogate code points, are omitted. All aliases of retained glyphs are included; callers need not provide a separate code-point list. Fonts returned whole retain their original cmap and empty ID map.

The builder selects a source Unicode subtable, preferring full repertoire over BMP (Windows 3/10, Unicode 0/6, Unicode 0/4, Windows 3/1, then legacy Unicode encodings). Source formats 0, 4, 6, 10, 12 and 13 are supported. Other selected formats throw `NotSupportedException`; absent Unicode mappings produce empty subtables. Symbol and legacy non-Unicode encodings are not reinterpreted as Unicode. Variation sequences (format 14) are not retained. Malformed selected subtables throw `InvalidDataException`, with offsets/counts bounded by their table and ranges bounded by Unicode. A BMP mapping too large for format 4's 65,535-byte length limit throws `NotSupportedException` rather than silently dropping mappings.

Synthetic tests re-read BMP and supplementary mappings through `OpenFontReader`, check both output subtables independently, non-monotonic remapping, aliases, dense BMP arrays, malformed input, and byte-identical default output. CFF and collection paths are also checked. External renderer interoperability is untested. Layout and kerning tables remain dropped.

## Other existing limitations

- The basic shaper applies GPOS PairPos (`kern`) and MarkBasePos (`mark`) positioning only when explicitly enabled. It remains a basic Latin/Cyrillic/Greek shaper, not a complex-script, bidi, supplementary-plane, or Indic-reordering engine.
- The `FontInfo` text measurement overload is a legacy character-indexed width helper; use the `Typeface` overload for cmap-based measurement.
- Subset output has no normal Unicode cmap coverage unless explicitly enabled as above, and always drops layout tables.
- CFF1 subsetting supports name-keyed and CID-keyed fonts (FDSelect formats 0 and 3) and glyphs using the deprecated `seac` operator (components are retained); CFF2 and Type 1 charstrings are not subset.

`ToUnicodeCMapBuilder` accepts a Unicode scalar or a non-empty scalar sequence per CID, including supplementary-plane characters. Destinations are written as UTF-16BE without a BOM, and valid consecutive mappings may be emitted as `bfrange`; CMap blocks are limited to 100 entries.

See the [README examples](../README.md) for choosing an appropriate workflow. These remaining limitations are not changed by WOFF2 conversion support.
