namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// The axis a camera orbit (<see cref="ParaViewTurntableData"/>) revolves about, through the
/// view's focal point.
/// </summary>
public enum ParaViewTurntableAxis
{
    /// <summary>
    /// The view-up vector the state carries for the camera — the orbit keeps the user's framing
    /// exactly (the usual choice when the camera was set through the view-direction toolbar).
    /// </summary>
    ViewUp = 0,

    /// <summary>
    /// The world X axis: the camera revolves rigidly about it through the focal point (position and
    /// view-up together), so a tilted camera keeps its tilt and the horizon stays level.
    /// </summary>
    X = 1,

    /// <summary>
    /// The world Y axis: the camera revolves rigidly about it through the focal point (position and
    /// view-up together), so a tilted camera keeps its tilt and the horizon stays level.
    /// </summary>
    Y = 2,

    /// <summary>
    /// The world Z axis: the camera revolves rigidly about it through the focal point (position and
    /// view-up together), so a tilted camera keeps its tilt and the horizon stays level.
    /// </summary>
    Z = 3
}
