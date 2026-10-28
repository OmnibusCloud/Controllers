using System.Text.RegularExpressions;
using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Runtime;

/// <summary>
/// Keeps a deck's <c>SOLVER=</c> choice within what the bundled ccx can run.
/// The Windows and Linux kits link SPOOLES and PARDISO (oneMKL), the macOS
/// kit SPOOLES only (oneMKL has no arm64 build); ccx's own iterative solvers
/// (ITERATIVESCALING, ITERATIVECHOLESKY) are always there. A step card
/// naming a solver that is not linked ends the solve at once with exit 201
/// ("the PASTIX library is not linked" - measured on the Windows kit
/// 2026-10-28 for PASTIX, TAUCS and SGI; PARDISO fails the same way on a
/// macOS node), and PrePoMax offers PaStiX in its solver list and writes
/// PARDISO by default, so the parameter is removed from the card before the
/// solve and ccx takes its default (PARDISO where linked, else SPOOLES),
/// with a note in the variant. A name ccx does not know at all is left as
/// written: ccx warns and uses its default by itself.
/// </summary>
public static partial class CcxSolverFallback
{
    #region Constants

    /// <summary>The solvers every kit runs: SPOOLES and ccx's two iterative solvers.</summary>
    public static readonly IReadOnlySet<string> ALWAYS_LINKED = new HashSet<string>(StringComparer.Ordinal)
    {
        "SPOOLES", "ITERATIVESCALING", "ITERATIVECHOLESKY"
    };

    /// <summary>Every solver name ccx 2.22 knows on a step card.</summary>
    public static readonly IReadOnlySet<string> KNOWN = new HashSet<string>(StringComparer.Ordinal)
    {
        "SPOOLES", "ITERATIVESCALING", "ITERATIVECHOLESKY", "SGI", "TAUCS", "PARDISO", "PASTIX"
    };

    [GeneratedRegex(@"SOLVER\s*=\s*([A-Za-z]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SolverParameterRegex();

    [GeneratedRegex(@"\s*,\s*SOLVER\s*=\s*[A-Za-z]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingCommaFormRegex();

    [GeneratedRegex(@"^\s*SOLVER\s*=\s*[A-Za-z]*\s*,?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineStartFormRegex();

    #endregion

    #region Functions

    /// <summary>
    /// The solvers the bundled ccx of a platform runs.
    /// </summary>
    /// <param name="pardisoLinked">Whether the platform's kit links PARDISO.</param>
    /// <returns>The accepted <c>SOLVER=</c> values.</returns>
    public static IReadOnlySet<string> Linked(bool pardisoLinked)
    {
        var linked = new HashSet<string>(ALWAYS_LINKED, StringComparer.Ordinal);
        if (pardisoLinked)
            linked.Add("PARDISO");

        return linked;
    }

    /// <summary>
    /// The solver ccx takes without a <c>SOLVER=</c> parameter.
    /// </summary>
    /// <param name="pardisoLinked">Whether the platform's kit links PARDISO.</param>
    /// <returns>PARDISO where linked, else SPOOLES.</returns>
    public static string Default(bool pardisoLinked)
    {
        return pardisoLinked ? "PARDISO" : "SPOOLES";
    }

    /// <summary>
    /// Rewrites the deck in place so that no step card names a solver the kit lacks.
    /// </summary>
    /// <param name="deckPath">The deck ccx will read.</param>
    /// <param name="pardisoLinked">Whether this platform's kit links PARDISO.</param>
    /// <returns>One note per parameter removed; empty when the deck was left as it was.</returns>
    public static List<CcxNoteData> Apply(string deckPath, bool pardisoLinked)
    {
        var lines = File.ReadAllLines(deckPath);
        var rewritten = Rewrite(lines, pardisoLinked, out var notes);
        if (notes.Count > 0)
            File.WriteAllLines(deckPath, rewritten);

        return notes;
    }

    /// <summary>
    /// Rewrites deck text so that no step card names a solver the kit lacks.
    /// </summary>
    /// <param name="lines">The deck, line by line.</param>
    /// <param name="pardisoLinked">Whether this platform's kit links PARDISO.</param>
    /// <param name="notes">Receives one note per parameter removed.</param>
    /// <returns>The deck with the offending parameters removed; the same lines when nothing was.</returns>
    public static List<string> Rewrite(IReadOnlyList<string> lines, bool pardisoLinked, out List<CcxNoteData> notes)
    {
        notes = [];
        var linked = Linked(pardisoLinked);
        var result = new List<string>(lines.Count);
        var inKeyword = false;
        var keyword = string.Empty;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("**", StringComparison.Ordinal))
            {
                result.Add(line);
                continue;
            }

            if (trimmed.StartsWith('*'))
            {
                inKeyword = true;
                keyword = Keyword(trimmed);
            }
            else if (!inKeyword)
            {
                result.Add(line);
                continue;
            }

            var continues = trimmed.EndsWith(',');
            var kept = line;
            foreach (Match match in SolverParameterRegex().Matches(line))
            {
                var solver = match.Groups[1].Value.ToUpperInvariant();
                if (!KNOWN.Contains(solver) || linked.Contains(solver))
                    continue;

                kept = Remove(kept);
                notes.Add(new CcxNoteData
                {
                    Kind = CcxNoteKind.Warning,
                    Text = $"SOLVER={solver} is not linked in the bundled ccx of this node: the parameter was removed from *{keyword} and the solve used {Default(pardisoLinked)}"
                });
            }

            if (kept.Trim().Length == 0)
            {
                // The parameter was the whole continuation line: the card
                // ends on the line before, whose trailing comma goes too
                // unless this line went on continuing.
                if (!continues && result.Count > 0 && result[^1].TrimEnd().EndsWith(','))
                    result[^1] = result[^1].TrimEnd().TrimEnd(',');
            }
            else
            {
                result.Add(kept);
            }

            inKeyword = continues;
        }

        return result;
    }

    private static string Remove(string line)
    {
        var removed = LeadingCommaFormRegex().Replace(line, string.Empty, 1);
        return ReferenceEquals(removed, line) || removed == line ? LineStartFormRegex().Replace(line, string.Empty, 1) : removed;
    }

    private static string Keyword(string trimmed)
    {
        var comma = trimmed.IndexOf(',');
        var text = comma < 0 ? trimmed : trimmed[..comma];
        return text.TrimStart('*').Trim().ToUpperInvariant();
    }

    #endregion
}
