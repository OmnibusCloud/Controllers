using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// Everything harvested so far, appended chunk by chunk. Uploaded as a fresh
/// blob after every chunk; rows survive job cancellation because a client
/// that has read the manifest owns every blob id in it.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepManifestData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepManifestData manifest)
            return false;

        return Rows.IsSequence(manifest.Rows, tolerance);
    }

    public override SweepManifestData Clone()
    {
        return new SweepManifestData
        {
            Rows = Rows.Select(row => row.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"manifest: {Rows.Count} row(s)";
    }

    #endregion

    #region Properties

    /// <summary>Harvested variants in harvest order.</summary>
    [MemoryPackOrder(0)]
    public List<SweepManifestRowData> Rows { get; set; } = [];

    #endregion
}
