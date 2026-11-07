using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Activities;

/// <summary>
/// Renders one task on a worker node: materializes the task's state and attachment subset into an isolated
/// package root, runs the controller-owned pvpython runner on the software-rendering baseline, validates the
/// output and publishes it as a blob.
/// </summary>
[Activity("ParaView.RenderFrame")]
[CanRunInParallelOnClient(false)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 10240, RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityParaViewRenderFrame : WitActivityFunction
{
    #region Functions

    /// <inheritdoc />
    protected override string InnerString()
    {
        return $"ParaView.RenderFrame({Task})";
    }

    #endregion

    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityParaViewRenderFrame other)
            return false;

        return base.Is(modelBase, tolerance)
               && Task.Check(other.Task);
    }

    /// <inheritdoc />
    protected override WitActivityParaViewRenderFrame InnerClone()
    {
        return new WitActivityParaViewRenderFrame
        {
            Task = Task?.Clone() as IWitParameter
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Self-contained render task (state, view, timestep, options, attachment subset).
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Task { get; init; }

    #endregion
}
