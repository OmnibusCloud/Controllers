using System.Text.Encodings.Web;
using System.Text.Json;
using OutWit.Controller.AI.Wasm;
using OutWit.Controller.Smt.Spike.Lang;
using OutWit.Controller.Smt.Spike.Protocol;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>The JSON-lines records the harness writes: one per problem and engine.</summary>
internal static class SpikeRecords
{
    #region Constants

    private static readonly JsonSerializerOptions JSON = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    #endregion

    #region Functions

    /// <summary>The record of an interactive session (wasm or native).</summary>
    /// <param name="problem">The slice's row.</param>
    /// <param name="canonical">The canonical problem.</param>
    /// <param name="engine">wasm or native.</param>
    /// <param name="driver">The session's answers.</param>
    /// <param name="outcome">How it ended.</param>
    /// <returns>The record.</returns>
    public static Dictionary<string, object?> Session(SpikeProblem problem, SmtProblem canonical, string engine, SmtSessionDriver driver, SmtSessionOutcome outcome)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = problem.Id,
            ["logic"] = problem.Logic,
            ["expected"] = problem.Status,
            ["engine"] = engine,
            ["verdict"] = driver.Verdict,
            ["agree"] = Agreement(problem.Status, driver.Verdict),
            ["reason"] = driver.ReasonUnknown,
            ["end"] = outcome.End,
            ["exit_code"] = outcome.ExitCode,
            ["fault"] = outcome.Fault,
            ["fuel"] = outcome.Fuel,
            ["fuel_check"] = driver.FuelAtFirstVerdict,
            ["fuel_startup"] = outcome.StartupFuel,
            ["fuel_instantiation"] = outcome.InstantiationFuel,
            ["rlimit_count"] = driver.RlimitCount,
            ["z3_max_memory_mb"] = Statistic(driver.Statistics, ":max-memory"),
            ["peak_memory"] = outcome.PeakMemoryBytes,
            ["wall_ms"] = outcome.WallMs,
            ["check_ms"] = driver.CheckMs,
            ["instantiate_ms"] = outcome.InstantiateMs,
            ["startup_ms"] = outcome.StartupMs,
            ["errors"] = driver.Errors,
            ["protocol_error"] = driver.ProtocolError,
            ["stdout_sha256"] = SpikeProblems.Sha256(outcome.Stdout),
            ["stdout_bytes"] = outcome.Stdout.Length,
            ["stderr_tail"] = Tail(outcome.Stderr, 400),
            ["canonical_sha256"] = canonical.Hash,
            ["constants"] = canonical.Constants.Count,
            ["model"] = driver.Model,
            ["values"] = driver.Values
        };
    }

    /// <summary>The record of a whole-script run of the native solver (no conversation).</summary>
    /// <param name="problem">The slice's row.</param>
    /// <param name="canonical">The canonical problem.</param>
    /// <param name="outcome">How the process ended.</param>
    /// <param name="checkMs">Milliseconds from the start until the verdict's block ended.</param>
    /// <returns>The record, with the fields of a session record.</returns>
    public static Dictionary<string, object?> Script(SpikeProblem problem, SmtProblem canonical, SmtSessionOutcome outcome, long checkMs)
    {
        var (verdict, errors, model, values, reason, statistics) = SpikeScripts.Read(canonical, outcome.Stdout);
        return new Dictionary<string, object?>
        {
            ["id"] = problem.Id,
            ["logic"] = problem.Logic,
            ["expected"] = problem.Status,
            ["engine"] = "native",
            ["mode"] = "script",
            ["verdict"] = verdict,
            ["agree"] = Agreement(problem.Status, verdict),
            ["reason"] = reason,
            ["end"] = outcome.End,
            ["exit_code"] = outcome.ExitCode,
            ["fault"] = outcome.Fault,
            ["fuel"] = 0,
            ["rlimit_count"] = (long?)Statistic(statistics, ":rlimit-count"),
            ["z3_max_memory_mb"] = Statistic(statistics, ":max-memory"),
            ["peak_memory"] = 0,
            ["wall_ms"] = outcome.WallMs,
            ["check_ms"] = checkMs,
            ["errors"] = errors,
            ["stdout_sha256"] = SpikeProblems.Sha256(outcome.Stdout),
            ["stdout_bytes"] = outcome.Stdout.Length,
            ["stderr_tail"] = Tail(outcome.Stderr, 400),
            ["canonical_sha256"] = canonical.Hash,
            ["constants"] = canonical.Constants.Count,
            ["model"] = model,
            ["values"] = values
        };
    }

    /// <summary>The record of a run through the unchanged host (the whole script as a stdin file).</summary>
    /// <param name="problem">The slice's row.</param>
    /// <param name="canonical">The canonical problem.</param>
    /// <param name="outcome">The host's outcome.</param>
    /// <returns>The record.</returns>
    public static Dictionary<string, object?> Batch(SpikeProblem problem, SmtProblem canonical, WasmHostRunOutcome outcome)
    {
        var blocks = Blocks(outcome.Stdout, SpikeProblems.Nonce(canonical));
        var verdict = blocks.Count > 0 ? blocks[0].Split('\n').Select(l => l.Trim()).LastOrDefault(l => l is "sat" or "unsat" or "unknown") ?? "none" : "none";
        var stats = blocks.Count > 0 ? blocks[^1] : null;
        return new Dictionary<string, object?>
        {
            ["id"] = problem.Id,
            ["logic"] = problem.Logic,
            ["expected"] = problem.Status,
            ["engine"] = "wasm-host-run",
            ["verdict"] = verdict,
            ["agree"] = Agreement(problem.Status, verdict),
            ["end"] = outcome.Verdict.ToString(),
            ["exit_code"] = outcome.ExitCode,
            ["fuel"] = outcome.FuelConsumed,
            ["rlimit_count"] = Statistic(stats, ":rlimit-count"),
            ["peak_memory"] = outcome.PeakMemoryBytes,
            ["wall_ms"] = outcome.WallMs,
            ["blocks"] = blocks.Count,
            ["stdout_sha256"] = SpikeProblems.Sha256(outcome.Stdout),
            ["model_sha256"] = blocks.Count > 1 && verdict == "sat" ? SpikeProblems.Sha256(blocks[1].Trim()) : null
        };
    }

    /// <summary>The answer blocks of a stdout: the text before each nonce line.</summary>
    /// <param name="stdout">The solver's stdout.</param>
    /// <param name="nonce">The nonce.</param>
    /// <returns>The blocks in order.</returns>
    public static List<string> Blocks(string stdout, string nonce)
    {
        var blocks = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var line in stdout.Replace("\r\n", "\n").Split('\n'))
        {
            if (line == nonce)
            {
                blocks.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(line).Append('\n');
            }
        }

        return blocks;
    }

    /// <summary>Writes one record as a JSON line.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="record">The record.</param>
    public static void Write(TextWriter writer, Dictionary<string, object?> record)
    {
        writer.WriteLine(JsonSerializer.Serialize(record, JSON));
        writer.Flush();
    }

    /// <summary>agree, disagree, or n/a (no expectation, or no sat/unsat verdict).</summary>
    /// <param name="expected">The :status.</param>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The agreement.</returns>
    public static string Agreement(string? expected, string verdict)
    {
        if (expected is not ("sat" or "unsat") || verdict is not ("sat" or "unsat"))
            return "n/a";
        return expected == verdict ? "agree" : "disagree";
    }

    /// <summary>A numeric statistic of a get-info :all-statistics answer.</summary>
    /// <param name="statistics">The answer.</param>
    /// <param name="key">The key (with its colon).</param>
    /// <returns>The number, or null.</returns>
    public static double? Statistic(string? statistics, string key)
    {
        if (statistics == null)
            return null;
        var at = statistics.IndexOf(key + " ", StringComparison.Ordinal);
        if (at < 0)
            return null;
        var rest = statistics[(at + key.Length)..].TrimStart();
        var number = new string(rest.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string Tail(string text, int length)
    {
        return text.Length <= length ? text : text[^length..];
    }

    #endregion
}
