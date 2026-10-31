# Changelog

All notable changes are recorded here. Versions follow [Semantic Versioning](https://semver.org) and are set by git tags (`vMAJOR.MINOR.PATCH[-prerelease]`, matching `<Version>` in `src/OpenFontSharp/OpenFontSharp.csproj`).

## [Unreleased]

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
