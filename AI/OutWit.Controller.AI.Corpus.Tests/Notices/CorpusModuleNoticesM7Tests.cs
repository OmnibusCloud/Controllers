using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Tests.Docs;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Notices;

/// <summary>
/// Audit finding M-7: the module carried three libraries and third-party data and no licence
/// notice for any of them. The build now writes THIRD-PARTY-NOTICES.txt into the module, and
/// these tests hold what was built: the staged module is a pinned list of files, each one the
/// controller's own or answered for by the notices; every library is noticed with the version
/// and the source commit the library itself states, and with the hash of the very file staged;
/// every licence text is in the notices whole; the zip and the package carry what the module
/// carries. A file added to the module without a place in that list, a library moved without its
/// notice, an archive of an older build: each fails here.
/// </summary>
[TestFixture]
public sealed class CorpusModuleNoticesM7Tests
{
    #region Constants

    private const string CONTROLLER = "OutWit.Controller.AI.Corpus";
    private const string MODULE = "corpus.module";
    private const string NOTICES = "THIRD-PARTY-NOTICES.txt";
    private const string MANIFEST = "controller.json";

    /// <summary>The controller's own files: its assemblies and their dependency lists, its manifest, its licence, the notices it writes.</summary>
    private static readonly IReadOnlyList<string> OWN =
    [
        "LICENSE.txt",
        "OutWit.Controller.AI.Corpus.Model.deps.json",
        "OutWit.Controller.AI.Corpus.Model.dll",
        "OutWit.Controller.AI.Corpus.deps.json",
        "OutWit.Controller.AI.Corpus.dll",
        NOTICES,
        MANIFEST
    ];

    /// <summary>The libraries of others the module carries: the file, its heading in the notices, and the version and source commit the file states of itself.</summary>
    private static readonly IReadOnlyList<(string File, string Heading, string Version, string Commit)> LIBRARIES =
    [
        ("AngleSharp.dll", "AngleSharp 1.10.0 (MIT)", "1.10.0", "49d47510cbcdc91c1327527f7df9228749059476"),
        ("Wasmtime.Dotnet.dll", "Wasmtime.Dotnet 48.0.2 (Apache-2.0 WITH LLVM-exception)", "48.0.2", "d9db6b0005b93ba09a6ada8ddce2d5a3846511eb"),
        ("ZstdSharp.dll", "ZstdSharp.Port 0.8.8 (MIT)", "0.8.8", "2cd0c019693bc786a5fe5c3be94e107b24e7267e")
    ];

    /// <summary>The licence files of others that travel beside the notices, and where the repository keeps each.</summary>
    private static readonly IReadOnlyList<(string File, string[] Source)> LICENCES =
    [
        ("LICENSE-fineweb-2.txt", ["AI", $"{CONTROLLER}.Assets", "languages", "LICENSE-fineweb-2.txt"]),
        ("LICENSE-unicode.txt", ["AI", CONTROLLER, "LICENSE-unicode.txt"])
    ];

    /// <summary>What is noticed without a file of its own in the module: its heading in the notices.</summary>
    private static readonly IReadOnlyList<string> INSIDE =
    [
        "Zstandard 1.5.7 (BSD-3-Clause)",
        "FineWeb-2 stop-word lists and repetition thresholds (Apache-2.0)",
        "Unicode Character Database 16.0.0 (Unicode-3.0)"
    ];

    /// <summary>The licence texts the build composes the notices from, as the repository keeps them.</summary>
    private static readonly IReadOnlyList<string[]> TEXTS =
    [
        ["AI", CONTROLLER, "Notices", "texts", "AngleSharp-1.10.0-LICENSE.txt"],
        ["AI", CONTROLLER, "Notices", "texts", "ZstdSharp-0.8.8-LICENSE.txt"],
        ["AI", CONTROLLER, "Notices", "texts", "Zstandard-1.5.7-LICENSE.txt"],
        ["AI", CONTROLLER, "Notices", "texts", "Wasmtime.Dotnet-48.0.2-LICENSE.txt"],
        ["AI", $"{CONTROLLER}.Assets", "languages", "LICENSE-fineweb-2.txt"],
        ["AI", CONTROLLER, "LICENSE-unicode.txt"]
    ];

    #endregion

    #region Module Tests

    [Test]
    public void M7TheModuleIsAPinnedListOfFilesTest()
    {
        var pinned = OWN.Concat(LIBRARIES.Select(library => library.File)).Concat(LICENCES.Select(licence => licence.File)).Order(StringComparer.Ordinal).ToList();

        Assert.That(Files(Module), Is.EqualTo(pinned), "a file of the module that is not in the list is redistributed without anyone having answered for it: add it to the list as the controller's own, or with its notice");
    }

    [Test]
    public void M7EveryLibraryIsNoticedAsTheFileItIsTest()
    {
        var notices = Notices;

        Assert.Multiple(() =>
        {
            foreach (var (file, heading, version, commit) in LIBRARIES)
            {
                var path = Path.Combine(Module, file);
                var states = FileVersionInfo.GetVersionInfo(path).ProductVersion;

                Assert.That(states, Is.EqualTo($"{version}+{commit}"), $"{file} states its own version and the commit it was built from");
                Assert.That(notices, Does.Contain($"\n{heading}\n"), $"{file} has its section");
                Assert.That(notices, Does.Contain($" at {commit}\n"), $"{file}: the licence text is the one of the commit the file names");
                Assert.That(notices, Does.Contain($"{file}, as the package ships it (SHA-256 {Sha256(File.ReadAllBytes(path))})"), $"{file}: the notice is for the very file staged");
            }

            foreach (var heading in INSIDE)
                Assert.That(notices, Does.Contain($"\n{heading}\n"), heading);
        });
    }

    [Test]
    public void M7TheNoticesHoldEveryLicenceTextWholeTest()
    {
        var notices = Notices;

        Assert.Multiple(() =>
        {
            foreach (var parts in TEXTS)
                Assert.That(notices, Does.Contain(CorpusRepositoryFiles.ReadText(parts).Trim('\n')), parts[^1]);

            foreach (var (file, source) in LICENCES)
                Assert.That(ReadText(Path.Combine(Module, file)), Is.EqualTo(CorpusRepositoryFiles.ReadText(source)), $"{file} beside the notices is the repository's, with line feeds");

            Assert.That(ReadText(Path.Combine(Module, "LICENSE.txt")), Is.EqualTo(CorpusRepositoryFiles.ReadText("AI", CONTROLLER, "LICENSE")), "the controller's own licence travels in the module too");
            Assert.That(notices, Does.Not.Contain("\r"), "line feeds on every platform: the module's digest does not depend on a checkout");
            Assert.That(notices, Does.StartWith($"THIRD-PARTY NOTICES\n\n{CONTROLLER} {CorpusTestRun.VERSION} ({MODULE})"));
        });
    }

    [Test]
    public void M7TheNoticesNameEveryDataAssetOfTheManifestTest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Module, MANIFEST)));
        var assets = manifest.RootElement.GetProperty("dataAssets").EnumerateArray().Select(asset => asset.GetProperty("id").GetString()!).ToList();
        var notices = Notices;

        Assert.Multiple(() =>
        {
            Assert.That(assets, Is.Not.Empty);
            foreach (var asset in assets)
                Assert.That(notices, Does.Contain(asset), $"the data asset {asset} is not in the module; the notices say where its own notices are");
        });
    }

    #endregion

    #region Archive Tests

    [Test]
    public void M7TheZipCarriesWhatTheModuleCarriesTest()
    {
        var zip = CorpusRepositoryFiles.PathOf("@Zips", Configuration, "corpus.zip");
        Assert.That(File.Exists(zip), Is.True, $"the build packs the module into {zip}");

        using var archive = ZipFile.OpenRead(zip);

        AssertSameAsModule(archive.Entries.ToDictionary(entry => entry.FullName, entry => entry, StringComparer.Ordinal), "the zip");
    }

    [Test]
    public void M7ThePackageCarriesTheNoticesAndTheModuleTest()
    {
        if (!string.Equals(Configuration, "Release", StringComparison.Ordinal))
            Assert.Ignore("a build that is not a Release build makes no package");

        var package = CorpusRepositoryFiles.PathOf("AI", CONTROLLER, "bin", Configuration, $"{CONTROLLER}.{CorpusTestRun.VERSION}.nupkg");
        Assert.That(File.Exists(package), Is.True, $"a Release build packs {package}");

        using var archive = ZipFile.OpenRead(package);
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, entry => entry, StringComparer.Ordinal);
        const string prefix = "content/module/";

        Assert.Multiple(() =>
        {
            foreach (var root in new[] { "LICENSE", NOTICES, "LICENSE-fineweb-2.txt", "LICENSE-unicode.txt" })
                Assert.That(entries.Keys, Does.Contain(root), $"{root} in the package's root");

            Assert.That(Read(entries[NOTICES]), Is.EqualTo(File.ReadAllBytes(Path.Combine(Module, NOTICES))), "the notices of the root are the module's");
        });

        AssertSameAsModule(entries.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(entry => entry.Key[prefix.Length..], entry => entry.Value, StringComparer.Ordinal), "the package's content/module/");
    }

    #endregion

    #region Tools

    /// <summary>An archive's entries against the staged module: the same names, the same bytes.</summary>
    private static void AssertSameAsModule(Dictionary<string, ZipArchiveEntry> entries, string what)
    {
        var files = Files(Module);

        Assert.Multiple(() =>
        {
            Assert.That(entries.Keys.Order(StringComparer.Ordinal), Is.EqualTo(files), $"{what} holds the module's files and no other");
            foreach (var file in files.Where(entries.ContainsKey))
            {
                // The manifest is stamped with the time of every staging (a pack stages again): it is
                // held to what it says of the content, every other file to its bytes.
                if (file == MANIFEST)
                    Assert.That(ContentDigest(Read(entries[file])), Is.EqualTo(ContentDigest(File.ReadAllBytes(Path.Combine(Module, file)))), $"{what}: the manifest names the staged module's content");
                else
                    Assert.That(Sha256(Read(entries[file])), Is.EqualTo(Sha256(File.ReadAllBytes(Path.Combine(Module, file)))), $"{what}: {file} is the staged one (an archive of an older build would differ)");
            }
        });
    }

    /// <summary>The content digest and the version a manifest states: the identity of a module's build.</summary>
    private static string ContentDigest(byte[] manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        return $"{document.RootElement.GetProperty("version").GetString()} {document.RootElement.GetProperty("contentDigest").GetString()}";
    }

    private static List<string> Files(string folder)
    {
        return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string ReadText(string path)
    {
        return File.ReadAllText(path);
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    #endregion

    #region Properties

    /// <summary>This build's module, checked to be this build's and whole before anything reads it (<see cref="CorpusRigModules"/>).</summary>
    private static string Module => Path.Combine(CorpusRigModules.Folder, MODULE);

    private static string Notices => ReadText(Path.Combine(Module, NOTICES));

    /// <summary>The configuration this test assembly was built in: its module, zip and package are under it.</summary>
    private static string Configuration => typeof(CorpusModuleNoticesM7Tests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

    #endregion
}
