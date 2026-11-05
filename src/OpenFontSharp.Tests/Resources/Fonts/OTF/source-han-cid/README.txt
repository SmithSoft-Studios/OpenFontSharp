OpenFontSharp-CID-Reference.otf is a small CID-keyed subset of Source Han Sans JP
Regular. The source is Adobe's Source Han Sans release at commit
a4f7cf94edfb9d7ffbdfc4841de276358bd7e0f2:
https://github.com/adobe-fonts/source-han-sans/tree/a4f7cf94edfb9d7ffbdfc4841de276358bd7e0f2/SubsetOTF/JP

The subset was made with fontTools 4.68 using --text='AB', retaining .notdef and
its outline and omitting recommended glyphs, layout closure and layout features.
Its primary family and PostScript names were changed to OpenFontSharp CID Test;
the reserved Source name is not used. FontTools independently reports three
glyphs, CIDs 0/34/35 and widths 1000/608/657. The outline reference assertions
for .notdef and A are in RealCidCffSubsetTests.

The font and its license are provided under SIL Open Font License 1.1; see
LICENSE.txt. This test fixture is not a production font.
