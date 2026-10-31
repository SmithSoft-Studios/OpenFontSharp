# OpenFontSharp
OpenFontSharp is a Lightweight Pure C# library that allows Reading various Font type information and is based on the project [Typography.OpenFont](https://github.com/LayoutFarm/Typography)  

# Cross Platform
The OpenFontSharp library is a cross-platform library.
You can use the library to read various font files (.ttf, .otf, .ttc, .otc, .woff, .woff2)

# Font subsetting
`OpenFontSharp.Subsetting.FontSubsetter` keeps only the glyphs you use (plus `.notdef` and any composite components), renumbers them contiguously and returns the subset font with an old-to-new glyph id map. Glyph outlines are copied verbatim, never re-encoded.

```csharp
var result = FontSubsetter.Subset(fontBytes, usedGlyphIds);        // or Subset(typeface, usedGlyphIds)
var member = FontSubsetter.Subset(ttcBytes, fontIndex, usedGlyphIds); // one font of a collection
```

The output is always a plain TrueType (`0x00010000`) or CFF (`OTTO`) font. When every glyph is requested the font is returned whole and `GlyphIdMap` is empty. A format that cannot be subset throws `NotSupportedException`; the subsetter never returns a font whose outlines are missing.

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
