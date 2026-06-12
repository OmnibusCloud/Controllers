using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Video encoding options for the first production RenderVideo path.
/// Current scope is intentionally minimal: MP4 container with H.264 video and no audio.
/// </summary>
[MemoryPackable]
public partial class VideoOptionsData : ModelBase
{
    #region Constants

    private const int DEFAULT_FRAME_RATE = 24;
    private const int DEFAULT_CONSTANT_RATE_FACTOR = 23;

    #endregion

    #region Properties

    /// <summary>
    /// Output frame rate in frames per second.
    /// </summary>
    public int FrameRate { get; set; } = DEFAULT_FRAME_RATE;

    /// <summary>
    /// CRF quality setting passed to the ffmpeg encoder (libx264/libx265/libvpx-vp9).
    /// Lower values mean higher quality and larger output.
    /// </summary>
    public int ConstantRateFactor { get; set; } = DEFAULT_CONSTANT_RATE_FACTOR;

    /// <summary>
    /// Container + codec preset. APPENDED after ConstantRateFactor (wire order is append-only);
    /// <see cref="VideoFormat.Default"/> keeps the legacy MP4/H.264 behaviour for old payloads.
    /// </summary>
    public VideoFormat Format { get; set; } = VideoFormat.Default;

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VideoOptionsData other)
            return false;

        return FrameRate == other.FrameRate
               && ConstantRateFactor == other.ConstantRateFactor
               && Format == other.Format;
    }

    public override ModelBase Clone()
    {
        return new VideoOptionsData
        {
            FrameRate = FrameRate,
            ConstantRateFactor = ConstantRateFactor,
            Format = Format
        };
    }

    #endregion
}
