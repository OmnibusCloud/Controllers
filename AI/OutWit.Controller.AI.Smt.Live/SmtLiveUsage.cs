using System.Globalization;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Live;

/// <summary>The tool's usage text.</summary>
internal static class SmtLiveUsage
{
    #region Functions

    /// <summary>
    /// Writes the usage.
    /// </summary>
    /// <param name="output">Where it goes.</param>
    public static void Write(TextWriter output)
    {
        output.WriteLine("smt-live diagnostics [--out <folder>]");
        output.WriteLine("smt-live preflight <problems.jsonl> [options]");
        output.WriteLine("smt-live run       <problems.jsonl> [options] [--out <folder>]");
        output.WriteLine("smt-live fetch     <job id> [--out <folder>]");
        output.WriteLine("smt-live compare   <folder-a> <folder-b>");
        output.WriteLine("smt-live pack      <folder of .smt2> [--query check|enumerate|prove] --out <problems.jsonl>");
        output.WriteLine("smt-live local     <problems.jsonl> [options but --rate] [--degree N] [--out <folder>]");
        output.WriteLine("smt-live comparative <local folder> <fleet run folder> [--fixed <fleet run folder of a small set>]");
        output.WriteLine();
        output.WriteLine("options, by the names of the controller's options document, with what absent and 0 mean:");
        output.WriteLine($"  --query check|enumerate|prove  query          the query of a row that names none (absent: {SmtLiveArguments.WordOf(SmtQuery.Check)})");
        output.WriteLine($"  --rate R                       sampling_rate  the share of the packages solved on two machines, 0 to 1 (absent: {Number(SmtOptionsRules.DEFAULT_SAMPLING_RATE)}; 0: no package on two machines)");
        output.WriteLine($"  --rlimit N                     rlimit         the solver's resource limit of one check (0 or absent: the profile's, {SmtProfile.DEFAULT_RESOURCE_LIMIT})");
        output.WriteLine($"  --hard-cap S                   hard_cap_s     seconds of the reference machine for one problem (0 or absent: {SmtOptionsRules.DEFAULT_HARD_CAP_SECONDS})");
        output.WriteLine($"  --memory MB                    memory_mb      the most memory of one problem (0 or absent: {SmtOptionsRules.DEFAULT_MEMORY_MEGABYTES})");
        output.WriteLine("  --seed N                       seed           the seed of the solver's random choices (0 or absent: 0)");
        output.WriteLine($"  --max-models N                 max_models     the models of an enumeration whose row names no number (0 or absent: {SmtOptionsRules.DEFAULT_MAX_MODELS})");
        output.WriteLine($"  --degree N                     (local)        the sessions at once, 1 to {Commands.SmtLiveLocal.MAX_DEGREE}, in place of the node's rule");
        output.WriteLine();
        output.WriteLine("--out: a folder of the tool's own, made when it is not there; one that holds the documents of a run is refused");
        output.WriteLine();
        output.WriteLine("environment (diagnostics, preflight, run, fetch):");
        output.WriteLine($"  {SmtLiveEnvironment.SERVER_VARIABLE}    the server's URL");
        output.WriteLine($"  {SmtLiveEnvironment.IDENTITY_VARIABLE}  the identity server's URL");
        output.WriteLine($"  {SmtLiveEnvironment.KEY_VARIABLE}    a wit_sk_ application key");
        output.WriteLine($"  {SmtLiveEnvironment.WHERE_THE_KEY_GOES}");
        output.WriteLine();
        output.WriteLine("Ctrl+C stops the work (a job goes on on the server, and the tool says how to fetch it); a second Ctrl+C ends the tool at once");
        output.WriteLine();
        output.WriteLine($"exit: {SmtLiveExitCodes.OK} done, and everything held; " +
                         $"{SmtLiveExitCodes.FAILED} a job, a document, a preflight, a packed row or a local run's sandbox failed; " +
                         $"{SmtLiveExitCodes.USAGE} the command line, an input, the folder or the environment is wrong (nothing was sent or run); " +
                         $"{SmtLiveExitCodes.ALARM} a run reports something a lab must look at; " +
                         $"{SmtLiveExitCodes.DIFFER} the two runs of compare or comparative differ; " +
                         $"{SmtLiveExitCodes.STOPPED} stopped by Ctrl+C; " +
                         $"{SmtLiveExitCodes.ERROR} the connection, the server or this machine failed (what is left on the server is said)");
    }

    private static string Number(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    #endregion
}
