namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The runner's verdict on one output of a task (status schema 2): whether it rendered and
/// verified, the stage it reached, the bounded error when it did not, and its own render seconds.
/// The task-level <see cref="ParaViewRunnerStatus.Ok"/> is true only when every output is.
/// </summary>
public sealed class ParaViewRunnerOutputStatus
{
    #region Properties

    /// <summary>Position of the output in the task's output list.</summary>
    public int Index { get; set; }

    /// <summary>True when the output rendered and its file verified.</summary>
    public bool Ok { get; set; }

    /// <summary>Stage the output reached (select, orbit, render, verify-output, done).</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>Bounded error text when not ok.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Seconds spent rendering this output (render + screenshot).</summary>
    public double RenderSeconds { get; set; }

    #endregion
}
