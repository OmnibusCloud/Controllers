using System.Globalization;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// The command line of the tool, read strictly: a verb, its positional arguments, and options of
/// the form <c>--name value</c>. An option the verb does not take, an option given twice, an
/// option without a value and a value that is not a number where a number is asked for are
/// refused with a sentence, before anything is read or sent. The options of a run are named as
/// the run's options document names them (<c>--rate</c> is <c>sampling_rate</c>,
/// <c>--hard-cap</c> is <c>hard_cap_s</c>, ...); whether a number is in its range is the
/// controller's rule (<see cref="SmtOptionsRules"/>), not this reader's.
/// </summary>
internal sealed class SmtLiveArguments
{
    #region Constants

    /// <summary>The folder a run's (or a fetched run's) documents go to; the file a pack writes; the folder diagnostics saves the capabilities to.</summary>
    public const string OUT = "--out";

    /// <summary><c>query</c>: the query of a row that names none; for <c>pack</c>, the query written into every row.</summary>
    public const string QUERY = "--query";

    /// <summary><c>sampling_rate</c>.</summary>
    public const string RATE = "--rate";

    /// <summary><c>rlimit</c>.</summary>
    public const string RLIMIT = "--rlimit";

    /// <summary><c>hard_cap_s</c>.</summary>
    public const string HARD_CAP = "--hard-cap";

    /// <summary><c>memory_mb</c>.</summary>
    public const string MEMORY = "--memory";

    /// <summary><c>seed</c>.</summary>
    public const string SEED = "--seed";

    /// <summary><c>max_models</c>.</summary>
    public const string MAX_MODELS = "--max-models";

    /// <summary>For <c>local</c>: the sessions at once, in place of the node's rule.</summary>
    public const string DEGREE = "--degree";

    /// <summary>For <c>comparative</c>: the folder of a fleet run of a small set, which measures the fixed cost of a fleet run.</summary>
    public const string FIXED = "--fixed";

    private const string OPTION_PREFIX = "--";

    private static readonly string[] HELP = ["-h", "--help"];
    private static readonly string[] RUN_OPTIONS = [QUERY, RATE, RLIMIT, HARD_CAP, MEMORY, SEED, MAX_MODELS];
    private static readonly string[] LOCAL_OPTIONS = [QUERY, RLIMIT, HARD_CAP, MEMORY, SEED, MAX_MODELS, DEGREE, OUT];

    private static readonly IReadOnlyDictionary<string, (SmtLiveVerb Verb, int Inputs, string[] Options)> VERBS =
        new Dictionary<string, (SmtLiveVerb Verb, int Inputs, string[] Options)>(StringComparer.Ordinal)
        {
            ["diagnostics"] = (SmtLiveVerb.Diagnostics, 0, [OUT]),
            ["preflight"] = (SmtLiveVerb.Preflight, 1, RUN_OPTIONS),
            ["run"] = (SmtLiveVerb.Run, 1, [.. RUN_OPTIONS, OUT]),
            ["fetch"] = (SmtLiveVerb.Fetch, 1, [OUT]),
            ["compare"] = (SmtLiveVerb.Compare, 2, []),
            ["pack"] = (SmtLiveVerb.Pack, 1, [QUERY, OUT]),
            ["local"] = (SmtLiveVerb.Local, 1, LOCAL_OPTIONS),
            ["comparative"] = (SmtLiveVerb.Comparative, 2, [FIXED])
        };

    #endregion

    #region Constructors

    private SmtLiveArguments(SmtLiveVerb verb, IReadOnlyList<string> inputs, string? output, SmtOptionsData options, SmtQuery? query, int? degree, string? fixedRun,
        Guid? job)
    {
        Verb = verb;
        Job = job;
        Inputs = inputs;
        Output = output;
        Options = options;
        Query = query;
        Degree = degree;
        Fixed = fixedRun;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads a command line.
    /// </summary>
    /// <param name="args">The arguments after the program's name.</param>
    /// <param name="error">Why the command line was refused; null when it was read, or when help was asked for.</param>
    /// <returns>The command; null when it was refused or when help was asked for (then <paramref name="error"/> is null).</returns>
    public static SmtLiveArguments? Parse(IReadOnlyList<string> args, out string? error)
    {
        error = null;
        if (args.Count == 0)
        {
            error = "no verb was given";
            return null;
        }

        if (HELP.Contains(args[0]) || args[0] == "help")
            return null;

        if (!VERBS.TryGetValue(args[0], out var verb))
        {
            error = $"'{args[0]}' is not a verb of the tool";
            return null;
        }

        var inputs = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];
            if (HELP.Contains(argument))
                return null;

            if (!argument.StartsWith(OPTION_PREFIX, StringComparison.Ordinal))
            {
                inputs.Add(argument);
                continue;
            }

            if (!verb.Options.Contains(argument))
            {
                error = $"'{argument}' is not an option of {args[0]}";
                return null;
            }

            if (values.ContainsKey(argument))
            {
                error = $"{argument} is given twice";
                return null;
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith(OPTION_PREFIX, StringComparison.Ordinal))
            {
                error = $"{argument} needs a value";
                return null;
            }

            values[argument] = args[++index];
        }

        if (inputs.Count != verb.Inputs)
        {
            error = $"{args[0]} takes {verb.Inputs} argument(s) besides its options, not {inputs.Count}";
            return null;
        }

        if (verb.Verb == SmtLiveVerb.Pack && !values.ContainsKey(OUT))
        {
            error = $"pack needs {OUT} <problems.jsonl>";
            return null;
        }

        Guid? job = null;
        if (verb.Verb == SmtLiveVerb.Fetch)
        {
            if (!Guid.TryParse(inputs[0], out var id))
            {
                error = $"'{inputs[0]}' is not a job id (run prints it: \"SmtRun submitted: job <id>\")";
                return null;
            }

            job = id;
        }

        if (!TryOptions(values, out var options, out var query, out error))
            return null;

        if (!TryDegree(values, out var degree, out error))
            return null;

        return new SmtLiveArguments(verb.Verb, inputs, values.GetValueOrDefault(OUT), options, query, degree, values.GetValueOrDefault(FIXED), job);
    }

    /// <summary>
    /// Reads the word of a query.
    /// </summary>
    /// <param name="word"><c>check</c>, <c>enumerate</c> or <c>prove</c>.</param>
    /// <param name="query">The query.</param>
    /// <returns>False when the word is none of the three.</returns>
    public static bool TryQuery(string word, out SmtQuery query)
    {
        switch (word)
        {
            case "check":
                query = SmtQuery.Check;
                return true;
            case "enumerate":
                query = SmtQuery.Enumerate;
                return true;
            case "prove":
                query = SmtQuery.Prove;
                return true;
            default:
                query = SmtQuery.Check;
                return false;
        }
    }

    /// <summary>
    /// The word of a query, as a problem set and the documents write it.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns><c>check</c>, <c>enumerate</c> or <c>prove</c>.</returns>
    public static string WordOf(SmtQuery query)
    {
        return query switch
        {
            SmtQuery.Enumerate => "enumerate",
            SmtQuery.Prove => "prove",
            _ => "check"
        };
    }

    private static bool TryOptions(IReadOnlyDictionary<string, string> values, out SmtOptionsData options, out SmtQuery? query, out string? error)
    {
        options = new SmtOptionsData();
        query = null;
        error = null;

        if (values.TryGetValue(QUERY, out var word))
        {
            if (!TryQuery(word, out var parsed))
            {
                error = $"{QUERY} must be check, enumerate or prove, not '{word}'";
                return false;
            }

            query = parsed;
            options.Query = parsed;
        }

        if (values.TryGetValue(RATE, out var rate))
        {
            if (!double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            {
                error = $"{RATE} must be a number, not '{rate}'";
                return false;
            }

            options.SamplingRate = number;
        }

        if (!TryLong(values, RLIMIT, out var resourceLimit, ref error)
            || !TryInt(values, HARD_CAP, out var hardCap, ref error)
            || !TryInt(values, MEMORY, out var memory, ref error)
            || !TryInt(values, SEED, out var seed, ref error)
            || !TryInt(values, MAX_MODELS, out var maxModels, ref error))
            return false;

        options.ResourceLimit = resourceLimit;
        options.HardCapSeconds = hardCap;
        options.MemoryMegabytes = memory;
        options.Seed = seed;
        options.MaxModels = maxModels;
        return true;
    }

    private static bool TryDegree(IReadOnlyDictionary<string, string> values, out int? degree, out string? error)
    {
        degree = null;
        error = null;
        if (!values.TryGetValue(DEGREE, out var text))
            return true;

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var sessions) && sessions is >= 1 and <= Commands.SmtLiveLocal.MAX_DEGREE)
        {
            degree = sessions;
            return true;
        }

        error = $"{DEGREE} must be a whole number of sessions from 1 to {Commands.SmtLiveLocal.MAX_DEGREE}, not '{text}'";
        return false;
    }

    private static bool TryLong(IReadOnlyDictionary<string, string> values, string name, out long number, ref string? error)
    {
        number = 0;
        if (!values.TryGetValue(name, out var text))
            return true;

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
            return true;

        error = $"{name} must be a whole number, not '{text}'";
        return false;
    }

    private static bool TryInt(IReadOnlyDictionary<string, string> values, string name, out int number, ref string? error)
    {
        number = 0;
        if (!values.TryGetValue(name, out var text))
            return true;

        if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
            return true;

        error = $"{name} must be a whole number of at most 10 digits, not '{text}'";
        return false;
    }

    #endregion

    #region Properties

    /// <summary>What the tool is asked to do.</summary>
    public SmtLiveVerb Verb { get; }

    /// <summary>The positional arguments: the problem set, the job id of <c>fetch</c>, the two folders of <c>compare</c> and of <c>comparative</c>, the folder of <c>pack</c>.</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>The value of <c>--out</c>; null when it was not given.</summary>
    public string? Output { get; }

    /// <summary>The options of a run as given: a number that was not given is 0 (the default), the sampling rate aside, which is the default of a new instance.</summary>
    public SmtOptionsData Options { get; }

    /// <summary>The query <c>--query</c> names; null when it was not given.</summary>
    public SmtQuery? Query { get; }

    /// <summary>The sessions at once <c>--degree</c> gives a local run; null for the node's rule.</summary>
    public int? Degree { get; }

    /// <summary>The folder <c>--fixed</c> names: a fleet run of a small set; null when it was not given.</summary>
    public string? Fixed { get; }

    /// <summary>The job <c>fetch</c> fetches; null for every other verb.</summary>
    public Guid? Job { get; }

    #endregion
}
