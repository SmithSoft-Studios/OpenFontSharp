# Test coverage matrix

This matrix maps the public features and recent fixes to the tests that exercise them.
“Synthetic” means hand-built or mutated test data; “real font” names a committed fixture.
Independent references are called out separately from the library's own reader.

## Reading and conversion

| Capability | Tests and evidence | Failure-path evidence |
|---|---|---|
| TrueType reading and previews | `OpenFontReaderTests.Read_RobotoTtf_ReturnsTypefaceWithMetrics`, `ReadPreview_RobotoTtf_ReturnsAllFonts`; real Roboto (Apache-2.0) | No general malformed-sfnt reader matrix |
| Name-keyed CFF reading | `OpenFontReaderTests.Read_GreatVibesOtf_ReturnsTypeface`, `NullableContractTests.CffGlyph_HasCffDataButNoTrueTypeArrays`; real Great Vibes (OFL-1.1) | No general malformed-CFF reader matrix |
| CID-keyed CFF reading and subsetting | `RealCidCffSubsetTests.SourceHanCidFont_SubsetsOutlinesCidsWidthsAndGlyphCountAgainstFontToolsReference`; real Source Han Sans JP subset (OFL-1.1) | Corrupt FDSelect input is bounded by the CFF parser; synthetic format 0/3 builder tests are in `CffSubsetBuilderTests` |
| CFF2 reading and default-instance subsetting | `Cff2DefaultInstanceTests.OpenTypeCff2_IsReadableAtItsDefaultInstance`, `OpenTypeCff2_SubsetsToStaticCffAndDropsVariationTables`, `OpenTypeCff2_WithEveryGlyphStillBecomesStatic`; synthetic CFF2 | Synthetic truncated and unsupported charstring cases in `Cff2DefaultInstanceTests` |
| Collections | `NullableContractTests.CollectionPreview_HasMembersButNoMemberSpecificTables`, `SubsetFormatSupportTests.Collection_WithFontIndex_IsSubsetFaithfully`, `CollectionMember_ReadAsTypeface_IsSubsetFaithfully`; real Roboto TTC (Apache-2.0) | `Collection_WithoutFontIndex_ThrowsNotSupported`, `Collection_FontIndexOutOfRange_Throws`, `FontIndex_ForNonCollection_ThrowsNotSupported` |
| WOFF 1 | `SubsetFormatSupportTests.Woff_TrueType_IsDecodedAndSubsetFaithfully`, `Woff_Cff_IsDecodedAndSubsetFaithfully`, `Woff_ReadAsTypeface_IsSubsetFaithfully`; real Roboto and Great Vibes WOFFs (fontTools-generated from licensed fixtures) | Invalid container cases are not a complete WOFF 1 corruption matrix |
| WOFF2 reading | `SubsetFormatSupportTests.Woff2_IsDecodedAndSubsetFaithfully`, `Woff2_ReadAsTypeface_IsSubsetFaithfully`, `Woff2_Preview_MatchesTrueType`; real Roboto WOFF2 (Apache-2.0) | `Woff2ConverterTests.RejectsMalformedHeadersAndTruncation`, `MissingHookIsExplicitAndFailedOrThrowingHooksAreInvalidData` |
| `Woff2Converter` | `Woff2ConverterTests` synthetic transformed/simple/composite glyph and metric tests; `Woff2RealFixtureTests.RealRobotoWoff2_ConvertsToTheSameGlyphsAsTheTtf` | Cycles, depth/expansion limits, truncation, invalid directories and Brotli failures are tested in `Woff2ConverterTests` |

The Roboto WOFF2 fixture was encoded from the committed TTF by fontTools 4.68. The
converter test compares untransformed sfnt tables with the source using the independent
test-only `SfntInspector`; it also compares every glyph's points, advances and cmap through
the public reader. Transformed `glyf`, `loca` and `hmtx` bytes are not expected to be
byte-identical encodings.

## Subsetting

| Input or option | Tests and evidence | Independent reference / failure paths |
|---|---|---|
| Static TrueType bytes and `Typeface` | `SubsetFormatSupportTests.TrueType_IsSubsetFaithfully`, `TrueType_TypefaceOverload_MatchesBytesOverload`; real Roboto and Shantell Sans | `SubsetVerifier` compares all retained outlines, bounds, advances and bearings; `SfntInspector` checks structure/checksums |
| Variable TrueType default instance | `SubsetFormatSupportTests.VariableTrueType_IsSubsetToItsDefaultInstance`; real Shantell Sans variable (OFL-1.1) | Same-font retained glyph comparison; no independent renderer comparison |
| Axis-coordinate overload | `VariableSubsetTests.NormalizesClampsMapsAndInterpolates`, `ExistingVariableFixtureProducesReadableStaticInstance`, and the other synthetic variation tests; synthetic plus real Shantell Sans | Synthetic invalid, non-finite, truncated, cyclic and oversized data cases; no second real variable font with a different axis layout is available in the fixture set |
| Name-keyed CFF | `SubsetFormatSupportTests.OpenTypeCff_IsSubsetFaithfully`, `OpenTypeCff_KeepsGlyphNames`, `Woff_Cff_IsDecodedAndSubsetFaithfully`; real Great Vibes OTF/WOFF (OFL-1.1) | `SubsetVerifier` checks outlines and metrics; `SfntInspector` checks output structure |
| CID-keyed CFF | `RealCidCffSubsetTests.SourceHanCidFont_SubsetsOutlinesCidsWidthsAndGlyphCountAgainstFontToolsReference`; real Source Han Sans JP subset (OFL-1.1) | Independent fontTools 4.68 outline, CID, width and glyph-count values are encoded as test expectations |
| CFF `seac` | `CffSubsetBuilderTests.SeacComposite_RetainsStandardEncodingComponents`, `SeacComposite_SubsetIncludesComponentsInGlyphIdMap`, `SeacComposite_MissingStandardEncodingGlyphCannotBeSubset`, `SeacComposite_NotRetained_DoesNotThrow`, `EndCharWithWidthOnly_IsNotSeac`; synthetic CFF | No real fixture using `seac` is included; no suitable redistributable real font was found for this audit |
| CFF2 | `Cff2DefaultInstanceTests.OpenTypeCff2_SubsetsToStaticCffAndDropsVariationTables`; synthetic CFF2 | No real CFF2 variable font fixture or independent instance comparison |
| Collection member | `SubsetFormatSupportTests.Collection_WithFontIndex_IsSubsetFaithfully`, `CollectionMember_ReadAsTypeface_IsSubsetFaithfully`; real Roboto TTC | Missing/invalid index cases are tested |
| WOFF / WOFF2 | `SubsetFormatSupportTests.Woff_TrueType_IsDecodedAndSubsetFaithfully`, `Woff_Cff_IsDecodedAndSubsetFaithfully`, `Woff2_IsDecodedAndSubsetFaithfully`, `Woff2_ReadAsTypeface_IsSubsetFaithfully`; real Roboto and Great Vibes containers | `SubsetVerifier` checks output outlines/metrics and `SfntInspector` checks the sfnt structure |
| `includeUnicodeCmap` | `SubsetUnicodeCmapTests.OptIn_RoundTripsRetainedUnicodeAndRenumberedGlyphs`, `CffAndCollectionOverloads_KeepUnicodeMappings`, `RealRobotoSubset_MapsEveryRetainedCodePointToItsOriginalOutline`; synthetic BMP/supplementary maps and real Roboto (Apache-2.0) | The real-font test checks every retained source Unicode mapping and uses `SubsetVerifier` for every retained outline; malformed source cmap and format-4 size limits are tested |
| Default cmap behavior | `SubsetUnicodeCmapTests.DefaultAndExplicitFalse_KeepLegacyEmptyCmapAndIdenticalBytes`, `AllGlyphsRetained_StillReturnsOriginalBytesAndEmptyMap` | Byte-for-byte regression checks |
| Bitmap/color-only input behavior | `SubsetFormatSupportTests` does not exercise a real bitmap-only font; `BitmapTableTests` and `ColorTableTests` cover table reading only | Color/bitmap-aware subsetting is not implemented |

## Shaping, mapping, metrics and lifecycle

| Capability | Tests and evidence | Independent reference / failure paths |
|---|---|---|
| Basic Latin defaults and glyph substitution | `ShapingTests.BasicLatinShaper_PlainText_KeepsCmapGlyphsWhenNoLigatureApplies`, `BasicLatinShaper_SingleChar_ReturnsGlyphAndAdvance`, `BasicLatinShaper_EmptyText_ReturnsEmptyResult`; real Roboto and Shantell Sans | Existing regression checks default behavior; no dedicated real-font positive ligature assertion |
| Opt-in GPOS PairPos 1/2 | `SyntheticGposShapingTests.PairPositionFormat1_AppliesBothValueRecordsWhenEnabled`, `PairPositionFormat2_AppliesClassPairAdjustmentsWhenEnabled`; synthetic tables | `RealFontGposShapingTests.GposPairMatchesHarfBuzzReference`; static uharfbuzz 0.57.0 references for AV in Roboto, Shantell Sans and the Lato subset, with GSUB disabled to match the basic shaper's scope |
| Opt-in MarkBasePos and extension lookups | `SyntheticGposShapingTests.MarkToBasePositionsCombiningMarkWhenEnabled`; synthetic table; real Shantell Sans and Lato subset | `RealFontGposShapingTests.MarkBaseMatchesHarfBuzzReference`; static uharfbuzz 0.57.0 references for A + combining cedilla; Shantell's extension lookups are exercised by `Gpos_KernsAVAndToOnRealFonts` |
| GPOS default-off behavior | `RealFontGposShapingTests.Gpos_IsOffByDefault`; real Roboto and Shantell Sans | `SyntheticGposShapingTests.FontWithoutGposAndMalformedLookupOffsetDoNotChangeLegacyShaping` |
| Script detection | `ShapingTests.ScriptDetector_LatinText_DoesNotRequireComplexShaping`, `ScriptDetector_CyrillicText_DoesNotRequireComplexShaping`, `ScriptDetector_ArabicText_RequiresComplexShaping` | Synthetic text input; complex-script shaping itself is out of scope |
| Unicode-to-glyph mapping | `SubsetUnicodeCmapTests.RealRobotoSubset_MapsEveryRetainedCodePointToItsOriginalOutline`; real Roboto | Iterates every retained Unicode code point; `SubsetVerifier` compares outlines and advances |
| `ToUnicodeCMapBuilder` | `ToUnicodeCMapBuilderTests` scalar, sequence, supplementary-plane, range/block and validation cases; synthetic CID-to-Unicode mappings | Exact CMap syntax and scalar encoding expectations; no second CMap implementation is invoked |
| COLR/CPAL | `ColorTableTests`; synthetic COLR v0 and CPAL v0/v1 tables | Truncation, invalid offsets/counts/references and unsupported versions tested; no real color font fixture |
| EBLC/CBLC bitmap strikes | `BitmapTableTests`; synthetic EBLC/EBDT and CBLC/CBDT formats | Truncation, malformed ranges/images, unsupported formats/layouts and resource limits tested; no real bitmap-font fixture |
| Kerning and metrics | `RealFontGposShapingTests.Gpos_KernsAVAndToOnRealFonts`, `MetricsTests` `FontLoader_*`, `TextMeasurer_*`, and `Typeface_GetAllKerningPairs_DoesNotThrow`; real Roboto and Shantell Sans | HarfBuzz references cover GPOS pair positioning; legacy `kern` extraction has only a non-null smoke check |
| Trim and restore | `NullableContractTests.TrimAndRestore_PreservesMetricsAndRestoresFormatSpecificOutlines`; real Roboto, Great Vibes and Roboto WOFF2; color/bitmap trim tests use synthetic tables | Matching restoration preserves metrics/outlines; mismatched restore is not covered by a real-font test |
| Adobe Glyph List first-use thread safety | `AdobeGlyphListConcurrencyTests.ConcurrentFirstUse_ReturnsConsistentResults`; concurrent synthetic calls | 64 workers over repeated lookups; no external implementation is needed |

## Verification commands

CI and local verification run both configurations:

```shell
dotnet test SmithSoftStudios.OpenFontSharp.sln -c Debug
dotnet test SmithSoftStudios.OpenFontSharp.sln -c Release
```

The committed Lato positioning subset is derived from Lato 2.015 (OFL-1.1); the
Source Han CID subset is derived from Source Han Sans JP Regular at the pinned upstream
revision (OFL-1.1). Each fixture directory records its source, subsetting command and
license. The CFF2/`seac`, COLR/CPAL and bitmap gaps above are intentional evidence
limitations, not claims of interoperability with unsupported real fonts.
