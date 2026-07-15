using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// The specification a task is verified against: expected-output cases.
/// (Assert-program suites are a planned extension — appended fields, not a redesign.)
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifySuiteData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifySuiteData other)
            return false;

        if (Cases.Count != other.Cases.Count)
            return false;

        for (var i = 0; i < Cases.Count; i++)
        {
            if (!Cases[i].Is(other.Cases[i], tolerance))
                return false;
        }

        return true;
    }

    public override VerifySuiteData Clone()
    {
        return new VerifySuiteData
        {
            Cases = Cases.Select(c => c.Clone()).ToList()
        };
    }

    #endregion

    #region Properties

    /// <summary>Test cases, executed in order; the task's verdict aggregates all of them.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(0)]
    public List<VerifySuiteCaseData> Cases { get; set; } = [];

    #endregion
}
