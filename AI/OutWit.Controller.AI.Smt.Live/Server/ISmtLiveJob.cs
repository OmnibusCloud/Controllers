namespace OutWit.Controller.AI.Smt.Live.Server;

/// <summary>A submitted job: waited for, then asked for the values of its variables.</summary>
internal interface ISmtLiveJob
{
    /// <summary>
    /// Waits until the job ends: completed, failed or cancelled.
    /// </summary>
    /// <param name="progress">Told the job's overall progress, 0 to 1, at every poll.</param>
    /// <param name="ct">Stops the local waiting only; the job goes on on the server.</param>
    /// <returns>How the job ended.</returns>
    Task<SmtLiveJobEnd> WaitAsync(IProgress<double>? progress, CancellationToken ct);

    /// <summary>
    /// The value of one of the ended job's variables.
    /// </summary>
    /// <typeparam name="T">The variable's type.</typeparam>
    /// <param name="variable">The variable's name in the script.</param>
    /// <param name="ct">Cancels the local call.</param>
    /// <returns>The value; default when the variable has none.</returns>
    Task<T?> ResultAsync<T>(string variable, CancellationToken ct);

    /// <summary>The job's id.</summary>
    Guid Id { get; }
}
