using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// The processing manager an adapter is given when a test runs it without an engine: no job is
/// ever cancelled, and what an activity reports goes nowhere.
/// </summary>
internal sealed class VerifyTestProcessingManager : IWitProcessingManager
{
    #region IWitProcessingManager

    /// <inheritdoc />
    public void ReportProgress(Guid jobId, IWitActivity activity)
    {
    }

    /// <inheritdoc />
    public void Trace(Guid jobId, string message)
    {
    }

    /// <inheritdoc />
    public void Return(Guid jobId, IReadOnlyList<object?> value)
    {
    }

    /// <inheritdoc />
    public CancellationToken CancellationToken(Guid jobId)
    {
        return System.Threading.CancellationToken.None;
    }

    /// <inheritdoc />
    public void ThrowIfCancellationRequested(Guid jobId)
    {
    }

    /// <inheritdoc />
    public Task WaitAsync(Guid jobId)
    {
        return Task.CompletedTask;
    }

    #endregion
}
