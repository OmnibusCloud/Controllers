using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Every way a problem's session on a node ends, each as a script: every answer of the solver the
/// session reads (a verdict, the statistics, a model, values, a reason, an error, something that
/// cannot be read) at every place of the conversation of every query, and every end of a run the
/// sandbox reports (a clean exit, another status, out of fuel, out of memory in its three shapes,
/// a trap, an error of the engine, the output cap, the backstop, a module that did not start, a
/// cancel), also the ends the real solver cannot be made to reach on demand; and the problems no
/// session runs for. Together they reach every branch of the code that makes a session's outcome,
/// so a rule about what a row may say is held to all of them.
/// </summary>
internal static class SmtSessionCases
{
    #region Constants

    /// <summary>The problem every case is about: two integers whose sum is ten.</summary>
    public const string PROBLEM = "(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(assert (= (+ x y) 10))";

    /// <summary>The solver's answer to a model.</summary>
    public const string MODEL = "(\n  (define-fun x () Int\n    7)\n  (define-fun y () Int\n    3)\n)\n";

    /// <summary>The solver's answer to the values a check asks for.</summary>
    public const string VALUES = "((x 7)\n ((+ x y) 10))\n";

    /// <summary>The fuel of a session under the default options: sixty seconds at the reference rate.</summary>
    public const long DEFAULT_FUEL = 300_000_000_000;

    /// <summary>The most models the enumerations of the cases look for, unless their name says otherwise.</summary>
    public const int MAX_MODELS = 3;

    private const string SILENT = "";
    private const string SAT = "sat\n";
    private const string UNSAT = "unsat\n";
    private const string UNKNOWN = "unknown\n";
    private const string COUNTED = "(:max-memory 9.29\n :rlimit-count 223)\n";
    private const string X7 = "((x 7))\n";
    private const string X8 = "((x 8))\n";
    private const string ERROR = "(error \"line 9 column 1: something is wrong\")\n";

    private const long SMALL_RESOURCE_LIMIT = 100;

    private const char HIGH_HALF = (char)0xD83D;
    private const char LOW_HALF = (char)0xDE00;

    private static readonly Lazy<IReadOnlyList<SmtSessionCase>> ALL = new(Build);

    #endregion

    #region Functions

    /// <summary>Finds a case.</summary>
    /// <param name="id">The case's name.</param>
    /// <returns>The case.</returns>
    public static SmtSessionCase Of(string id)
    {
        return ALL.Value.Single(one => one.Id == id);
    }

    private static IReadOnlyList<SmtSessionCase> Build()
    {
        var plain = new SmtOptionsData();
        var small = new SmtOptionsData { ResourceLimit = SMALL_RESOURCE_LIMIT };
        var cases = new List<SmtSessionCase>();

        var check = SmtSolverProblems.Check("check", PROBLEM);
        var values = SmtSolverProblems.Check("check-values", PROBLEM, "x", "(+ x y)");
        var prove = SmtSolverProblems.Prove("prove", PROBLEM, "(> x 0)");
        var enumerate = SmtSolverProblems.Enumerate("enumerate", PROBLEM, MAX_MODELS, "x");
        var two = SmtSolverProblems.Enumerate("enumerate-two", PROBLEM, 2, "x");
        var byOptions = SmtSolverProblems.Enumerate("enumerate-default", PROBLEM, 0, "x");

        string[] one = [SILENT, SAT, COUNTED, X7, MODEL];
        string[] both = [.. one, SILENT, SAT, COUNTED, X8, MODEL];
        string[] complete = [.. both, SILENT, UNSAT, COUNTED];

        void Add(string id, SmtProblemData problem, string expected, string[] answers, SmtScriptedEnd? end = null, SmtOptionsData? options = null, int cancelAfter = -1)
        {
            cases.Add(new SmtSessionCase(id, problem, options ?? plain, answers, expected) { End = end, CancelAfter = cancelAfter });
        }

        void Without(string id, SmtProblemData problem, string expected, SmtOptionsData? options = null, int cancelAfter = -1)
        {
            cases.Add(new SmtSessionCase(id, problem, options ?? plain, [], expected) { NoSession = true, CancelAfter = cancelAfter });
        }

        // check: answered.
        Add("check-sat", check, "ok sat", [SILENT, SAT, COUNTED, MODEL]);
        Add("check-sat-values", values, "ok sat", [SILENT, SAT, COUNTED, MODEL, VALUES]);
        Add("check-unsat", check, "ok unsat", [SILENT, UNSAT, COUNTED]);
        Add("check-unknown-incomplete", check, "ok unknown incomplete", [SILENT, UNKNOWN, COUNTED, Reason("\"incomplete\"")]);
        Add("check-unknown-rlimit", check, "ok unknown rlimit", [SILENT, UNKNOWN, COUNTED, Reason("\"canceled\"")], options: small);
        Add("check-unknown-memout", check, "ok unknown memout", [SILENT, UNKNOWN, COUNTED, Reason("\"memout\"")]);
        Add("check-unknown-other", check, "ok unknown other", [SILENT, UNKNOWN, COUNTED, Reason("\"timeout\"")]);
        Add("check-unknown-reason-list", check, "ok unknown incomplete", [SILENT, UNKNOWN, COUNTED, Reason("(incomplete (theory arithmetic))")]);

        // A model of surrogate pairs, longer than the transcript keeps: where a count of
        // characters cuts it, the cut falls inside a pair, and the transcript a row carries still
        // starts at a whole character.
        Add("check-sat-model-of-pairs", check, "ok sat", [SILENT, SAT, COUNTED, "(" + string.Concat(Enumerable.Repeat(HIGH_HALF.ToString() + LOW_HALF, 9_000)) + ")"]);

        // check: a budget ended the session.
        Add("check-fuel-before-the-check", check, "ok unknown fuel", [], Ended("OutOfFuel"));
        Add("check-fuel-in-the-check", check, "ok unknown fuel", [SILENT], new SmtScriptedEnd { Verdict = "OutOfFuel", ExitCode = -1, Fuel = DEFAULT_FUEL });
        Add("check-fuel-in-the-model", check, "ok unknown fuel", [SILENT, SAT, COUNTED], Ended("OutOfFuel"));
        Add("check-memory-exit", check, "ok unknown memout", [SILENT], new SmtScriptedEnd { ExitCode = 101 });
        Add("check-memory-refused", check, "ok unknown memout", [SILENT], new SmtScriptedEnd { Verdict = "OutOfMemory", ExitCode = 101, Stderr = "(error \"out of memory\")\n" });
        Add("check-memory-exception-heap", check, "ok unknown memout", [SILENT], new SmtScriptedEnd { Verdict = "EngineError", ExitCode = -1, EngineError = "error while executing at wasm backtrace: GC heap out of memory" });

        // check: an error of the solver's, at every place.
        Add("check-error-in-the-problem", check, "input_error", [ERROR]);
        Add("check-error-in-the-check", check, "input_error", [SILENT, ERROR]);
        Add("check-error-in-the-statistics", check, "input_error", [SILENT, SAT, ERROR]);
        Add("check-error-in-the-model", check, "input_error", [SILENT, SAT, COUNTED, ERROR]);
        Add("check-error-in-the-values", values, "input_error", [SILENT, SAT, COUNTED, MODEL, ERROR]);
        Add("check-error-in-the-reason", check, "input_error", [SILENT, UNKNOWN, COUNTED, ERROR]);
        Add("check-error-the-solver-counted", check, "input_error", [SILENT, SAT, COUNTED, MODEL + ERROR], new SmtScriptedEnd { ExitCode = 1 });

        // check: an answer that cannot be read, at every place.
        Add("check-banner", check, "engine_crash unreadable_answer", [], new SmtScriptedEnd { Before = "a banner nobody asked for\n" });
        Add("check-verdict-unreadable", check, "engine_crash unreadable_answer", [SILENT, "maybe\n"]);
        Add("check-statistics-unreadable", check, "engine_crash unreadable_answer", [SILENT, SAT, "statistics\n"]);
        Add("check-model-empty", check, "engine_crash unreadable_answer", [SILENT, SAT, COUNTED, SILENT]);
        Add("check-values-empty", values, "engine_crash unreadable_answer", [SILENT, SAT, COUNTED, MODEL, SILENT]);
        Add("check-reason-empty", check, "engine_crash unreadable_answer", [SILENT, UNKNOWN, COUNTED, SILENT]);

        // check: a crash, and a solver that ended by itself without an answer.
        Add("check-trap", check, "engine_crash trap", [SILENT, SAT, COUNTED], new SmtScriptedEnd { Verdict = "Trap", ExitCode = -1, Fault = "wasm trap: unreachable code reached" });
        Add("check-engine-error", check, "engine_crash engine_error", [SILENT], new SmtScriptedEnd { Verdict = "EngineError", ExitCode = -1, EngineError = "something of the engine's own" });
        Add("check-exit-in-the-middle", check, "engine_crash exit", [SILENT], new SmtScriptedEnd { ExitCode = 110 });
        Add("check-exit-before-the-end", check, "engine_crash exit", [SILENT], new SmtScriptedEnd { ExitCode = 0 });
        Add("check-exit-after-the-answer", check, "engine_crash exit", [SILENT, UNSAT, COUNTED], new SmtScriptedEnd { ExitCode = 114 });

        // check: the output past its cap, whatever had been answered by then.
        Add("check-output-in-the-check", check, "output_exceeded", [SILENT], Ended("OutputLimit"));
        Add("check-output-in-the-model", check, "output_exceeded", [SILENT, SAT, COUNTED], Ended("OutputLimit"));
        Add("check-output-in-the-values", values, "output_exceeded", [SILENT, SAT, COUNTED, MODEL], Ended("OutputLimit"));
        Add("check-output-after-unsat", check, "output_exceeded", [SILENT, UNSAT], Ended("OutputLimit"));
        Add("check-output-after-unknown", check, "output_exceeded", [SILENT, UNKNOWN, COUNTED], Ended("OutputLimit"));

        // check: a fault of the node.
        Add("check-backstop", check, "node_fault backstop", [SILENT], Ended("Interrupted"));
        Add("check-backstop-after-the-model", check, "node_fault backstop", [SILENT, SAT, COUNTED, MODEL], Ended("Interrupted"));
        Add("check-not-instantiated", check, "node_fault not_instantiated", [], new SmtScriptedEnd { Started = false, Verdict = "NotInstantiated", ExitCode = -1, Fault = "module failed to instantiate", Fuel = 299_990_300_672 });
        Add("check-no-start", check, "node_fault not_instantiated", [], new SmtScriptedEnd { Started = false, Verdict = "NoStart", ExitCode = -1 });
        Add("check-cancelled-at-once", check, "node_fault cancelled", [SILENT, SAT, COUNTED, MODEL], cancelAfter: 0);
        Add("check-cancelled-in-the-model", check, "node_fault cancelled", [SILENT, SAT, COUNTED, MODEL], cancelAfter: 4);

        // prove.
        Add("prove-proved", prove, "ok unsat proved", [SILENT, UNSAT, COUNTED]);
        Add("prove-counterexample", prove, "ok sat counterexample", [SILENT, SAT, COUNTED, MODEL]);
        Add("prove-unknown", prove, "ok unknown incomplete", [SILENT, UNKNOWN, COUNTED, Reason("\"incomplete\"")]);
        Add("prove-fuel", prove, "ok unknown fuel", [SILENT], Ended("OutOfFuel"));
        Add("prove-trap", prove, "engine_crash trap", [SILENT, SAT, COUNTED, MODEL], new SmtScriptedEnd { Verdict = "Trap", ExitCode = -1 });
        Add("prove-output-in-the-model", prove, "output_exceeded", [SILENT, SAT, COUNTED], Ended("OutputLimit"));
        Add("prove-output-after-unsat", prove, "output_exceeded", [SILENT, UNSAT], Ended("OutputLimit"));

        // enumerate: answered.
        Add("enumerate-complete", enumerate, "ok sat 2 complete", complete);
        Add("enumerate-none", enumerate, "ok unsat 0 complete", [SILENT, UNSAT, COUNTED]);
        Add("enumerate-limit", two, "ok sat 2 limit", both);
        Add("enumerate-limit-of-the-options", byOptions, "ok sat 2 limit", both);
        Add("enumerate-then-unknown", enumerate, "ok sat incomplete 1 incomplete", [.. one, SILENT, UNKNOWN, COUNTED, Reason("\"incomplete\"")]);
        Add("enumerate-unknown-at-once", enumerate, "ok unknown other 0 incomplete", [SILENT, UNKNOWN, COUNTED, Reason("\"timeout\"")]);

        // enumerate: a budget ended the session, at every place that changes what is kept.
        Add("enumerate-fuel-at-once", enumerate, "ok unknown fuel 0 incomplete", [SILENT], Ended("OutOfFuel"));
        Add("enumerate-fuel-in-the-second-model", enumerate, "ok sat fuel 1 incomplete", [.. one, SILENT, SAT, COUNTED, X8], Ended("OutOfFuel"));
        Add("enumerate-fuel-at-the-limit", two, "ok sat fuel 2 incomplete", both, Ended("OutOfFuel"));
        Add("enumerate-memory-after-the-last-check", enumerate, "ok sat memout 2 incomplete", complete, new SmtScriptedEnd { ExitCode = 101 });
        Add("enumerate-memory-after-its-word", enumerate, "ok sat memout 1 incomplete", [.. one, SILENT, UNKNOWN, COUNTED, Reason("\"memout\"")], new SmtScriptedEnd { ExitCode = 101 });

        // enumerate: errors, crashes, the output cap and faults, with and without a model kept.
        Add("enumerate-error-in-the-values", enumerate, "input_error", [SILENT, SAT, COUNTED, ERROR]);
        Add("enumerate-error-in-the-model", enumerate, "input_error", [SILENT, SAT, COUNTED, X7, ERROR]);
        Add("enumerate-error-in-the-block", enumerate, "input_error", [.. one, ERROR]);
        Add("enumerate-values-unreadable", enumerate, "engine_crash unreadable_answer", [SILENT, SAT, COUNTED, "((y 3))\n"]);
        Add("enumerate-model-empty", enumerate, "engine_crash unreadable_answer", [SILENT, SAT, COUNTED, X7, SILENT]);
        Add("enumerate-trap-with-a-model", enumerate, "engine_crash trap", [.. one, SILENT], new SmtScriptedEnd { Verdict = "Trap", ExitCode = -1 });
        Add("enumerate-output-in-the-check", enumerate, "output_exceeded", [SILENT], Ended("OutputLimit"));
        Add("enumerate-output-after-unsat", enumerate, "output_exceeded", [SILENT, UNSAT], Ended("OutputLimit"));
        Add("enumerate-output-in-the-first-model", enumerate, "output_exceeded", [SILENT, SAT, COUNTED, X7], Ended("OutputLimit"));
        Add("enumerate-output-with-a-model", enumerate, "output_exceeded", [.. one, SILENT, SAT, COUNTED, X8], Ended("OutputLimit"));
        Add("enumerate-backstop-with-a-model", enumerate, "node_fault backstop", [.. one, SILENT], Ended("Interrupted"));
        Add("enumerate-cancelled-with-a-model", enumerate, "node_fault cancelled", complete, cancelAfter: 6);

        // No session: a problem that is refused, and a node that cannot run one.
        var forged = SmtSolverProblems.Check("refused-hash", PROBLEM);
        forged.Hash = new string('0', 64);
        var undeclared = SmtSolverProblems.Enumerate("refused-projection", PROBLEM, MAX_MODELS, "x");
        undeclared.Project = ["z"];
        var goalless = SmtSolverProblems.Prove("refused-goal", PROBLEM, "(> x 0)");
        goalless.Goal = "";

        Without("refused-hash", forged, "refused hash_mismatch");
        Without("refused-text", new SmtProblemData { Id = "refused-text", Query = SmtQuery.Check, Text = "(assert" }, "refused syntax_error");
        Without("refused-projection", undeclared, "refused unknown_constant");
        Without("refused-goal", goalless, "refused field_missing");
        Without("refused-options", check, "refused options_invalid", new SmtOptionsData { MaxModels = -1 });
        Without("refused-profile", check, "refused profile_unknown", new SmtOptionsData { Profile = "another-solver" });
        Without("no-sandbox", check, "node_fault sandbox_unavailable");
        Without("cancelled-before-the-session", enumerate, "node_fault cancelled", cancelAfter: 0);

        return cases;
    }

    #endregion

    #region Tools

    private static string Reason(string word)
    {
        return $"(:reason-unknown {word})\n";
    }

    private static SmtScriptedEnd Ended(string verdict)
    {
        return new SmtScriptedEnd { Verdict = verdict, ExitCode = -1 };
    }

    #endregion

    #region Properties

    /// <summary>Every case.</summary>
    public static IReadOnlyList<SmtSessionCase> All => ALL.Value;

    /// <summary>The names of the cases, for a test that takes one a case.</summary>
    public static IEnumerable<string> Ids => ALL.Value.Select(one => one.Id);

    #endregion
}
