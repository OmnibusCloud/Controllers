namespace OutWit.Controller.Render.Utils;

internal sealed class RenderRawImage
{
    #region Properties

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required byte[] PixelBytes { get; init; }

    #endregion
}
