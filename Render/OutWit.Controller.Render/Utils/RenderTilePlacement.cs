namespace OutWit.Controller.Render.Utils;

internal sealed class RenderTilePlacement
{
    #region Properties

    public required string InputPath { get; init; }

    public required int OffsetX { get; init; }

    public required int OffsetY { get; init; }

    public required int CropX { get; init; }

    public required int CropY { get; init; }

    public required int CropWidth { get; init; }

    public required int CropHeight { get; init; }

    #endregion
}
