# Changelog

All notable changes are recorded here. Versions follow [Semantic Versioning](https://semver.org) and are set by git tags (`vMAJOR.MINOR.PATCH[-prerelease]`, matching `<Version>` in `src/OpenFontSharp/OpenFontSharp.csproj`).

## [Unreleased]

### Added
- Opt-in axis-coordinate overload for `FontSubsetter.Subset` instantiates variable TrueType fonts using `fvar`/`avar`, `gvar` with IUP, `HVAR` advances (or phantom-point fallback), and `cvar`, clamping user values to each axis range and producing static subsets.
- Opt-in `includeUnicodeCmap` overloads for `FontSubsetter.Subset` write format 4 (BMP) and format 12 (full Unicode) cmaps mapping retained characters to renumbered glyph IDs. Existing overloads and default output are unchanged; layout tables and variation sequences remain omitted.

## [1.3.0]

### Added
- `FontSubsetter.Subset` now accepts standalone WOFF2 bytes and typefaces read from WOFF2. `OpenFontReader` uses the standalone converter for WOFF2 fonts.
- GPOS positioning through the basic shaper also applies lookups wrapped in extension lookups (type 9), as large fonts use; checked on Roboto and Shantell Sans.
- Opt-in GPOS PairPos format 1/2 kerning and MarkBasePos positioning in `BasicLatinShaper`, with per-glyph placement offsets. The default shaping behavior is unchanged.
- Standalone WOFF2-to-sfnt conversion via `Woff2Converter.ToSfnt`, including transformed TrueType `glyf`/`loca` and `hmtx`, bounded decoding, table checksums, and `head.checkSumAdjustment`. Brotli remains host-supplied; WOFF2 collections are not supported.
- CID-keyed CFF1 subsetting retains the CIDs, remaps FDSelect formats 0/3, and keeps only the Font DICTs used by retained glyphs; subroutines are kept whole.
- `ToUnicodeCMapBuilder` now accepts Unicode scalar sequences per CID, including supplementary code points, and emits valid `bfchar`/`bfrange` blocks.
- Read-only COLR v0 layers and CPAL v0/v1 BGRA palettes via `Typeface.GetColorLayers`, `PaletteCount`, and `GetPalette`, with table-local bounds and glyph/palette reference validation. COLR v1 paint graphs and CPAL v1 metadata APIs remain unsupported.

### Fixed
- CBLC/CBDT and EBLC/EBDT bitmap lookup is keyed by glyph ID. The previous reader returned the bitmap of a different glyph (checked with Noto Color Emoji 2.051: U+1F600 returned the sweat-smile emoji and U+1F355 a bowl of rice); `Typeface.GlyphCount` now follows `maxp.numGlyphs` for these fonts.
- Bitmap-only EBLC/EBDT and CBLC/CBDT reading supports index subtable formats 1–5, sparse glyph IDs and shared metrics instead of unconditional or placeholder throws. Bitmap counts, offsets and image lengths are bounded; unsupported image formats and vertical layouts report specific `OpenFontNotSupportedException` messages. Rendering, composite EBDT images (8/9), and strike selection remain out of scope.
- `BasicLatinShaper` applied every GSUB lookup in the font, not only the `liga` and `clig` ligature features, so ordinary letters were replaced by unrelated alternate glyphs (for example Roboto "Hello" shaped to different glyphs than the cmap). It now applies only lookups of those two features.

## [1.2.3]

### Fixed
- `AdobeGlyphList` lazy initialisation is now thread-safe. Concurrent first use could read half-built dictionaries or throw on a duplicate key.

## [1.2.2]

### Licensing
- Added `THIRD-PARTY-NOTICES.md` and the Apache-2.0, MIT, FreeType and Adobe Glyph List licence texts under `licenses/`, with a statement of changes to imported files, and ship them in the NuGet package.
- `LICENSE.md` now distinguishes SmithSoft original code (MIT) from code imported from Typography.OpenFont and the projects it credits, which keep the licences in their file headers. The README licence section was corrected to match.

### Fixed
- ToUnicode CMap destinations encode supplementary Unicode scalars as UTF-16BE surrogate pairs without a BOM instead of overlong scalar hex values. Source IDs must fit the declared two-byte codespace, and invalid Unicode scalars are explicitly rejected. Existing BMP mappings and 100-entry blocks are retained (SmithSoft-Studios/TrinePDF#341 E7).

## [1.2.0]

### Added
- XML API documentation shipped with the library, product workflows and examples in the README, and nullable migration guidance in [compatibility notes](docs/COMPATIBILITY.md).
- Regression checks for Release nullable/XML warning cleanliness, optional font data, trim/restore, and documentation examples.
- Subsetting of OpenType fonts with CFF outlines (`.otf`), WOFF 1.0 fonts, and one font of a collection (`FontSubsetter.Subset(fontData, fontIndex, usedGlyphIds)`). The README has a table of the formats the subsetter supports.

### Changed
- **Source compatibility:** genuinely optional reference results and fields now advertise nullable metadata. This can produce new warnings in callers using nullable analysis (including build failures with warnings-as-errors); add presence/format guards. Public CLR signatures and valid-font behavior are preserved. See [nullable migration guidance](docs/COMPATIBILITY.md).
- `FontSubsetter` throws `NotSupportedException` for formats it cannot subset (WOFF2, CFF2, CID-keyed CFF, a collection without a font index, unrecognised data, a typeface without its original bytes). Before, it returned the input unchanged.

### Fixed
- Uninitialized-state nullable warnings and missing/incomplete XML API documentation tracked by #9, without fabricated defaults or broad warning suppression.
- A font read from a collection with `OpenFontReader.Read(stream, offset)` kept bytes that were not a valid standalone font, so subsetting it silently produced a broken font. `GetOriginalFontData()` now returns the member as a standalone font.
- Subset fonts now carry a valid `head.checkSumAdjustment` (it was left at zero).

## [1.1.1]

### Fixed
- `FontSubsetter` generated its random subset-tag prefix from a shared, non-thread-safe `Random`; concurrent subsetting could corrupt it and produce a constant prefix. It now uses `Random.Shared`.

## [1.1.0]

First release since the 2024 `1.0.0` package. Targets .NET 10.

### Added
- Font subsetting (`OpenFontSharp.Subsetting.FontSubsetter`, `SubsetResult`) that copies real glyph outlines.
- Basic Latin text shaping (`OpenFontSharp.Shaping`).
- Font metrics utilities and ToUnicode CMap building (`OpenFontSharp.Metrics`).
- GitHub Actions CI on Linux, Windows and macOS, and a tag-driven NuGet release pipeline (see `docs/RELEASING.md`).

### Changed
- Target framework is now `net10.0` (the 2024 package targeted `net8.0`).

## 1.0.0
Published to nuget.org on 2024-01-17.
