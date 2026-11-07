using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Activities;

/// <summary>
/// Renders one chunk of outputs on a worker node in ONE pvpython process (FrameBatch, docs 03,
/// section 27, item 2): materializes the chunk's state and attachment union into an isolated package
/// root, runs the controller-owned runner once — the state loads and validates once, every output
/// then selects its timestep, applies its camera move and renders — validates every output and
/// publishes each as a blob. Process startup, the dominant cost of a single-frame task, is paid once
/// per chunk.
/// </summary>
[Activity("ParaView.RenderFrameBatch")]
[CanRunInParallelOnClient(false)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 10240, RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityParaViewRenderFrameBatch : WitActivityFunction
{
    #region Functions

    /// <inheritdoc />
    protected override string InnerString()
    {
        return $"ParaView.RenderFrameBatch({Batch})";
    }

    #endregion

    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityParaViewRenderFrameBatch other)
            return false;

        return base.Is(modelBase, tolerance)
               && Batch.Check(other.Batch);
    }

    /// <inheritdoc />
    protected override WitActivityParaViewRenderFrameBatch InnerClone()
    {
        return new WitActivityParaViewRenderFrameBatch
        {
            Batch = Batch?.Clone() as IWitParameter
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Self-contained render batch (state, options, the attachment union, the outputs).
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Batch { get; init; }

    #endregion
}
