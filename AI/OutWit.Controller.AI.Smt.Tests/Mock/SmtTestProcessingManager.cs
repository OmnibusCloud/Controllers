using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The processing manager an adapter is given when a test runs it without an engine: every job
/// shares one cancellation, which the test may cancel, and the activity progress the adapter
/// reports (found by its name, as a host that can receive it has it) is kept in order.
/// </summary>
internal sealed class SmtTestProcessingManager : IWitProcessingManager
{
    #region Fields

    private readonly CancellationTokenSource m_cancellation = new();

    #endregion

    #region Functions

    /// <summary>Cancels every job.</summary>
    public void Cancel()
    {
        m_cancellation.Cancel();
    }

    /// <summary>
    /// The host's sink of an activity's live progress, found by the controller by its name and
    /// signature: what a newer host has. Keeps every report; calls <see cref="OnProgress"/>.
    /// </summary>
    /// <param name="jobId">The job.</param>
    /// <param name="fraction">The share of the work done.</param>
    /// <param name="stage">The words of the stage.</param>
    public void ReportActivityProgress(Guid jobId, double fraction, string? stage)
    {
        Reports.Enqueue((fraction, stage ?? ""));
        OnProgress?.Invoke(fraction, stage ?? "");
    }

    #endregion

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
        return m_cancellation.Token;
    }

    /// <inheritdoc />
    public void ThrowIfCancellationRequested(Guid jobId)
    {
        m_cancellation.Token.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public Task WaitAsync(Guid jobId)
    {
        return Task.CompletedTask;
    }

    #endregion

    #region Properties

    /// <summary>Every progress report, in order.</summary>
    public ConcurrentQueue<(double Fraction, string Stage)> Reports { get; } = new();

    /// <summary>Called with every progress report, on the thread that reported it.</summary>
    public Action<double, string>? OnProgress { get; set; }

    #endregion
}
