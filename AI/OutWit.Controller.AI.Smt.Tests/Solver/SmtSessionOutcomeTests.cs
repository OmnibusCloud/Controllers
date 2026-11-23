using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// Every way a session can end, mapped to its outcome, held with a solver that is a script
/// (<see cref="SmtSessionScript"/>): the answers a solver could give and the ends a run could
/// have, also the ones the real solver cannot be made to give on demand (an answer that cannot
/// be read, a trap, an exit status that is no answer, an error of the engine, a module that does
/// not start). The ends the real solver does reach are held again with it, in the tests of the
/// category Solver.
/// <para>
/// The rule the mapping follows: a verdict is claimed only by a session that was answered and
/// whose solver then ended cleanly; a budget that ends a session makes its answer
/// <c>unknown</c>; the clock is never a verdict.
/// </para>
/// </summary>
[TestFixture]
public sealed class SmtSessionOutcomeTests
{
    #region Constants

    private const string PROBLEM = "(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(assert (= (+ x y) 10))";

    private const string SILENT = "";
    private const string SAT = "sat\n";
    private const string UNKNOWN = "unknown\n";
    private const string COUNTED = "(:max-memory 9.29\n :rlimit-count 223)\n";
    private const string A_MODEL = "(\n  (define-fun x () Int\n    7)\n  (define-fun y () Int\n    3)\n)\n";

    private const long LIMIT = 1_000_000;

    #endregion

    #region Input Error Tests

    [Test]
    public void AnErrorInAnswerToTheProblemIsAnInputErrorWithTheSolversMessageTest()
    {
        var held = Check([SILENT + "(error \"line 7 column 11: unknown constant y\")\n"]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("input_error"));
            Assert.That(held.Outcome.Message, Is.EqualTo("line 7 column 11: unknown constant y"));
            Assert.That(held.Sent, Has.Count.EqualTo(1), "no check is sent for a problem the solver did not take");
            Assert.That(held.Ended, Is.True);
            Assert.That(held.Outcome.Verdict, Is.EqualTo("none"));
            Assert.That(held.Outcome.Checks, Is.Zero);
        });
    }

    [Test]
    public void TheSolversMessageIsCutToFiveHundredAndTwelveCharactersTest()
    {
        var held = Check(["(error \"line 7 column 11: unknown constant " + new string('y', 2000) + "\")\n"]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("input_error"));
            Assert.That(held.Outcome.Message, Has.Length.EqualTo(512));
            Assert.That(held.Outcome.Message, Does.StartWith("line 7 column 11: unknown constant yyyy"));
        });
    }

    [Test]
    public void AnythingSaidInAnswerToAStatementIsAnInputErrorTest()
    {
        // The solver says "unsupported" to a logic it does not know, and goes on; a statement is
        // answered by nothing, and whatever is said to one is about what was given.
        var held = Check(["unsupported\n"]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("input_error"));
            Assert.That(held.Outcome.Message, Is.EqualTo("unsupported"));
            Assert.That(held.Sent, Has.Count.EqualTo(1));
        });
    }

    [TestCase(2, "the check")]
    [TestCase(3, "the statistics")]
    [TestCase(4, "the model")]
    public void AnErrorInAnswerToAnyCommandIsAnInputErrorTest(int step, string what)
    {
        var answers = new List<string> { SILENT, SAT, COUNTED, A_MODEL }.Take(step - 1).Append("(error \"line 9 column 1: model is not available\")\n").ToList();

        var held = Check(answers);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("input_error"), what);
            Assert.That(held.Outcome.Message, Is.EqualTo("line 9 column 1: model is not available"), what);
            Assert.That(held.Sent, Has.Count.EqualTo(step), $"{what}: nothing is sent after an error");
        });
    }

    [Test]
    public void AnErrorInAnswerToTheAssertionThatRulesAModelOutIsAnInputErrorThatKeepsTheModelsTest()
    {
        var problem = SmtSolverProblems.Enumerate("enumerate", PROBLEM, 10, "x");

        var held = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, SAT, COUNTED, "((x 7))\n", A_MODEL, "(error \"line 12 column 20: unknown constant k!0\")\n"]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("input_error"));
            Assert.That(held.Outcome.Message, Is.EqualTo("line 12 column 20: unknown constant k!0"));
            Assert.That(held.Outcome.Models, Has.Count.EqualTo(1), "what was whole is kept for whoever looks into it");
            Assert.That(held.Outcome.Count, Is.EqualTo(1));
            Assert.That(held.Outcome.Complete, Is.False);
        });
    }

    [Test]
    public void AnErrorTheSolverCountedButGaveNoCommandsAnswerIsAnInputErrorTest()
    {
        // The model came, and an error after it in the same answer: the answer is taken for the
        // model, and the solver's exit status 1 says that it counted an error.
        var held = Check([SILENT, SAT, COUNTED, A_MODEL + "(error \"line 9 column 3: something else\")\n"], new SmtScriptedEnd { ExitCode = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(held.Ended, Is.True);
            Assert.That(held.Outcome.Words(), Is.EqualTo("input_error"));
            Assert.That(held.Outcome.Message, Is.EqualTo("line 9 column 3: something else"));
        });
    }

    #endregion

    #region Unreadable Answer Tests

    [Test]
    public void ASolverThatWritesBeforeItReadsIsACrashTest()
    {
        var held = Check([], new SmtScriptedEnd { Before = "a banner nobody asked for\n" });

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("engine_crash unreadable_answer"));
            Assert.That(held.Sent, Is.Empty, "the problem is not sent to a solver that does not keep to the conversation");
            Assert.That(held.Ended, Is.True);
        });
    }

    [TestCase("maybe\n")]
    [TestCase("sat\nunsat\n")]
    [TestCase("(sat)\n")]
    [TestCase("")]
    public void AnAnswerOfACheckThatIsNoVerdictIsACrashTest(string answer)
    {
        var held = Check([SILENT, answer]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("engine_crash unreadable_answer"));
            Assert.That(held.Outcome.Message, Does.Contain("check-sat"));
            Assert.That(held.Sent, Has.Count.EqualTo(2), "nothing is sent after an answer that cannot be read");
            Assert.That(held.Outcome.Verdict, Is.EqualTo("none"));
        });
    }

    [TestCase("statistics\n")]
    [TestCase("(:rlimit-count)\n")]
    [TestCase("(:rlimit-count many)\n")]
    [TestCase("(:rlimit-count (1 2))\n")]
    [TestCase("(:rlimit-count 5")]
    [TestCase("")]
    public void StatisticsThatAreNotStatisticsAreACrashTest(string answer)
    {
        var held = Check([SILENT, SAT, answer]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("engine_crash unreadable_answer"));
            Assert.That(held.Sent, Has.Count.EqualTo(3));
        });
    }

    [TestCase("((x 7))\n", TestName = "{m}(one value for two constants)")]
    [TestCase("((y 3)\n (x 7))\n", TestName = "{m}(the constants in another order)")]
    [TestCase("((x 7)\n (z 3))\n", TestName = "{m}(another constant)")]
    [TestCase("((x 7 8)\n (y 3))\n", TestName = "{m}(a pair of three)")]
    [TestCase("(x 7)\n", TestName = "{m}(no list of pairs)")]
    [TestCase("((x 7)\n (y 3)", TestName = "{m}(a list that is not closed)")]
    [TestCase("", TestName = "{m}(nothing)")]
    public void ValuesOfAnEnumerationThatAreNotTheValuesOfItsConstantsAreACrashTest(string answer)
    {
        var problem = SmtSolverProblems.Enumerate("enumerate", PROBLEM, 10, "x", "y");

        var held = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, SAT, COUNTED, answer]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("engine_crash unreadable_answer"));
            Assert.That(held.Outcome.Count, Is.Zero, "a model whose values could not be read is not counted");
            Assert.That(held.Sent[^1], Is.EqualTo("(get-value (x y))\n"), "no assertion is made of values that were not read");
        });
    }

    #endregion

    #region Reason Tests

    [TestCase("canceled", LIMIT, "rlimit", TestName = "{m}(canceled at the limit)")]
    [TestCase("canceled", LIMIT + 31, "rlimit", TestName = "{m}(canceled with the counter at the limit and what the problem had counted)")]
    [TestCase("max. resource limit exceeded", LIMIT, "rlimit", TestName = "{m}(the limit ended the check by an exception)")]
    [TestCase("canceled", LIMIT - 1, "other", TestName = "{m}(canceled below the limit is not the limit)")]
    [TestCase("max. resource limit exceeded", 12, "other", TestName = "{m}(the words without the counter are not the limit)")]
    [TestCase("incomplete", 12, "incomplete")]
    [TestCase("(incomplete (theory arithmetic))", 12, "incomplete")]
    [TestCase("smt tactic failed to show goal to be sat/unsat (incomplete quantifiers)", 12, "incomplete")]
    [TestCase("memout", 12, "memout")]
    [TestCase("timeout", 12, "other")]
    [TestCase("unknown", 12, "other")]
    public void TheReasonOfAnUnknownIsReadFromTheSolversWordAndItsCounterTest(string word, long count, string reason)
    {
        var held = SmtSessionScript.Hold(SmtSolverProblems.Check("check", PROBLEM), new SmtOptionsData { ResourceLimit = LIMIT },
            [SILENT, UNKNOWN, $"(:rlimit-count {count})\n", $"(:reason-unknown \"{word}\")\n"]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo($"ok unknown {reason}"));
            Assert.That(held.Outcome.ReasonText, Is.EqualTo(word), "the solver's own word is kept");
            Assert.That(held.Outcome.ResourceCount, Is.EqualTo(count));
        });
    }

    [Test]
    public void ACounterAboveThirtyTwoBitsIsReadAsTheSolverWritesItTest()
    {
        // The solver writes a counter above 4,294,967,295 as a number with two decimals, and leaves a counter of zero out.
        var large = Check([SILENT, SAT, "(:max-memory 9.29\n :rlimit-count 5000000000.00)\n", A_MODEL]);
        var none = Check([SILENT, SAT, "(:max-memory 9.29)\n", A_MODEL]);

        Assert.Multiple(() =>
        {
            Assert.That(large.Outcome.ResourceCount, Is.EqualTo(5_000_000_000));
            Assert.That(none.Outcome.Words(), Is.EqualTo("ok sat"));
            Assert.That(none.Outcome.ResourceCount, Is.Zero);
        });
    }

    #endregion

    #region Budget Tests

    [Test]
    public void OutOfFuelIsUnknownByFuelWhateverWasAnsweredBeforeTest()
    {
        var end = new SmtScriptedEnd { Verdict = "OutOfFuel", ExitCode = -1, Fuel = 300_000_000_000 };

        var inTheCheck = Check([SILENT], end);
        var inTheModel = Check([SILENT, SAT, COUNTED], end);

        Assert.Multiple(() =>
        {
            Assert.That(inTheCheck.Outcome.Words(), Is.EqualTo("ok unknown fuel"));
            Assert.That(inTheCheck.Outcome.Fuel, Is.EqualTo(300_000_000_000));
            Assert.That(inTheCheck.Outcome.Checks, Is.EqualTo(1));
            Assert.That(inTheModel.Outcome.Words(), Is.EqualTo("ok unknown fuel"), "a sat whose model did not come whole is no answer: the budget ended the session");
            Assert.That(inTheModel.Outcome.Models, Is.Empty);
            Assert.That(inTheModel.Outcome.ResourceCount, Is.EqualTo(223), "the counter of the last check that was answered");
        });
    }

    [Test]
    public void AnEnumerationABudgetEndsKeepsItsWholeModelsAndIsIncompleteTest()
    {
        var problem = SmtSolverProblems.Enumerate("enumerate", PROBLEM, 10, "x");
        var end = new SmtScriptedEnd { Verdict = "OutOfFuel", ExitCode = -1 };

        // One model whole, then the values of a second one, and no model for them.
        var held = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, SAT, COUNTED, "((x 7))\n", A_MODEL, SILENT, SAT, COUNTED, "((x 8))\n"], end);
        var atOnce = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT], end);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("ok sat fuel 1 incomplete"));
            Assert.That(held.Outcome.Values, Is.EqualTo(new[] { "((x 7))\n" }), "the model in the making is not half kept");
            Assert.That(held.Outcome.Models, Has.Count.EqualTo(1));
            Assert.That(atOnce.Outcome.Words(), Is.EqualTo("ok unknown fuel 0 incomplete"));
        });
    }

    [TestCase("OutOfMemory", 101, null, TestName = "{m}(the solver's exit 101 with its line)")]
    [TestCase("Exited", 101, null, TestName = "{m}(the solver's exit 101 alone)")]
    [TestCase("EngineError", -1, "error while executing at wasm backtrace: GC heap out of memory", TestName = "{m}(the heap of the exception objects at the sandbox's cap)")]
    public void ARefusalOfMemoryIsUnknownByMemoryTest(string verdict, int exitCode, string? engineError)
    {
        var end = new SmtScriptedEnd { Verdict = verdict, ExitCode = exitCode, EngineError = engineError, Stderr = "(error \"out of memory\")\n" };

        var held = Check([SILENT], end);

        Assert.That(held.Outcome.Words(), Is.EqualTo("ok unknown memout"));
    }

    #endregion

    #region Crash Tests

    [Test]
    public void ATrapIsACrashNamedInFixedWordsTest()
    {
        const string FAULT = "wasm trap: unreachable code reached (abort, a failed assertion, a trap instruction)";

        var held = Check([SILENT, SAT, COUNTED], new SmtScriptedEnd { Verdict = "Trap", ExitCode = -1, Fault = FAULT, Stderr = FAULT });

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("engine_crash trap"));
            Assert.That(held.Outcome.Message, Is.EqualTo(FAULT));
            Assert.That(held.Outcome.Verdict, Is.EqualTo("none"), "the sat before the crash is claimed by nobody");
            Assert.That(held.Outcome.Checks, Is.EqualTo(1));
        });
    }

    [Test]
    public void AnErrorOfTheEngineThatIsNotTheExceptionHeapIsACrashTest()
    {
        var held = Check([SILENT], new SmtScriptedEnd { Verdict = "EngineError", ExitCode = -1, EngineError = "something of the engine's own at 0x7ff612345678" });

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("engine_crash engine_error"));
            Assert.That(held.Outcome.Message, Does.Not.Contain("0x7ff6"), "the engine's own text may name an address of this machine: the outcome has fixed words");
        });
    }

    [TestCase(110, false, TestName = "{m}(a status that is no answer, in the middle)")]
    [TestCase(0, false, TestName = "{m}(status 0 before the conversation was over)")]
    [TestCase(114, true, TestName = "{m}(a status that is no answer, after the last answer)")]
    public void ASolverThatEndsByItselfWithoutHavingAnsweredIsACrashTest(int exitCode, bool afterTheLastAnswer)
    {
        var answers = afterTheLastAnswer ? new[] { SILENT, "unsat\n", COUNTED } : [SILENT];

        var held = Check(answers, new SmtScriptedEnd { ExitCode = exitCode });

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("engine_crash exit"));
            Assert.That(held.Outcome.Message, Does.Contain($"status {exitCode}"));
            Assert.That(held.Outcome.Verdict, Is.EqualTo("none"));
        });
    }

    [Test]
    public void OutputPastTheCapIsItsOwnStatusAndKeepsWhatTheCheckHadAnsweredTest()
    {
        var end = new SmtScriptedEnd { Verdict = "OutputLimit", ExitCode = -1, Rest = "(\n  (define-fun x () Int\n" };

        var inTheModel = Check([SILENT, SAT, COUNTED], end);
        var inTheCheck = Check([SILENT], end);
        var refuted = SmtSessionScript.Hold(SmtSolverProblems.Prove("prove", PROBLEM, "(> x 0)"), new SmtOptionsData(), [SILENT, SAT, COUNTED], end);
        var enumerated = SmtSessionScript.Hold(SmtSolverProblems.Enumerate("enumerate", PROBLEM, 10, "x"), new SmtOptionsData(), [SILENT, SAT, COUNTED, "((x 7))\n"], end);

        Assert.Multiple(() =>
        {
            Assert.That(inTheModel.Outcome.Words(), Is.EqualTo("output_exceeded"));
            Assert.That(inTheModel.Outcome.Verdict, Is.EqualTo("sat"), "the solver had said sat: it is the model that did not fit");
            Assert.That(inTheModel.Outcome.Models, Is.Empty, "a model that did not come whole is not kept");
            Assert.That(inTheModel.Outcome.ResourceCount, Is.EqualTo(223), "the statistics came before the model: the counter is known");
            Assert.That(inTheCheck.Outcome.Words(), Is.EqualTo("output_exceeded"));
            Assert.That(inTheCheck.Outcome.Verdict, Is.EqualTo("none"), "nothing was answered before the cap");
            Assert.That(refuted.Outcome.Verdict, Is.EqualTo("sat"));
            Assert.That(refuted.Outcome.Proof, Is.EqualTo("counterexample"), "a counterexample exists, though it did not fit");
            Assert.That(enumerated.Outcome.Verdict, Is.EqualTo("sat"));
            Assert.That(enumerated.Outcome.Count, Is.Zero, "the model in the making is not counted");
            Assert.That(enumerated.Outcome.Complete, Is.False);
        });
    }

    #endregion

    #region Node Fault Tests

    [Test]
    public void TheWallClockBackstopIsAFaultOfTheNodeAndNeverAVerdictTest()
    {
        var end = new SmtScriptedEnd { Verdict = "Interrupted", ExitCode = -1 };

        var inTheCheck = Check([SILENT], end);
        var afterAnAnswer = Check([SILENT, SAT, COUNTED], end);

        Assert.Multiple(() =>
        {
            Assert.That(inTheCheck.Outcome.Words(), Is.EqualTo("node_fault backstop"));
            Assert.That(inTheCheck.Outcome.Verdict, Is.EqualTo("none"), "the clock says something about the machine, never 'unknown' about the problem");
            Assert.That(inTheCheck.Outcome.Reason, Is.EqualTo("none"));
            Assert.That(afterAnAnswer.Outcome.Words(), Is.EqualTo("node_fault backstop"));
            Assert.That(afterAnAnswer.Outcome.Verdict, Is.EqualTo("none"));
        });
    }

    [TestCase("NotInstantiated")]
    [TestCase("NoStart")]
    public void AModuleThatDoesNotStartIsAFaultOfTheNodeTest(string verdict)
    {
        var held = Check([], new SmtScriptedEnd { Started = false, Verdict = verdict, ExitCode = -1, Fault = "module failed to instantiate: the store could not grant the memory", Fuel = 299_990_300_672 });

        Assert.Multiple(() =>
        {
            Assert.That(held.Sent, Is.Empty, "a module that never ran was never asked");
            Assert.That(held.Outcome.Words(), Is.EqualTo("node_fault not_instantiated"));
            Assert.That(held.Outcome.Fuel, Is.Zero, "the solver ran nothing: what the host counted for the attempt is not its fuel");
            Assert.That(held.Outcome.Message, Does.Contain("failed to instantiate"));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void ACancelledSessionEndsTheInputAtTheNextReadAndIsAFaultOfItsOwnKindTest(int after)
    {
        using var cancellation = new CancellationTokenSource();

        var held = SmtSessionScript.Hold(SmtSolverProblems.Check("check", PROBLEM), new SmtOptionsData(), [SILENT, SAT, COUNTED, A_MODEL], null,
            beforeAsk: sent =>
            {
                if (sent == after)
                    cancellation.Cancel();
            },
            cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("node_fault cancelled"));
            Assert.That(held.Sent, Has.Count.EqualTo(after), "nothing is sent once the session is cancelled");
            Assert.That(held.Ended, Is.True, "the input is ended, so the solver ends by itself");
            Assert.That(held.Outcome.Verdict, Is.EqualTo("none"));
        });
    }

    #endregion

    #region Tools

    private static SmtScripted Check(IReadOnlyList<string> answers, SmtScriptedEnd? end = null)
    {
        return SmtSessionScript.Hold(SmtSolverProblems.Check("check", PROBLEM), new SmtOptionsData(), answers, end);
    }

    #endregion
}
