using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

internal sealed class RenderTileValidationContext
{
    #region Properties

    public required RenderResultData Result { get; init; }

    public required string LocalPath { get; init; }

    public required RenderImageInfo ImageInfo { get; init; }

    #endregion
}
