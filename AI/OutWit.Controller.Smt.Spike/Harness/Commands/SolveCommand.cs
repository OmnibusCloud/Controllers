using OutWit.Controller.Smt.Spike.Lang;
using OutWit.Controller.Smt.Spike.Protocol;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>
/// <c>wasm</c> and <c>native</c>: the slice through the Z3 module in the shared host, or through
/// the native Z3 release, one interactive session per problem, one JSON line per session.
/// </summary>
internal static class SolveCommand
{
    #region Functions

    /// <summary>Runs the slice on the wasm module.</summary>
    /// <param name="args">--module, --problems, --list, --out; --cache, --batch-every, --limit and the envelope.</param>
    /// <returns>The exit code.</returns>
    public static int Wasm(SpikeArgs args)
    {
        var options = SpikeOptions.From(args);
        var problems = SpikeProblems.Load(args.Required("list"), args.Required("problems"), args.Long("limit", 0));
        var batchEvery = args.Long("batch-every", 5);

        using var engine = SpikeEngine.Create(args.Required("module"), args.Optional("cache"));
        using var writer = new StreamWriter(args.Required("out"));
        var meta = new Dictionary<string, object?>(engine.Meta) { ["options"] = Describe(options) };
        SpikeRecords.Write(writer, meta);
        Console.WriteLine($"wasm: {problems.Count} problems; compile {meta["compile_ms"]} ms, deserialize {meta["deserialize_ms"]} ms, explicit traps {meta["explicit_traps"]}");

        for (var i = 0; i < problems.Count; i++)
        {
            var problem = problems[i];
            var canonical = Canonical(problem, writer);
            if (canonical == null)
                continue;

            var (driver, outcome) = engine.Session(canonical, options);
            var record = SpikeRecords.Session(problem, canonical, "wasm", driver, outcome);
            SpikeRecords.Write(writer, record);
            Progress(i, problems.Count, record);

            if (batchEvery > 0 && i % batchEvery == 0)
            {
                var (batch, _) = engine.Batch(canonical, options);
                SpikeRecords.Write(writer, SpikeRecords.Batch(problem, canonical, batch));
            }
        }

        return 0;
    }

    /// <summary>Runs the slice on the native release.</summary>
    /// <param name="args">--z3, --problems, --list, --out; --limit and the envelope.</param>
    /// <returns>The exit code.</returns>
    public static int Native(SpikeArgs args)
    {
        var options = SpikeOptions.From(args);
        var problems = SpikeProblems.Load(args.Required("list"), args.Required("problems"), args.Long("limit", 0));
        var native = new SmtNativeSession(args.Required("z3"));

        using var writer = new StreamWriter(args.Required("out"));
        SpikeRecords.Write(writer, new Dictionary<string, object?>
        {
            ["kind"] = "meta",
            ["platform"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            ["z3"] = args.Required("z3"),
            ["options"] = Describe(options)
        });

        for (var i = 0; i < problems.Count; i++)
        {
            var problem = problems[i];
            var canonical = Canonical(problem, writer);
            if (canonical == null)
                continue;

            // The whole script at once: a conversation over pipes stalled on the Windows build of
            // Z3 (run 43596434994); the answers are the same, the check is timed to its block's end.
            var (script, nonce) = SpikeScripts.Check(canonical, options.Rlimit);
            var (outcome, checkMs) = native.RunScript(options.Argv().Skip(1).ToList(), script, nonce, options.WallMs);
            var record = SpikeRecords.Script(problem, canonical, outcome, checkMs);
            SpikeRecords.Write(writer, record);
            Progress(i, problems.Count, record);
        }

        return 0;
    }

    private static SmtProblem? Canonical(SpikeProblem problem, StreamWriter writer)
    {
        if (problem.Text == null)
        {
            SpikeRecords.Write(writer, new Dictionary<string, object?> { ["id"] = problem.Id, ["engine"] = "none", ["end"] = "missing" });
            return null;
        }

        try
        {
            return SmtProblem.Parse(problem.Text);
        }
        catch (FormatException exception)
        {
            SpikeRecords.Write(writer, new Dictionary<string, object?> { ["id"] = problem.Id, ["engine"] = "none", ["end"] = "refused", ["fault"] = exception.Message });
            return null;
        }
    }

    private static Dictionary<string, object?> Describe(SpikeOptions options)
    {
        return new Dictionary<string, object?>
        {
            ["rlimit"] = options.Rlimit,
            ["fuel"] = options.Fuel,
            ["memory_mb"] = options.MemoryMb,
            ["wasm_memory_bytes"] = options.WasmMemoryBytes,
            ["wall_ms"] = options.WallMs,
            ["seed"] = options.Seed,
            ["argv"] = options.Argv()
        };
    }

    private static void Progress(int index, int total, Dictionary<string, object?> record)
    {
        if (index % 25 == 0 || index == total - 1 || (string?)record["agree"] == "disagree")
            Console.WriteLine($"  {index + 1}/{total} {record["id"]} {record["logic"]} {record["verdict"]} ({record["agree"]}) end={record["end"]} fuel={record["fuel"]} {record["wall_ms"]} ms");
    }

    #endregion
}
