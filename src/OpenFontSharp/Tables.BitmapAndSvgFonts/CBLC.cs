//MIT, 2019-present, WinterDev

namespace OpenFontSharp.Tables.BitmapAndSvgFonts
{
    /// <summary>Color bitmap location data.</summary>
    class CBLC : BitmapLocationTable
    {
        public const string _N = "CBLC";
        public override string Name => _N;
        protected override ushort MajorVersion => 3;
    }
}
