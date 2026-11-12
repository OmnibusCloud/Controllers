using System.Formats.Tar;
using System.IO.Compression;

namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// Cuts the Ruby runtime out of the ruby.wasm release archive, unchanged: the interpreter
/// module, the standard library as the files Ruby loads (it has no way to load a library
/// from one archive), and the library folders of the bundled gems a program is given without
/// RubyGems. Everything else in the archive (headers, the static library, man pages, the gem
/// cache) stays out. The library tree is pinned by one hash over its names and contents.
/// </summary>
internal static class RubyRuntimeBuilder
{
    #region Constants

    private const string ARCHIVE_ROOT = "ruby-3.4-wasm32-unknown-wasip1-full/";
    private const string MODULE_ENTRY = "usr/local/bin/ruby";
    private const string LIBRARY_PREFIX = "usr/local/lib/ruby/" + AssetPins.RUBY_API_VERSION + "/";
    private const string GEMS_PREFIX = "usr/local/lib/ruby/gems/" + AssetPins.RUBY_API_VERSION + "/gems/";

    /// <summary>The module's name in the runtime folder.</summary>
    public const string MODULE_FILE = "ruby.wasm";

    /// <summary>
    /// The folder of the runtime folder that holds the library tree. It is the guest's whole
    /// file system (its "/"), so a program sees pinned files only: the module and the licence
    /// texts stay beside it, out of the guest's sight.
    /// </summary>
    public const string TREE_DIR = "root";

    private static readonly string[] GEM_LICENCE_FILES = ["LICENSE.txt", "COPYING", "BSDL"];

    #endregion

    #region Functions

    /// <summary>Writes the Ruby runtime folder from the release archive.</summary>
    /// <param name="archivePath">The pinned ruby.wasm release archive (tar.gz).</param>
    /// <param name="runtimeDir">The runtime folder to write; its library tree is replaced.</param>
    /// <returns>What was written.</returns>
    /// <exception cref="InvalidDataException">The archive does not hold the module.</exception>
    public static RubyRuntimeSummary Build(string archivePath, string runtimeDir)
    {
        Directory.CreateDirectory(runtimeDir);

        // A stale file would change the tree hash: the tree is always written from nothing.
        var treeDir = Path.Combine(runtimeDir, TREE_DIR);
        if (Directory.Exists(treeDir))
            Directory.Delete(treeDir, recursive: true);

        var moduleWritten = false;
        var libraryFiles = 0;
        var gemFiles = 0;
        var licences = new List<string>();

        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
                continue;

            var name = entry.Name.Replace('\\', '/');
            if (!name.StartsWith(ARCHIVE_ROOT, StringComparison.Ordinal))
                continue;

            var path = name[ARCHIVE_ROOT.Length..];

            if (path == MODULE_ENTRY)
            {
                Write(entry, Path.Combine(runtimeDir, MODULE_FILE));
                moduleWritten = true;
            }
            else if (path.StartsWith(LIBRARY_PREFIX, StringComparison.Ordinal))
            {
                Write(entry, Path.Combine(treeDir, Local(path)));
                libraryFiles++;
            }
            else if (path.StartsWith(GEMS_PREFIX, StringComparison.Ordinal))
            {
                var inGems = path[GEMS_PREFIX.Length..];
                var slash = inGems.IndexOf('/');
                if (slash <= 0)
                    continue;

                var gem = inGems[..slash];
                if (!AssetPins.RUBY_BUNDLED_GEMS.Contains(gem, StringComparer.Ordinal))
                    continue;

                var inGem = inGems[(slash + 1)..];
                if (inGem.StartsWith("lib/", StringComparison.Ordinal))
                {
                    Write(entry, Path.Combine(treeDir, Local(path)));
                    gemFiles++;
                }
                else if (GEM_LICENCE_FILES.Contains(inGem, StringComparer.Ordinal))
                {
                    var licence = $"LICENSE-{gem}-{inGem.Replace(".txt", "", StringComparison.Ordinal)}.txt";
                    Write(entry, Path.Combine(runtimeDir, licence));
                    licences.Add(licence);
                }
            }
        }

        if (!moduleWritten)
            throw new InvalidDataException($"'{ARCHIVE_ROOT}{MODULE_ENTRY}' is not in the archive.");

        licences.Sort(StringComparer.Ordinal);
        return new RubyRuntimeSummary(libraryFiles, gemFiles, licences);
    }

    private static string Local(string archivePath)
    {
        return archivePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private static void Write(TarEntry entry, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        entry.DataStream!.CopyTo(output);
    }

    #endregion
}
