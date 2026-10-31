//Apache2, 2016-present, WinterDev

namespace OpenFontSharp.Tables.AdvancedLayout
{
    /// <summary>
    /// Maps OpenType script tags to their parsed script tables.
    /// </summary>
    public class ScriptList : Dictionary<uint, ScriptTable>
    {
        //https://docs.microsoft.com/en-us/typography/opentype/spec/chapter2
        // The ScriptList identifies the scripts in a font,
        // each of which is represented by a Script table that contains script and language-system data.
        // Language system tables reference features, which are defined in the FeatureList.
        // Each feature table references the lookup data defined in the LookupList that describes how, when, and where to implement the feature.
        private ScriptList() { }

        /// <summary>
        /// Gets the script table for a tag, or <see langword="null"/> when the tag is absent.
        /// </summary>
        /// <param name="tagName">The four-byte OpenType script tag.</param>
        /// <returns>The matching script table, if present.</returns>
        public new ScriptTable? this[uint tagName] => TryGetValue(tagName, out ScriptTable? ret) ? ret : null;

        /// <summary>
        /// Reads a ScriptList table at the specified stream offset.
        /// </summary>
        /// <param name="reader">The reader for the OpenType data.</param>
        /// <param name="beginAt">The offset of the ScriptList table.</param>
        /// <returns>The parsed script list.</returns>
        public static ScriptList CreateFrom(BinaryReader reader, long beginAt)
        {

            // ScriptList table
            // Type           Name                          Description
            // uint16         scriptCount                   Number of ScriptRecords
            // ScriptRecord   scriptRecords[scriptCount]    Array of ScriptRecords,listed alphabetically by ScriptTag
            //                                   
            // ScriptRecord
            // Type      Name             Description
            // Tag       scriptTag        4-byte ScriptTag identifier
            // Offset16  scriptOffset     Offset to Script table-from beginning of ScriptList

            reader.BaseStream.Seek(beginAt, SeekOrigin.Begin);
            
            ushort scriptCount = reader.ReadUInt16();
            ScriptList scriptList = new ScriptList();

            // Read records (tags and table offsets)
            uint[] scriptTags = new uint[scriptCount];
            ushort[] scriptOffsets = new ushort[scriptCount];
            for (int i = 0; i < scriptCount; ++i)
            {
                scriptTags[i] = reader.ReadUInt32();
                scriptOffsets[i] = reader.ReadUInt16();
            }

            // Read each table and add it to the dictionary
            for (int i = 0; i < scriptCount; ++i)
            {
                ScriptTable scriptTable = ScriptTable.CreateFrom(reader, beginAt + scriptOffsets[i]);
                scriptTable.scriptTag = scriptTags[i];
                scriptList.Add(scriptTags[i], scriptTable);
            }

            return scriptList;
        }
    }
}
