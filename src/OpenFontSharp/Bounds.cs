//Apache2, 2017-present, WinterDev
//Apache2, 2014-2016, Samuel Carlsson, WinterDev

namespace OpenFontSharp
{
    /// <summary>
    /// original glyph bounds
    /// </summary>
    public readonly struct Bounds
    {

        //TODO: will be changed to => public readonly struct Bounds 

        /// <summary>A bounding box with all coordinates equal to zero.</summary>
        public static readonly Bounds Zero = new Bounds(0, 0, 0, 0);
        /// <summary>Creates a bounding box in font design units.</summary>
        /// <param name="xmin">Minimum X coordinate.</param>
        /// <param name="ymin">Minimum Y coordinate.</param>
        /// <param name="xmax">Maximum X coordinate.</param>
        /// <param name="ymax">Maximum Y coordinate.</param>
        public Bounds(short xmin, short ymin, short xmax, short ymax)
        {
            XMin = xmin;
            YMin = ymin;
            XMax = xmax;
            YMax = ymax;
        }

        /// <summary>Gets the minimum X coordinate.</summary>
        public short XMin { get; }
        /// <summary>Gets the minimum Y coordinate.</summary>
        public short YMin { get; }
        /// <summary>Gets the maximum X coordinate.</summary>
        public short XMax { get; }
        /// <summary>Gets the maximum Y coordinate.</summary>
        public short YMax { get; }
#if DEBUG
        public override string ToString()
        {
            return "(" + XMin + "," + YMin + "," + XMax + "," + YMax + ")";
        }
#endif
    }
}
