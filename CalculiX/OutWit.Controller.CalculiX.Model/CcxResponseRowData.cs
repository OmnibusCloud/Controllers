using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// One named response value extracted from a solve.
/// </summary>
[MemoryPackable]
public sealed partial class CcxResponseValueData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CcxResponseValueData value)
            return false;

        return Name.Is(value.Name)
               && Value.Is(value.Value, tolerance);
    }

    public override CcxResponseValueData Clone()
    {
        return new CcxResponseValueData
        {
            Name = Name,
            Value = Value
        };
    }

    public override string ToString()
    {
        return $"{Name} = {Value}";
    }

    #endregion

    #region Properties

    /// <summary>Response name, e.g. "max_von_mises".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Extracted value.</summary>
    public double Value { get; set; }

    #endregion
}

/// <summary>
/// The extracted responses of one variant — an ordered list of named values,
/// small enough to travel inline with the result (numbers, not files).
/// </summary>
[MemoryPackable]
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
    public List<CcxResponseValueData> Values { get; set; } = [];

    #endregion
}
