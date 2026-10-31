# Changelog

All notable changes are recorded here. Versions follow [Semantic Versioning](https://semver.org) and are set by git tags (`vMAJOR.MINOR.PATCH[-prerelease]`, matching `<Version>` in `src/OpenFontSharp/OpenFontSharp.csproj`).

## [Unreleased]

### Added
- Subsetting of OpenType fonts with CFF outlines (`.otf`), WOFF 1.0 fonts, and one font of a collection (`FontSubsetter.Subset(fontData, fontIndex, usedGlyphIds)`). The README has a table of the formats the subsetter supports.

### Changed
- `FontSubsetter` throws `NotSupportedException` for formats it cannot subset (WOFF2, CFF2, CID-keyed CFF, a collection without a font index, unrecognised data, a typeface without its original bytes). Before, it returned the input unchanged.

### Fixed
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
