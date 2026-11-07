using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// The checker of a generate-and-check run: one program that reads a candidate on stdin and
/// says whether it holds. Authored as a single taskset line (runtime, entry, sources, args,
/// limits — the same vocabulary as a task) plus the verdict rule at the top level:
/// <c>expected_stdout</c> (optional; when absent only the exit code counts) and
/// <c>expected_exit</c> (default 0).
/// </summary>
/// <param name="RuntimeId">The checker's runtime.</param>
/// <param name="EntryPoint">The checker's entry file.</param>
/// <param name="Sources">The checker's source files.</param>
/// <param name="Args">Arguments passed to every check.</param>
/// <param name="ExpectedStdout">The exact stdout a passing check prints, or null for "exit code only".</param>
/// <param name="ExpectedExitCode">The exit code of a passing check.</param>
/// <param name="Limits">Limits for every check, or null for the job defaults.</param>
public sealed record VerifyCheckerSpec(
    string RuntimeId,
    string EntryPoint,
    IReadOnlyList<VerifySourceFileData> Sources,
    IReadOnlyList<string> Args,
    string? ExpectedStdout,
    int ExpectedExitCode,
    VerifyLimitsData? Limits)
{
    #region Constants

    private const string EXPECTED_STDOUT = "expected_stdout";
    private const string EXPECTED_EXIT = "expected_exit";

    #endregion

    #region Functions

    /// <summary>
    /// Parses a checker spec document.
    /// </summary>
    /// <param name="json">One JSON object (a taskset line with the verdict rule at the top level).</param>
    /// <returns>The spec.</returns>
    /// <exception cref="FormatException">The document is not a valid task line.</exception>
    public static VerifyCheckerSpec Parse(string json)
    {
        var parsed = VerifyTasksetParser.Parse(json);
        if (parsed.Errors.Count > 0 || parsed.Tasks.Count != 1)
            throw new FormatException($"checker spec must be one task line: {string.Join("; ", parsed.Errors)}");

        var task = parsed.Tasks[0];

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var expectedStdout = root.TryGetProperty(EXPECTED_STDOUT, out var stdoutElement) && stdoutElement.ValueKind == JsonValueKind.String
            ? stdoutElement.GetString()
            : null;
        var expectedExit = root.TryGetProperty(EXPECTED_EXIT, out var exitElement) && exitElement.ValueKind == JsonValueKind.Number
            ? exitElement.GetInt32()
            : 0;

        return new VerifyCheckerSpec(task.RuntimeId, task.EntryPoint, task.Sources, task.Args, expectedStdout, expectedExit, task.Limits);
    }

    #endregion
}
