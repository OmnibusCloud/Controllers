using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// The extracted responses of one variant — an ordered list of named values,
/// small enough to travel inline with the result (numbers, not files).
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class CcxResponseRowData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CcxResponseRowData row)
            return false;

        return Values.IsSequence(row.Values, tolerance);
    }

    public override CcxResponseRowData Clone()
    {
        return new CcxResponseRowData
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
    public List<CcxResponseValueData> Values { get; set; } = [];

    #endregion
}
