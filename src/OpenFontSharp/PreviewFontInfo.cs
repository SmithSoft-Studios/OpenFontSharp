//Apache2, 2017-present, WinterDev
//Apache2, 2014-2016, Samuel Carlsson, WinterDev


using OpenFontSharp.AdditionalInfo;
using OpenFontSharp.Tables;

namespace OpenFontSharp
{
    /// <summary>Lightweight font metadata, or a collection of member previews.</summary>
    public class PreviewFontInfo
    {
        /// <summary>The family name, collection label, or null when the font name record is absent.</summary>
        public readonly string? Name;
        /// <summary>The style name, an empty string for collections, or null when the record is absent.</summary>
        public readonly string? SubFamilyName;
        /// <summary>The translated OS/2 style flags; unset for collection previews.</summary>
        public readonly TranslatedOS2FontStyle OS2TranslatedStyle;
        /// <summary>The raw OS/2 selection flags; zero for collection previews.</summary>
        public readonly OS2FsSelection OS2FsSelection;

        readonly PreviewFontInfo[]? _ttcfMembers;

        /// <summary>Gets declared language coverage; collection previews have no member-specific coverage.</summary>
        public Languages Languages { get; }
        /// <summary>Gets the name table, or null for a collection preview.</summary>
        public NameEntry? NameEntry { get; }
        /// <summary>Gets the OS/2 table, or null for a collection preview.</summary>
        public OS2Table? OS2Table { get; }

        internal PreviewFontInfo(
            NameEntry nameEntry,
            OS2Table os2Table,
            Languages langs)
        {
            NameEntry = nameEntry;
            OS2Table = os2Table;
            Languages = langs;

            Name = nameEntry.FontName;
            SubFamilyName = nameEntry.FontSubFamily;
            OS2TranslatedStyle = TypefaceExtensions.TranslateOS2FontStyle(os2Table);
            OS2FsSelection = TypefaceExtensions.TranslateOS2FsSelection(os2Table);
        }
        internal PreviewFontInfo(string fontName, PreviewFontInfo[] ttcfMembers)
        {
            Name = fontName;
            SubFamilyName = "";
            _ttcfMembers = ttcfMembers;
            Languages = new Languages();
        }

        /// <summary>Gets the typographic family name, or an empty string when unavailable.</summary>
        public string TypographicFamilyName => (NameEntry?.TypographicFamilyName) ?? string.Empty;
        /// <summary>Gets the typographic style name, or an empty string when unavailable.</summary>
        public string TypographicSubFamilyName => (NameEntry?.TypographyicSubfamilyName) ?? string.Empty;
        /// <summary>Gets the PostScript name, or an empty string when unavailable.</summary>
        public string PostScriptName => (NameEntry?.PostScriptName) ?? string.Empty;
        /// <summary>Gets the unique font identifier, or an empty string when unavailable.</summary>
        public string UniqueFontIden => (NameEntry?.UniqueFontIden) ?? string.Empty;
        /// <summary>Gets the version string, or an empty string when unavailable.</summary>
        public string VersionString => (NameEntry?.VersionString) ?? string.Empty;
        /// <summary>Gets the OS/2 weight class, or zero for collection previews.</summary>
        public ushort WeightClass => (OS2Table != null) ? OS2Table.usWeightClass : ushort.MinValue;
        /// <summary>Gets the OS/2 width class, or zero for collection previews.</summary>
        public ushort WidthClass => (OS2Table != null) ? OS2Table.usWidthClass : ushort.MinValue;


        /// <summary>Gets the member's offset from the start of the original collection stream.</summary>
        public int ActualStreamOffset { get; internal set; }
        /// <summary>Gets whether the preview was read from a WOFF container.</summary>
        public bool IsWebFont { get; internal set; }
        /// <summary>Gets whether this preview represents a font collection.</summary>
        public bool IsFontCollection => _ttcfMembers != null;
        /// <summary>
        /// get font collection's member count
        /// </summary>
        public int MemberCount => _ttcfMembers!.Length;
        /// <summary>
        /// get font collection's member
        /// </summary>
        /// <param name="index">Zero-based member index; call only when <see cref="IsFontCollection"/> is true.</param>
        /// <returns>The selected member preview.</returns>
        /// <exception cref="IndexOutOfRangeException">The member index is outside the collection.</exception>
        public PreviewFontInfo GetMember(int index) => _ttcfMembers![index];
#if DEBUG
        public override string ToString()
        {
            return (IsFontCollection) ? Name : Name + ", " + SubFamilyName + ", " + OS2TranslatedStyle;
        }
#endif
    }


}