namespace OpenFontSharp
{
    /// <summary>A COLR v0 layer, in back-to-front painting order.</summary>
    public readonly struct ColorLayer
    {
        internal ColorLayer(ushort glyphId, ushort paletteIndex)
        {
            GlyphId = glyphId;
            PaletteIndex = paletteIndex;
        }

        /// <summary>Gets the glyph providing this layer's outline.</summary>
        public ushort GlyphId { get; }

        /// <summary>Gets the CPAL entry index, or 0xFFFF to use the foreground colour.</summary>
        public ushort PaletteIndex { get; }
    }

    /// <summary>An immutable CPAL colour record with sRGB components in BGRA order.</summary>
    public readonly struct PaletteColor
    {
        internal PaletteColor(byte blue, byte green, byte red, byte alpha)
        {
            Blue = blue;
            Green = green;
            Red = red;
            Alpha = alpha;
        }

        /// <summary>Gets the blue component.</summary>
        public byte Blue { get; }
        /// <summary>Gets the green component.</summary>
        public byte Green { get; }
        /// <summary>Gets the red component.</summary>
        public byte Red { get; }
        /// <summary>Gets the alpha component (255 is opaque).</summary>
        public byte Alpha { get; }
    }
}
