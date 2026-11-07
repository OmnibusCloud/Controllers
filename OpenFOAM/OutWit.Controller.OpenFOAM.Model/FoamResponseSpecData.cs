using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// One response of the request: a function object the node writes into the
/// case under <see cref="Name"/>, runs in the post step, and reads back from
/// <c>postProcessing/&lt;Name&gt;/&lt;latest time&gt;/*.dat</c>. Every column
/// of the last row becomes a response value named <c>&lt;Name&gt;.&lt;column&gt;</c>.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamResponseSpecData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamResponseSpecData spec)
            return false;

        return Name.Is(spec.Name)
               && Kind.Is(spec.Kind)
               && Patches.Is(spec.Patches)
               && Fields.Is(spec.Fields)
               && Operation.Is(spec.Operation)
               && Parameters.IsSequence(spec.Parameters, tolerance);
    }

    public override FoamResponseSpecData Clone()
    {
        return new FoamResponseSpecData
        {
            Name = Name,
            Kind = Kind,
            Patches = Patches.ToList(),
            Fields = Fields.ToList(),
            Operation = Operation,
            Parameters = Parameters.Select(parameter => parameter.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"{Name}: {Kind} on {string.Join(',', Patches)}";
    }

    #endregion

    #region Properties

    /// <summary>The function object's name; a valid OpenFOAM word (letters, digits, underscore).</summary>
    [MemoryPackOrder(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>What is measured.</summary>
    [MemoryPackOrder(1)]
    public FoamResponseKind Kind { get; set; }

    /// <summary>Patch names (forces, coefficients, patch values); empty where the kind does not use patches.</summary>
    [MemoryPackOrder(2)]
    public List<string> Patches { get; set; } = [];

    /// <summary>Field names (patch and volume values, min/max, probes); empty for forces and coefficients.</summary>
    [MemoryPackOrder(3)]
    public List<string> Fields { get; set; } = [];

    /// <summary>The statistic for patch and volume values (<c>areaAverage</c>, <c>areaIntegrate</c>, <c>min</c>, <c>max</c>, <c>volAverage</c>, ...).</summary>
    [MemoryPackOrder(4)]
    public string Operation { get; set; } = string.Empty;

    /// <summary>Further entries of the function-object dictionary, verbatim (reference values, probe locations, a cell zone).</summary>
    [MemoryPackOrder(5)]
    public List<FoamNamedValueData> Parameters { get; set; } = [];

    #endregion
}
