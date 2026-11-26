using OutWit.Controller.AI.Smt.Live.Server;

namespace OutWit.Controller.AI.Smt.Live.Tests.Mock;

/// <summary>
/// A job that has ended the way a test says, with the values a test gives its variables; or one
/// that never ends (it is waited for until the waiting is stopped), or whose status cannot be
/// read.
/// </summary>
internal sealed class SmtLiveJobFake : ISmtLiveJob
{
    #region Fields

    private readonly SmtLiveJobEnd? m_end;
    private readonly Exception? m_lost;
    private readonly IReadOnlyDictionary<string, object?> m_values;

    #endregion

    #region Constructors

    private SmtLiveJobFake(SmtLiveJobEnd? end, Exception? lost, IReadOnlyDictionary<string, object?> values)
    {
        m_end = end;
        m_lost = lost;
        m_values = values;
    }

    #endregion

    #region Functions

    /// <summary>A job that completed: the server took it at noon, started it 2 s later and ended it 3 s after that.</summary>
    /// <param name="values">The values of its variables.</param>
    /// <returns>The job.</returns>
    public static SmtLiveJobFake Completed(IReadOnlyDictionary<string, object?> values)
    {
        var submitted = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        return new SmtLiveJobFake(new SmtLiveJobEnd(true, "Completed", null, TimeSpan.FromSeconds(3), submitted, submitted.AddSeconds(2), submitted.AddSeconds(5)), null, values);
    }

    /// <summary>A job that failed.</summary>
    /// <param name="error">The server's message.</param>
    /// <returns>The job.</returns>
    public static SmtLiveJobFake Failed(string error)
    {
        return new SmtLiveJobFake(new SmtLiveJobEnd(false, "Failed", error, TimeSpan.FromSeconds(1)), null, new Dictionary<string, object?>());
    }

    /// <summary>A job that runs on: waiting for it ends only when the waiting is stopped.</summary>
    /// <returns>The job.</returns>
    public static SmtLiveJobFake Running()
    {
        return new SmtLiveJobFake(null, null, new Dictionary<string, object?>());
    }

    /// <summary>A job whose status cannot be read: the waiting ends with the exception given.</summary>
    /// <param name="lost">What the waiting throws.</param>
    /// <returns>The job.</returns>
    public static SmtLiveJobFake Lost(Exception lost)
    {
        return new SmtLiveJobFake(null, lost, new Dictionary<string, object?>());
    }

    #endregion

    #region ISmtLiveJob

    public async Task<SmtLiveJobEnd> WaitAsync(IProgress<double>? progress, CancellationToken ct)
    {
        progress?.Report(0.5);
        if (m_lost != null)
            throw m_lost;

        if (m_end == null)
            await Task.Delay(Timeout.Infinite, ct);

        progress?.Report(1.0);
        return m_end!;
    }

    public Task<T?> ResultAsync<T>(string variable, CancellationToken ct)
    {
        return Task.FromResult(m_values.TryGetValue(variable, out var value) && value is T typed ? typed : default);
    }

    #endregion

    #region Properties

    public Guid Id { get; } = Guid.NewGuid();

    public string Script { get; set; } = "SmtRun";

    #endregion
}
