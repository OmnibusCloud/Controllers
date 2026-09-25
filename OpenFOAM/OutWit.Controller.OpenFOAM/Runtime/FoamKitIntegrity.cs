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

    /// <summary>Attempts at reading a sampled file another process holds locked, before the lock is a finding.</summary>
    public const int READ_ATTEMPTS = 4;

    /// <summary>Wait between two attempts at a locked file.</summary>
    public static readonly TimeSpan READ_RETRY_DELAY = TimeSpan.FromMilliseconds(250);

    private const int ERROR_SHARING_VIOLATION = 32;

    private const int ERROR_LOCK_VIOLATION = 33;

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
    /// <param name="mutablePaths">Relative paths the controller itself rewrites after unpacking (the Windows Pstream); never sampled.</param>
    /// <returns>Findings, one per missing, altered or unreadable file; empty when the sample is intact. A kit without a BUILDINFO is one finding.</returns>
    public static IReadOnlyList<string> Check(string kitRoot, int sampleSize = SAMPLE_SIZE, IReadOnlyCollection<string>? mutablePaths = null)
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

        var excluded = new HashSet<string>(mutablePaths ?? [], StringComparer.Ordinal);
        var candidates = listed.Where(entry => !excluded.Contains(entry.RelativePath)).ToList();

        foreach (var (relativePath, expected) in Sample(candidates, sampleSize))
        {
            var path = Path.Combine(kitRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                findings.Add($"{relativePath}: missing from the kit.");
                continue;
            }

            string actual;
            try
            {
                actual = HashWithRetry(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A file still locked after the retries, or unreadable, is a
                // finding, not an infrastructure failure: the node says which
                // file, and the next resolution tries again.
                findings.Add($"{relativePath}: could not be read ({e.Message}).");
                continue;
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

    /// <summary>
    /// The SHA-256 of a file, read again after a short wait while another
    /// process holds it locked: an antivirus scanning freshly unpacked
    /// binaries holds each one for a moment, and a kit is not altered because
    /// it is being scanned.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The lowercase hex hash.</returns>
    /// <exception cref="IOException">The file stayed locked through every attempt, or could not be read.</exception>
    private static string HashWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
            catch (IOException e) when (attempt < READ_ATTEMPTS && IsLockViolation(e))
            {
                Thread.Sleep(READ_RETRY_DELAY);
            }
        }
    }

    private static bool IsLockViolation(IOException e)
    {
        // The Win32 code sits in the HRESULT's low word (0x80070020, 0x80070021).
        var code = e.HResult & 0xFFFF;
        return code is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION;
    }

    #endregion
}
