namespace OutWit.Controller.Render.Model;

/// <summary>
/// Output bit depth per channel (Blender's <c>image_settings.color_depth</c>).
/// </summary>
public enum RenderColorDepth
{
    /// <summary>
    /// Leave the scene/format default untouched (legacy behaviour).
    /// </summary>
    Default,

    /// <summary>
    /// 8-bit (PNG/JPEG).
    /// </summary>
    Eight,

    /// <summary>
    /// 16-bit (PNG, EXR half).
    /// </summary>
    Sixteen,

    /// <summary>
    /// 32-bit (EXR full float).
    /// </summary>
    ThirtyTwo
}
