using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>
/// The whole-script form of a check, for a solver that is not conversed with: every query at
/// once, each answer ended by the nonce line, then exit. Blocks in order: the verdict, the model,
/// the values of every declared constant (when there are any), the reason, the statistics. After
/// unsat the model block holds Z3's error "model is not available"; it is not read then.
/// </summary>
internal static class SpikeScripts
{
    #region Functions

    /// <summary>The script of a check.</summary>
    /// <param name="problem">The canonical problem.</param>
    /// <param name="rlimit">The resource limit of the check.</param>
    /// <returns>The script and its nonce.</returns>
    public static (string Script, string Nonce) Check(SmtProblem problem, long rlimit)
    {
        var nonce = SpikeProblems.Nonce(problem);
        var echo = $"(echo \"{nonce}\")\n";
        var values = problem.Constants.Count > 0 ? $"(get-value ({string.Join(" ", problem.Constants.Select(c => c.Symbol.ToString()))}))\n" + echo : "";
        var script = "(set-option :print-success false)\n(set-option :produce-models true)\n" + problem.Canonical
                     + $"(set-option :rlimit {rlimit})\n(check-sat)\n" + echo + "(get-model)\n" + echo + values
                     + "(get-info :reason-unknown)\n" + echo + "(get-info :all-statistics)\n" + echo + "(exit)\n";
        return (script, nonce);
    }

    /// <summary>Reads the answers of a check script's stdout.</summary>
    /// <param name="problem">The canonical problem.</param>
    /// <param name="stdout">The solver's stdout.</param>
    /// <returns>Verdict, errors before it, model, values, reason and statistics (null where missing).</returns>
    public static (string Verdict, List<string> Errors, string? Model, string? Values, string? Reason, string? Statistics) Read(SmtProblem problem, string stdout)
    {
        var blocks = SpikeRecords.Blocks(stdout, SpikeProblems.Nonce(problem));
        var errors = new List<string>();
        var verdict = "none";
        if (blocks.Count > 0)
        {
            foreach (var line in blocks[0].Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                if (line is "sat" or "unsat" or "unknown")
                    verdict = line;
                else if (line.StartsWith("(error", StringComparison.Ordinal))
                    errors.Add(line);
            }
        }

        var hasValues = problem.Constants.Count > 0;
        var expected = hasValues ? 5 : 4;
        if (blocks.Count < expected)
            return (verdict, errors, null, null, null, null);

        var model = verdict == "sat" ? blocks[1].Trim() : null;
        var values = verdict == "sat" && hasValues ? blocks[2].Trim() : null;
        var reasonBlock = blocks[expected - 2].Trim();
        var statistics = blocks[expected - 1].Trim();
        string? reason = null;
        if (verdict == "unknown")
        {
            var at = reasonBlock.IndexOf('"');
            var end = reasonBlock.LastIndexOf('"');
            reason = at >= 0 && end > at ? reasonBlock.Substring(at + 1, end - at - 1) : reasonBlock;
        }

        return (verdict, errors, model, values, reason, statistics);
    }

    #endregion
}
