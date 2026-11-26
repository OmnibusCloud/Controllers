using System.Buffers;
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
    #region Constants

    private const int READ_BUFFER_BYTES = 128 * 1024;
    private const byte LINE_FEED = 0x0A;
    private const byte CARRIAGE_RETURN = 0x0D;

    private static readonly byte[] BYTE_ORDER_MARK = [0xEF, 0xBB, 0xBF];

    #endregion

    #region Functions

    /// <summary>
    /// Describes a problem set and uploads it.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="path">The problem set.</param>
    /// <param name="output">Where the lines go.</param>
    /// <param name="ct">Cancels the upload.</param>
    /// <returns>The blob of the set, and the set's rows, bytes and SHA-256.</returns>
    public static async Task<(Guid Blob, int Rows, long Bytes, string Sha256)> UploadAsync(ISmtLiveServer server, string path, TextWriter output, CancellationToken ct)
    {
        var (rows, bytes, sha256) = await DescribeAsync(path, ct);
        output.WriteLine($"input    : {Path.GetFileName(path)}, {rows} row(s), {bytes} byte(s), sha256 {sha256}");

        var blob = await server.UploadAsync(path, new SmtLiveProgress(output, "upload"), ct);
        output.WriteLine($"uploaded : blob {blob}");
        return (blob, rows, bytes, sha256);
    }

    /// <summary>
    /// Counts a problem set's rows as the model package's reader counts them and hashes its
    /// bytes, in one pass over the file: a line ends at a line feed (a carriage return before it
    /// is part of the line end, one anywhere else part of the line), a line of nothing but
    /// spaces, tabs and carriage returns is blank and not a row, and a byte order mark before
    /// the first line is not part of it. Any other line is a row, taken or refused, so the count
    /// is the one the server reads.
    /// </summary>
    /// <param name="path">The problem set.</param>
    /// <param name="ct">Cancels the reading.</param>
    /// <returns>The rows, the bytes and the SHA-256 in lower-case hex.</returns>
    public static async Task<(int Rows, long Bytes, string Sha256)> DescribeAsync(string path, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(READ_BUFFER_BYTES);
        try
        {
            await using var stream = File.OpenRead(path);
            var rows = 0;
            var bytes = 0L;
            var filled = false;
            var start = true;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, READ_BUFFER_BYTES), ct)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                var from = 0;
                if (start && read >= BYTE_ORDER_MARK.Length && buffer.AsSpan(0, BYTE_ORDER_MARK.Length).SequenceEqual(BYTE_ORDER_MARK))
                    from = BYTE_ORDER_MARK.Length;

                start = false;
                bytes += read;
                foreach (var value in buffer.AsSpan(from, read - from))
                {
                    if (value == LINE_FEED)
                    {
                        rows += filled ? 1 : 0;
                        filled = false;
                    }
                    else if (value is not ((byte)' ' or (byte)'\t' or CARRIAGE_RETURN))
                    {
                        filled = true;
                    }
                }
            }

            rows += filled ? 1 : 0;
            return (rows, bytes, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
