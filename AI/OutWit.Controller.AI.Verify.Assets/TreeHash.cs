using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// The hash that pins a folder of library files, computed exactly as the controller computes
/// it when it resolves a runtime (its VerifyRuntimeTreeHash): SHA-256 over every file under
/// the folder in ordinal order of its relative path ('/' between segments), each fed as the
/// path's UTF-8 bytes, a zero byte, the length as eight little-endian bytes, and the bytes.
/// </summary>
internal static class TreeHash
{
    #region Constants

    private const int BUFFER_SIZE = 1 << 16;

    #endregion

    #region Functions

    /// <summary>Computes the tree hash of a folder.</summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The hash (lower-case hex), the number of files and their bytes.</returns>
    public static (string Sha256, int Files, long Bytes) Compute(string directory)
    {
        var root = Path.GetFullPath(directory);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), Path: path))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .ToList();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BUFFER_SIZE];
        Span<byte> length = stackalloc byte[sizeof(long)];
        long bytes = 0;

        foreach (var (relative, path) in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE);
            BinaryPrimitives.WriteInt64LittleEndian(length, stream.Length);
            hash.AppendData(length);
            bytes += stream.Length;

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }

        return (Convert.ToHexStringLower(hash.GetHashAndReset()), files.Count, bytes);
    }

    #endregion
}
