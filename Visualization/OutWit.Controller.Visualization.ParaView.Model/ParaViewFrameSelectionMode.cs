namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// How <see cref="ParaViewFrameSelectionData"/> picks the timesteps to render.
/// </summary>
public enum ParaViewFrameSelectionMode
{
    /// <summary>
    /// One output: the timestep at index <see cref="ParaViewFrameSelectionData.First"/>.
    /// </summary>
    Single = 0,

    /// <summary>
    /// Timestep indices <see cref="ParaViewFrameSelectionData.First"/>..<see cref="ParaViewFrameSelectionData.Last"/>
    /// inclusive, every <see cref="ParaViewFrameSelectionData.Step"/>.
    /// </summary>
    Range = 1,

    /// <summary>
    /// Every timestep of the state's timeline (one output for a static scene).
    /// </summary>
    All = 2,

    /// <summary>
    /// Exactly the indices listed in <see cref="ParaViewFrameSelectionData.Indices"/>, in that order.
    /// </summary>
    Explicit = 3
}
