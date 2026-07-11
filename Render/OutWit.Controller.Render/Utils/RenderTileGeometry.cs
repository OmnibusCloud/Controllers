namespace OutWit.Controller.Render.Utils;

/// <summary>
/// THE pixel-space mapping for tile geometry, shared by Render.Frame output normalization,
/// the Render.CollectTiles stitch placement and the alpha-blend composer. Boundary-first: every
/// pixel size is a difference of independently rounded BOUNDARIES, never a rounded width —
/// adjacent tiles share the exact boundary pixel, so spans tile an axis with no 1px gaps or
/// overlaps at fractional boundaries. (A rounded delta — round((max-min)·dim) — disagrees with
/// the boundary difference by ±1 exactly at such boundaries, which is how the farm rejected a
/// healthy tile with "expected 983x694 but got 983x693".)
/// </summary>
internal static class RenderTileGeometry
{
    #region Constants

    /// <summary>
    /// The most a Blender-snapped edge may deviate from our rounded boundary (floor vs round is at
    /// most 1; 2 leaves headroom for float noise on the product). Anything larger is a real
    /// geometry error and must keep failing loudly.
    /// </summary>
    public const int MAX_EDGE_SNAP_PX = 2;

    #endregion

    #region Functions

    /// <summary>A normalized [0..1] boundary in pixel space (round-half-away-from-zero, double math).</summary>
    public static int BoundaryPixel(float fraction, int dimension)
        => (int)Math.Round(fraction * (double)dimension, MidpointRounding.AwayFromZero);

    /// <summary>Pixel span between two boundaries — the size of any tile/crop rect.</summary>
    public static int Span(float minFraction, float maxFraction, int dimension)
        => BoundaryPixel(maxFraction, dimension) - BoundaryPixel(minFraction, dimension);

    /// <summary>
    /// Top-based Y offset of a rect whose TOP edge is <paramref name="maxYFraction"/> (image row 0
    /// is the top of the frame; render fractions are bottom-based like Blender's border).
    /// </summary>
    public static int TopOffset(float maxYFraction, int dimension)
        => dimension - BoundaryPixel(maxYFraction, dimension);

    /// <summary>
    /// Blender's own pixel snapping of a border boundary: the float32 border value multiplied at
    /// DOUBLE precision and TRUNCATED to int. Confirmed by the farm incident: border_max_y =
    /// f32(0.5 + 8/1372) sits a hair below the rational, the double product is 693.999… →
    /// truncation gives 693 rows while our rounded boundary says 694. (A float32 multiply would
    /// collapse the product to exactly 694.0f and hide the effect — the incident proves the
    /// product is taken wider than float32.) Replicated here so the output normalizer can
    /// recognize a Blender-snapped tile and re-anchor it deterministically.
    /// </summary>
    public static int BlenderBoundaryPixel(float fraction, int dimension)
        => Math.Clamp((int)(fraction * (double)dimension), 0, dimension);

    /// <summary>The tile size Blender actually produces for a border render (see <see cref="BlenderBoundaryPixel"/>).</summary>
    public static int BlenderSpan(float minFraction, float maxFraction, int dimension)
        => BlenderBoundaryPixel(maxFraction, dimension) - BlenderBoundaryPixel(minFraction, dimension);

    #endregion
}
