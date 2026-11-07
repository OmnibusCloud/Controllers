using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Which timesteps of the state's timeline to render. Indices refer to the resolved timeline
/// (the state's TimeKeeper); a static scene has exactly one timestep, index 0.
/// </summary>
[JobDocumentContract("paraview.frameSelection@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewFrameSelectionData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewFrameSelectionData other)
            return false;

        return Mode.Is(other.Mode)
               && First.Is(other.First)
               && Last.Is(other.Last)
               && Step.Is(other.Step)
               && Indices.Is(other.Indices);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewFrameSelectionData
        {
            Mode = Mode,
            First = First,
            Last = Last,
            Step = Step,
            Indices = [.. Indices]
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Selection mode.
    /// </summary>
    [MemoryPackOrder(0)]
    public ParaViewFrameSelectionMode Mode { get; set; } = ParaViewFrameSelectionMode.Single;

    /// <summary>
    /// First timestep index (Single: the only index; Range: the start).
    /// </summary>
    [MemoryPackOrder(1)]
    public int First { get; set; }

    /// <summary>
    /// Last timestep index, inclusive (Range only).
    /// </summary>
    [MemoryPackOrder(2)]
    public int Last { get; set; }

    /// <summary>
    /// Index stride (Range only; at least 1).
    /// </summary>
    [MemoryPackOrder(3)]
    public int Step { get; set; } = 1;

    /// <summary>
    /// Explicit timestep indices, in render order (Explicit only).
    /// </summary>
    [MemoryPackOrder(4)]
    public List<int> Indices { get; set; } = [];

    #endregion
}
