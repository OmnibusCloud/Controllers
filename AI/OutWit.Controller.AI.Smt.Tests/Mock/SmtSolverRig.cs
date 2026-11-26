using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// How the tests run the solver: the controller's own entry in the process's sandbox, or a
/// session with something changed that the options cannot change (<see cref="SmtSolverSetup"/>),
/// and every outcome handed back as the tests' own <see cref="SmtSolved"/>.
/// <para>
/// This is where the tests that run sessions meet the controller's types, and only inside
/// method bodies. A test framework loads every type of the test assembly when it looks
/// for tests, with its base type, its interfaces, the types its attributes name and the value
/// types its fields hold (the class the compiler makes for a closure is such a type too); a
/// controller type there would bring the controller's assembly in before the first test runs,
/// and a proof that the module loads in a context of its own could no longer be made in this
/// process. So no fixture, helper or test signature here names one: they see words, numbers and
/// texts.
/// </para>
/// </summary>
internal static class SmtSolverRig
{
    #region Constants

    /// <summary>The properties of the controller's outcome that <see cref="Canonical"/> writes out, the wall time aside: a new one must be added there.</summary>
    public const int OUTCOME_PROPERTIES = 20;

    private static readonly object SYNC = new();

    #endregion

    #region Fields

    // The sandbox with explicit trap checks, made when a test first asks for it: an IDisposable
    // here, a sandbox inside the methods.
    private static IDisposable? m_explicit;

    #endregion

    #region Functions

    /// <summary>
    /// Solves a problem by the controller's entry: the options checked, the problem sanitized
    /// again, the session in the process-wide sandbox.
    /// </summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="cancellationToken">The session's cancellation.</param>
    /// <returns>The outcome.</returns>
    public static SmtSolved Solve(SmtProblemData problem, SmtOptionsData options, CancellationToken cancellationToken = default)
    {
        return Take(SmtSolver.Solve(problem, options, cancellationToken));
    }

    /// <summary>
    /// Solves a problem with something changed that the options cannot change.
    /// </summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <param name="options">The run's options, each in its range.</param>
    /// <param name="setup">What is changed.</param>
    /// <param name="cancellationToken">The session's cancellation.</param>
    /// <returns>The outcome.</returns>
    public static SmtSolved Solve(SmtProblemData problem, SmtOptionsData options, SmtSolverSetup setup, CancellationToken cancellationToken = default)
    {
        return Take(Raw(problem, options, setup, cancellationToken));
    }

    /// <summary>
    /// Solves a problem as <see cref="Solve(SmtProblemData, SmtOptionsData, SmtSolverSetup, CancellationToken)"/>
    /// does and gives the controller's own outcome, as an object: for a rig that makes more of it
    /// (a row), inside a method body.
    /// </summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="setup">What is changed, or null for the controller's entry.</param>
    /// <param name="cancellationToken">The session's cancellation.</param>
    /// <returns>The controller's outcome.</returns>
    public static object Raw(SmtProblemData problem, SmtOptionsData options, SmtSolverSetup? setup, CancellationToken cancellationToken = default)
    {
        if (setup == null)
            return SmtSolver.Solve(problem, options, cancellationToken);

        SmtSandbox? sandbox = null;
        var unavailable = setup.WithoutSandbox;
        if (unavailable == null)
            sandbox = setup.ExplicitTraps ? (SmtSandbox)Explicit() : SmtSandboxHost.Get(out unavailable);

        // The options are looked at first, as the entry looks at them; an envelope is made of options that were taken.
        var changed = setup.OutputLimitBytes != null || setup.BackstopMs != null || setup.Fuel != null || setup.HostMemoryBytes != null;
        if (!changed || SmtSolver.Refusal(options) != null)
            return SmtSolver.Solve(problem, options, sandbox, unavailable, cancellationToken);

        var envelope = SmtProfile.Envelope(options);
        envelope = envelope with
        {
            OutputLimitBytes = setup.OutputLimitBytes ?? envelope.OutputLimitBytes,
            BackstopMs = setup.BackstopMs ?? envelope.BackstopMs,
            Fuel = setup.Fuel ?? envelope.Fuel,
            HostMemoryBytes = setup.HostMemoryBytes ?? envelope.HostMemoryBytes
        };

        return SmtSolver.Solve(problem, envelope, sandbox, unavailable, cancellationToken);
    }

    /// <summary>
    /// Makes the process-wide sandbox now, so that a test's own time does not hold the compile.
    /// </summary>
    /// <returns>How long the module's compile took in this process, milliseconds; -1 when the process has no sandbox.</returns>
    public static long Warm()
    {
        return SmtSandboxHost.Get(out _)?.CompileMs ?? -1;
    }

    /// <summary>
    /// Makes the sandbox with explicit trap checks now.
    /// </summary>
    /// <returns>How long the module's compile took for it, milliseconds.</returns>
    public static long WarmExplicit()
    {
        return ((SmtSandbox)Explicit()).CompileMs;
    }

    /// <summary>Whether the process's own sandbox has explicit trap checks already (a process with hardware stack protection).</summary>
    /// <returns>True when the two trap modes cannot be compared in this process.</returns>
    public static bool ExplicitByDefault()
    {
        return SmtSandboxHost.Get(out _)?.ExplicitTraps ?? false;
    }

    /// <summary>Why this process has no sandbox; null when it has one.</summary>
    /// <returns>The reason in the controller's fixed words, or null.</returns>
    public static string? Unavailable()
    {
        SmtSandboxHost.Get(out var unavailable);
        return unavailable;
    }

    /// <summary>
    /// Solves a problem in a sandbox that was closed under it: the sandbox with explicit trap
    /// checks is released first (the next test that asks for one makes another), and the session
    /// is then started in it.
    /// </summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <param name="options">The run's options, each in its range.</param>
    /// <param name="logger">The node's own log, as a package's sessions have it; null for none.</param>
    /// <returns>The outcome.</returns>
    public static SmtSolved SolveInAClosedSandbox(SmtProblemData problem, SmtOptionsData options, ILogger? logger = null)
    {
        var closed = (SmtSandbox)Explicit();
        Release();

        return Take(SmtSolver.Solve(problem, options, closed, null, CancellationToken.None, logger));
    }

    /// <summary>
    /// Makes a sandbox as the process-wide one is made, its own files in a scope of a temp storage,
    /// and closes it again.
    /// </summary>
    /// <param name="modulePath">The module file, or null when none was found.</param>
    /// <param name="storage">The host's temp storage.</param>
    /// <param name="made">Called with the scopes the storage holds while the sandbox is open; not called when none was made.</param>
    /// <returns>Why no sandbox was made, in the controller's fixed words; null when one was.</returns>
    public static string? MakeAndClose(string? modulePath, IWitTempStorage storage, Action? made = null)
    {
        using var sandbox = SmtSandboxHost.Make(modulePath, storage, out var problem);
        if (sandbox != null)
            made?.Invoke();

        return problem;
    }

    /// <summary>Releases the sandbox with explicit trap checks, when a test made one.</summary>
    public static void Release()
    {
        lock (SYNC)
        {
            m_explicit?.Dispose();
            m_explicit = null;
        }
    }

    /// <summary>
    /// Writes an outcome out for its digest: every value but the wall time, one after the other,
    /// each with its name and, where it is a text, its length, so that no two outcomes write the
    /// same text.
    /// </summary>
    /// <param name="outcome">The controller's outcome.</param>
    /// <returns>The text.</returns>
    public static string Canonical(object outcome)
    {
        var solved = (SmtSolveOutcome)outcome;
        var builder = new StringBuilder();

        Word(builder, "status", SmtSolveWords.Of(solved.Status));
        Text(builder, "code", solved.Code);
        Text(builder, "message", solved.Message);
        Text(builder, "refusal", Refusal(solved.Refusal));
        Word(builder, "query", solved.Query.ToString());
        Word(builder, "verdict", SmtSolveWords.Of(solved.Verdict));
        Word(builder, "reason", SmtSolveWords.Of(solved.Reason));
        Text(builder, "reason_text", solved.ReasonText);
        Word(builder, "count", solved.Count.ToString(CultureInfo.InvariantCulture));
        Word(builder, "complete", solved.Complete ? "true" : "false");
        Word(builder, "limit_reached", solved.LimitReached ? "true" : "false");
        Word(builder, "proof", SmtSolveWords.Of(solved.Proof));
        Word(builder, "checks", solved.Checks.ToString(CultureInfo.InvariantCulture));
        Word(builder, "resource_count", solved.ResourceCount.ToString(CultureInfo.InvariantCulture));
        Word(builder, "fuel", solved.Fuel.ToString(CultureInfo.InvariantCulture));
        Word(builder, "peak_memory", solved.PeakMemoryBytes.ToString(CultureInfo.InvariantCulture));

        Word(builder, "models", solved.Models.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var model in solved.Models)
            Text(builder, "model", model);

        Word(builder, "values", solved.Values.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var values in solved.Values)
            Text(builder, "value", values);

        Text(builder, "transcript", solved.Transcript);
        return builder.ToString();
    }

    /// <summary>
    /// Makes the tests' own outcome of the controller's.
    /// </summary>
    /// <param name="outcome">The controller's outcome.</param>
    /// <returns>Its values, and their digest.</returns>
    public static SmtSolved Take(object outcome)
    {
        var solved = (SmtSolveOutcome)outcome;
        var canonical = Canonical(solved);

        return new SmtSolved
        {
            Status = SmtSolveWords.Of(solved.Status),
            Code = solved.Code,
            Message = solved.Message,
            Refusal = Refusal(solved.Refusal),
            Query = solved.Query.ToString().ToLowerInvariant(),
            Verdict = SmtSolveWords.Of(solved.Verdict),
            Reason = SmtSolveWords.Of(solved.Reason),
            ReasonText = solved.ReasonText,
            Count = solved.Count,
            Complete = solved.Complete,
            LimitReached = solved.LimitReached,
            Proof = SmtSolveWords.Of(solved.Proof),
            Models = solved.Models.ToList(),
            Values = solved.Values.ToList(),
            Checks = solved.Checks,
            ResourceCount = solved.ResourceCount,
            Fuel = solved.Fuel,
            PeakMemoryBytes = solved.PeakMemoryBytes,
            Transcript = solved.Transcript,
            WallMs = solved.WallMs,
            Canonical = canonical,
            Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
        };
    }

    #endregion

    #region Tools

    private static IDisposable Explicit()
    {
        lock (SYNC)
        {
            return m_explicit ??= new SmtSandbox(SmtSolverModule.Require(), null, explicitTraps: true);
        }
    }

    private static string Refusal(SmtRefusalData? refusal)
    {
        return refusal == null ? "" : $"{refusal.Code} line {refusal.Line} command '{refusal.Command}' row {refusal.Row} file '{refusal.File}': {refusal.Reason}";
    }

    private static void Word(StringBuilder builder, string name, string value)
    {
        builder.Append(name).Append('=').Append(value).Append('\n');
    }

    private static void Text(StringBuilder builder, string name, string value)
    {
        builder.Append(name).Append('[').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append("]=").Append(value).Append('\n');
    }

    #endregion
}
