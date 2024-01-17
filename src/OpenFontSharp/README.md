Pure Lightweight C# Font Reader
---

We needed a way to read font files, returning the font information in C# cross-platform, so we created this library.

---
Cross Platform
---

The Typography library is **a cross-platform library** and does **NOT** have other dependancies.

You can use the library to read font files (.ttf, .otf, .ttc, .otc, .woff, .woff2) and

1) Access [all information inside the font](OpenFontSharp/Typeface.cs). 

---
Project arrangement: The purpose of each project
---

The core module OpenFontSharp.OpenFont.
 
**OpenFontSharp.OpenFont**

- This project is the core and does not depend on other projects.
- This project contains [a font reader](OpenFontSharp.OpenFont/OpenFontReader.cs) that can read files implementing Open Font Format
  ([ISO/IEC 14496-22:2015](http://www.iso.org/iso/home/store/catalogue_ics/catalogue_detail_ics.htm?csnumber=66391) and [Microsoft OpenType Specification](https://www.microsoft.com/en-us/Typography/OpenTypeSpecification.aspx))
  or Web Open Font Format (either WOFF [1.0](https://www.w3.org/TR/2012/REC-WOFF-20121213/) or [2.0](https://www.w3.org/TR/WOFF2/))

-----------
License
-----------

The project is based on multiple open-sourced projects (listed below) **all using permissive licenses**.

A license for a whole project is [**MIT**](https://opensource.org/licenses/MIT).

But if you copy source code directly, please check each source file's header for the licensing info if available.

 
**Font** 

Apache2, 2014-2016, Samuel Carlsson, Big thanks for https://github.com/vidstige/NRasterizer

MIT, 2015, Michael Popoloski, https://github.com/MikePopoloski/SharpFont

The FreeType Project LICENSE (3-clauses BSD style),2003-2016, David Turner, Robert Wilhelm, and Werner Lemberg and others, https://www.freetype.org/

Apache2, 2018, Apache/PDFBox Authors,  https://github.com/apache/pdfbox

Apache2, 2020, Adobe Font Development Kit for OpenType (AFDKO), https://github.com/adobe-type-tools/afdko
