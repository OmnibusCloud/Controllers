using System.Globalization;
using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// Reads the facts out of an OpenFOAM log (a solver's or a utility's): the
/// time loop, the residuals, convergence, warnings, fatal errors, the mesh
/// size. Line by line, so a multi-gigabyte transient log costs memory for
/// one line at a time.
/// </summary>
public static class FoamLogReader
{
    #region Constants

    private static readonly Regex TIME = new(@"^Time = ([-+0-9.eE]+)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RESIDUAL = new(@"Solving for ([A-Za-z0-9_.:]+),\s+Initial residual = ([-+0-9.eE]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CONVERGED = new(@"solution converged|reached convergence|converged in \d+ iterations", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CELLS = new(@"^\s*(?:nCells|cells):\s+(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string END = "End";

    private const string WARNING = "--> FOAM Warning";

    private const string FATAL = "--> FOAM FATAL";

    private const string SIGFPE = "sigFpe";

    private const string FPE = "Floating point exception";

    #endregion

    #region Functions

    /// <summary>
    /// Reads a log file.
    /// </summary>
    /// <param name="path">The log's path.</param>
    /// <returns>The facts; empty facts when the file does not exist.</returns>
    public static FoamLogFacts Read(string path)
    {
        if (!File.Exists(path))
            return new FoamLogFacts();

        using var reader = new StreamReader(path);
        return Read(reader);
    }

    /// <summary>
    /// Reads log text.
    /// </summary>
    /// <param name="reader">The log.</param>
    /// <returns>The facts.</returns>
    public static FoamLogFacts Read(TextReader reader)
    {
        var facts = new FoamLogFacts();
        var residuals = new Dictionary<string, double>(StringComparer.Ordinal);
        var order = new List<string>();
        var lastNonEmpty = string.Empty;

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            lastNonEmpty = line;

            var time = TIME.Match(line);
            if (time.Success)
            {
                facts.Iterations++;
                if (double.TryParse(time.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    facts.FinalTime = value;
                continue;
            }

            var residual = RESIDUAL.Match(line);
            if (residual.Success)
            {
                var field = residual.Groups[1].Value;
                if (double.TryParse(residual.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    if (!residuals.ContainsKey(field))
                        order.Add(field);
                    residuals[field] = value;
                }
                continue;
            }

            if (line.StartsWith(WARNING, StringComparison.Ordinal))
                facts.WarningCount++;
            else if (line.StartsWith(FATAL, StringComparison.Ordinal))
                facts.Fatal = true;
            else if (line.Contains(SIGFPE, StringComparison.Ordinal) || line.Contains(FPE, StringComparison.Ordinal))
                facts.FloatingPointException = true;
            else if (CONVERGED.IsMatch(line))
                facts.Converged = true;
            else
            {
                var cells = CELLS.Match(line);
                if (cells.Success && long.TryParse(cells.Groups[1].Value, out var count))
                    facts.CellCount = count;
            }
        }

        facts.ReachedEnd = lastNonEmpty.Trim() == END;

        foreach (var field in order)
            facts.FinalResiduals.Add(new KeyValuePair<string, double>(field, residuals[field]));

        return facts;
    }

    #endregion
}
