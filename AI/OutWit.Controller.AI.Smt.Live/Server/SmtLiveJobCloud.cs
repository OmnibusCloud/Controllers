using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK.Jobs;

namespace OutWit.Controller.AI.Smt.Live.Server;

/// <summary>
/// A job on a real server. Its status is polled until it ends; a poll that fails (a connection
/// that drops during a long run) is tried again at every interval while the failures have lasted
/// less than the tolerance, which is minutes, not a number of polls: only then does the tool give
/// up waiting, which leaves the job running on the server. A variable's value is asked for a few
/// times too: the server may mark a job completed a moment before its last value can be read.
/// </summary>
internal sealed class SmtLiveJobCloud : ISmtLiveJob
{
    #region Constants

    /// <summary>The tries of reading one variable.</summary>
    private const int RESULT_ATTEMPTS = 3;

    /// <summary>How often a status that still cannot be read is said again.</summary>
    private static readonly TimeSpan SAY_AGAIN = TimeSpan.FromMinutes(1);

    #endregion

    #region Fields

    private readonly IWitCloudJobs m_jobs;
    private readonly TimeSpan m_interval;
    private readonly TimeSpan m_tolerance;
    private readonly TextWriter m_output;

    #endregion

    #region Constructors

    /// <summary>
    /// Wraps a job.
    /// </summary>
    /// <param name="jobs">The SDK's jobs of the signed-in client.</param>
    /// <param name="id">The job's id.</param>
    /// <param name="script">The name of the script it runs.</param>
    /// <param name="interval">How often its status is asked for.</param>
    /// <param name="tolerance">How long its status may not be read before the waiting ends.</param>
    /// <param name="output">Where a failed poll is said.</param>
    public SmtLiveJobCloud(IWitCloudJobs jobs, Guid id, string script, TimeSpan interval, TimeSpan tolerance, TextWriter output)
    {
        m_jobs = jobs;
        Id = id;
        Script = script;
        m_interval = interval;
        m_tolerance = tolerance;
        m_output = output;
    }

    #endregion

    #region ISmtLiveJob

    /// <inheritdoc/>
    public async Task<SmtLiveJobEnd> WaitAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        DateTime? lost = null;
        var said = DateTime.MinValue;

        while (true)
        {
            await Task.Delay(m_interval, ct);

            ProcessingJobInfo job;
            try
            {
                job = await m_jobs.GetStatusAsync(Id, ct);
                if (lost is { } since)
                    m_output.WriteLine($"  the job's status is read again, after {DateTime.UtcNow - since:hh\\:mm\\:ss}");

                lost = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var now = DateTime.UtcNow;
                lost ??= now;
                if (now - lost.Value >= m_tolerance)
                    throw;

                if (now - said >= SAY_AGAIN)
                {
                    m_output.WriteLine($"  the job's status could not be read for {now - lost.Value:hh\\:mm\\:ss}; the tool asks again for up to {m_tolerance.TotalMinutes:0.#} minute(s): {exception.Message}");
                    said = now;
                }

                continue;
            }

            progress?.Report(job.OverallProgress);
            if (job.Status is ProcessingJobStatus.Completed or ProcessingJobStatus.Failed or ProcessingJobStatus.Cancelled)
                return new SmtLiveJobEnd(job.Status == ProcessingJobStatus.Completed, job.Status.ToString(), job.ErrorMessage, DateTime.UtcNow - started,
                    Utc(job.SubmittedAtUtc), Utc(job.StartedAtUtc), Utc(job.CompletedAtUtc));
        }
    }

    /// <inheritdoc/>
    public async Task<T?> ResultAsync<T>(string variable, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await m_jobs.GetResultAsync<T>(Id, variable, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && attempt < RESULT_ATTEMPTS)
            {
                await Task.Delay(m_interval, ct);
            }
        }
    }

    #endregion

    #region Tools

    /// <summary>A time of the server's that it set; null for one it left at its default.</summary>
    private static DateTime? Utc(DateTime? time)
    {
        return time is { } value && value > DateTime.MinValue ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null;
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public Guid Id { get; }

    /// <inheritdoc/>
    public string Script { get; }

    #endregion
}
