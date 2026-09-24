using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// The extracted responses of one variant - an ordered list of named values,
/// small enough to travel inline with the result (numbers, not files).
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamResponseRowData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamResponseRowData row)
            return false;

        return Values.IsSequence(row.Values, tolerance);
    }

    public override FoamResponseRowData Clone()
    {
        return new FoamResponseRowData
        {
            Values = Values.Select(value => value.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"row: {Values.Count} value(s)";
    }

    #endregion

    #region Properties

    /// <summary>Named response values in extraction order.</summary>
    [MemoryPackOrder(0)]
    public List<FoamResponseValueData> Values { get; set; } = [];

    #endregion
}
