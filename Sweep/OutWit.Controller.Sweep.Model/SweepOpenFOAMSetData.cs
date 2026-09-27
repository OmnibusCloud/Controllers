using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// The cases of an OpenFOAM case-set study, one per variant: the second input
/// of the <c>SweepOpenFOAMSet</c> script beside the study, whose OpenFOAM
/// block then carries only what every case shares (threads, responses,
/// artifacts) and whose variants carry no values. A separate input, not a
/// member of the study: the study's layout stays what every released host
/// reads, and a host without case sets refuses the unknown script by name
/// instead of every study.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepOpenFOAMSetData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepOpenFOAMSetData data)
            return false;

        return Cases.IsSequence(data.Cases, tolerance);
    }

    public override SweepOpenFOAMSetData Clone()
    {
        return new SweepOpenFOAMSetData
        {
            Cases = Cases.Select(item => item.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"OpenFOAM case set: {Cases.Count} case(s)";
    }

    #endregion

    #region Properties

    /// <summary>Every variant's own case.</summary>
    [MemoryPackOrder(0)]
    public List<SweepOpenFOAMCaseData> Cases { get; set; } = [];

    #endregion
}
