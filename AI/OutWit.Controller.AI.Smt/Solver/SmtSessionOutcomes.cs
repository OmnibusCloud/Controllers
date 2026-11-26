using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// Makes the outcome of a session whose run is over, of how the run ended and of what the
/// conversation had gathered (<see cref="SmtSessionRecord"/>). Every way a run can end is one
/// of these:
/// <list type="bullet">
/// <item>the conversation ran its course and the solver ended with status 0: the answer;</item>
/// <item>the fuel ran out: <c>unknown</c> by <c>fuel</c>;</item>
/// <item>the solver ended with its status 101 and its line <c>(error "out of memory")</c>, at its own
/// ceiling or at the host's cap of the linear memory, or the engine refused more memory for
/// the solver's exception objects: <c>unknown</c> by <c>memout</c>;</item>
/// <item>an answer that was an error, or anything said to a statement of the problem's: <c>input_error</c>;</item>
/// <item>anything said to the assertion that rules a found model of an enumeration out (the
/// solver did not take a value back as it printed it): the enumeration is over with the models
/// it has, <c>sat</c>, incomplete by <c>other</c>, with the solver's answer for its word;</item>
/// <item>the output passed its cap: <c>output_exceeded</c>;</item>
/// <item>a trap, another error of the engine, an exit status that is no answer, an answer that
/// cannot be read: <c>engine_crash</c>;</item>
/// <item>the wall-clock backstop, a cancelled session, a module that did not start:
/// <c>node_fault</c>, which says something about the machine and never about the problem.</item>
/// </list>
/// A session a budget ended has no verdict of its own: what the solver had answered before does
/// not stand for the query (a <c>sat</c> whose model did not come whole is no answer), except
/// that an enumeration keeps the models that were whole.
/// </summary>
internal static class SmtSessionOutcomes
{
    #region Constants

    /// <summary>The engine's words when the heap of the solver's exception objects passes the store's cap.</summary>
    private const string EXCEPTION_HEAP_FULL = "GC heap out of memory";

    private const string ERROR_LINE = "\n(error";
    private const string ERROR_HEAD = "(error";
    private const string STDERR_NOTE = "the solver's stderr";

    private const int EXIT_CLEAN = 0;
    private const int EXIT_WITH_ERRORS = 1;

    #endregion

    #region Functions

    /// <summary>
    /// Makes the outcome: how the run ended decides first, then what the conversation came to.
    /// </summary>
    /// <param name="record">What the session gathered.</param>
    /// <param name="run">The run's outcome.</param>
    /// <returns>The session's outcome.</returns>
    public static SmtSolveOutcome Of(SmtSessionRecord record, WasmHostRunOutcome run)
    {
        // What the solver wrote after it was last asked: a run that did not end at a read.
        var rest = run.Stdout.Length > record.Seen ? run.Stdout[(int)record.Seen..] : "";
        record.Transcript.Answer(rest);
        if (run.Stderr.Length > 0)
        {
            record.Transcript.Note(STDERR_NOTE);
            record.Transcript.Answer(run.Stderr);
        }

        if (record.Cancelled)
            return Ended(record, run, SmtSolveStatus.NodeFault, SmtSolveCodes.CANCELLED, "the session was cancelled");

        switch (run.Verdict)
        {
            case WasmHostVerdict.Interrupted:
                return Ended(record, run, SmtSolveStatus.NodeFault, SmtSolveCodes.BACKSTOP,
                    $"the session ran past the wall-clock backstop of {record.Envelope.BackstopMs} ms with fuel left: the machine, not the problem, stopped it");
            case WasmHostVerdict.NotInstantiated:
            case WasmHostVerdict.NoStart:
                // The solver never ran: the fuel the host counts for such a run is not the solver's.
                return Ended(record, run with { FuelConsumed = 0 }, SmtSolveStatus.NodeFault, SmtSolveCodes.NOT_INSTANTIATED, run.Fault ?? "the solver module did not start");
        }

        // An answer the session could not go on from was given before the run ended.
        if (record.Failed)
            return Ended(record, run, record.FailureStatus, record.FailureCode, record.FailureMessage);

        switch (run.Verdict)
        {
            case WasmHostVerdict.OutOfFuel:
                return Stopped(record, run, SmtUnknownReason.Fuel);
            case WasmHostVerdict.OutOfMemory:
                return Stopped(record, run, SmtUnknownReason.Memory);
            case WasmHostVerdict.EngineError when run.EngineError?.Message.Contains(EXCEPTION_HEAP_FULL, StringComparison.Ordinal) == true:
                return Stopped(record, run, SmtUnknownReason.Memory);
            case WasmHostVerdict.EngineError:
                return Ended(record, run, SmtSolveStatus.EngineCrash, SmtSolveCodes.ENGINE_ERROR, WasmHost.ENGINE_ERROR_FAULT);
            case WasmHostVerdict.Trap:
                return Ended(record, run, SmtSolveStatus.EngineCrash, SmtSolveCodes.TRAP, run.Fault ?? WasmHostTraps.FAULT_PREFIX.Trim());
            case WasmHostVerdict.OutputLimit:
                return Exceeded(record, run);
            case WasmHostVerdict.Exited when run.ExitCode == SmtProfile.MEMORY_EXIT_CODE:
                return Stopped(record, run, SmtUnknownReason.Memory);
            case WasmHostVerdict.Exited when record.BlockRefused && record.Over && run.ExitCode is EXIT_CLEAN or EXIT_WITH_ERRORS:
                // The solver counts what it said to the assertion as an error and ends with that
                // status. It is the enumeration's end, not an error of the problem's.
                return Enumerated(record, run, SmtUnknownReason.Other);
            case WasmHostVerdict.Exited when record.Over && run.ExitCode == EXIT_CLEAN:
                return Answered(record, run);
            case WasmHostVerdict.Exited when record.Over && run.ExitCode == EXIT_WITH_ERRORS:
                // The solver counted an error the session did not see as the answer of a command.
                return Ended(record, run, SmtSolveStatus.InputError, "", UnseenError(run.Stdout));
            case WasmHostVerdict.Exited:
                return Ended(record, run, SmtSolveStatus.EngineCrash, SmtSolveCodes.EXIT,
                    record.Over ? $"the solver ended with status {run.ExitCode}" : $"the solver ended with status {run.ExitCode} before its conversation was over");
            default:
                return Ended(record, run, SmtSolveStatus.EngineCrash, SmtSolveCodes.ENGINE_ERROR, $"the sandbox ended the session as {run.Verdict}");
        }
    }

    /// <summary>A session whose conversation ran its course and whose solver ended cleanly: the answer of the query.</summary>
    private static SmtSolveOutcome Answered(SmtSessionRecord record, WasmHostRunOutcome run)
    {
        if (record.Plan.Query == SmtQuery.Enumerate)
            return Enumerated(record, run, record.Verdict == SmtVerdict.Unknown ? record.Reason : SmtUnknownReason.None);

        return new SmtSolveOutcome
        {
            Status = SmtSolveStatus.Ok,
            Query = record.Plan.Query,
            Verdict = record.Verdict,
            Reason = record.Reason,
            ReasonText = record.ReasonText,
            Proof = Proof(record.Plan.Query, record.Verdict),
            Models = record.Models,
            Values = record.Values,
            Checks = record.Checks,
            ResourceCount = record.ResourceCount,
            Fuel = run.FuelConsumed,
            PeakMemoryBytes = run.PeakMemoryBytes,
            Transcript = record.Transcript.Tail(),
            WallMs = run.WallMs
        };
    }

    /// <summary>
    /// A session one of its budgets ended (the fuel, the memory): the answer is <c>unknown</c>
    /// with that reason. An enumeration keeps the models that were whole by then.
    /// </summary>
    private static SmtSolveOutcome Stopped(SmtSessionRecord record, WasmHostRunOutcome run, SmtUnknownReason reason)
    {
        if (record.Plan.Query == SmtQuery.Enumerate)
        {
            record.Complete = false;
            record.LimitReached = false;
            return Enumerated(record, run, reason);
        }

        return new SmtSolveOutcome
        {
            Status = SmtSolveStatus.Ok,
            Query = record.Plan.Query,
            Verdict = SmtVerdict.Unknown,
            Reason = reason,
            Checks = record.Checks,
            ResourceCount = record.ResourceCount,
            Fuel = run.FuelConsumed,
            PeakMemoryBytes = run.PeakMemoryBytes,
            Transcript = record.Transcript.Tail(),
            WallMs = run.WallMs
        };
    }

    private static SmtSolveOutcome Enumerated(SmtSessionRecord record, WasmHostRunOutcome run, SmtUnknownReason reason)
    {
        var verdict = record.Models.Count > 0 ? SmtVerdict.Sat : record.Complete ? SmtVerdict.Unsat : SmtVerdict.Unknown;

        return new SmtSolveOutcome
        {
            Status = SmtSolveStatus.Ok,
            Query = record.Plan.Query,
            Verdict = verdict,
            Reason = reason,
            ReasonText = reason == record.Reason ? record.ReasonText : "",
            Count = record.Models.Count,
            Complete = record.Complete,
            LimitReached = record.LimitReached,
            Models = record.Models,
            Values = record.Values,
            Checks = record.Checks,
            ResourceCount = record.ResourceCount,
            Fuel = run.FuelConsumed,
            PeakMemoryBytes = run.PeakMemoryBytes,
            Transcript = record.Transcript.Tail(),
            WallMs = run.WallMs
        };
    }

    /// <summary>
    /// A session whose output passed its cap. What the solver had answered by then stands: a
    /// check that said <c>sat</c> is <c>sat</c>, and it is the model that did not fit.
    /// </summary>
    private static SmtSolveOutcome Exceeded(SmtSessionRecord record, WasmHostRunOutcome run)
    {
        var verdict = record.Plan.Query == SmtQuery.Enumerate
            ? record.Models.Count > 0 || record.Verdict == SmtVerdict.Sat ? SmtVerdict.Sat : SmtVerdict.None
            : record.Verdict;

        return Ended(record, run, SmtSolveStatus.OutputExceeded, "", $"the session wrote more than the {record.Envelope.OutputLimitBytes} bytes it may", verdict);
    }

    /// <summary>A session without an answer: it keeps what it had gathered, and claims no verdict unless it is given one.</summary>
    private static SmtSolveOutcome Ended(SmtSessionRecord record, WasmHostRunOutcome run, SmtSolveStatus status, string code, string message, SmtVerdict verdict = SmtVerdict.None)
    {
        return new SmtSolveOutcome
        {
            Status = status,
            Code = code,
            Message = message,
            Query = record.Plan.Query,
            Verdict = verdict,
            Proof = Proof(record.Plan.Query, verdict),
            Count = record.Plan.Query == SmtQuery.Enumerate ? record.Models.Count : 0,
            Models = record.Models,
            Values = record.Values,
            Checks = record.Checks,
            ResourceCount = record.ResourceCount,
            Fuel = run.FuelConsumed,
            PeakMemoryBytes = run.PeakMemoryBytes,
            Transcript = record.Transcript.Tail(),
            WallMs = run.WallMs
        };
    }

    #endregion

    #region Tools

    /// <summary>What a proof came to, by the verdict of the problem with the negated goal.</summary>
    private static SmtProofResult Proof(SmtQuery query, SmtVerdict verdict)
    {
        if (query != SmtQuery.Prove)
            return SmtProofResult.None;

        return verdict switch
        {
            SmtVerdict.Unsat => SmtProofResult.Proved,
            SmtVerdict.Sat => SmtProofResult.Counterexample,
            _ => SmtProofResult.None
        };
    }

    /// <summary>The first error in everything the solver wrote, or the fact that it counted one.</summary>
    private static string UnseenError(string stdout)
    {
        var at = stdout.StartsWith(ERROR_HEAD, StringComparison.Ordinal) ? 0 : stdout.IndexOf(ERROR_LINE, StringComparison.Ordinal);
        return at < 0 ? "the solver ended with an error it did not name in the answer of a command" : SmtAnswers.Message(stdout[at..]);
    }

    #endregion
}
