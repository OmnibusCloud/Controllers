namespace OutWit.Controller.Render.Dcc.Models.Build;

/// <summary>
/// The generated Blender build script plus its binary sidecar. The Python text carries structure
/// and offsets; the bulk mesh data travels in <see cref="SceneData"/>, written next to the script
/// as <c>scene-data.bin</c> and read back with numpy — Python literals for mesh data made the
/// script parser the pipeline bottleneck.
/// </summary>
internal sealed class DccBlenderSceneScript
{
    #region Properties

    public string PythonScript { get; init; } = string.Empty;

    public byte[] SceneData { get; init; } = [];

    #endregion
}
