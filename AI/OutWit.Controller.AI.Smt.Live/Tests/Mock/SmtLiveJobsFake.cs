using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK.Jobs;

namespace OutWit.Controller.AI.Smt.Live.Tests.Mock;

/// <summary>
/// The SDK's jobs of a server in memory, for one job: each status read takes the next answer a
/// test lined up (a status, or an exception: the connection lost), and the last answer stands
/// once the line is used up.
/// </summary>
internal sealed class SmtLiveJobsFake : IWitCloudJobs
{
    #region Fields

    private readonly Queue<Func<ProcessingJobInfo>> m_answers = new();
    private Func<ProcessingJobInfo>? m_last;

    #endregion

    #region Functions

    /// <summary>Lines up status reads that fail.</summary>
    /// <param name="count">How many.</param>
    /// <returns>This fake.</returns>
    public SmtLiveJobsFake Failing(int count)
    {
        for (var index = 0; index < count; index++)
            Then(() => throw new IOException("the server did not answer"));

        return this;
    }

    /// <summary>Lines up a status read that says the job runs.</summary>
    /// <returns>This fake.</returns>
    public SmtLiveJobsFake Running()
    {
        return Then(() => new ProcessingJobInfo { Status = ProcessingJobStatus.Processing, OverallProgress = 0.5, ScriptName = "SmtRun" });
    }

    /// <summary>Lines up a status read that says the job completed.</summary>
    /// <returns>This fake.</returns>
    public SmtLiveJobsFake Completed()
    {
        return Then(() => new ProcessingJobInfo { Status = ProcessingJobStatus.Completed, OverallProgress = 1, ScriptName = "SmtRun" });
    }

    private SmtLiveJobsFake Then(Func<ProcessingJobInfo> answer)
    {
        m_answers.Enqueue(answer);
        return this;
    }

    #endregion

    #region IWitCloudJobs

    public Task<ProcessingJobInfo> GetStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        Reads++;
        if (m_answers.Count > 0)
            m_last = m_answers.Dequeue();

        return Task.FromResult(m_last!());
    }

    public Task<TResult?> GetResultAsync<TResult>(Guid jobId, string resultVariable = "result", CancellationToken ct = default)
    {
        return Task.FromResult<TResult?>(default);
    }

    public Task<ProcessingJobInfoPage> ListAsync(int page = 0, int pageSize = 50, ProcessingJobStatus? statusFilter = null, string? scriptNameFilter = null, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<TResult?> GetVariableAsync<TResult>(Guid jobId, string variable, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<string> GetVariableDocumentAsync(Guid jobId, string variable, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    #endregion

    #region Properties

    /// <summary>The status reads so far.</summary>
    public int Reads { get; private set; }

    #endregion
}
