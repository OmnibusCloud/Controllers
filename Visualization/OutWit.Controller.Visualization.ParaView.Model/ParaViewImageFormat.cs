namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Image format of a rendered output. Version 1 supports the two formats the runner's
/// SaveScreenshot path and the adapter's output validator both understand.
/// </summary>
public enum ParaViewImageFormat
{
    /// <summary>
    /// Lossless PNG (RGB or RGBA when the background is transparent).
    /// </summary>
    Png = 0,

    /// <summary>
    /// JPEG (RGB; transparency is not representable).
    /// </summary>
    Jpeg = 1
}
