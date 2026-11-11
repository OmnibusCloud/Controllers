using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// What a node knows about its share of one job: how many shards it was given, how much of
/// them has arrived from the origin, how many are processed. A node starts every shard of
/// its share together, so the totals are known at once; each shard reports through its
/// <see cref="ShardShareTicket"/>, and the host receives one picture of the share: first the
/// delivery ("receiving shards: 3/12, 512.3/1888.0 MB; 1/12 processed"), then the work
/// ("7/12 shards processed"). The fraction is the share's, not one shard's.
/// </summary>
public sealed class ShardShareProgress
{
    #region Constants

    private const double MEGABYTE = 1024.0 * 1024.0;

    /// <summary>The part of a share's progress that its delivery stands for; processing is the rest.</summary>
    private const double FETCH_WEIGHT = 0.2;

    /// <summary>Progress reaches the host at most this often; the reports that end a phase always do.</summary>
    private static readonly TimeSpan REPORT_INTERVAL = TimeSpan.FromMilliseconds(500);

    #endregion

    #region Fields

    /// <summary>The jobs with shards on this node now; also the lock of every counter below.</summary>
    private static readonly Dictionary<Guid, ShardShareProgress> JOBS = new();

    private readonly Guid m_jobId;

    private readonly Action<double, string>? m_report;

    private readonly ILogger m_logger;

    private readonly Stopwatch m_clock = Stopwatch.StartNew();

    private readonly object m_reportGate = new();

    private TimeSpan m_lastReport = TimeSpan.MinValue;

    private TimeSpan m_delivered;

    private int m_active;

    private int m_shards;

    private int m_shardsFetched;

    private int m_shardsDone;

    private long m_bytesExpected;

    private long m_bytesFetched;

    private bool m_sizesKnown = true;

    #endregion

    #region Constructors

    private ShardShareProgress(Guid jobId, Action<double, string>? report, ILogger logger)
    {
        m_jobId = jobId;
        m_report = report;
        m_logger = logger;
    }

    #endregion

    #region Functions

    /// <summary>Registers a shard of a job on this node; the first one of a job starts its picture.</summary>
    /// <param name="jobId">The job.</param>
    /// <param name="expectedBytes">The shard's size at its origin, or 0 when the manifest does not say.</param>
    /// <param name="report">The host's progress sink (fraction, stage), or null when the host has none.</param>
    /// <param name="logger">Receives the delivery and the summary lines.</param>
    /// <returns>The shard's ticket; disposed when the shard is over.</returns>
    public static ShardShareTicket Enter(Guid jobId, long expectedBytes, Action<double, string>? report, ILogger logger)
    {
        ShardShareProgress progress;

        lock (JOBS)
        {
            if (!JOBS.TryGetValue(jobId, out progress!))
            {
                progress = new ShardShareProgress(jobId, report, logger);
                JOBS[jobId] = progress;
            }

            progress.m_active++;
            progress.m_shards++;

            if (expectedBytes > 0)
                progress.m_bytesExpected += expectedBytes;
            else
                progress.m_sizesKnown = false;
        }

        progress.Report(force: false);
        return new ShardShareTicket(progress);
    }

    /// <summary>More bytes of a shard have arrived, or a fetch started over (a negative difference).</summary>
    /// <param name="difference">The change in bytes received.</param>
    internal void Received(long difference)
    {
        Interlocked.Add(ref m_bytesFetched, difference);
        Report(force: false);
    }

    /// <summary>A shard has arrived whole, or will not arrive (refused, failed): its delivery is over.</summary>
    internal void Fetched()
    {
        bool complete;
        int shards;

        lock (JOBS)
        {
            m_shardsFetched++;
            complete = m_shardsFetched == m_shards;
            shards = m_shards;

            if (complete)
                m_delivered = m_clock.Elapsed;
        }

        if (complete)
        {
            m_logger.LogInformation("Corpus job {JobId}: {Shards} shard(s), {Megabytes} MB, received in {Seconds} s",
                m_jobId, shards, Megabytes(Interlocked.Read(ref m_bytesFetched)), Seconds(m_clock.Elapsed));
        }

        Report(force: complete);
    }

    /// <summary>A shard has left the node: its products are uploaded, or it failed.</summary>
    internal void Left()
    {
        var last = false;

        lock (JOBS)
        {
            m_shardsDone++;
            m_active--;
            if (m_active == 0)
            {
                JOBS.Remove(m_jobId);
                last = true;
            }
        }

        Report(force: true);

        if (!last)
            return;

        m_logger.LogInformation("Corpus job {JobId}: this node's share is over - {Shards} shard(s), {Megabytes} MB; delivery {Delivery} s, all {Seconds} s",
            m_jobId, m_shards, Megabytes(Interlocked.Read(ref m_bytesFetched)), Seconds(m_delivered), Seconds(m_clock.Elapsed));
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
            var fetchedBytes = Math.Max(0, Interlocked.Read(ref m_bytesFetched));
            var known = m_sizesKnown && m_bytesExpected > 0;

            var delivered = known
                ? Math.Min(1.0, (double)fetchedBytes / m_bytesExpected)
                : m_shards == 0 ? 0.0 : (double)m_shardsFetched / m_shards;
            var processed = m_shards == 0 ? 0.0 : (double)m_shardsDone / m_shards;

            fraction = FETCH_WEIGHT * delivered + (1 - FETCH_WEIGHT) * processed;

            var megabytes = known
                ? $"{Megabytes(fetchedBytes)}/{Megabytes(m_bytesExpected)} MB"
                : $"{Megabytes(fetchedBytes)} MB";

            stage = m_shardsFetched < m_shards
                ? $"receiving shards: {m_shardsFetched}/{m_shards}, {megabytes}; {m_shardsDone}/{m_shards} processed"
                : $"{m_shardsDone}/{m_shards} shards processed";
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
