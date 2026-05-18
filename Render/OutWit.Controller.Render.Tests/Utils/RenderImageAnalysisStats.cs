namespace OutWit.Controller.Render.Tests.Utils;

internal sealed class RenderImageAnalysisStats
{
    #region Properties

    public int Width { get; set; }

    public int Height { get; set; }

    public long TotalPixels { get; set; }

    public long NonBlackPixels { get; set; }

    public long NonTransparentPixels { get; set; }

    public long NonBlackFullyTransparentPixels { get; set; }

    public long FullyBlackOpaquePixels { get; set; }

    public double AverageR { get; set; }

    public double AverageG { get; set; }

    public double AverageB { get; set; }

    public double AverageA { get; set; }

    public byte MinR { get; set; }

    public byte MinG { get; set; }

    public byte MinB { get; set; }

    public byte MinA { get; set; }

    public byte MaxR { get; set; }

    public byte MaxG { get; set; }

    public byte MaxB { get; set; }

    public byte MaxA { get; set; }

    #endregion
}
