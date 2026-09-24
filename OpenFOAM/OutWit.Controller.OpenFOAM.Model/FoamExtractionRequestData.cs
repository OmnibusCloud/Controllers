using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// What to read back from a finished run, evaluated on the node while the
/// case is still local. Convergence facts, step times and the cell count are
/// always reported; this is the list of physical responses on top of them.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamExtractionRequestData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamExtractionRequestData request)
            return false;

        return Responses.IsSequence(request.Responses, tolerance);
    }

    public override FoamExtractionRequestData Clone()
    {
        return new FoamExtractionRequestData
        {
            Responses = Responses.Select(response => response.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"extraction: {Responses.Count} response(s)";
    }

    #endregion

    #region Properties

    /// <summary>The responses, in the order their values appear in the row.</summary>
    [MemoryPackOrder(0)]
    public List<FoamResponseSpecData> Responses { get; set; } = [];

    #endregion
}
