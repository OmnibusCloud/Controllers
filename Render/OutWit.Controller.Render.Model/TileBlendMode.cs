namespace OutWit.Controller.Render.Model;

/// <summary>
/// Stitching mode for tiled still rendering.
/// </summary>
public enum TileBlendMode
{
    /// <summary>
    /// Crop each rendered tile back to its logical core area before overlay.
    /// </summary>
    CenterPriorityCrop = 0,

    /// <summary>
    /// Blend overlap regions with linear feathering and normalized weight accumulation.
    /// </summary>
    AlphaBlend = 1
}
