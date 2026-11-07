using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Activities;

/// <summary>
/// Composes a scene from bare data on a worker node (docs 06, part A): materializes the data scene's
/// attachment, builds the pipeline (reader → representation → colouring → camera) through the
/// controller-owned pvpython composer, saves a REAL ParaView state, publishes it as a blob and returns
/// an ordinary package reference — the input of <c>ParaView.Validate</c>, which then treats it exactly
/// like a state a user saved. Runs once per job through <c>Grid.Delegate()</c>.
/// </summary>
[Activity("ParaView.Compose")]
[CanRunInParallelOnClient(false)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 10240, RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityParaViewCompose : WitActivityFunction
{
    #region Functions

    /// <inheritdoc />
    protected override string InnerString()
    {
        return $"ParaView.Compose({Data}, {Options})";
    }

    #endregion

    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityParaViewCompose other)
            return false;

        return base.Is(modelBase, tolerance)
               && Data.Check(other.Data)
               && Options.Check(other.Options);
    }

    /// <inheritdoc />
    protected override WitActivityParaViewCompose InnerClone()
    {
        return new WitActivityParaViewCompose
        {
            Data = Data?.Clone() as IWitParameter,
            Options = Options?.Clone() as IWitParameter
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The data scene: blob-referenced data plus presentation choices.
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Data { get; init; }

    /// <summary>
    /// Output options (the view size the camera is framed for; the frame selection the fit honours).
    /// </summary>
    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion
}
