using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// The hash that pins a folder of library files: SHA-256 over every file under the folder,
/// in ordinal order of its path relative to the folder ('/' between segments), each fed as
/// the path's UTF-8 bytes, a zero byte, the file's length as eight little-endian bytes, and
/// the file's bytes. It depends on the names and the contents only — not on the operating
/// system, the order the file system lists files in, or timestamps — so the asset builder
/// and every node compute the same value for the same tree.
/// </summary>
public static class VerifyRuntimeTreeHash
{
    #region Constants

    private const int BUFFER_SIZE = 1 << 16;

    #endregion

    #region Functions

    /// <summary>Computes the tree hash of a folder.</summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The hash, lower-case hex.</returns>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public static string Compute(string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Runtime tree not found at '{root}'.");

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), Path: path))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .ToList();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BUFFER_SIZE];
        Span<byte> length = stackalloc byte[sizeof(long)];

        foreach (var (relative, path) in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE);
            BinaryPrimitives.WriteInt64LittleEndian(length, stream.Length);
            hash.AppendData(length);

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    #endregion
}
