using OutWit.Controller.AI.Smt.Adapters;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The two sides of a problem's refusal, as a test compares them: what a node's plan says of a
/// problem before a session starts (the options the package carries, then the problem: the text
/// sanitized again, its hash, the terms beside it), and whether the host starts a run on options.
/// What a node's refused row may say is the model package's list of refusal codes and the node's
/// own three (a hash that is not the text's, a profile or options it does not take), as the host's
/// check of a row takes them. The controller's types are met inside method bodies only.
/// </summary>
internal static class SmtPremiseRig
{
    #region Functions

    /// <summary>
    /// What a node's plan says of a problem it was given in a package of the given options: the
    /// code of its refusal, or null when a session would start.
    /// </summary>
    /// <param name="problem">The problem as it travelled.</param>
    /// <param name="options">The options the package carries: the host's, resolved.</param>
    /// <returns>The refusal's code, or null.</returns>
    public static string? NodeRefusal(SmtProblemData problem, SmtOptionsData options)
    {
        if (SmtSolver.Refusal(options) is { } refused)
            return refused.Code;

        return SmtSolvePlan.Prepare(problem, SmtProfile.Envelope(options), out var refusal) == null ? refusal!.Code : null;
    }

    /// <summary>Whether the host starts a run on options: in range and of a profile it has.</summary>
    /// <param name="options">The options as a job gives them.</param>
    /// <returns>True when the run starts.</returns>
    public static bool HostStarts(SmtOptionsData options)
    {
        try
        {
            SmtAdapterTools.EnsureRunnable(options);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The options as the host puts them into every package of a run.</summary>
    /// <param name="options">The options as a job gives them.</param>
    /// <returns>The options, resolved.</returns>
    public static SmtOptionsData Resolve(SmtOptionsData options)
    {
        return SmtOptionsRules.Resolve(options);
    }

    #endregion

    #region Properties

    /// <summary>The codes a node's refused row may carry: the model package's list of refusals and the node's own three.</summary>
    public static IReadOnlyCollection<string> NodeCodes => [.. SmtRefusalCodes.All, .. NodeOwnCodes];

    /// <summary>The codes only a node's plan gives: a hash that is not its text's, a profile the controller does not have, options out of range.</summary>
    public static IReadOnlyCollection<string> NodeOwnCodes => [SmtSolveCodes.HASH_MISMATCH, SmtSolveCodes.PROFILE_UNKNOWN, SmtSolveCodes.OPTIONS_INVALID];

    #endregion
}
