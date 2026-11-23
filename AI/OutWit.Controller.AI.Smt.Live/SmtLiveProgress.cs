using OutWit.Cloud.SDK.Blobs;

namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// Prints the progress of a job or of a transfer as lines a log keeps: the first report, every
/// ten percent, the end, and once a minute while nothing moves, each with the time since the
/// start.
/// </summary>
internal sealed class SmtLiveProgress : IProgress<double>, IProgress<WitTransferProgress>
{
    #region Constants

    private const int STEP_PERCENT = 10;
    private const int WHOLE_PERCENT = 100;
    private const double BYTES_PER_MIB = 1024.0 * 1024.0;

    private static readonly TimeSpan HEARTBEAT = TimeSpan.FromMinutes(1);

    #endregion

    #region Fields

    private readonly TextWriter m_output;
    private readonly string m_label;
    private readonly DateTime m_started = DateTime.UtcNow;
    private DateTime m_lastLine = DateTime.MinValue;
    private int m_lastPercent = -1;

    #endregion

    #region Constructors

    /// <summary>
    /// Makes a progress printer.
    /// </summary>
    /// <param name="output">Where the lines go.</param>
    /// <param name="label">What progresses: <c>progress</c>, <c>upload</c>, <c>download</c>.</param>
    public SmtLiveProgress(TextWriter output, string label)
    {
        m_output = output;
        m_label = label;
    }

    #endregion

    #region Tools

    private void Print(double fraction, string? detail)
    {
        var percent = (int)Math.Floor(Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1) * WHOLE_PERCENT);
        var now = DateTime.UtcNow;

        var first = m_lastPercent < 0;
        var step = percent / STEP_PERCENT > m_lastPercent / STEP_PERCENT;
        var end = percent == WHOLE_PERCENT && m_lastPercent != WHOLE_PERCENT;
        var quiet = now - m_lastLine >= HEARTBEAT;
        if (!first && !step && !end && !quiet)
            return;

        m_lastPercent = percent;
        m_lastLine = now;
        m_output.WriteLine($"  {m_label,-8} {percent,3} %{detail} after {now - m_started:hh\\:mm\\:ss}");
    }

    #endregion

    #region IProgress

    /// <summary>Takes a job's overall progress.</summary>
    /// <param name="value">0 to 1.</param>
    public void Report(double value)
    {
        Print(value, null);
    }

    /// <summary>Takes a transfer's progress.</summary>
    /// <param name="value">The bytes moved and the bytes in all.</param>
    public void Report(WitTransferProgress value)
    {
        var fraction = value.TotalBytes <= 0 ? 1.0 : (double)value.CompletedBytes / value.TotalBytes;
        Print(fraction, $" ({value.CompletedBytes / BYTES_PER_MIB:F1} of {value.TotalBytes / BYTES_PER_MIB:F1} MiB)");
    }

    #endregion
}
