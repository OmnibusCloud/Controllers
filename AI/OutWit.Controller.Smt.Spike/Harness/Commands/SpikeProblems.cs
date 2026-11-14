using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>The benchmark slice on disk: the fetch list's rows and their files (<c>&lt;id&gt;.smt2</c>).</summary>
internal static class SpikeProblems
{
    #region Functions

    /// <summary>Reads the fetch list and loads each listed file that is present.</summary>
    /// <param name="list">fetch-list.tsv.</param>
    /// <param name="folder">The folder of the fetched files.</param>
    /// <param name="limit">At most this many rows (0 = all).</param>
    /// <returns>The rows in list order, with their text (null when the file is missing).</returns>
    public static List<SpikeProblem> Load(string list, string folder, long limit)
    {
        var lines = File.ReadAllLines(list);
        var header = lines[0].Split('\t');
        var problems = new List<SpikeProblem>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Trim().Length == 0)
                continue;

            var cells = line.Split('\t');
            var row = header.Zip(cells).ToDictionary(p => p.First, p => p.Second);
            var path = Path.Combine(folder, row["id"] + ".smt2");
            problems.Add(new SpikeProblem(row["id"], row["logic"], row["status"], row["member"], File.Exists(path) ? File.ReadAllText(path) : null));
            if (limit > 0 && problems.Count >= limit)
                break;
        }

        return problems;
    }

    /// <summary>
    /// The nonce of a problem's session: derived from the problem's hash, so that a session's
    /// transcript is the same bytes on every platform. (A controller draws it at random per
    /// process; a problem cannot print it anyway, echo being refused.)
    /// </summary>
    /// <param name="problem">The canonical problem.</param>
    /// <returns>A 26-character token.</returns>
    public static string Nonce(SmtProblem problem)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("sm0-nonce|" + problem.Hash));
        return "oc" + Convert.ToHexString(digest)[..24].ToLowerInvariant();
    }

    /// <summary>SHA-256 of a text as UTF-8, hex.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The digest.</returns>
    public static string Sha256(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    #endregion
}
