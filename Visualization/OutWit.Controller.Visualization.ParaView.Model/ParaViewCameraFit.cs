namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Which timesteps a composed scene's camera framing and colour range must accommodate. The
/// composer inspects the data at the chosen timesteps once, bakes one camera and one colour range
/// into the state, and every rendered frame then shares them — no per-frame drift.
/// </summary>
public enum ParaViewCameraFit
{
    /// <summary>The union of the data bounds and array ranges over the timeline (sampled when it is long).</summary>
    AllTimesteps = 0,

    /// <summary>The last timestep only — the final result of a transient solve.</summary>
    LastTimestep = 1,

    /// <summary>The first timestep only.</summary>
    FirstTimestep = 2
}
