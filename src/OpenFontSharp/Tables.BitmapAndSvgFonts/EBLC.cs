//MIT, 2017-present, WinterDev
//MIT, 2015, Michael Popoloski, WinterDev

namespace OpenFontSharp.Tables.BitmapAndSvgFonts
{
    /// <summary>Embedded bitmap location data.</summary>
    class EBLC : BitmapLocationTable
    {
        public const string _N = "EBLC";
        public override string Name => _N;
        protected override ushort MajorVersion => 2;
    }
}
