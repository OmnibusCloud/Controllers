using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Controller.AI.Wasm;
using Wasmtime;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Holds a session's conversation with a solver that is a script: a list of answers, one for
/// each time the session sends something, and the way the run then ended. No sandbox and no
/// module is in it: the session is asked as a run asks it, and handed the outcome of a run as
/// the sandbox would report it. So every way a run can end, and every answer a solver could
/// give, reaches the session's own code, also the ones the real solver cannot be made to give
/// on demand.
/// <para>
/// As in <see cref="SmtSolverRig"/>, the controller's types are named inside method bodies only.
/// </para>
/// </summary>
internal static class SmtSessionScript
{
    #region Constants

    private static readonly UTF8Encoding TEXT = new(encoderShouldEmitUTF8Identifier: false);

    #endregion

    #region Functions

    /// <summary>
    /// Holds a session with a scripted solver.
    /// </summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <param name="options">The run's options, each in its range.</param>
    /// <param name="answers">
    /// What the solver writes after each thing the session sends, in order, before it reads
    /// again. When the session sends more than there are answers, the solver did not read
    /// again: its run ended there, as <paramref name="end"/> says.
    /// </param>
    /// <param name="end">How the run ended; null for a solver that ended by itself with status 0.</param>
    /// <param name="beforeAsk">Called before each time the session is asked, with the number of times it has sent something so far; null for nothing.</param>
    /// <param name="cancellationToken">The session's cancellation.</param>
    /// <returns>The outcome and what the session sent.</returns>
    /// <exception cref="InvalidOperationException">The problem is refused: a session needs one that is taken.</exception>
    public static SmtScripted Hold(SmtProblemData problem, SmtOptionsData options, IReadOnlyList<string> answers, SmtScriptedEnd? end = null,
        Action<int>? beforeAsk = null, CancellationToken cancellationToken = default)
    {
        end ??= new SmtScriptedEnd();

        var envelope = SmtProfile.Envelope(options);
        var plan = SmtSolvePlan.Prepare(problem, envelope, out var refusal)
                   ?? throw new InvalidOperationException($"the problem is refused: {refusal?.Code}: {refusal?.Reason}");
        var session = new SmtSession(plan, envelope, cancellationToken);

        var sent = new List<string>();
        var stdout = new StringBuilder();
        var written = end.Before;
        var ended = false;
        while (end.Started)
        {
            beforeAsk?.Invoke(sent.Count);

            // The solver reads: the session sees what it wrote since its last read.
            stdout.Append(written);
            var input = session.Next(TEXT.GetBytes(written));
            if (input.IsEmpty)
            {
                ended = true;
                break;
            }

            sent.Add(TEXT.GetString(input.Span));
            if (sent.Count > answers.Count)
                break;

            written = answers[sent.Count - 1];
        }

        stdout.Append(end.Rest);
        var run = new WasmHostRunOutcome(
            Enum.Parse<WasmHostVerdict>(end.Verdict),
            end.ExitCode,
            null,
            end.Fault,
            stdout.ToString(),
            false,
            end.Stderr,
            false,
            end.Fuel,
            end.PeakMemoryBytes,
            1,
            end.EngineError == null ? null : new WasmtimeException(end.EngineError),
            TEXT.GetByteCount(stdout.ToString()));

        return new SmtScripted(SmtSolverRig.Take(session.Finish(run)), sent, ended);
    }

    #endregion
}
