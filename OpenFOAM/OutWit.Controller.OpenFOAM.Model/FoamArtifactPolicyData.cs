using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// Which time directories of a finished case travel back.
/// </summary>
public enum FoamArtifactTimes
{
    /// <summary>No time directory.</summary>
    None = 0,

    /// <summary>The latest written time only.</summary>
    Latest = 1,

    /// <summary>Every written time (transient runs: large).</summary>
    All = 2
}

/// <summary>
/// What of a finished case is zipped and uploaded as the variant's artifact.
/// The result row travels regardless; the artifact is what a person opens in
/// ParaView afterwards, and every part of it is paid for in upload time and
/// storage, so the default is nothing and the user chooses per sweep.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamArtifactPolicyData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamArtifactPolicyData policy)
            return false;

        return Times.Is(policy.Times)
               && Mesh.Is(policy.Mesh)
               && Logs.Is(policy.Logs)
               && PostProcessing.Is(policy.PostProcessing);
    }

    public override FoamArtifactPolicyData Clone()
    {
        return new FoamArtifactPolicyData
        {
            Times = Times,
            Mesh = Mesh,
            Logs = Logs,
            PostProcessing = PostProcessing
        };
    }

    public override string ToString()
    {
        return $"artifact: times {Times}{(Mesh ? ", mesh" : "")}{(Logs ? ", logs" : "")}{(PostProcessing ? ", postProcessing" : "")}";
    }

    #endregion

    #region Functions

    /// <summary>
    /// True when the policy asks for nothing, in which case no artifact is made or uploaded.
    /// </summary>
    /// <returns>True when every part is off.</returns>
    public bool IsEmpty()
    {
        return Times == FoamArtifactTimes.None && !Mesh && !Logs && !PostProcessing;
    }

    #endregion

    #region Properties

    /// <summary>Which time directories travel.</summary>
    [MemoryPackOrder(0)]
    public FoamArtifactTimes Times { get; set; }

    /// <summary>True to include <c>constant/polyMesh</c> (needed to open the fields in ParaView).</summary>
    [MemoryPackOrder(1)]
    public bool Mesh { get; set; }

    /// <summary>True to include every step's log.</summary>
    [MemoryPackOrder(2)]
    public bool Logs { get; set; }

    /// <summary>True to include the <c>postProcessing</c> tree (the function-object histories).</summary>
    [MemoryPackOrder(3)]
    public bool PostProcessing { get; set; }

    #endregion
}
