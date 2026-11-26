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

    /// <summary><c>sampling_rate</c> of a new instance of the options: one package in twenty is solved on two machines (one at least).</summary>
    public const double DEFAULT_SAMPLING_RATE = 0.05;

    /// <summary>The least <c>sampling_rate</c>: no package is solved twice.</summary>
    public const double MIN_SAMPLING_RATE = 0;

    /// <summary>The greatest <c>sampling_rate</c>: every package is solved twice.</summary>
    public const double MAX_SAMPLING_RATE = 1;

    /// <summary>
    /// The rule of each option, in the order a check names them: whether its value is in range
    /// (0, where it means "not set", is), the sentence that says it is not, and how a reader that
    /// reads a set all the same puts the option back at the value that means its default.
    /// <see cref="Check"/> and <see cref="Readable"/> are both made of these, so a range is
    /// written once.
    /// </summary>
    private static readonly (Func<SmtOptionsData, bool> InRange, string Problem, Action<SmtOptionsData> Reset)[] RULES =
    [
        (options => Enum.IsDefined(options.Query), "'query' must be check, enumerate or prove", options => options.Query = SmtQuery.Check),
        (options => options.MaxModels is >= 0 and <= MAX_MODELS, $"'max_models' must be between 1 and {MAX_MODELS}", options => options.MaxModels = 0),
        (options => options.Seed >= 0, $"'seed' must be between 0 and {MAX_SEED}", options => options.Seed = 0),
        (options => options.ResourceLimit is >= 0 and <= MAX_RESOURCE_LIMIT, $"'rlimit' must be between 1 and {MAX_RESOURCE_LIMIT}", options => options.ResourceLimit = 0),
        (options => options.HardCapSeconds is >= 0 and <= MAX_HARD_CAP_SECONDS, $"'hard_cap_s' must be between 1 and {MAX_HARD_CAP_SECONDS}", options => options.HardCapSeconds = 0),
        (options => options.MemoryMegabytes is >= 0 and <= MAX_MEMORY_MEGABYTES, $"'memory_mb' must be between 1 and {MAX_MEMORY_MEGABYTES}", options => options.MemoryMegabytes = 0),

        // Written so that a value that is no number at all is out of range too.
        (options => options.SamplingRate >= MIN_SAMPLING_RATE && options.SamplingRate <= MAX_SAMPLING_RATE, "'sampling_rate' must be between 0 and 1", options => options.SamplingRate = DEFAULT_SAMPLING_RATE)
    ];

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

        return [.. RULES.Where(rule => !rule.InRange(options)).Select(rule => rule.Problem)];
    }

    /// <summary>
    /// The options a reader of a problem set takes: the options themselves when every one is in
    /// its range, else a copy with each option that is out of range put back at the value that
    /// means its default (0 for a number, the check query, the default sampling rate), so that a
    /// preflight still reads the set and says what is wrong. Made of the same rules as
    /// <see cref="Check"/>: <c>Check(Readable(options))</c> is empty for any options.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>Options in range: the same instance when they were.</returns>
    /// <exception cref="ArgumentNullException">There are no options.</exception>
    public static SmtOptionsData Readable(SmtOptionsData options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (RULES.All(rule => rule.InRange(options)))
            return options;

        var readable = options.Clone();
        foreach (var (inRange, _, reset) in RULES)
        {
            if (!inRange(readable))
                reset(readable);
        }

        return readable;
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
