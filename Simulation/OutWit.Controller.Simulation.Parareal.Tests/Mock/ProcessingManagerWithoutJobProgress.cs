using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Tests.Mock;

/// <summary>
/// A processing manager of a host that predates the job-level progress sink (an older server, the SDK).
/// </summary>
internal sealed class ProcessingManagerWithoutJobProgress : IWitProcessingManager
{
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
}
