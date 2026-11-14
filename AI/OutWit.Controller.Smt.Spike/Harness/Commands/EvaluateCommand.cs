using System.Text.Json;
using OutWit.Controller.Smt.Spike.Eval;
using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>
/// <c>evaluate</c>: the host-side model evaluator over every sat answer of a results file: the
/// model the solver printed against the problem's assertions, then the same with wrong models
/// (one constant changed) to show they fail. One JSON line per sat answer and a summary.
/// </summary>
internal static class EvaluateCommand
{
    #region Constants

    private const int EVALUATOR_STACK_BYTES = 512 << 20;

    #endregion

    #region Functions

    /// <summary>Evaluates the sat answers of a results file.</summary>
    /// <param name="args">--problems, --list, --results, --out; --mutants, --engine.</param>
    /// <returns>The exit code: 0, or 1 when a model of a sat answer failed.</returns>
    public static int Run(SpikeArgs args)
    {
        var exit = 0;
        var thread = new Thread(() => exit = Evaluate(args), EVALUATOR_STACK_BYTES);
        thread.Start();
        thread.Join();
        return exit;
    }

    private static int Evaluate(SpikeArgs args)
    {
        var problems = SpikeProblems.Load(args.Required("list"), args.Required("problems"), 0).ToDictionary(p => p.Id);
        var mutantsPerModel = (int)args.Long("mutants", 3);
        var engine = args.Optional("engine") ?? "wasm";
        var summary = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var failedModels = 0;

        using var writer = new StreamWriter(args.Required("out"));
        foreach (var line in File.ReadLines(args.Required("results")))
        {
            var record = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
            if (record == null || !record.TryGetValue("engine", out var e) || e.GetString() != engine)
                continue;
            if (record["verdict"].GetString() != "sat")
                continue;

            var id = record["id"].GetString()!;
            if (!problems.TryGetValue(id, out var problem) || problem.Text == null)
                continue;

            var canonical = SmtProblem.Parse(problem.Text);
            var validator = new SmtModelValidator(canonical);
            var modelText = Text(record, "model");
            var valuesText = Text(record, "values");

            SmtValidation validation;
            SmtModel model;
            if (modelText == null)
            {
                model = new SmtModel();
                validation = new SmtValidation { Class = "partial", Reason = "no model printed (" + Text(record, "end") + ")" };
            }
            else
            try
            {
                model = SmtModel.Parse(modelText, valuesText);
                validation = validator.Check(model);
            }
            catch (FormatException exception)
            {
                model = new SmtModel();
                validation = new SmtValidation { Class = "partial", Reason = "model unreadable: " + exception.Message };
            }

            Count(summary, $"{problem.Logic}\t{validation.Class}");
            if (validation.Class == "partial")
                Count(summary, $"partial reason\t{validation.Reason}");
            if (validation.Class == "failed")
                failedModels++;

            var mutants = new List<Dictionary<string, object?>>();
            if (validation.Class == "validated")
            {
                foreach (var (constant, mutant) in validator.Mutants(model, mutantsPerModel))
                {
                    var checkedMutant = validator.Check(mutant);
                    Count(summary, $"mutant\t{checkedMutant.Class}");
                    mutants.Add(new Dictionary<string, object?> { ["constant"] = constant, ["class"] = checkedMutant.Class, ["reason"] = checkedMutant.Reason });
                }
            }

            SpikeRecords.Write(writer, new Dictionary<string, object?>
            {
                ["id"] = id,
                ["logic"] = problem.Logic,
                ["class"] = validation.Class,
                ["reason"] = validation.Reason,
                ["assertions"] = validation.Assertions,
                ["true"] = validation.True,
                ["defined"] = validation.Defined,
                ["completion"] = validation.UsedCompletion,
                ["us"] = validation.Microseconds,
                ["mutants"] = mutants
            });
        }

        foreach (var (key, count) in summary)
            Console.WriteLine($"{key}\t{count}");
        return failedModels > 0 ? 1 : 0;
    }

    private static string? Text(Dictionary<string, JsonElement> record, string key)
    {
        return record.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static void Count(SortedDictionary<string, int> summary, string key)
    {
        summary[key] = summary.GetValueOrDefault(key) + 1;
    }

    #endregion
}
