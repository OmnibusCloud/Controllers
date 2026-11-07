namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// How a camera orbit (<see cref="ParaViewTurntableData"/>) composes with the selected timesteps.
/// </summary>
public enum ParaViewTurntableTimeMode
{
    /// <summary>
    /// The data time stands still while the camera orbits: every selected timestep receives a
    /// full orbit of <see cref="ParaViewTurntableData.Frames"/> outputs (usually one timestep in,
    /// one showcase orbit out).
    /// </summary>
    Fixed = 0,

    /// <summary>
    /// The data time advances along the selected timesteps while the camera completes one orbit:
    /// exactly <see cref="ParaViewTurntableData.Frames"/> outputs, the first at the first selected
    /// timestep, the last at the last, timesteps spread evenly in between (repeated or skipped as
    /// the counts dictate).
    /// </summary>
    Advancing = 1
}
