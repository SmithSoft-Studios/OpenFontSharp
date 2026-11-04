# Third-Party Notices

OpenFontSharp is based in part on **Typography.OpenFont** (LayoutFarm/Typography, by WinterDev and contributors, https://github.com/LayoutFarm/Typography) and on the projects that code credits. Imported files keep the licence in their own header. This file lists those licences, the notices they require, and a statement of the changes SmithSoft made. The full licence texts are in the [licenses](licenses) folder and ship in the NuGet package.

SmithSoft's own original code (`Metrics/`, `Shaping/`, `Subsetting/`, `KerningPair.cs`, added from 2026) is under the MIT licence in [LICENSE.md](LICENSE.md).

## Components and licences

| Origin | Licence (per file header) | Where it appears |
|---|---|---|
| Typography.OpenFont, WinterDev and contributors | Apache-2.0 or MIT, as stated in each file | Most of `Tables*`, `Typeface*`, `OpenFontReader.cs`, `Glyph.cs`, `WebFont/`, `AdditionalInfo/` |
| NRasterizer, Samuel Carlsson (2014-2016), https://github.com/vidstige/NRasterizer | Apache-2.0 | `Bounds.cs`, `ByteOrderSwappingBinaryReader.cs`, `Glyph.cs`, `OpenFontReader.cs`, `Typeface.cs`, `Tables/` core tables, `Tables.TrueType/Glyf.cs`, `Tables.Others/Kern.cs` |
| SharpFont, Michael Popoloski (2015), https://github.com/MikePopoloski/SharpFont | MIT | `Geometry.cs`, `IGlyphTranslator.cs`, `TrueTypeInterperter/`, `Tables.TrueType/Cvt_Programs.cs`, parts of `Tables.BitmapAndSvgFonts/EBLC.cs` |
| The FreeType Project (David Turner, Robert Wilhelm, Werner Lemberg and others) | FreeType License (BSD-style) | Referenced in `IGlyphTranslator.cs` |
| Apache PDFBox / FontBox (2018), https://github.com/apache/pdfbox, including code by Villu Ruusmann | Apache-2.0 | `Tables.CFF/` (CFF parsing and Type 2 charstrings) |
| Sam Hocevar | Apache-2.0 (per file header) | `Tables.AdvancedLayout/COLR.cs`, `CPAL.cs`, parts of `GPOS.cs` and `Tables/CharacterMap.cs` |
| Adobe Glyph List (Adobe Systems Incorporated, 2002-2015) | BSD-3-Clause style | `AdditionalInfo/AdobeGlyphList.cs`, `AdobeGlyphListForNewFont.cs` |
| Adobe Font Development Kit for OpenType (AFDKO) | Apache-2.0 | Credited by the upstream Typography project; no file header in this repository names it. Listed here for completeness. |
| SmithSoft (Pty) Ltd | MIT | Original code described in [LICENSE.md](LICENSE.md) |

## Statement of changes (Apache-2.0 section 4(b))

Files imported from the projects above were modified by SmithSoft (Pty) Ltd after import: migration to current .NET and C# versions, nullable annotations, XML documentation, bug fixes, API additions, and thread-safety and security hardening. Original copyright and licence headers are preserved in each file. The git history records each change.

## Required notices

**FreeType credit.** Portions of this software are copyright (c) 1996-2002, 2006 The FreeType Project (www.freetype.org). All rights reserved. The FreeType License is in [licenses/FTL.txt](licenses/FTL.txt).

**Apache PDFBox NOTICE** (reproduced from the project's NOTICE file and from the headers of `Tables.CFF/CFF.cs` and `CFFTable.cs`):

```
Apache PDFBox
Copyright 2014 The Apache Software Foundation

This product includes software developed at
The Apache Software Foundation (http://www.apache.org/).

Based on source code originally developed in the PDFBox and
FontBox projects.

Copyright (c) 2002-2007, www.pdfbox.org

Based on source code originally developed in the PaDaF project.
Copyright (c) 2010 Atos Worldline SAS

Includes the Adobe Glyph List
Copyright 1997, 1998, 2002, 2007, 2010 Adobe Systems Incorporated.

Includes the Zapf Dingbats Glyph List
Copyright 2002, 2010 Adobe Systems Incorporated.

Includes OSXAdapter
Copyright (C) 2003-2007 Apple, Inc., All Rights Reserved
```

**Adobe Glyph List.** The notice and conditions are in [licenses/BSD-3-Clause-Adobe.txt](licenses/BSD-3-Clause-Adobe.txt). The name of Adobe Systems Incorporated may not be used to endorse or promote products derived from this software without prior written permission.

## Licence texts

- [Apache-2.0](licenses/Apache-2.0.txt)
- [MIT](licenses/MIT.txt)
- [FreeType License](licenses/FTL.txt)
- [Adobe Glyph List (BSD-3-Clause style)](licenses/BSD-3-Clause-Adobe.txt)

## Test fixtures (not shipped in the package)

The test project's font files carry their own licences next to them: Roboto (Apache-2.0), Great Vibes (SIL OFL 1.1), Shantell Sans (SIL OFL 1.1).
