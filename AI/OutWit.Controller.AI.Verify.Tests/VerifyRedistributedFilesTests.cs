using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using OutWit.Controller.AI.Verify.Assets;
using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests;

/// <summary>
/// What the controller hands on of other people's work, file by file (audit finding V:M-9): the
/// module, each folder of the language runtimes and the folder of the native engine library are
/// held to a pinned list of files, and every file of a folder is named in the folder's
/// third-party notices. A new file that is redistributed without being put on its list and into
/// its notices fails here, by its name.
/// <para>
/// The notices themselves are committed files written by a tool from pinned sources
/// (toolchain/notices); the toolchain job holds them to what the tool writes. Here they are held
/// to the folders they travel in.
/// </para>
/// </summary>
[TestFixture]
public sealed class VerifyRedistributedFilesTests
{
    #region Constants

    private const string NOTICES = "THIRD-PARTY-NOTICES.txt";
    private const string NOTICES_TITLE = "THIRD-PARTY NOTICES";
    private const string PROJECT_FOLDER = "OutWit.Controller.AI.Verify";
    private const string PACKAGE_MODULE = "content/module/";
    private const string THIRD_PARTY_ASSEMBLY = "Wasmtime.Dotnet.dll";
    private const string RELEASE_SCHEME = "gh-release://";

    // The module: what the build stages and the package ships under content/module/.
    private static readonly string[] MODULE =
    [
        "OutWit.Controller.AI.Verify.Model.deps.json",
        "OutWit.Controller.AI.Verify.Model.dll",
        "OutWit.Controller.AI.Verify.deps.json",
        "OutWit.Controller.AI.Verify.dll",
        NOTICES,
        "Wasmtime.Dotnet.dll",
        "controller.json"
    ];

    // The language runtimes, folder by folder ("" is the top of the set). A name that ends with
    // a slash is a folder pinned as a whole by the runtime catalog (a tree hash), or the patches.
    private static readonly (string Folder, string[] Files)[] RUNTIMES =
    [
        ("", ["LICENSE-quickjs-ng.txt", "NOTICE.txt", NOTICES, "qjs-wasi.wasm"]),
        ("python-3.14.6", ["LICENSE", "LICENSE-mpmath.txt", "LICENSE-sympy.txt", NOTICES, "lib/python314.zip", "lib/sympy-1.14.0.zip", "python.wasm"]),
        ("python-3.14.6-numpy-2.5.4",
        [
            "BUILD.txt", "LICENSE-numpy.txt", "LICENSE-python.txt", "LICENSE-wasi-python.txt", NOTICES, "lib/numpy-2.5.4.zip", "lib/python314.zip",
            "patches/0001-npy-files-without-dup.patch", "patches/0002-callback-signatures.patch", "python-plain.wasm", "python.wasm"
        ]),
        ("ruby-3.4.1",
        [
            "LICENSE-base64-0.2.0-LICENSE.txt", "LICENSE-csv-3.3.2-LICENSE.txt", "LICENSE-matrix-0.4.2-LICENSE.txt", "LICENSE-prime-0.1.3-BSDL.txt",
            "LICENSE-prime-0.1.3-COPYING.txt", "LICENSE-ruby-BSDL.txt", "LICENSE-ruby.txt", "LICENSE-ruby.wasm.txt", NOTICES, "root/", "ruby.wasm"
        ]),
        ("lua-5.4.8", ["LICENSE-lua.txt", NOTICES, "lua.wasm"]),
        ("typescript-sucrase-3.35.1", ["LICENSE-sucrase.txt", NOTICES, "sucrase.js"]),
        ("clang-22.1.0",
        [
            "COPYING-musl-fts.txt", "COPYRIGHT-musl.txt", "LICENSE-cloudlibc.txt", "LICENSE-llvm.txt", "LICENSE-wasi-libc-APACHE-LLVM.txt",
            "LICENSE-wasi-libc-MIT.txt", "LICENSE-wasi-libc.txt", "LICENSE-yowasp-clang.txt", NOTICES, "llvm.wasm", "usr/"
        ])
    ];

    // Where the committed notices of a folder of the set lie under the asset tool's modules/.
    private static readonly (string Folder, string Committed)[] COMMITTED =
    [
        ("", "quickjs-0.15.1"),
        ("python-3.14.6", "python-3.14.6"),
        ("python-3.14.6-numpy-2.5.4", "python-3.14.6-numpy-2.5.4"),
        ("ruby-3.4.1", "ruby-3.4.1"),
        ("lua-5.4.8", "lua-5.4.8"),
        ("typescript-sucrase-3.35.1", "typescript-sucrase-3.35.1"),
        ("clang-22.1.0", "clang-22.1.0")
    ];

    // The native engine library's folder of each platform.
    private static readonly (string Platform, string Library)[] NATIVES =
    [
        ("win-x64", "wasmtime.dll"),
        ("linux-x64", "libwasmtime.so"),
        ("osx-arm64", "libwasmtime.dylib")
    ];

    private static readonly string[] NATIVE_COMPANIONS = ["LICENSE-wasmtime.txt", "NOTICE.txt", NOTICES];

    #endregion

    #region Module Tests

    [Test]
    public void M9TheStagedModuleHoldsThePinnedFilesTest()
    {
        var module = StagedModule();

        // Its own files: what an earlier run left in a folder of it is not the module (the
        // package test below is where such a folder would be noticed).
        var files = Directory.GetFiles(module).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();

        Assert.That(files, Is.EqualTo(MODULE.Order(StringComparer.Ordinal)),
            "the module's files are not the pinned ones: a file that is added goes onto the list here and, when it is another's work, into the module's THIRD-PARTY-NOTICES.txt");
    }

    [Test]
    public void M9TheModulesNoticesNameWhatItCarriesOfOthersTest()
    {
        var module = StagedModule();
        var notices = File.ReadAllText(Path.Combine(module, NOTICES));
        var foreign = MODULE.Where(file => file.EndsWith(".dll", StringComparison.Ordinal) && !file.StartsWith("OutWit.", StringComparison.Ordinal)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(foreign, Is.EqualTo(new[] { THIRD_PARTY_ASSEMBLY }), "the one assembly of the module that is not the controller's own");
            Assert.That(notices, Does.StartWith(NOTICES_TITLE));
            foreach (var assembly in foreign)
                Assert.That(notices, Does.Contain(assembly), $"the module's notices do not name {assembly}");

            Assert.That(File.ReadAllBytes(Path.Combine(module, NOTICES)), Is.EqualTo(File.ReadAllBytes(Path.Combine(SolutionRoot(), "AI", PROJECT_FOLDER, NOTICES))),
                "the staged notices are the committed file, byte for byte");
        });
    }

    [Test]
    public void M9ThePackageShipsThePinnedFilesOfTheModuleTest()
    {
        // The package is made by a Release build; what it ships under content/module/ is what a
        // server unpacks and a node is sent. A folder left in the staged module by an earlier
        // run (natives, runtimes) would be packed with it.
        var project = Path.Combine(SolutionRoot(), "AI", PROJECT_FOLDER);
        var version = (string?)XDocument.Load(Path.Combine(project, PROJECT_FOLDER + ".csproj")).Descendants("Version").FirstOrDefault();
        var package = Path.Combine(project, "bin", VerifyRigModules.Configuration, $"{PROJECT_FOLDER}.{version}.nupkg");
        if (!File.Exists(package))
        {
            if (string.Equals(VerifyRigModules.Configuration, "Release", StringComparison.Ordinal))
                Assert.Fail($"a Release build makes the package, and there is none at '{package}'");

            Assert.Ignore("the package is made by a Release build: nothing to hold in this configuration");
        }

        using var zip = ZipFile.OpenRead(package);
        var shipped = zip.Entries
            .Where(entry => entry.FullName.StartsWith(PACKAGE_MODULE, StringComparison.Ordinal) && !entry.FullName.EndsWith('/'))
            .Select(entry => Uri.UnescapeDataString(entry.FullName[PACKAGE_MODULE.Length..]))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(shipped, Is.EqualTo(MODULE.Order(StringComparer.Ordinal)),
            "the package does not ship exactly the module's pinned files. A file under a folder (runtimes/, wasm/) was left in the staged module " +
            "by an earlier run and packed with it: delete that folder of the staged module and build again");

        using var packed = zip.GetEntry(PACKAGE_MODULE + NOTICES)!.Open();
        Assert.That(Sha256(packed), Is.EqualTo(Sha256(Path.Combine(project, NOTICES))), "the package ships the committed notices");
    }

    #endregion

    #region Runtime Tests

    [Test]
    public void M9EachRuntimeFolderHoldsThePinnedFilesTest()
    {
        var root = RuntimesRoot();

        Assert.Multiple(() =>
        {
            var folders = Directory.GetDirectories(root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            Assert.That(folders, Is.EqualTo(RUNTIMES.Select(runtime => runtime.Folder).Where(folder => folder.Length > 0).Order(StringComparer.Ordinal)),
                "the folders of the runtime set are not the pinned ones");

            foreach (var (folder, files) in RUNTIMES)
            {
                Assert.That(Listed(Path.Combine(root, folder), files, top: folder.Length == 0), Is.EqualTo(files.Order(StringComparer.Ordinal)),
                    $"'{(folder.Length == 0 ? "the top of the set" : folder)}' does not hold exactly its pinned files: a file that is added goes onto the list here and into the folder's {NOTICES}");
            }
        });
    }

    [Test]
    public void M9EveryFileOfARuntimeFolderIsNamedInItsNoticesTest()
    {
        var root = RuntimesRoot();

        Assert.Multiple(() =>
        {
            foreach (var (folder, files) in RUNTIMES)
            {
                var notices = File.ReadAllText(Path.Combine(root, folder, NOTICES));
                Assert.That(notices, Does.StartWith(NOTICES_TITLE), folder);

                foreach (var name in Named(folder, files))
                    Assert.That(notices, Does.Contain(name), $"the notices of '{folder}' do not name {name}");
            }
        });
    }

    [Test]
    public void M9TheNoticesOfARuntimeFolderAreTheCommittedOnesTest()
    {
        var root = RuntimesRoot();
        var modules = Path.Combine(SolutionRoot(), "AI", "OutWit.Controller.AI.Verify.Assets", "modules");

        Assert.Multiple(() =>
        {
            Assert.That(COMMITTED.Select(pair => pair.Folder), Is.EquivalentTo(RUNTIMES.Select(runtime => runtime.Folder)), "every folder of the set has committed notices");

            foreach (var (folder, committed) in COMMITTED)
            {
                var bytes = File.ReadAllBytes(Path.Combine(modules, committed, NOTICES));

                Assert.That(File.ReadAllBytes(Path.Combine(root, folder, NOTICES)), Is.EqualTo(bytes), $"the notices in '{folder}' are not the committed file of {committed}");
                Assert.That(bytes, Does.Not.Contain((byte)'\r'), $"{committed}: the notices are kept with LF line ends on every checkout, or their bytes differ between machines");
            }
        });
    }

    #endregion

    #region Native Library Tests

    [Test]
    public void M9TheNativeLibrarysFolderHoldsThePinnedFilesTest()
    {
        var natives = Path.Combine(SolutionRoot(), "AI", "OutWit.Controller.AI.Verify.Tests", "@Data", "natives");
        if (!Directory.Exists(natives))
            VerifyRigModules.Missing($"the pinned engine libraries are not laid out under '{natives}' (toolchain/natives.sh)");

        var committed = File.ReadAllBytes(Path.Combine(SolutionRoot(), "AI", "OutWit.Controller.AI.Verify.Assets", "modules", "wasmtime-48.0.8", NOTICES));

        Assert.Multiple(() =>
        {
            foreach (var (platform, library) in NATIVES)
            {
                var folder = Path.Combine(natives, platform);
                var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(folder, path)).Order(StringComparer.Ordinal).ToArray();
                var notices = File.ReadAllText(Path.Combine(folder, NOTICES));

                Assert.That(files, Is.EqualTo(NATIVE_COMPANIONS.Append(library).Order(StringComparer.Ordinal)), $"the folder of the {platform} library does not hold exactly its pinned files");
                Assert.That(File.ReadAllBytes(Path.Combine(folder, NOTICES)), Is.EqualTo(committed), $"{platform}: the notices are the committed file");
                Assert.That(notices, Does.Contain(library), $"{platform}: the notices do not name {library}");
                Assert.That(notices, Does.Contain("LICENSE-wasmtime.txt").And.Contain("NOTICE.txt"), platform);
            }
        });
    }

    #endregion

    #region Pin Tests

    [Test]
    public void M8TheNumpyArchiveHasOnePinTest()
    {
        // The asset tool lays the numpy runtime out from the archive it pins, and the catalog
        // pins the files inside; the project file pins the archive a node is sent. Were the two
        // archive pins to part, the tests would run on one archive and a node on another.
        var asset = XDocument.Load(Path.Combine(SolutionRoot(), "AI", PROJECT_FOLDER, PROJECT_FOLDER + ".csproj"))
            .Descendants("ControllerDataAsset").Single(item => (string?)item.Attribute("Include") == "verify-python-numpy");
        var uri = (string?)asset.Element("Uri") ?? "";

        Assert.Multiple(() =>
        {
            Assert.That((string?)asset.Element("Sha256"), Is.EqualTo(AssetPins.PYTHON_NUMPY.Sha256), "the project file and the asset tool pin one numpy archive");
            Assert.That(uri, Does.StartWith(RELEASE_SCHEME));

            // gh-release://<owner>/<repository>/<tag>/<file> is https://github.com/<owner>/<repository>/releases/download/<tag>/<file>.
            var parts = uri[RELEASE_SCHEME.Length..].Split('/');
            Assert.That(AssetPins.PYTHON_NUMPY.Url, Is.EqualTo($"https://github.com/{parts[0]}/{parts[1]}/releases/download/{parts[2]}/{parts[3]}"), "from one release");
        });
    }

    #endregion

    #region Tools

    /// <summary>The files of a folder as its list names them: plain files by their relative name, a folder pinned as a whole by its name and a slash.</summary>
    private static string[] Listed(string folder, string[] pinned, bool top)
    {
        var whole = pinned.Where(name => name.EndsWith('/')).Select(name => name.TrimEnd('/')).ToHashSet(StringComparer.Ordinal);
        var names = new List<string>();

        foreach (var file in Directory.GetFiles(folder, "*", top ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            var first = relative.Split('/')[0];
            if (!whole.Contains(first))
                names.Add(relative);
        }

        foreach (var directory in Directory.GetDirectories(folder).Select(Path.GetFileName))
        {
            if (!top && whole.Contains(directory!))
                names.Add(directory + "/");
        }

        return names.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>What the notices of a folder must name: every file of it but the notices themselves and the set's own record, the patches by their folder.</summary>
    private static IEnumerable<string> Named(string folder, string[] files)
    {
        return files
            .Where(file => file != NOTICES && !(folder.Length == 0 && file == "NOTICE.txt"))
            .Select(file => file.StartsWith("patches/", StringComparison.Ordinal) ? "patches/" : file)
            .Distinct(StringComparer.Ordinal);
    }

    private static string StagedModule()
    {
        var staged = VerifyRigModules.StagedRoot();
        var module = staged == null ? null : Path.Combine(staged, VerifyRigModules.VERIFY);
        if (module == null || !File.Exists(Path.Combine(module, "controller.json")))
            VerifyRigModules.Missing($"no staged module at '{module}'");

        return module;
    }

    private static string RuntimesRoot()
    {
        var root = Path.Combine(SolutionRoot(), "AI", "OutWit.Controller.AI.Verify.Tests", "@Data", "runtimes");
        if (!File.Exists(Path.Combine(root, "NOTICE.txt")))
            VerifyRigModules.Missing($"the runtime set is not built under '{root}' (verify-assets build @Data/runtimes --clean)");

        return root;
    }

    private static string SolutionRoot()
    {
        var root = VerifyRigModules.SolutionRoot();
        if (root == null)
            VerifyRigModules.Missing("Solution root not found");

        return root;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Sha256(stream);
    }

    private static string Sha256(Stream stream)
    {
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    #endregion
}
