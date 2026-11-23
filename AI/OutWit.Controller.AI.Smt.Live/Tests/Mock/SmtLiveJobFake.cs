using OutWit.Controller.AI.Smt.Live.Server;

namespace OutWit.Controller.AI.Smt.Live.Tests.Mock;

/// <summary>A job that has ended the way a test says, with the values a test gives its variables.</summary>
internal sealed class SmtLiveJobFake : ISmtLiveJob
{
    #region Fields

    private readonly SmtLiveJobEnd m_end;
    private readonly IReadOnlyDictionary<string, object?> m_values;

    #endregion

    #region Constructors

    private SmtLiveJobFake(SmtLiveJobEnd end, IReadOnlyDictionary<string, object?> values)
    {
        m_end = end;
        m_values = values;
    }

    #endregion

    #region Functions

    /// <summary>A job that completed.</summary>
    /// <param name="values">The values of its variables.</param>
    /// <returns>The job.</returns>
    public static SmtLiveJobFake Completed(IReadOnlyDictionary<string, object?> values)
    {
        return new SmtLiveJobFake(new SmtLiveJobEnd(true, "Completed", null, TimeSpan.FromSeconds(3)), values);
    }

    /// <summary>A job that failed.</summary>
    /// <param name="error">The server's message.</param>
    /// <returns>The job.</returns>
    public static SmtLiveJobFake Failed(string error)
    {
        return new SmtLiveJobFake(new SmtLiveJobEnd(false, "Failed", error, TimeSpan.FromSeconds(1)), new Dictionary<string, object?>());
    }

    #endregion

    #region ISmtLiveJob

    public Task<SmtLiveJobEnd> WaitAsync(IProgress<double>? progress, CancellationToken ct)
    {
        progress?.Report(0.5);
        progress?.Report(1.0);
        return Task.FromResult(m_end);
    }

    public Task<T?> ResultAsync<T>(string variable, CancellationToken ct)
    {
        return Task.FromResult(m_values.TryGetValue(variable, out var value) && value is T typed ? typed : default);
    }

    #endregion

    #region Properties

    public Guid Id { get; } = Guid.NewGuid();

    #endregion
}
