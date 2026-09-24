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
