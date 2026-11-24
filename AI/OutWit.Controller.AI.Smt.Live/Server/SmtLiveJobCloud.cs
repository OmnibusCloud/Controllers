using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;

namespace OutWit.Controller.AI.Smt.Live.Server;

/// <summary>
/// A job on a real server. Its status is polled until it ends; a poll that fails (a connection
/// that drops for a moment during a long run) is tried again a few times before the tool gives
/// up waiting, which leaves the job running on the server. A variable's value is asked for a few
/// times too: the server may mark a job completed a moment before its last value can be read.
/// </summary>
internal sealed class SmtLiveJobCloud : ISmtLiveJob
{
    #region Constants

    /// <summary>The polls in a row that may fail before the waiting ends.</summary>
    private const int MAX_POLL_FAILURES = 5;

    /// <summary>The tries of reading one variable.</summary>
    private const int RESULT_ATTEMPTS = 3;

    #endregion

    #region Fields

    private readonly WitJobHandle m_handle;
    private readonly TimeSpan m_interval;
    private readonly TextWriter m_output;

    #endregion

    #region Constructors

    /// <summary>
    /// Wraps a submitted job.
    /// </summary>
    /// <param name="handle">The SDK's handle of the job.</param>
    /// <param name="interval">How often its status is asked for.</param>
    /// <param name="output">Where a failed poll is said.</param>
    public SmtLiveJobCloud(WitJobHandle handle, TimeSpan interval, TextWriter output)
    {
        m_handle = handle;
        m_interval = interval;
        m_output = output;
    }

    #endregion

    #region ISmtLiveJob

    /// <inheritdoc/>
    public async Task<SmtLiveJobEnd> WaitAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var failures = 0;

        while (true)
        {
            await Task.Delay(m_interval, ct);

            ProcessingJobInfo job;
            try
            {
                job = await m_handle.GetStatusAsync(ct);
                failures = 0;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures++;
                if (failures >= MAX_POLL_FAILURES)
                    throw;

                m_output.WriteLine($"  the job's status could not be read ({failures} of {MAX_POLL_FAILURES} tries): {exception.Message}");
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
                return await m_handle.GetResultAsync<T>(variable, ct);
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
    public Guid Id => m_handle.JobId;

    #endregion
}
