using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The processing manager an activity adapter is given, for tests that drive an adapter the way
/// the engine does without the engine: nothing is cancelled, nothing is reported.
/// </summary>
public sealed class CorpusTestProcessingManager : IWitProcessingManager
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
