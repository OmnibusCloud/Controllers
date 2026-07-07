namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Supported first-slice texture slots.
/// </summary>
public enum DccTextureSlotKind
{
    BaseColor,
    Opacity,
    Metallic,
    Roughness,
    Normal,
    Displacement,

    /// <summary>
    /// Grayscale height (bump) map. Perturbs shading normals from heights — unlike
    /// <see cref="Normal"/>, whose texels are normal vectors. Feeding a height map into a
    /// normal-map node renders black craters instead of relief.
    /// </summary>
    Bump
}
