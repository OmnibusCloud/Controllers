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
        output.WriteLine("smt-live compare   <folder-a> <folder-b>");
        output.WriteLine("smt-live pack      <folder of .smt2> [--query check|enumerate|prove] --out <problems.jsonl>");
        output.WriteLine("smt-live local     <problems.jsonl> [options but --rate] [--degree N] [--out <folder>]");
        output.WriteLine("smt-live comparative <local folder> <fleet run folder> [--fixed <fleet run folder of a small set>]");
        output.WriteLine();
        output.WriteLine("options (0 or absent: the controller's default):");
        output.WriteLine("  --query check|enumerate|prove  query          the query of a row that names none");
        output.WriteLine("  --rate R                       sampling_rate  the share of the packages solved on two machines, 0 to 1");
        output.WriteLine("  --rlimit N                     rlimit         the solver's resource limit of one check");
        output.WriteLine("  --hard-cap S                   hard_cap_s     seconds of the reference machine for one problem");
        output.WriteLine("  --memory MB                    memory_mb      the most memory of one problem");
        output.WriteLine("  --seed N                       seed           the seed of the solver's random choices");
        output.WriteLine("  --max-models N                 max_models     the models of an enumeration whose row names no number");
        output.WriteLine($"  --degree N                     (local)        the sessions at once, 1 to {Commands.SmtLiveLocal.MAX_DEGREE}, in place of the node's rule");
        output.WriteLine();
        output.WriteLine("environment (diagnostics, preflight, run):");
        output.WriteLine($"  {SmtLiveEnvironment.SERVER_VARIABLE}    the server's URL");
        output.WriteLine($"  {SmtLiveEnvironment.IDENTITY_VARIABLE}  the identity server's URL");
        output.WriteLine($"  {SmtLiveEnvironment.KEY_VARIABLE}    a wit_sk_ application key");
        output.WriteLine();
        output.WriteLine($"exit: {SmtLiveExitCodes.OK} done; {SmtLiveExitCodes.FAILED} a job, a document, a preflight, a comparison, a local run or a packed row failed; " +
                         $"{SmtLiveExitCodes.USAGE} the command line or the environment is wrong; {SmtLiveExitCodes.ALARM} a run reports something alarming " +
                         "(local: as run; comparative: 1 when the two runs differ)");
    }

    #endregion
}
