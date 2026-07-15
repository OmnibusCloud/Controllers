using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Parses a taskset blob into tasks. The wire format is JSON-lines — one task per
/// non-blank line — the pipeline-native shape a lab produces from its own tooling:
///
///   {"index":0,"runtime":"python-3.14.6","entry":"main.py",
///    "sources":{"main.py":"print(input())"},"args":[],"stdin":"hi","seed":0,
///    "suite":[{"stdin":"2\n3\n","expected_stdout":"5\n","expected_exit":0}],
///    "limits":{"fuel":5000000000,"memory":268435456,"wall_ms":10000,
///              "stdout_bytes":262144,"stderr_bytes":65536}}
///
/// `index` defaults to the line ordinal; `entry` defaults to the sole source; `suite`
/// and `limits` are optional. Parsing collects errors rather than throwing, so a
/// preflight can report every malformed line at once.
/// </summary>
public static class VerifyTasksetParser
{
    #region Functions

    public static VerifyTasksetParseResult Parse(string jsonl)
    {
        var tasks = new List<VerifyTaskData>();
        var errors = new List<string>();

        var lines = jsonl.Split('\n');
        var ordinal = 0;
        for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            var line = lines[lineNumber].Trim();
            if (line.Length == 0)
                continue;

            try
            {
                tasks.Add(ParseTask(line, ordinal));
                ordinal++;
            }
            catch (Exception exception)
            {
                errors.Add($"line {lineNumber + 1}: {exception.Message}");
            }
        }

        return new VerifyTasksetParseResult(tasks, errors);
    }

    private static VerifyTaskData ParseTask(string line, int ordinal)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        var runtime = GetString(root, "runtime")
                      ?? throw new FormatException("missing 'runtime'");

        var sources = new List<VerifySourceFileData>();
        if (root.TryGetProperty("sources", out var sourcesElement) && sourcesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in sourcesElement.EnumerateObject())
                sources.Add(new VerifySourceFileData { Name = property.Name, Content = property.Value.GetString() ?? "" });
        }

        if (sources.Count == 0)
            throw new FormatException("missing or empty 'sources'");

        var entry = GetString(root, "entry") ?? sources[0].Name;
        if (sources.All(s => s.Name != entry))
            throw new FormatException($"'entry' '{entry}' is not among the sources");

        return new VerifyTaskData
        {
            TaskIndex = GetInt(root, "index") ?? ordinal,
            RuntimeId = runtime,
            Sources = sources,
            EntryPoint = entry,
            Args = GetStringList(root, "args"),
            Stdin = GetString(root, "stdin"),
            RandomSeed = GetLong(root, "seed") ?? 0,
            Suite = ParseSuite(root),
            Limits = ParseLimits(root)
        };
    }

    private static VerifySuiteData? ParseSuite(JsonElement root)
    {
        if (!root.TryGetProperty("suite", out var suiteElement) || suiteElement.ValueKind != JsonValueKind.Array)
            return null;

        var cases = new List<VerifySuiteCaseData>();
        foreach (var caseElement in suiteElement.EnumerateArray())
        {
            cases.Add(new VerifySuiteCaseData
            {
                Stdin = GetString(caseElement, "stdin"),
                Args = GetStringList(caseElement, "args"),
                ExpectedStdout = GetString(caseElement, "expected_stdout"),
                ExpectedExitCode = GetInt(caseElement, "expected_exit") ?? 0
            });
        }

        return cases.Count > 0 ? new VerifySuiteData { Cases = cases } : null;
    }

    private static VerifyLimitsData? ParseLimits(JsonElement root)
    {
        if (!root.TryGetProperty("limits", out var limitsElement) || limitsElement.ValueKind != JsonValueKind.Object)
            return null;

        return new VerifyLimitsData
        {
            FuelBudget = GetLong(limitsElement, "fuel") ?? 0,
            MemoryBytes = GetLong(limitsElement, "memory") ?? 0,
            WallTimeMs = GetInt(limitsElement, "wall_ms") ?? 0,
            StdoutLimitBytes = GetInt(limitsElement, "stdout_bytes") ?? 0,
            StderrLimitBytes = GetInt(limitsElement, "stderr_bytes") ?? 0
        };
    }

    #endregion

    #region JSON Helpers

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }

    private static long? GetLong(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
    }

    private static List<string> GetStringList(JsonElement element, string name)
    {
        var result = new List<string>();
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    result.Add(item.GetString() ?? "");
            }
        }

        return result;
    }

    #endregion
}

/// <summary>Parse outcome: the tasks that parsed and one message per malformed line.</summary>
public sealed record VerifyTasksetParseResult(List<VerifyTaskData> Tasks, List<string> Errors);
