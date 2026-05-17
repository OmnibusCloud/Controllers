using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Preflight validation result for video rendering on the current packaged runtime.
/// </summary>
[MemoryPackable]
public partial class RenderPreflightVideoData : ModelBase
{
    #region Properties

    /// <summary>
    /// Whether the current packaged runtime can execute the requested video render.
    /// </summary>
    public bool CanRender { get; set; }

    /// <summary>
    /// Runtime target resolved for the current process, such as <c>windows-x64</c>.
    /// </summary>
    public string? RuntimeTarget { get; set; }

    /// <summary>
    /// Human-readable preflight issues that block the requested video render.
    /// </summary>
    public List<string> Issues { get; set; } = [];

    /// <summary>
    /// Human-readable preflight warnings that do not block the requested video render.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderPreflightVideoData other)
            return false;

        return CanRender.Is(other.CanRender)
               && RuntimeTarget.Is(other.RuntimeTarget)
               && Issues.Is(other.Issues)
               && Warnings.Is(other.Warnings);
    }

    public override ModelBase Clone()
    {
        return new RenderPreflightVideoData
        {
            CanRender = CanRender,
            RuntimeTarget = RuntimeTarget,
            Issues = [.. Issues],
            Warnings = [.. Warnings]
        };
    }

    #endregion
}
