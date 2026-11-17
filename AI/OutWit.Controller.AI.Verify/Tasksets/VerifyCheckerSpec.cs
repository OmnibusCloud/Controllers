using System.Text;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// The checker of a generate-and-check run: one program that reads a candidate on stdin and
/// says whether it holds. Authored as one JSON object (runtime, entry, sources, args, limits —
/// the same vocabulary as a task) plus the verdict rule at the top level:
/// <c>expected_stdout</c> (optional; when absent only the exit code counts) and
/// <c>expected_exit</c> (default 0). It is held to its schema (docs/verify-checker.schema.json)
/// exactly as a taskset line is to its own: a field the schema does not have is refused by name.
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
    #region Functions

    /// <summary>
    /// Parses a checker spec document.
    /// </summary>
    /// <param name="json">One JSON object (a task's fields with the verdict rule at the top level).</param>
    /// <returns>The spec.</returns>
    /// <exception cref="FormatException">The document is not a checker spec the schema allows.</exception>
    public static VerifyCheckerSpec Parse(string json)
    {
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Parses a checker spec document from its bytes.
    /// </summary>
    /// <param name="utf8">One JSON object, UTF-8, with or without a byte-order mark.</param>
    /// <returns>The spec.</returns>
    /// <exception cref="FormatException">The document is not a checker spec the schema allows.</exception>
    public static VerifyCheckerSpec Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.StartsWith(Encoding.UTF8.Preamble))
            utf8 = utf8[Encoding.UTF8.Preamble.Length..];

        try
        {
            return VerifyTasksetParser.ParseChecker(utf8);
        }
        catch (FormatException exception)
        {
            throw new FormatException($"the checker spec is not one the schema allows: {exception.Message}");
        }
    }

    /// <summary>
    /// Reads a checker spec from a file, refusing one larger than a task's line may be before it
    /// is read.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The spec.</returns>
    /// <exception cref="FormatException">The file is too large, or is not a checker spec the schema allows.</exception>
    public static async Task<VerifyCheckerSpec> ReadAsync(string path)
    {
        if (new FileInfo(path).Length > VerifyTasksetLimits.MAX_LINE_BYTES)
            throw new FormatException($"the checker spec is longer than {VerifyTasksetLimits.MAX_LINE_BYTES} bytes");

        return Parse(await File.ReadAllBytesAsync(path));
    }

    #endregion
}
