using System.Text.Json;
using OutWit.Controller.Smt.Spike.Eval;
using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>
/// <c>oracle</c>: the evaluator checked against Z3 itself. For every sat answer of a results file,
/// and for the wrong models the evaluator makes from it, the closed script of §4.5 runs in the
/// wasm module: Z3 should say sat where the evaluator says validated and unsat where it says
/// failed. One JSON line per model and an agreement table.
/// </summary>
internal static class OracleCommand
{
    #region Constants

    private const int EVALUATOR_STACK_BYTES = 512 << 20;

    #endregion

    #region Functions

    /// <summary>Runs the oracle check.</summary>
    /// <param name="args">--module, --problems, --list, --results, --out; --cache, --mutants and the envelope.</param>
    /// <returns>0, or 1 when the evaluator and Z3 contradict each other.</returns>
    public static int Run(SpikeArgs args)
    {
        var exit = 0;
        var thread = new Thread(() => exit = Check(args), EVALUATOR_STACK_BYTES);
        thread.Start();
        thread.Join();
        return exit;
    }

    private static int Check(SpikeArgs args)
    {
        var options = SpikeOptions.From(args);
        var problems = SpikeProblems.Load(args.Required("list"), args.Required("problems"), 0).ToDictionary(p => p.Id);
        var mutantsPerModel = (int)args.Long("mutants", 3);
        var table = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var contradictions = 0;

        using var engine = SpikeEngine.Create(args.Required("module"), args.Optional("cache"));
        using var writer = new StreamWriter(args.Required("out"));
        foreach (var line in File.ReadLines(args.Required("results")))
        {
            var record = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
            if (record == null || !record.TryGetValue("engine", out var e) || e.GetString() != "wasm" || record["verdict"].GetString() != "sat")
                continue;
            if (record["model"].ValueKind != JsonValueKind.String)
                continue;

            var id = record["id"].GetString()!;
            if (!problems.TryGetValue(id, out var row) || row.Text == null)
                continue;

            var problem = SmtProblem.Parse(row.Text);
            var validator = new SmtModelValidator(problem);
            var model = SmtModel.Parse(record["model"].GetString(), record.TryGetValue("values", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null);

            var cases = new List<(string Kind, string? Constant, SmtModel Model)> { ("model", null, model) };
            cases.AddRange(validator.Mutants(model, mutantsPerModel).Select(m => ("mutant", (string?)m.Constant, m.Model)));
            foreach (var (kind, constant, candidate) in cases)
            {
                var evaluator = validator.Check(candidate).Class;
                string oracle;
                try
                {
                    var closed = SmtProblem.Parse(SmtClosedScript.Build(problem, candidate));
                    oracle = engine.Session(closed, options).Driver.Verdict;
                }
                catch (FormatException exception)
                {
                    oracle = "unreadable: " + exception.Message;
                }

                var contradiction = (evaluator == "validated" && oracle == "unsat") || (evaluator == "failed" && oracle == "sat");
                contradictions += contradiction ? 1 : 0;
                Count(table, $"{kind}\tevaluator {evaluator}\tz3 {oracle}");
                SpikeRecords.Write(writer, new Dictionary<string, object?>
                {
                    ["id"] = id, ["logic"] = row.Logic, ["kind"] = kind, ["constant"] = constant,
                    ["evaluator"] = evaluator, ["z3"] = oracle, ["contradiction"] = contradiction
                });
            }
        }

        foreach (var (key, count) in table)
            Console.WriteLine($"{key}\t{count}");
        Console.WriteLine($"contradictions\t{contradictions}");
        return contradictions > 0 ? 1 : 0;
    }

    private static void Count(SortedDictionary<string, int> table, string key)
    {
        table[key] = table.GetValueOrDefault(key) + 1;
    }

    #endregion
}
