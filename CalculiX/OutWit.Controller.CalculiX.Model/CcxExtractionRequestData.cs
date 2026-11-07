using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// What to extract from a finished solve's result files, evaluated on the node
/// right after ccx exits (the files are already local there). The automatic
/// response set of the analysis type is always extracted; probes are extras.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class CcxExtractionRequestData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CcxExtractionRequestData request)
            return false;

        return Probes.IsSequence(request.Probes, tolerance);
    }

    public override CcxExtractionRequestData Clone()
    {
        return new CcxExtractionRequestData
        {
            Probes = Probes.Select(probe => probe.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"extraction: {Probes.Count} probe(s)";
    }

    #endregion

    #region Properties

    /// <summary>User-defined probes on top of the automatic response set.</summary>
    [MemoryPackOrder(0)]
    public List<CcxProbeData> Probes { get; set; } = [];

    #endregion
}
