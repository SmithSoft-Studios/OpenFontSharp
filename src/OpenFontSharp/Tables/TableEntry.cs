//Apache2, 2017-present, WinterDev
//Apache2, 2014-2016, Samuel Carlsson, WinterDev

using System;
using System.IO;
namespace OpenFontSharp.Tables
{
    /// <summary>
    /// this is base class of all 'top' font table
    /// </summary>
    public abstract class TableEntry
    {
        TableHeader? _header;

        /// <summary>
        /// Initializes a font table entry.
        /// </summary>
        public TableEntry()
        {
        }

        internal TableHeader Header
        {
            get => _header ?? throw new NullReferenceException("The table header has not been assigned.");
            set => _header = value;
        }

        /// <summary>
        /// Reads the table's contents from the font data stream.
        /// </summary>
        /// <param name="reader">The stream positioned at the beginning of this table's data.</param>
        protected abstract void ReadContentFrom(BinaryReader reader);

        /// <summary>
        /// Gets the four-character tag identifying this table.
        /// </summary>
        public abstract string Name { get; }

        internal void LoadDataFrom(BinaryReader reader)
        {
            //ensure that we always start at the correct offset***
            reader.BaseStream.Seek(this.Header.Offset, SeekOrigin.Begin);
            ReadContentFrom(reader);
        }

        /// <summary>
        /// Gets the length of this table in bytes.
        /// </summary>
        /// <exception cref="NullReferenceException">The table header has not been assigned.</exception>
        public uint TableLength => this.Header.Length;

    }
    class UnreadTableEntry : TableEntry
    {
        public UnreadTableEntry(TableHeader header)
        {
            this.Header = header;
        }
        public override string Name => this.Header.Tag;
        //
        protected sealed override void ReadContentFrom(BinaryReader reader)
        {
            //intend ***
            throw new NotImplementedException();
        }

        public bool HasCustomContentReader { get; protected set; }
        public virtual T CreateTableEntry<T>(BinaryReader reader, T expectedResult)
            where T : TableEntry
        {
            throw new NotImplementedException();
        }
#if DEBUG
        public override string ToString()
        {
            return this.Name;
        }
#endif
    }
}
