using System.Formats.Tar;
using System.IO.Compression;

namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// Cuts the C/C++ runtime out of YoWASP's clang package: LLVM for WASI as one multicall module
/// (clang, wasm-ld, …), and the sysroot it expects at /usr (wasi-libc, libc++, libc++abi,
/// compiler-rt builtins, clang's own headers), unchanged. The sysroot is the guest's /usr while
/// a task compiles; it is pinned as a tree like Ruby's library.
/// </summary>
internal static class ClangRuntimeBuilder
{
    #region Constants

    private const string MODULE_ENTRY = "package/gen/llvm.core.wasm";
    private const string SYSROOT_ENTRY = "package/gen/llvm-resources.tar";

    public const string MODULE_FILE = "llvm.wasm";

    public const string TREE_DIR = "usr";

    #endregion

    #region Functions

    /// <summary>
    /// Writes the module and the sysroot tree into <paramref name="runtimeDir"/>.
    /// </summary>
    /// <param name="packagePath">The pinned npm package (.tgz).</param>
    /// <param name="runtimeDir">The runtime's folder in the set.</param>
    /// <returns>The number of files in the sysroot.</returns>
    /// <exception cref="InvalidDataException">The package lacks the module or the sysroot.</exception>
    public static int Build(string packagePath, string runtimeDir)
    {
        Directory.CreateDirectory(runtimeDir);

        // A stale file would change the tree hash: the tree is always written from nothing.
        var treeDir = Path.Combine(runtimeDir, TREE_DIR);
        if (Directory.Exists(treeDir))
            Directory.Delete(treeDir, recursive: true);

        var moduleWritten = false;
        var sysrootFiles = -1;

        using var file = File.OpenRead(packagePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
                continue;

            var name = entry.Name.Replace('\\', '/');
            if (name == MODULE_ENTRY)
            {
                Write(entry.DataStream, Path.Combine(runtimeDir, MODULE_FILE));
                moduleWritten = true;
            }
            else if (name == SYSROOT_ENTRY)
            {
                sysrootFiles = ExtractSysroot(entry.DataStream, treeDir);
            }
        }

        if (!moduleWritten)
            throw new InvalidDataException($"'{MODULE_ENTRY}' is not in the package.");
        if (sysrootFiles < 0)
            throw new InvalidDataException($"'{SYSROOT_ENTRY}' is not in the package.");

        return sysrootFiles;
    }

    private static int ExtractSysroot(Stream sysroot, string treeDir)
    {
        // The inner tar is read from the outer one's stream: copy it out first (TarReader reads forward only).
        using var buffer = new MemoryStream();
        sysroot.CopyTo(buffer);
        buffer.Position = 0;

        var files = 0;
        using var tar = new TarReader(buffer);
        while (tar.GetNextEntry() is { } entry)
        {
            // An empty file has no data stream, and an empty header is still a header to find.
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                continue;

            var name = entry.Name.Replace('\\', '/').TrimStart('/');
            if (name.Length == 0 || name.Split('/').Any(segment => segment is "" or "." or ".."))
                throw new InvalidDataException($"unexpected sysroot entry '{entry.Name}'");

            Write(entry.DataStream ?? Stream.Null, Path.Combine(treeDir, name.Replace('/', Path.DirectorySeparatorChar)));
            files++;
        }

        return files;
    }

    private static void Write(Stream source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(output);
    }

    #endregion
}
