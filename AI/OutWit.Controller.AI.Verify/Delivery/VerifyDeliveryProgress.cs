using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// What a node knows about its share of one job: how many packages it was given, how many
/// have arrived, how many tasks are done. A node starts every package of its share together,
/// so the totals are known at once; each package then reports through here, and the host
/// receives one picture of the share: first the delivery ("receiving tasks: 12/215
/// packages, 3.1/14.8 MB"), then the work ("51200/220160 tasks"). The fraction is the share's,
/// not one package's, so the job's progress moves as the node's work does.
/// </summary>
public sealed class VerifyDeliveryProgress
{
    #region Constants

    private const double MEGABYTE = 1024.0 * 1024.0;

    /// <summary>Progress reaches the host at most this often; the reports that end a phase always do.</summary>
    private static readonly TimeSpan REPORT_INTERVAL = TimeSpan.FromMilliseconds(500);

    #endregion

    #region Fields

    /// <summary>The jobs with packages on this node now; also the lock of every counter below.</summary>
    private static readonly Dictionary<Guid, VerifyDeliveryProgress> JOBS = new();

    private readonly Guid m_jobId;

    private readonly Action<double, string>? m_report;

    private readonly ILogger m_logger;

    private readonly Stopwatch m_clock = Stopwatch.StartNew();

    private readonly object m_reportGate = new();

    private TimeSpan m_lastReport = TimeSpan.MinValue;

    private TimeSpan m_delivered;

    private int m_active;

    private int m_packages;

    private int m_packagesReceived;

    private long m_bytes;

    private long m_bytesReceived;

    private int m_tasks;

    private int m_tasksDone;

    #endregion

    #region Constructors

    private VerifyDeliveryProgress(Guid jobId, Action<double, string>? report, ILogger logger)
    {
        m_jobId = jobId;
        m_report = report;
        m_logger = logger;
    }

    #endregion

    #region Functions

    /// <summary>Registers a package of a job on this node; the first one of a job starts its picture.</summary>
    /// <param name="jobId">The job.</param>
    /// <param name="package">The package about to be fetched.</param>
    /// <param name="report">The host's progress sink (fraction, stage), or null when the host has none.</param>
    /// <param name="logger">Receives the delivery and the summary lines.</param>
    /// <returns>The job's picture on this node.</returns>
    public static VerifyDeliveryProgress Enter(Guid jobId, VerifyTaskPackageData package, Action<double, string>? report, ILogger logger)
    {
        VerifyDeliveryProgress progress;

        lock (JOBS)
        {
            if (!JOBS.TryGetValue(jobId, out progress!))
            {
                progress = new VerifyDeliveryProgress(jobId, report, logger);
                JOBS[jobId] = progress;
            }

            progress.m_active++;
            progress.m_packages++;
            progress.m_bytes += package.SizeBytes;
            progress.m_tasks += package.TaskCount;
        }

        progress.Report(force: false);
        return progress;
    }

    /// <summary>A package has arrived on this node.</summary>
    /// <param name="package">The package.</param>
    public void Received(VerifyTaskPackageData package)
    {
        bool complete;
        int packages;
        long bytes;

        lock (JOBS)
        {
            m_packagesReceived++;
            m_bytesReceived += package.SizeBytes;

            complete = m_packagesReceived == m_packages;
            packages = m_packages;
            bytes = m_bytes;

            if (complete)
                m_delivered = m_clock.Elapsed;
        }

        if (complete)
        {
            m_logger.LogInformation("Verify job {JobId}: {Packages} task package(s), {Megabytes} MB, received in {Seconds} s",
                m_jobId, packages, Megabytes(bytes), Seconds(m_clock.Elapsed));
        }

        Report(force: complete);
    }

    /// <summary>A task of the job has finished on this node.</summary>
    public void TaskDone()
    {
        Interlocked.Increment(ref m_tasksDone);
        Report(force: false);
    }

    /// <summary>A package has left the node: its results are uploaded, or it failed.</summary>
    public void Leave()
    {
        var last = false;

        lock (JOBS)
        {
            m_active--;
            if (m_active == 0)
            {
                JOBS.Remove(m_jobId);
                last = true;
            }
        }

        if (!last)
            return;

        m_logger.LogInformation(
            "Verify job {JobId}: this node's share is over - {Tasks} of {TotalTasks} task(s) in {Packages} package(s); delivery {Delivery} s, all {Seconds} s",
            m_jobId, Volatile.Read(ref m_tasksDone), m_tasks, m_packages, Seconds(m_delivered), Seconds(m_clock.Elapsed));
    }

    private void Report(bool force)
    {
        if (m_report == null)
            return;

        double fraction;
        string stage;

        lock (m_reportGate)
        {
            var now = m_clock.Elapsed;
            if (!force && m_lastReport != TimeSpan.MinValue && now - m_lastReport < REPORT_INTERVAL)
                return;

            m_lastReport = now;
        }

        lock (JOBS)
        {
            var done = Volatile.Read(ref m_tasksDone);
            fraction = m_tasks == 0 ? 0.0 : (double)done / m_tasks;

            stage = m_packagesReceived < m_packages
                ? $"receiving tasks: {m_packagesReceived}/{m_packages} packages, {Megabytes(m_bytesReceived)}/{Megabytes(m_bytes)} MB; {done}/{m_tasks} tasks done"
                : $"{done}/{m_tasks} tasks";
        }

        m_report(fraction, stage);
    }

    private static string Megabytes(long bytes)
    {
        return (bytes / MEGABYTE).ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string Seconds(TimeSpan span)
    {
        return span.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
    }

    #endregion
}
