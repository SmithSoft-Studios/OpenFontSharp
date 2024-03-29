﻿//MIT, 2015, Michael Popoloski's SharpFont

namespace OpenFontSharp.TrueTypeInterperter
{

    class InvalidFontException : Exception
    {
        public InvalidFontException() { }
        public InvalidFontException(string msg) : base(msg) { }
    }
    class InvalidTrueTypeFontException : InvalidFontException
    {
        public InvalidTrueTypeFontException() { }
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidTrueTypeFontException"/> class.
        /// </summary>
        public InvalidTrueTypeFontException(string msg) : base(msg) { }
    }

}
