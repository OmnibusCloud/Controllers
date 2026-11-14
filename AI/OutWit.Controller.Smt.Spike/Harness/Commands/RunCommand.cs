using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary><c>run</c>: one SMT-LIB file through the wasm module, the record printed (a diagnostic).</summary>
internal static class RunCommand
{
    #region Functions

    /// <summary>Runs one file.</summary>
    /// <param name="args">--module, --smt; --cache, --repeat and the envelope.</param>
    /// <returns>The exit code.</returns>
    public static int Run(SpikeArgs args)
    {
        var options = SpikeOptions.From(args);
        var text = File.ReadAllText(args.Required("smt"));
        var problem = SmtProblem.Parse(text);
        var row = new SpikeProblem("run", problem.Logic ?? "", problem.Status ?? "", args.Required("smt"), text);
        using var engine = SpikeEngine.Create(args.Required("module"), args.Optional("cache"));
        for (var i = 0; i < args.Long("repeat", 1); i++)
        {
            var (driver, outcome) = engine.Session(problem, options);
            var record = SpikeRecords.Session(row, problem, "wasm", driver, outcome);
            record.Remove("model");
            record.Remove("values");
            SpikeRecords.Write(Console.Out, record);
        }

        return 0;
    }

    #endregion
}
