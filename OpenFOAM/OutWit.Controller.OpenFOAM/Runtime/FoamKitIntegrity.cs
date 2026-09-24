using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// A spot check of an unpacked kit against its own BUILDINFO.txt, which
/// lists the SHA-256 of every file the pack step wrote. A kit that was
/// unpacked short of a file, or whose library was clobbered by an antivirus
/// or a half-finished update, fails strangely in the middle of a case; the
/// check makes it fail before the first case, naming the file. A sample, not
/// the whole kit: hashing 500 MB on every node start is not the cost this
/// is worth, and a truncated or tampered kit shows up in a sample of a few
/// dozen files spread over the list.
/// </summary>
public static class FoamKitIntegrity
{
    #region Constants

    /// <summary>The file the pack step writes at the kit root.</summary>
    public const string BUILDINFO = "BUILDINFO.txt";

    /// <summary>How many files of the list are hashed, spread evenly over it.</summary>
    public const int SAMPLE_SIZE = 32;

    /// <summary>Files always hashed when present: the ones every run depends on.</summary>
    private static readonly IReadOnlyList<string> ALWAYS =
    [
        "KIT.env",
        "OpenFOAM-v2606/etc/controlDict"
    ];

    private static readonly Regex LINE = new(@"^([0-9a-fA-F]{64})  (.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    #endregion

    #region Functions

    /// <summary>
    /// Checks a sample of the kit's files against BUILDINFO.txt.
    /// </summary>
    /// <param name="kitRoot">The kit folder.</param>
    /// <param name="sampleSize">How many listed files to hash besides the ones always checked.</param>
    /// <returns>Findings, one per missing or altered file; empty when the sample is intact. A kit without a BUILDINFO is one finding.</returns>
    public static IReadOnlyList<string> Check(string kitRoot, int sampleSize = SAMPLE_SIZE)
    {
        var findings = new List<string>();
        var manifest = Path.Combine(kitRoot, BUILDINFO);
        if (!File.Exists(manifest))
        {
            findings.Add($"The kit carries no {BUILDINFO}.");
            return findings;
        }

        var listed = Parse(File.ReadAllLines(manifest));
        if (listed.Count == 0)
        {
            findings.Add($"{BUILDINFO} lists no files.");
            return findings;
        }

        foreach (var (relativePath, expected) in Sample(listed, sampleSize))
        {
            var path = Path.Combine(kitRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                findings.Add($"{relativePath}: missing from the kit.");
                continue;
            }

            string actual;
            using (var stream = File.OpenRead(path))
            {
                actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                findings.Add($"{relativePath}: content differs from the kit's {BUILDINFO} (the file was altered or truncated).");
        }

        return findings;
    }

    /// <summary>
    /// The hash lines of a BUILDINFO: "&lt;sha256&gt;  &lt;relative path&gt;", in order.
    /// </summary>
    /// <param name="lines">The file's lines.</param>
    /// <returns>Relative path and expected hash, in file order.</returns>
    public static IReadOnlyList<(string RelativePath, string Sha256)> Parse(IEnumerable<string> lines)
    {
        var entries = new List<(string, string)>();
        foreach (var line in lines)
        {
            var match = LINE.Match(line.TrimEnd());
            if (match.Success)
                entries.Add((match.Groups[2].Value, match.Groups[1].Value));
        }

        return entries;
    }

    /// <summary>
    /// The files to hash: the always-checked ones that are listed, plus an
    /// evenly spread sample of the rest. Deterministic, so two checks of the
    /// same kit look at the same files.
    /// </summary>
    /// <param name="listed">Every listed file.</param>
    /// <param name="sampleSize">Sample size besides the always-checked files.</param>
    /// <returns>The files to check.</returns>
    public static IReadOnlyList<(string RelativePath, string Sha256)> Sample(IReadOnlyList<(string RelativePath, string Sha256)> listed, int sampleSize)
    {
        var chosen = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in listed)
        {
            if (ALWAYS.Contains(entry.RelativePath, StringComparer.Ordinal) && seen.Add(entry.RelativePath))
                chosen.Add(entry);
        }

        if (sampleSize <= 0 || listed.Count == 0)
            return chosen;

        var step = Math.Max(1.0, listed.Count / (double)sampleSize);
        for (var position = 0.0; position < listed.Count && chosen.Count < sampleSize + ALWAYS.Count; position += step)
        {
            var entry = listed[(int)position];
            if (seen.Add(entry.RelativePath))
                chosen.Add(entry);
        }

        return chosen;
    }

    #endregion
}
