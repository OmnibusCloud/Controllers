using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Makes conversations of a session at random, from a seed: the answers a solver gives to a
/// query as its conversation goes (a verdict, the statistics, a model, values, a reason), some of
/// them replaced by something else (an error, nothing, a word that is no answer, an answer of
/// another place, a text of megabytes' shape in small), the conversation cut where a run might
/// end, and the run ended in any of the ways the sandbox reports, with any cost its envelope
/// allows. Where <see cref="SmtSessionCases"/> names the ways a session ends one by one, this
/// walks their combinations. The same seed makes the same conversations on every machine.
/// </summary>
internal sealed class SmtSessionCaseGenerator
{
    #region Constants

    private const string SILENT = "";
    private const string SAT = "sat\n";
    private const string UNSAT = "unsat\n";
    private const string UNKNOWN = "unknown\n";
    private const string ERROR = "(error \"line 9 column 1: something is wrong\")\n";

    private const int MOST_ROUNDS = 5;
    private const double CHANGED_SHARE = 0.08;
    private const double CUT_SHARE = 0.35;
    private const double CLEAN_END_SHARE = 0.45;
    private const double CANCELLED_SHARE = 0.08;

    private const char HIGH_HALF = (char)0xD83D;
    private const char LOW_HALF = (char)0xDE00;
    private const char TWO_BYTES = (char)0xE9;

    private static readonly string[] REASONS =
        ["\"canceled\"", "\"max. resource limit exceeded\"", "\"incomplete\"", "\"memout\"", "\"timeout\"", "\"\"", "(incomplete (theory arithmetic))", "unknown"];

    private static readonly long[] COUNTS = [0, 1, 99, 100, 223, 4_294_967_295, 5_000_000_000];

    private static readonly string[] VERDICTS = ["Exited", "Exited", "Exited", "OutOfFuel", "OutOfMemory", "Trap", "EngineError", "EngineError", "Interrupted", "OutputLimit", "NotInstantiated", "NoStart"];

    private static readonly int[] EXIT_CODES = [1, 101, 3];

    #endregion

    #region Fields

    private readonly Random m_random;
    private readonly SmtProblemData[] m_problems;
    private readonly SmtOptionsData[] m_options;
    private readonly string[] m_noise;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a generator.
    /// </summary>
    /// <param name="seed">What every choice follows from.</param>
    public SmtSessionCaseGenerator(int seed)
    {
        m_random = new Random(seed);

        m_problems =
        [
            SmtSessionCases.Of("check-sat").Problem, SmtSessionCases.Of("check-sat-values").Problem, SmtSessionCases.Of("prove-proved").Problem,
            SmtSessionCases.Of("enumerate-complete").Problem, SmtSessionCases.Of("enumerate-limit").Problem, SmtSessionCases.Of("enumerate-limit-of-the-options").Problem
        ];

        m_options =
        [
            new SmtOptionsData(), new SmtOptionsData { ResourceLimit = 100 }, new SmtOptionsData { MaxModels = 1 },
            new SmtOptionsData { HardCapSeconds = 1, MemoryMegabytes = 16, MaxModels = 4 }
        ];

        // A pair of surrogates is two characters and four bytes: a text of them, an odd number of
        // characters long, is cut inside a pair wherever a count of characters cuts it.
        var pairs = string.Concat(Enumerable.Repeat(HIGH_HALF.ToString() + LOW_HALF, 9_000));

        m_noise =
        [
            ERROR, SILENT, SAT, UNSAT, UNKNOWN, "maybe\n", "statistics\n", "((y 3))\n", "((x 7))\n", SmtSessionCases.MODEL, SmtSessionCases.VALUES,
            "(:reason-unknown \"memout\")\n", "(:rlimit-count 7)\n", ERROR + SAT, SmtSessionCases.MODEL + ERROR, "(define-fun |" + TWO_BYTES + "| () Int 1)\n",
            new string('z', 20_000) + "\n", "(" + new string(TWO_BYTES, 9_000) + ")\n", pairs + "\n", "(" + pairs + ")"
        ];
    }

    #endregion

    #region Functions

    /// <summary>
    /// Makes the next conversation.
    /// </summary>
    /// <param name="number">Its number, for its name.</param>
    /// <returns>The case; what its row must say is not stated.</returns>
    public SmtSessionCase Next(int number)
    {
        var problem = Pick(m_problems);
        var options = Pick(m_options);

        var answers = problem.Query == SmtQuery.Enumerate ? Enumeration() : Answer(problem);
        for (var index = 0; index < answers.Count; index++)
        {
            if (m_random.NextDouble() < CHANGED_SHARE)
                answers[index] = Pick(m_noise);
        }

        // The run may end before the conversation does: the solver then answers no further.
        var cut = m_random.NextDouble() < CUT_SHARE ? m_random.Next(answers.Count + 1) : answers.Count;
        var kept = answers.Take(cut).ToList();

        return new SmtSessionCase($"generated-{number}", problem, options, kept, "")
        {
            End = End(options),
            CancelAfter = m_random.NextDouble() < CANCELLED_SHARE ? m_random.Next(kept.Count + 2) : -1
        };
    }

    #endregion

    #region Tools

    /// <summary>What a solver answers to a check or a proof, in order: the problem, the check, the statistics, then the model (and the values) or the reason.</summary>
    private List<string> Answer(SmtProblemData problem)
    {
        var verdict = Verdict();
        var answers = new List<string> { SILENT, verdict, Counted() };

        if (verdict == SAT)
        {
            answers.Add(SmtSessionCases.MODEL);
            if (problem.Values.Count > 0)
                answers.Add(SmtSessionCases.VALUES);
        }
        else if (verdict == UNKNOWN)
        {
            answers.Add(Reason());
        }

        return answers;
    }

    /// <summary>What a solver answers to an enumeration: a round a model (the check, the statistics, the values, the model, the assertion that rules it out) until a check does not say sat.</summary>
    private List<string> Enumeration()
    {
        var answers = new List<string> { SILENT };

        for (var round = 0; round < MOST_ROUNDS; round++)
        {
            var verdict = Verdict();
            answers.Add(verdict);
            answers.Add(Counted());

            if (verdict == UNKNOWN)
                answers.Add(Reason());

            if (verdict != SAT)
                break;

            answers.Add($"((x {round}))\n");
            answers.Add(SmtSessionCases.MODEL);
            answers.Add(SILENT);
        }

        return answers;
    }

    private SmtScriptedEnd? End(SmtOptionsData options)
    {
        if (m_random.NextDouble() < CLEAN_END_SHARE)
            return null;

        var verdict = Pick(VERDICTS);
        var fuel = SmtRowCheckRig.Fuel(options);
        var memory = SmtRowCheckRig.HostMemoryBytes(options);

        return new SmtScriptedEnd
        {
            Verdict = verdict,
            ExitCode = verdict == "Exited" ? Pick(EXIT_CODES) : verdict == "OutOfMemory" ? 101 : -1,
            Started = verdict is not ("NotInstantiated" or "NoStart"),
            Fault = m_random.Next(2) == 0 ? null : "wasm trap: unreachable code reached",
            EngineError = verdict != "EngineError" ? null : m_random.Next(2) == 0 ? "something of the engine's own" : "error while executing at wasm backtrace: GC heap out of memory",
            Before = m_random.Next(20) == 0 ? "a banner nobody asked for\n" : "",
            Rest = Pick(["", "", "(\n  (define-fun x () Int\n", ERROR]),
            Stderr = Pick(["", "", "(error \"out of memory\")\n", "a warning of the solver's\n"]),
            Fuel = Pick([0, 1, 1_000, fuel / 2, fuel]),
            PeakMemoryBytes = Pick([0, 65_536, memory / 2, memory])
        };
    }

    private string Verdict()
    {
        return m_random.Next(10) switch
        {
            < 6 => SAT,
            < 8 => UNSAT,
            _ => UNKNOWN
        };
    }

    private string Counted()
    {
        return $"(:max-memory 9.29\n :rlimit-count {Pick(COUNTS)})\n";
    }

    private string Reason()
    {
        return $"(:reason-unknown {Pick(REASONS)})\n";
    }

    private T Pick<T>(T[] items)
    {
        return items[m_random.Next(items.Length)];
    }

    #endregion
}
