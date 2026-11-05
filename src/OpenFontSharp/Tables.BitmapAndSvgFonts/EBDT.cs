//MIT, 2019-present, WinterDev

namespace OpenFontSharp.Tables.BitmapAndSvgFonts
{
    /// <summary>Embedded monochrome or grayscale bitmap data.</summary>
    class EBDT : CBDT
    {
        public new const string _N = "EBDT";
        public override string Name => _N;
        protected override ushort MajorVersion => 2;
    }
}
