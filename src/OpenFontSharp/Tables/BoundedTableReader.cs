namespace OpenFontSharp.Tables
{
    // Validate table-local ranges before seeking, reading, or allocating.
    internal sealed class BoundedTableReader
    {
        readonly BinaryReader _reader;
        readonly long _start;
        readonly uint _length;
        readonly string _name;

        internal BoundedTableReader(BinaryReader reader, uint length, string name)
        {
            _reader = reader;
            _start = reader.BaseStream.Position;
            _length = length;
            _name = name;
            if (_start < 0 || _start > reader.BaseStream.Length ||
                length > reader.BaseStream.Length - _start)
                throw new InvalidDataException($"{name} table is truncated.");
        }

        internal void RequireRange(ulong offset, ulong length)
        {
            if (offset > _length || length > _length - offset)
                throw new InvalidDataException($"{_name} count or offset exceeds the table bounds.");
        }

        internal void Seek(uint offset, ulong length)
        {
            RequireRange(offset, length);
            _reader.BaseStream.Position = _start + offset;
        }
    }
}
