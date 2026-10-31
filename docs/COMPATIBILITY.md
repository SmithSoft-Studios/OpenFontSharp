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
| `CS0219` | 2 | Unused `useMatrix` and `hasScale` locals in the incomplete WOFF2 outline decoder |
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

## Existing limitations remain unchanged

- The basic shaper is not a complex-script, bidi, or supplementary-plane shaping engine and does not apply GPOS pair adjustments.
- The `FontInfo` text measurement overload is a legacy character-indexed width helper; use the `Typeface` overload for cmap-based measurement.
- Subset output has no normal Unicode cmap coverage and drops layout tables. It is intended for glyph-addressed embedding.
- The ToUnicode helper supports one BMP scalar mapping per CID, not multi-character sequences or supplementary-plane UTF-16 encoding.
- WOFF2 transformed-outline decoding remains incomplete.

See the [README examples](../README.md) for choosing an appropriate workflow. These limitations are documented, not changed by the warning cleanup.
