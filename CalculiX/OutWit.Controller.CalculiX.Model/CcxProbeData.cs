using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// Aggregate applied to a probed quantity over a named set.
/// </summary>
public enum CcxProbeAggregate
{
    Max = 0,
    Min = 1,
    Avg = 2
}

/// <summary>
/// One user-defined probe: aggregate × quantity × named node/element set from
/// the deck. Composed from fixed menus — no free-form expressions.
/// </summary>
[MemoryPackable]
public sealed partial class CcxProbeData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CcxProbeData probe)
            return false;

        return Aggregate.Is(probe.Aggregate)
               && Quantity.Is(probe.Quantity)
               && SetName.Is(probe.SetName);
    }

    public override CcxProbeData Clone()
    {
        return new CcxProbeData
        {
            Aggregate = Aggregate,
            Quantity = Quantity,
            SetName = SetName
        };
    }

    public override string ToString()
    {
        return $"{Aggregate} {Quantity} @ {SetName}";
    }

    #endregion

    #region Properties

    /// <summary>Aggregate applied over the set.</summary>
    public CcxProbeAggregate Aggregate { get; set; }

    /// <summary>Result quantity name (dataset vocabulary of the analysis type).</summary>
    public string Quantity { get; set; } = string.Empty;

    /// <summary>Named node/element set from the deck the probe evaluates over.</summary>
    public string SetName { get; set; } = string.Empty;

    #endregion
}
