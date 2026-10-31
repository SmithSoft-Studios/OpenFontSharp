//Apache2, 2016-present, WinterDev
//https://docs.microsoft.com/en-us/typography/opentype/spec/chapter2#script-table-and-language-system-record

namespace OpenFontSharp.Tables.AdvancedLayout
{
    //Script Table and Language System Record 
    //A Script table identifies each language system that defines how to use the glyphs in a script for a particular language.
    //It also references a default language system that defines how to use the script's glyphs in the absence of language-specific knowledge.

    //A Script table begins with an offset to the Default Language System table (DefaultLangSys), 
    //which defines the set of features that regulate the default behavior of the script. 
    //Next, Language System Count (LangSysCount) defines the number of language systems (excluding the DefaultLangSys) that use the script. 
    //In addition, an array of Language System Records (LangSysRecord) defines each language system (excluding the default)
    //with an identification tag (LangSysTag) and an offset to a Language System table (LangSys). 
    //The LangSysRecord array stores the records alphabetically by LangSysTag.

    //If no language-specific script behavior is defined, the LangSysCount is set to zero (0), and no LangSysRecords are allocated.
    //-----------------------
    //Script table
    //Type 	        Name 	                      Description
    //Offset16 	    defaultLangSys 	              Offset to DefaultLangSys table-from beginning of Script table-may be NULL
    //uint16 	    langSysCount 	              Number of LangSysRecords for this script-excluding the DefaultLangSys
    //LangSysRecord langSysRecords[langSysCount]  Array of LangSysRecords-listed alphabetically by LangSysTag

    //-----------------------
    //LangSysRecord
    //Type 	    Name 	    Description
    //Tag 	    langSysTag 	4-byte LangSysTag identifier
    //Offset16 	langSysOffset 	Offset to LangSys table-from beginning of Script table
    //-----------------------
    //
    //Language System Table
    //-----------------------
    //The Language System table (LangSys) identifies language-system features 
    //used to render the glyphs in a script. (The LookupOrder offset is reserved for future use.)

    //Optionally, a LangSys table may define a Required Feature Index (ReqFeatureIndex) to specify one feature as required 
    //within the context of a particular language system. For example, in the Cyrillic script,
    //the Serbian language system always renders certain glyphs differently than the Russian language system.

    //Only one feature index value can be tagged as the ReqFeatureIndex.
    //This is not a functional limitation, however, because the feature and lookup definitions in OpenType
    //Layout are structured so that one feature table can reference many glyph substitution and positioning lookups.
    //When no required features are defined, then the ReqFeatureIndex is set to 0xFFFF.

    //All other features are optional. For each optional feature,
    //a zero-based index value references a record (FeatureRecord) in the FeatureRecord array, 
    //which is stored in a Feature List table (FeatureList). 
    //The feature indices themselves (excluding the ReqFeatureIndex) are stored in arbitrary order in the FeatureIndex array.
    //The FeatureCount specifies the total number of features listed in the FeatureIndex array.

    //Features are specified in full in the FeatureList table, FeatureRecord, and Feature table, 
    //which are described later in this chapter.
    //Example 2 at the end of this chapter shows a Script table, LangSysRecord, and LangSys table used for contextual positioning in the Arabic script.

    //---------------------
    //LangSys table
    //Type 	    Name 	                    Description
    //Offset16 	lookupOrder 	            = NULL (reserved for an offset to a reordering table)
    //uint16 	requiredFeatureIndex        Index of a feature required for this language system- if no required features = 0xFFFF
    //uint16 	featureIndexCount 	            Number of FeatureIndex values for this language system-excludes the required feature
    //uint16 	featureIndices[featureIndexCount] 	Array of indices into the FeatureList-in arbitrary order
    //---------------------
    /// <summary>
    /// Stores the language-system records associated with one OpenType script.
    /// </summary>
    public class ScriptTable
    {
        /// <summary>Gets the four-byte OpenType script tag.</summary>
        public uint scriptTag { get; internal set; }
        /// <summary>Gets the default language system, or <see langword="null"/> when none is defined.</summary>
        public LangSysTable? defaultLang { get; private set; }
        /// <summary>Gets the language-specific systems defined for this script.</summary>
        public LangSysTable[] langSysTables { get; private set; } = Array.Empty<LangSysTable>();

        /// <summary>Gets the four-byte script tag as a string.</summary>
        public string ScriptTagName => Utils.TagToString(this.scriptTag);

        /// <summary>
        /// Reads a Script table at the specified stream offset.
        /// </summary>
        /// <param name="reader">The reader for the OpenType data.</param>
        /// <param name="beginAt">The offset of the Script table.</param>
        /// <returns>The parsed script table.</returns>
        public static ScriptTable CreateFrom(BinaryReader reader, long beginAt)
        {
            reader.BaseStream.Seek(beginAt, SeekOrigin.Begin);
            //---------------
            //Script table
            //Type 	        Name 	                      Description
            //Offset16 	    defaultLangSys 	              Offset to DefaultLangSys table-from beginning of Script table-may be NULL
            //uint16 	    langSysCount 	              Number of LangSysRecords for this script-excluding the DefaultLangSys
            //LangSysRecord langSysRecords[langSysCount]  Array of LangSysRecords-listed alphabetically by LangSysTag

            //---------------
            ScriptTable scriptTable = new ScriptTable();
            ushort defaultLangSysOffset = reader.ReadUInt16();
            ushort langSysCount = reader.ReadUInt16();
            LangSysTable[] langSysTables = scriptTable.langSysTables = new LangSysTable[langSysCount];
            for (int i = 0; i < langSysCount; ++i)
            {
                //-----------------------
                //LangSysRecord
                //Type 	    Name 	        Description
                //Tag 	    langSysTag  	4-byte LangSysTag identifier
                //Offset16 	langSysOffset 	Offset to LangSys table-from beginning of Script table
                //-----------------------

                langSysTables[i] = new LangSysTable(
                    reader.ReadUInt32(),  //	4-byte LangSysTag identifier
                    reader.ReadUInt16()); //offset
            }

            //-----------
            if (defaultLangSysOffset > 0)
            {
                scriptTable.defaultLang = new LangSysTable(0, defaultLangSysOffset);
                reader.BaseStream.Seek(beginAt + defaultLangSysOffset, SeekOrigin.Begin);
                scriptTable.defaultLang.ReadFrom(reader);
            }


            //-----------
            //read actual content of each table
            for (int i = 0; i < langSysCount; ++i)
            {
                LangSysTable langSysTable = langSysTables[i];
                reader.BaseStream.Seek(beginAt + langSysTable.offset, SeekOrigin.Begin);
                langSysTable.ReadFrom(reader);
            }

            return scriptTable;
        }

#if DEBUG
        public override string ToString()
        {
            return Utils.TagToString(this.scriptTag);
        }
#endif

        /// <summary>
        /// Stores the feature indices and required feature for one language system.
        /// </summary>
        public class LangSysTable
        {
            //The Language System table (LangSys) identifies language-system features 
            //used to render the glyphs in a script. (The LookupOrder offset is reserved for future use.)
            //
            /// <summary>Gets the language-system tag, or zero for the default system.</summary>
            public uint langSysTagIden { get; private set; }
            internal readonly ushort offset;

            //
            /// <summary>Gets the indices of optional features for this language system.</summary>
            public ushort[] featureIndexList { get; private set; } = Array.Empty<ushort>();
            /// <summary>Gets the required feature index, or 0xFFFF when no feature is required.</summary>
            public ushort RequiredFeatureIndex { get; private set; }

            /// <summary>Creates a language-system record with its tag and table offset.</summary>
            /// <param name="langSysTagIden">The four-byte language-system tag.</param>
            /// <param name="offset">The offset to the language-system data.</param>
            public LangSysTable(uint langSysTagIden, ushort offset)
            {
                this.offset = offset;
                this.langSysTagIden = langSysTagIden;
            }
            /// <summary>Reads the language-system data at the reader's current position.</summary>
            /// <param name="reader">The reader for the OpenType data.</param>
            public void ReadFrom(BinaryReader reader)
            {
                //---------------------
                //LangSys table
                //Type 	    Name 	                    Description
                //Offset16 	lookupOrder 	            = NULL (reserved for an offset to a reordering table)
                //uint16 	requiredFeatureIndex        Index of a feature required for this language system- if no required features = 0xFFFF
                //uint16 	featureIndexCount 	            Number of FeatureIndex values for this language system-excludes the required feature
                //uint16 	featureIndices[featureIndexCount] 	Array of indices into the FeatureList-in arbitrary order
                //---------------------

                ushort lookupOrder = reader.ReadUInt16();//reserve
                RequiredFeatureIndex = reader.ReadUInt16();
                ushort featureIndexCount = reader.ReadUInt16();
                featureIndexList = Utils.ReadUInt16Array(reader, featureIndexCount);

            }
            /// <summary>Gets whether this language system declares a required feature.</summary>
            public bool HasRequireFeature => RequiredFeatureIndex != 0xFFFF;
            /// <summary>Gets the language-system tag as a string, or an empty string for the default.</summary>
            public string LangSysTagIdenString => (langSysTagIden == 0) ? "" : Utils.TagToString(langSysTagIden);
#if DEBUG
            public override string ToString() => LangSysTagIdenString;
#endif

        }
    }
}