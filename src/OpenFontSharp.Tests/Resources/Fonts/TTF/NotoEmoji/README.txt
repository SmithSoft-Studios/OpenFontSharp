OpenFontSharp-Noto-COLRv1.ttf is a small subset of Google Noto Emoji's
Noto-COLRv1.ttf, licensed under SIL Open Font License 1.1 (see OFL.txt).
The original copyright and naming metadata are retained in the font.

Source:
https://github.com/googlefonts/noto-emoji/blob/99701e07d3d1fe1bdbcdb14964547f0fd592966f/2D/fonts/Noto-COLRv1.ttf
License:
https://github.com/googlefonts/noto-emoji/blob/99701e07d3d1fe1bdbcdb14964547f0fd592966f/2D/fonts/LICENSE

Generated with fontTools 4.60.2:
python -m fontTools.subset Noto-COLRv1.ttf --unicodes=U+2764,U+1F355,U+1F600 --notdef-glyph --notdef-outline --name-IDs='*' --output-file=OpenFontSharp-Noto-COLRv1.ttf

Independent fontTools inspection: 37 glyphs; COLR version 1;
3 BaseGlyphPaintRecords; 0 v0 BaseGlyphRecords; 0 v0 LayerRecords.
U+1F355, U+1F600 and U+2764 map to glyphs 1, 2 and 3.
All advances are 1275 font units. The emoji base glyph outlines are empty;
ordinary layer outlines and the .notdef outline are retained. Glyph 0 has
8 points, glyph 4 has 35 points, and glyph 5 has 38 points.

This fixture tests loading and outline-only subsetting, not colour rendering.
