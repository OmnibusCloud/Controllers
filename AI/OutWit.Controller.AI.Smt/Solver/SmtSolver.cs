using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// Solves one problem: one fresh instance of the solver in the sandbox, one conversation, one
/// outcome. Nothing here throws for what a problem, its options or the solver hold: a problem
/// that is not taken, an option out of its range, an error of the solver's, a crash, a budget
/// that ran out and a machine that cannot run the session are all outcomes
/// (<see cref="SmtSolveOutcome"/>).
/// <para>
/// What is looked at, in this order: the options (the profile they name, their ranges); the
/// problem's text, sanitized again and held to the hash it came with, and the terms beside it;
/// then the sandbox of this process, which is made at the first problem that gets this far
/// (the solver module is compiled then, once, before the session's budgets start). A problem
/// that is refused is refused the same way on a machine that has no sandbox.
/// </para>
/// </summary>
internal static class SmtSolver
{
    #region Constants

    private const int NAME_CHARACTERS = 64;

    #endregion

    #region Functions

    /// <summary>
    /// Solves one problem under a run's options, in the sandbox of this process.
    /// </summary>
    /// <param name="problem">The problem as it travelled: its canonical text, its hash, its query and the terms of the query.</param>
    /// <param name="options">The run's options; a number that is 0 is the default.</param>
    /// <param name="cancellationToken">Ends the session at the solver's next read of its input; a check that is running ends by its own budgets first.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException">There is no problem, or there are no options.</exception>
    public static SmtSolveOutcome Solve(SmtProblemData problem, SmtOptionsData options, CancellationToken cancellationToken = default)
    {
        if (Prepare(problem, options, out var envelope, out var plan) is { } refused)
            return refused;

        var sandbox = SmtSandboxHost.Get(out var unavailable);
        return Run(plan!, envelope!, sandbox, unavailable, cancellationToken, null, problem.Index);
    }

    /// <summary>
    /// Solves one problem under a run's options in a given sandbox: for a caller that holds a
    /// sandbox of its own, or none.
    /// </summary>
    /// <param name="problem">The problem as it travelled.</param>
    /// <param name="options">The run's options; a number that is 0 is the default.</param>
    /// <param name="sandbox">The sandbox, or null when there is none.</param>
    /// <param name="unavailable">Why there is no sandbox, in fixed words; ignored when there is one.</param>
    /// <param name="cancellationToken">Ends the session at the solver's next read of its input.</param>
    /// <param name="logger">The node's own log, for a failure of the host outside the solver; null for none.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException">There is no problem, or there are no options.</exception>
    public static SmtSolveOutcome Solve(SmtProblemData problem, SmtOptionsData options, SmtSandbox? sandbox, string? unavailable, CancellationToken cancellationToken = default, ILogger? logger = null)
    {
        return Prepare(problem, options, out var envelope, out var plan) ?? Run(plan!, envelope!, sandbox, unavailable, cancellationToken, logger, problem.Index);
    }

    /// <summary>
    /// Solves one problem in a given envelope and a given sandbox: for a caller that needs an
    /// envelope the options do not give.
    /// </summary>
    /// <param name="problem">The problem as it travelled.</param>
    /// <param name="envelope">The session's envelope.</param>
    /// <param name="sandbox">The sandbox, or null when there is none.</param>
    /// <param name="unavailable">Why there is no sandbox, in fixed words; ignored when there is one.</param>
    /// <param name="cancellationToken">Ends the session at the solver's next read of its input.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException">There is no problem, or no envelope.</exception>
    public static SmtSolveOutcome Solve(SmtProblemData problem, SmtEnvelope envelope, SmtSandbox? sandbox, string? unavailable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(envelope);

        var plan = SmtSolvePlan.Prepare(problem, envelope, out var refusal);
        return plan == null ? Refused(problem.Query, refusal!) : Run(plan, envelope, sandbox, unavailable, cancellationToken, null, problem.Index);
    }

    /// <summary>
    /// Why a run's options are not taken: a profile this controller does not have, by its name,
    /// or an option out of its range. A resource limit above what the solver's parameter holds is
    /// one of the ranges: it never reaches the solver.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The refusal, or null when the options are taken.</returns>
    public static SmtRefusalData? Refusal(SmtOptionsData options)
    {
        if (!SmtProfile.Is(options.Profile))
            return new SmtRefusalData { Code = SmtSolveCodes.PROFILE_UNKNOWN, Reason = $"'profile' names '{Name(options.Profile)}', which this controller does not have: its profile is {SmtProfile.ID}" };

        var problems = SmtOptionsRules.Check(options);
        return problems.Count == 0 ? null : new SmtRefusalData { Code = SmtSolveCodes.OPTIONS_INVALID, Reason = string.Join("; ", problems) };
    }

    /// <summary>What is looked at before a sandbox is asked for: the options, then the problem. Gives the refusal, or the envelope and the plan.</summary>
    private static SmtSolveOutcome? Prepare(SmtProblemData problem, SmtOptionsData options, out SmtEnvelope? envelope, out SmtSolvePlan? plan)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(options);

        envelope = null;
        plan = null;

        var refusal = Refusal(options);
        if (refusal != null)
            return Refused(problem.Query, refusal);

        envelope = SmtProfile.Envelope(options);
        plan = SmtSolvePlan.Prepare(problem, envelope, out refusal);
        return plan == null ? Refused(problem.Query, refusal!) : null;
    }

    private static SmtSolveOutcome Run(SmtSolvePlan plan, SmtEnvelope envelope, SmtSandbox? sandbox, string? unavailable, CancellationToken cancellationToken, ILogger? logger, int index)
    {
        if (cancellationToken.IsCancellationRequested)
            return Faulted(plan.Query, SmtSolveCodes.CANCELLED, "the session was cancelled before it started");

        if (sandbox == null)
            return Faulted(plan.Query, SmtSolveCodes.SANDBOX_UNAVAILABLE, unavailable ?? "this process has no sandbox for the solver");

        var session = new SmtSession(plan, envelope, cancellationToken);
        WasmHostRunOutcome run;
        try
        {
            run = sandbox.Run(session.Next, envelope);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Nothing the solver did: a file of the host's own, an engine that could not make a
            // store, or a fault of this controller's own code inside the conversation (which
            // reaches here wrapped, as the inner exception). The kind of failure travels; its
            // text may name a path of the machine, so the exception itself, with its text and
            // its stack, goes to the node's own log and nowhere else.
            logger?.LogError(exception, "Smt: the session of problem {Index} failed on this node outside the solver; its row says {Code}", index, SmtSolveCodes.HOST_FAULT);

            var inner = exception.InnerException == null ? "" : $" ({exception.InnerException.GetType().Name})";
            return Faulted(plan.Query, SmtSolveCodes.HOST_FAULT, $"the host failed outside the solver: {exception.GetType().Name}{inner}");
        }

        return session.Finish(run);
    }

    #endregion

    #region Tools

    private static SmtSolveOutcome Refused(SmtQuery query, SmtRefusalData refusal)
    {
        return new SmtSolveOutcome
        {
            Status = SmtSolveStatus.Refused,
            Code = refusal.Code,
            Message = refusal.Reason,
            Refusal = refusal,
            Query = query
        };
    }

    private static SmtSolveOutcome Faulted(SmtQuery query, string code, string message)
    {
        return new SmtSolveOutcome
        {
            Status = SmtSolveStatus.NodeFault,
            Code = code,
            Message = message,
            Query = query
        };
    }

    /// <summary>A name as a message shows it: cut short, with a blank for every character that is no printable one.</summary>
    private static string Name(string name)
    {
        var shown = name.Length > NAME_CHARACTERS ? name[..NAME_CHARACTERS] : name;
        return string.Create(shown.Length, shown, static (span, text) =>
        {
            for (var index = 0; index < span.Length; index++)
                span[index] = char.IsControl(text[index]) || char.IsSurrogate(text[index]) ? ' ' : text[index];
        });
    }

    #endregion
}
