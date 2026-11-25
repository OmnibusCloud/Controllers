using System.Reflection;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// How the tests meet the host's gate of a node's row: a row made as a node makes it (of a
/// scripted session, or of the solver's entry without a session), a made-up row sealed as the
/// node seals its own, and the host's word on a row against the bounds a package of that one
/// problem gives. As in <see cref="SmtNodeRig"/>, the controller's types are met inside method
/// bodies only: what a test sees are the model package's types, words, numbers and texts.
/// </summary>
internal static class SmtRowCheckRig
{
    #region Constants

    /// <summary>The ticket of the package every row here answers.</summary>
    public const string TICKET = SmtNodeRig.TICKET;

    private const string NO_SANDBOX = "this process has no sandbox for the solver";

    #endregion

    #region Rows

    /// <summary>The row a node makes of a case: of its scripted session, or of the solver's entry when the case has none.</summary>
    /// <param name="one">The case.</param>
    /// <returns>The row, its digest its own.</returns>
    public static SmtResultData Row(SmtSessionCase one)
    {
        using var cancellation = new CancellationTokenSource();
        var cancelAfter = one.CancelAfter;

        if (one.NoSession)
        {
            if (cancelAfter >= 0)
                cancellation.Cancel();

            return SmtResultRows.Of(one.Problem, SmtSolver.Solve(one.Problem, one.Options, null, NO_SANDBOX, cancellation.Token), TICKET);
        }

        var outcome = SmtSessionScript.Outcome(one.Problem, one.Options, one.Answers, one.End, sent =>
        {
            if (sent == cancelAfter)
                cancellation.Cancel();
        }, cancellation.Token);

        return SmtResultRows.Of(one.Problem, (SmtSolveOutcome)outcome, TICKET);
    }

    /// <summary>Puts into a row what a node puts into its own: the problem it answers, the ticket, the version, the platform, and its digest.</summary>
    /// <param name="row">The row; changed.</param>
    /// <param name="problem">The problem.</param>
    /// <returns>The row.</returns>
    public static SmtResultData Seal(SmtResultData row, SmtProblemData problem)
    {
        row.Index = problem.Index;
        row.Id = problem.Id;
        row.Hash = SmtPackageCodec.HashText(problem.Text);
        row.Query = problem.Query;
        row.Ticket = TICKET;
        row.ControllerVersion = ControllerBuildInfo.VERSION;
        row.Platform = SmtResultRows.Platform;
        row.Digest = SmtResultDigest.Of(row);
        return row;
    }

    #endregion

    #region The Host's Word

    /// <summary>
    /// Whether the host takes a row as a node's answer to a problem: against the bounds of a
    /// package that holds that one problem under the options. The bound's hash is the hash of the
    /// problem's text, as a package the host made says it.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="problem">The problem.</param>
    /// <param name="options">The package's options.</param>
    /// <returns>True when the row is taken.</returns>
    public static bool Holds(SmtResultData row, SmtProblemData problem, SmtOptionsData options)
    {
        var hosted = problem.Clone();
        hosted.Hash = SmtPackageCodec.HashText(problem.Text);

        return SmtRowChecks.Holds(row, TICKET, SmtTaskBounds.Of(TICKET, options, [hosted]));
    }

    #endregion

    #region Numbers and Words

    /// <summary>Every word the controller names an outcome's kind by, read off its own list of them.</summary>
    /// <returns>The words.</returns>
    public static IReadOnlyList<string> SolveCodes()
    {
        return typeof(SmtSolveCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
    }

    /// <summary>The fuel of a session under options: the ceiling of a row's fuel.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The fuel.</returns>
    public static long Fuel(SmtOptionsData options)
    {
        return SmtProfile.Envelope(options).Fuel;
    }

    /// <summary>The host's cap of the solver's linear memory under options: the ceiling of a row's peak memory.</summary>
    /// <param name="options">The options.</param>
    /// <returns>Bytes.</returns>
    public static long HostMemoryBytes(SmtOptionsData options)
    {
        return SmtProfile.Envelope(options).HostMemoryBytes;
    }

    /// <summary>The most a session writes: the ceiling of the characters of a row's models and values.</summary>
    public static int OutputLimit => SmtProfile.OUTPUT_LIMIT_BYTES;

    /// <summary>The most bytes of UTF-8 a row's transcript holds.</summary>
    public static int TranscriptLimit => SmtTranscript.LIMIT_BYTES;

    /// <summary>The most characters of the solver's own word for an unknown.</summary>
    public static int ReasonCharacters => SmtAnswers.MESSAGE_CHARACTERS;

    /// <summary>The most bytes a row's frame in a package of results holds.</summary>
    public static int MaxResultFrameBytes => SmtPackageCodec.MAX_RESULT_FRAME_BYTES;

    #endregion
}
