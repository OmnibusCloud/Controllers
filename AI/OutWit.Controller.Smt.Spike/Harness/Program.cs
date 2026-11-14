using System.Diagnostics;
using OutWit.Controller.Smt.Spike.Commands;

namespace OutWit.Controller.Smt.Spike;

/// <summary>
/// The SM-0 harness. Verbs:
/// <list type="bullet">
/// <item><c>wasm --module z3.wasm --problems DIR --list fetch-list.tsv --out FILE [--cache FILE] [--batch-every N] [--limit N]</c></item>
/// <item><c>native --z3 PATH --problems DIR --list fetch-list.tsv --out FILE [--limit N]</c></item>
/// <item><c>limits --module z3.wasm --out FILE [--z3 PATH] [--cache FILE] [--big] [--native-big]</c></item>
/// <item><c>evaluate --problems DIR --list fetch-list.tsv --results FILE --out FILE [--mutants N] [--engine wasm|native]</c></item>
/// <item><c>oracle --module z3.wasm --problems DIR --list fetch-list.tsv --results FILE --out FILE [--mutants N]</c></item>
/// <item><c>run --module z3.wasm --smt FILE</c>, <c>selftest</c></item>
/// </list>
/// Envelope options for wasm, native and limits: --rlimit, --fuel, --memory-mb, --wall-ms, --seed.
/// The harness runs below normal priority.
/// </summary>
internal static class Program
{
    #region Functions

    private static int Main(string[] argv)
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // best effort
        }

        var args = new SpikeArgs(argv);
        try
        {
            return args.Verb switch
            {
                "wasm" => SolveCommand.Wasm(args),
                "native" => SolveCommand.Native(args),
                "limits" => LimitsCommand.Run(args),
                "evaluate" => EvaluateCommand.Run(args),
                "selftest" => SelfTestCommand.Run(),
                "run" => RunCommand.Run(args),
                "oracle" => OracleCommand.Run(args),
                _ => Usage()
            };
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine("smt-spike: " + exception.Message);
            return 64;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: smt-spike wasm|native|limits|evaluate|selftest [options]");
        return 64;
    }

    #endregion
}
