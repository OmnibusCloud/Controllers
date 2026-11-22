namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// The defaults and the ceilings of a run's options (<see cref="SmtOptionsData"/>), and the two
/// things done with them: saying which options are out of range, and filling the defaults in.
/// An option is named here as a run's specification document names it (<c>max_models</c>,
/// <c>hard_cap_s</c>, ...).
/// </summary>
public static class SmtOptionsRules
{
    #region Constants

    /// <summary><c>max_models</c> when neither the row nor the options name a number: two models tell "exactly one" from "more than one".</summary>
    public const int DEFAULT_MAX_MODELS = 2;

    /// <summary>The greatest <c>max_models</c>.</summary>
    public const int MAX_MODELS = 1_000;

    /// <summary>The greatest <c>seed</c>.</summary>
    public const int MAX_SEED = int.MaxValue;

    /// <summary>
    /// The greatest <c>rlimit</c>: the solver's parameter is 32 bits wide
    /// (<see cref="Lang.SmtScripts.MAX_RESOURCE_LIMIT"/>).
    /// </summary>
    public const long MAX_RESOURCE_LIMIT = 4_294_967_295;

    /// <summary><c>hard_cap_s</c> when the options name none.</summary>
    public const int DEFAULT_HARD_CAP_SECONDS = 60;

    /// <summary>The greatest <c>hard_cap_s</c>.</summary>
    public const int MAX_HARD_CAP_SECONDS = 600;

    /// <summary><c>memory_mb</c> when the options name none.</summary>
    public const int DEFAULT_MEMORY_MEGABYTES = 1_024;

    /// <summary>The greatest <c>memory_mb</c>.</summary>
    public const int MAX_MEMORY_MEGABYTES = 4_096;

    /// <summary><c>sampling_rate</c> of a new instance of the options: one problem in twenty is run on two machines.</summary>
    public const double DEFAULT_SAMPLING_RATE = 0.05;

    /// <summary>The least <c>sampling_rate</c>: no problem is run twice.</summary>
    public const double MIN_SAMPLING_RATE = 0;

    /// <summary>The greatest <c>sampling_rate</c>: every problem is run twice.</summary>
    public const double MAX_SAMPLING_RATE = 1;

    #endregion

    #region Functions

    /// <summary>
    /// Says which options are out of range: one sentence an option, the option named as a run's
    /// specification names it.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The sentences; none when every option is in its range or not set.</returns>
    /// <exception cref="ArgumentNullException">There are no options.</exception>
    public static IReadOnlyList<string> Check(SmtOptionsData options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var problems = new List<string>();

        if (!Enum.IsDefined(options.Query))
            problems.Add("'query' must be check, enumerate or prove");

        if (options.MaxModels < 0 || options.MaxModels > MAX_MODELS)
            problems.Add($"'max_models' must be between 1 and {MAX_MODELS}");

        if (options.Seed < 0)
            problems.Add($"'seed' must be between 0 and {MAX_SEED}");

        if (options.ResourceLimit < 0 || options.ResourceLimit > MAX_RESOURCE_LIMIT)
            problems.Add($"'rlimit' must be between 1 and {MAX_RESOURCE_LIMIT}");

        if (options.HardCapSeconds < 0 || options.HardCapSeconds > MAX_HARD_CAP_SECONDS)
            problems.Add($"'hard_cap_s' must be between 1 and {MAX_HARD_CAP_SECONDS}");

        if (options.MemoryMegabytes < 0 || options.MemoryMegabytes > MAX_MEMORY_MEGABYTES)
            problems.Add($"'memory_mb' must be between 1 and {MAX_MEMORY_MEGABYTES}");

        // Written so that a value that is no number at all is out of range too.
        if (!(options.SamplingRate >= MIN_SAMPLING_RATE && options.SamplingRate <= MAX_SAMPLING_RATE))
            problems.Add("'sampling_rate' must be between 0 and 1");

        return problems;
    }

    /// <summary>
    /// Fills the defaults in where this package gives them: <c>max_models</c>,
    /// <c>hard_cap_s</c> and <c>memory_mb</c>. The profile and the resource limit stay as they
    /// are: their defaults are the controller's and the profile's. Nothing is checked here
    /// (<see cref="Check"/> does that).
    /// </summary>
    /// <param name="options">The options as given.</param>
    /// <returns>A copy with the defaults in place of what was not set.</returns>
    /// <exception cref="ArgumentNullException">There are no options.</exception>
    public static SmtOptionsData Resolve(SmtOptionsData options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var resolved = options.Clone();
        if (resolved.MaxModels == 0)
            resolved.MaxModels = DEFAULT_MAX_MODELS;

        if (resolved.HardCapSeconds == 0)
            resolved.HardCapSeconds = DEFAULT_HARD_CAP_SECONDS;

        if (resolved.MemoryMegabytes == 0)
            resolved.MemoryMegabytes = DEFAULT_MEMORY_MEGABYTES;

        return resolved;
    }

    #endregion
}
