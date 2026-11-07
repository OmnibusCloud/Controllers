namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// One output of a runner task (schema 2): the timestep it selects, the camera move it applies and
/// the file it renders to. A single-frame task carries one; a batch carries the chunk's outputs in
/// render order. Everything shared — the state, the view, the size, the format, the policy lists —
/// lives once on <see cref="ParaViewRunnerTask"/>.
/// </summary>
public sealed class ParaViewRunnerOutput
{
    #region Properties

    /// <summary>Position of the output in the task's output list (0-based; diagnostics and status matching).</summary>
    public int Index { get; set; }

    /// <summary>Task identity of the output (diagnostics only).</summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Absolute output image path inside the work directory; distinct per output.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Timestep index to select.</summary>
    public int TimestepIndex { get; set; }

    /// <summary>Physical time value of the timestep, null for a static scene.</summary>
    public double? TimeValue { get; set; }

    /// <summary>Camera azimuth in degrees to apply about the orbit axis before rendering (0: none).</summary>
    public double CameraAzimuth { get; set; }

    /// <summary>Orbit axis token: view-up (the state's camera view-up), x, y or z (world axes).</summary>
    public string CameraAxis { get; set; } = ParaViewCameraAxes.VIEW_UP;

    /// <summary>Camera elevation in degrees about the camera's right axis (0: none).</summary>
    public double CameraElevation { get; set; }

    /// <summary>Factor applied to the camera's distance from the focal point (1: none).</summary>
    public double CameraDolly { get; set; } = 1.0;

    #endregion
}
