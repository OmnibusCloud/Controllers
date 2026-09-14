using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Tests.Mock;

/// <summary>
/// A processing manager that carries the host's job-level progress sink, with the exact
/// signature the controller looks up by reflection, and records what it receives.
/// </summary>
internal sealed class JobProgressRecordingManager : IWitProcessingManager
{
    #region Functions

    public void ReportJobProgress(Guid jobId, double fraction, string? stage)
    {
        if (Failure != null)
            throw Failure;

        Reports.Add((jobId, fraction, stage));
    }

    #endregion

    #region IWitProcessingManager

    public void ReportProgress(Guid jobId, IWitActivity activity)
    {
    }

    public void Trace(Guid jobId, string message)
    {
    }

    public void Return(Guid jobId, IReadOnlyList<object?> value)
    {
    }

    public CancellationToken CancellationToken(Guid jobId)
    {
        return System.Threading.CancellationToken.None;
    }

    public void ThrowIfCancellationRequested(Guid jobId)
    {
    }

    public Task WaitAsync(Guid jobId)
    {
        return Task.CompletedTask;
    }

    #endregion

    #region Properties

    public List<(Guid JobId, double Fraction, string? Stage)> Reports { get; } = [];

    /// <summary>When set, the sink throws it instead of recording — a host whose sink fails.</summary>
    public Exception? Failure { get; set; }

    #endregion
}
