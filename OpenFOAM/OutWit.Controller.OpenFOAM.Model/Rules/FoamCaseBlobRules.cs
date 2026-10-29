using System.Security.Cryptography;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// The rule a base file's bytes obey where they are read: a blob holds the
/// bytes the task describes it with - the size and the SHA-256 the initiator
/// measured when it read the case - or the variant is refused. A case the
/// user edited between the reading that made the study and the upload, a
/// blob stored under another file's id, a transfer that lost bytes: each
/// would run as the case the task describes while being another, so the
/// node checks every blob before it lands in the case, and a host with the
/// blobs in hand may check them once for the whole study.
/// </summary>
public static class FoamCaseBlobRules
{
    #region Constants

    /// <summary>The read buffer of the digest: one sequential pass over the blob, 64 KiB at a time.</summary>
    private const int BUFFER_SIZE = 64 * 1024;

    #endregion

    #region Functions

    /// <summary>
    /// Checks a blob against the file reference that names it.
    /// </summary>
    /// <param name="file">The file reference, with the size and the SHA-256 (lower-case hex) the task describes.</param>
    /// <param name="path">The blob's local path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A finding when the bytes are not the described ones; null when they are.</returns>
    /// <exception cref="IOException">The blob cannot be read.</exception>
    public static async Task<string?> ValidateAsync(FoamFileRefData file, string path, CancellationToken cancellationToken = default)
    {
        var (size, sha256) = await DescribeAsync(path, cancellationToken);
        if (size == file.Size && string.Equals(sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            return null;

        return $"{file.RelativePath}: the blob's bytes are not the ones the task describes (size {size}, sha256 {sha256}; expected size {file.Size}, sha256 {file.Sha256}) - the case changed after it was read; open it again and submit it again.";
    }

    /// <summary>
    /// The size and the SHA-256 of a file, as a file reference describes them.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The size in bytes and the digest in lower-case hex.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static async Task<(long Size, string Sha256)> DescribeAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var sha256 = await SHA256.HashDataAsync(stream, cancellationToken);
        return (stream.Length, Convert.ToHexStringLower(sha256));
    }

    #endregion
}
