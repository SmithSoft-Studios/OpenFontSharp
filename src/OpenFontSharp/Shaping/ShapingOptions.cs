namespace OpenFontSharp.Shaping;

/// <summary>Options controlling the basic text shaper.</summary>
public sealed class ShapingOptions
{
    /// <summary>
    /// Gets or sets whether the basic shaper applies GPOS 'kern' PairPos and 'mark'
    /// MarkBasePos lookups. The default is <see langword="false"/> to preserve the
    /// existing shaping behavior.
    /// </summary>
    public bool EnableGposPositioning { get; init; }
}
