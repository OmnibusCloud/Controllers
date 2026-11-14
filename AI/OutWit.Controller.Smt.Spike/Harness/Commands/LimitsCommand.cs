using System.Diagnostics;
using OutWit.Controller.Smt.Spike.Lang;
using OutWit.Controller.Smt.Spike.Protocol;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>
/// <c>limits</c>: how the engine and the solver end at the edges, wasm and (with --z3) native: an
/// input error, the resource limit, fuel, Z3's memory ceiling, the wasm32 ceiling of 4 GiB, deep
/// terms, the start-up cost, and the enumerate loop (8 queens: 92, complete) with the rlimit's
/// per-check semantics.
/// </summary>
internal static class LimitsCommand
{
    #region Functions

    /// <summary>Runs the limit cases.</summary>
    /// <param name="args">--module, --out; --z3, --cache, --big (the 4 GiB cases), --native-big.</param>
    /// <returns>The exit code.</returns>
    public static int Run(SpikeArgs args)
    {
        var baseOptions = SpikeOptions.From(args);
        using var engine = SpikeEngine.Create(args.Required("module"), args.Optional("cache"));
        var native = args.Optional("z3") is { } z3 ? new SmtNativeSession(z3) : null;
        using var writer = new StreamWriter(args.Required("out"));
        SpikeRecords.Write(writer, engine.Meta);

        void Case(string name, string text, SpikeOptions options, bool onNative = true, IReadOnlyList<SExpr>? projection = null, int maxModels = 2)
        {
            var problem = SmtProblem.Parse(text);
            var row = new SpikeProblem(name, problem.Logic ?? "", problem.Status ?? "", name, text);
            var (driver, outcome) = engine.Session(problem, options, projection, maxModels);
            Write(writer, name, row, problem, "wasm", driver, outcome, options);

            if (native != null && onNative && projection != null)
            {
                // enumerate needs the conversation (each model blocked before the next check)
                var nativeDriver = new SmtSessionDriver(problem, SpikeProblems.Nonce(problem), options.Rlimit, Stopwatch.StartNew(), projection, maxModels);
                var nativeOutcome = native.Run(options.Argv().Skip(1).ToList(), nativeDriver, Math.Min(options.WallMs, 120_000));
                Write(writer, name, row, problem, "native", nativeDriver, nativeOutcome, options);
            }
            else if (native != null && onNative)
            {
                var (script, nonce) = SpikeScripts.Check(problem, options.Rlimit);
                var (nativeOutcome, checkMs) = native.RunScript(options.Argv().Skip(1).ToList(), script, nonce, options.WallMs);
                var record = SpikeRecords.Script(row, problem, nativeOutcome, checkMs);
                record["case"] = name;
                record["query"] = "check";
                record["memory_mb"] = options.MemoryMb;
                record["rlimit"] = options.Rlimit;
                record.Remove("model");
                record.Remove("values");
                SpikeRecords.Write(writer, record);
                Console.WriteLine($"{name,-38} native verdict={record["verdict"]} end={nativeOutcome.End} exit={nativeOutcome.ExitCode} {nativeOutcome.WallMs} ms {record["reason"]}");
            }
        }

        Case("input_error", LimitsCases.InputError(), baseOptions);
        Case("factoring_sat", LimitsCases.Factoring(), baseOptions);
        Case("rlimit_unknown", LimitsCases.Pigeonhole(11), With(baseOptions, rlimit: 5_000_000));
        Case("rlimit_above_32_bits", LimitsCases.Pigeonhole(11), With(baseOptions, rlimit: 5_000_000_000, fuel: 5_000_000_000), onNative: false);
        Case("out_of_fuel", LimitsCases.Pigeonhole(11), With(baseOptions, rlimit: 0, fuel: 5_000_000_000), onNative: false);
        Case("memout_z3_ceiling_256mb", LimitsCases.MemoryBomb(4096), With(baseOptions, memoryMb: 256, fuel: 5_000_000_000_000, wallMs: 600_000));
        for (var depth = 1000; depth <= 100_000; depth *= 10)
        {
            Case($"deep_not_{depth}", LimitsCases.Deep("not", depth), baseOptions);
            Case($"deep_plus_{depth}", LimitsCases.Deep("plus", depth), baseOptions);
        }

        var queens = SmtProblem.Parse(LimitsCases.Queens(8));
        var projection = queens.Constants.Select(c => c.Symbol).ToList();
        Case("enumerate_queens8", LimitsCases.Queens(8), With(baseOptions, rlimit: 0), projection: projection, maxModels: 100);
        Case("enumerate_queens8_k2", LimitsCases.Queens(8), With(baseOptions, rlimit: 0), projection: projection, maxModels: 2);
        Case("enumerate_queens8_rlimit_per_check", LimitsCases.Queens(8), With(baseOptions, rlimit: (long)args.Long("per-check-rlimit", 2_000_000)), projection: projection, maxModels: 100);

        for (var i = 0; i < 10; i++)
            Case($"startup_{i}", "(set-logic QF_UF)\n", baseOptions);

        if (args.Flag("big"))
        {
            Case("memout_z3_ceiling_1gb", LimitsCases.MemoryBomb(8192), With(baseOptions, memoryMb: 1024, fuel: 10_000_000_000_000, wallMs: 900_000));
            Case("memout_wasm32_ceiling_4gb", LimitsCases.MemoryBomb(16384), With(baseOptions, memoryMb: 8192, fuel: 50_000_000_000_000, wallMs: 1_500_000), onNative: args.Flag("native-big"));
        }

        return 0;
    }

    private static SpikeOptions With(SpikeOptions options, long? rlimit = null, long? fuel = null, int? memoryMb = null, int? wallMs = null)
    {
        return new SpikeOptions
        {
            Rlimit = rlimit ?? options.Rlimit,
            Fuel = fuel ?? options.Fuel,
            MemoryMb = memoryMb ?? options.MemoryMb,
            WallMs = wallMs ?? options.WallMs,
            Seed = options.Seed,
            StdoutLimit = options.StdoutLimit
        };
    }

    private static void Write(StreamWriter writer, string name, SpikeProblem row, SmtProblem problem, string engine, SmtSessionDriver driver, SmtSessionOutcome outcome, SpikeOptions options)
    {
        var record = SpikeRecords.Session(row, problem, engine, driver, outcome);
        record["case"] = name;
        record["query"] = driver.Query;
        record["checks"] = driver.Checks;
        record["count"] = driver.Models.Count;
        record["complete"] = driver.Complete;
        record["complete_reason"] = driver.CompleteReason;
        record["memory_mb"] = options.MemoryMb;
        record["wasm_memory_bytes"] = options.WasmMemoryBytes;
        record["fuel_budget"] = options.Fuel;
        record["rlimit"] = options.Rlimit;
        record.Remove("model");
        record.Remove("values");
        SpikeRecords.Write(writer, record);
        Console.WriteLine($"{name,-38} {engine,-6} verdict={driver.Verdict} end={outcome.End} exit={outcome.ExitCode} fuel={outcome.Fuel} peak={outcome.PeakMemoryBytes} count={driver.Models.Count} complete={driver.Complete} {outcome.WallMs} ms {driver.ReasonUnknown} {string.Join(" ", driver.Errors).Replace('\n', ' ')}");
    }

    #endregion
}
