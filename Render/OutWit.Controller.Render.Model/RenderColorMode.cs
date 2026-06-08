namespace OutWit.Controller.Render.Model;

/// <summary>
/// Output colour channels for a render.
/// </summary>
public enum RenderColorMode
{
    /// <summary>
    /// Legacy behaviour: force RGB for PNG/JPEG, leave the scene default for EXR. Keeps old clients
    /// (which don't send a colour mode) rendering exactly as before.
    /// </summary>
    Default,

    /// <summary>
    /// RGB (no alpha).
    /// </summary>
    RGB,

    /// <summary>
    /// RGBA (with alpha) — needed for transparent renders / compositing.
    /// </summary>
    RGBA
}
