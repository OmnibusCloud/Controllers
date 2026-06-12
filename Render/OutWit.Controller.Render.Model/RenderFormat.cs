namespace OutWit.Controller.Render.Model;

/// <summary>
/// Output image format for rendering.
/// APPEND-ONLY: values travel as MemoryPack ints — never reorder or remove members.
/// </summary>
public enum RenderFormat
{
    PNG,
    EXR,
    JPEG,
    TIFF,
    WEBP
}
