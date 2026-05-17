namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Internal render backend selected by the local Blender runtime.
/// </summary>
internal enum RenderDevice
{
    CPU,
    CUDA,
    OPTIX,
    HIP,
    METAL
}
