using System.Globalization;
using System.Security.Cryptography;
using OutWit.Controller.AI.Smt.Live.Server;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Commands;

/// <summary>
/// The problem set and the options of a preflight or a run: the set described (its rows, its
/// size, its SHA-256, the name the provenance gives it) and uploaded as a blob, the options
/// printed as the numbers the controller will use.
/// </summary>
internal static class SmtLiveInput
{
    #region Functions

    /// <summary>
    /// Describes a problem set and uploads it.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="path">The problem set.</param>
    /// <param name="output">Where the lines go.</param>
    /// <param name="ct">Cancels the upload.</param>
    /// <returns>The blob of the set.</returns>
    public static async Task<Guid> UploadAsync(ISmtLiveServer server, string path, TextWriter output, CancellationToken ct)
    {
        var (rows, bytes, sha256) = await DescribeAsync(path, ct);
        output.WriteLine($"input    : {Path.GetFileName(path)}, {rows} row(s), {bytes} byte(s), sha256 {sha256}");

        var blob = await server.UploadAsync(path, new SmtLiveProgress(output, "upload"), ct);
        output.WriteLine($"uploaded : blob {blob}");
        return blob;
    }

    /// <summary>
    /// Counts a problem set's rows (lines that are not blank, as its reader counts them) and
    /// hashes its bytes, reading it as a stream.
    /// </summary>
    /// <param name="path">The problem set.</param>
    /// <param name="ct">Cancels the reading.</param>
    /// <returns>The rows, the bytes and the SHA-256 in lower-case hex.</returns>
    public static async Task<(int Rows, long Bytes, string Sha256)> DescribeAsync(string path, CancellationToken ct)
    {
        var rows = 0;
        using (var reader = new StreamReader(path))
        {
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    rows++;
            }
        }

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return (rows, stream.Length, Convert.ToHexStringLower(hash));
    }

    /// <summary>
    /// The options as the controller will take them: a number that is not set is shown as its
    /// default, the resource limit as the profile's when it is not set.
    /// </summary>
    /// <param name="options">The options as given.</param>
    /// <returns>One line.</returns>
    public static string Describe(SmtOptionsData options)
    {
        var resolved = SmtOptionsRules.Resolve(options);
        var rlimit = resolved.ResourceLimit == 0 ? "the profile's" : resolved.ResourceLimit.ToString(CultureInfo.InvariantCulture);
        return $"query {SmtLiveArguments.WordOf(resolved.Query)}, sampling rate {resolved.SamplingRate.ToString("0.###", CultureInfo.InvariantCulture)}, " +
               $"rlimit {rlimit}, hard cap {resolved.HardCapSeconds} s, memory {resolved.MemoryMegabytes} MB, seed {resolved.Seed}, max models {resolved.MaxModels}";
    }

    #endregion
}
