using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The runner's camera axis tokens (task file <c>camera_axis</c>), mirrored by render_task.py.
/// </summary>
public static class ParaViewCameraAxes
{
    #region Constants

    /// <summary>The state's camera view-up vector.</summary>
    public const string VIEW_UP = "view-up";

    /// <summary>World X.</summary>
    public const string X = "x";

    /// <summary>World Y.</summary>
    public const string Y = "y";

    /// <summary>World Z.</summary>
    public const string Z = "z";

    #endregion

    #region Functions

    /// <summary>
    /// The task file token of a turntable axis.
    /// </summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The token.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Unknown axis.</exception>
    public static string WireToken(ParaViewTurntableAxis axis)
    {
        return axis switch
        {
            ParaViewTurntableAxis.ViewUp => VIEW_UP,
            ParaViewTurntableAxis.X => X,
            ParaViewTurntableAxis.Y => Y,
            ParaViewTurntableAxis.Z => Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown turntable axis.")
        };
    }

    #endregion
}
