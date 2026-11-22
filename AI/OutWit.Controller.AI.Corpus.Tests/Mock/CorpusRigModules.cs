using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The modules the Corpus rigs run: a named set, checked and staged into a folder of the rig's
/// own, so that the engine loads these and nothing else.
/// <para>
/// The engine loads every module it finds in the folder it is given. The folder the builds stage
/// into (<c>@Controllers/&lt;configuration&gt;</c>) is shared by every controller of the
/// repository: a module another build left there would be loaded too, and a module left by an
/// older build would be tested in place of the new one, neither with a word. So a rig never gives
/// the engine that folder. It copies the modules it names, each only when
/// </para>
/// <list type="bullet">
/// <item>it is there (a missing module fails the rig: it is not skipped);</item>
/// <item>its files are the ones its own manifest was stamped over (the build's content digest:
/// a folder mixed from two builds, or with a file left from another, does not pass);</item>
/// <item>its assembly is the one this build made: for the controller under test the very
/// assembly the tests run against, for the others the output of their project in this tree.</item>
/// </list>
/// A module that fails one of these fails the rig by name.
/// </summary>
internal static class CorpusRigModules
{
    #region Constants

    /// <summary>The modules the Corpus rigs load: the controller under test and what its scripts call (Grid.ForEach, the typed variables, the flow constructs).</summary>
    public static readonly IReadOnlyList<string> NAMES = ["corpus.module", "grid.module", "special.module", "variables.module"];

    private const string MANIFEST = "controller.json";
    private const string DIGEST_PREFIX = "sha256:";
    private const string EXCLUDED_FOLDER = "Resources/";

    /// <summary>The assembly each module is built around, and the project that builds it (from the solution root).</summary>
    private static readonly IReadOnlyDictionary<string, (string Assembly, string Project)> BUILT = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["corpus.module"] = ("OutWit.Controller.AI.Corpus.dll", "AI/OutWit.Controller.AI.Corpus"),
        ["grid.module"] = ("OutWit.Controller.Grid.dll", "Grid/OutWit.Controller.Grid"),
        ["special.module"] = ("OutWit.Controller.Special.dll", "Special/OutWit.Controller.Special"),
        ["variables.module"] = ("OutWit.Controller.Variables.dll", "Variables/OutWit.Controller.Variables")
    };

    private static readonly Lazy<string> STAGED = new(StageForThisBuild);

    #endregion

    #region Functions

    /// <summary>Checks the named modules of a source folder and copies them, and nothing else, into a destination folder that is emptied first.</summary>
    /// <param name="source">The folder the builds staged their modules into.</param>
    /// <param name="destination">The rig's own folder.</param>
    /// <param name="names">The modules the rig loads.</param>
    /// <param name="expected">For a module, the file its assembly must be equal to, byte for byte (the build under test); a module without an entry is held to its own manifest only.</param>
    /// <returns>The destination folder.</returns>
    /// <exception cref="InvalidOperationException">A module is missing, mixed, or not the expected build's.</exception>
    public static string Stage(string source, string destination, IReadOnlyList<string> names, IReadOnlyDictionary<string, string> expected)
    {
        foreach (var name in names)
        {
            var problem = Problem(Path.Combine(source, name), expected.GetValueOrDefault(name));
            if (problem != null)
                throw new InvalidOperationException($"The rig cannot load {name}: {problem} Rebuild the test project (it builds the modules it loads).");
        }

        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);

        foreach (var name in names)
            Copy(Path.Combine(source, name), Path.Combine(destination, name));

        return destination;
    }

    /// <summary>Why a module folder cannot be loaded by a rig, or null when it can.</summary>
    /// <param name="module">The module's folder.</param>
    /// <param name="expectedAssembly">The file the module's assembly must equal, or null for no such check.</param>
    /// <returns>The reason, or null.</returns>
    public static string? Problem(string module, string? expectedAssembly)
    {
        var manifest = Path.Combine(module, MANIFEST);
        if (!Directory.Exists(module) || !File.Exists(manifest))
            return "the module is not staged.";

        string? stamped;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
            stamped = document.RootElement.TryGetProperty("contentDigest", out var digest) ? digest.GetString() : null;
        }
        catch (JsonException)
        {
            return "its manifest cannot be read.";
        }

        if (!string.Equals(stamped, Digest(module), StringComparison.Ordinal))
            return "its files are not the ones its manifest was stamped over (a folder mixed from two builds, or a file left from another).";

        if (expectedAssembly == null)
            return null;

        var staged = Path.Combine(module, Path.GetFileName(expectedAssembly));
        if (!File.Exists(staged) || !File.Exists(expectedAssembly) || !Hash(staged).AsSpan().SequenceEqual(Hash(expectedAssembly)))
            return $"its {Path.GetFileName(expectedAssembly)} is not the one this build made (a module of another build).";

        return null;
    }

    /// <summary>
    /// The content digest of a module folder, by the build's own definition
    /// (<c>Build/OutWit.Controller.BuildTasks.cs</c>): every file except the manifest and
    /// <c>Resources/</c>, ordered by relative path (forward slashes, ordinal); SHA-256 over, per
    /// file, the UTF-8 path, a line feed, the 32 bytes of the file's SHA-256 and a line feed.
    /// </summary>
    /// <param name="module">The module's folder.</param>
    /// <returns>"sha256:" and the lower-case hex digest.</returns>
    public static string Digest(string module)
    {
        var root = Path.GetFullPath(module).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (Relative: path[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'), Path: path))
            .Where(file => file.Relative != MANIFEST && !file.Relative.StartsWith(EXCLUDED_FOLDER, StringComparison.Ordinal))
            .OrderBy(file => file.Relative, StringComparer.Ordinal);

        using var total = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            total.AppendData(Encoding.UTF8.GetBytes(file.Relative));
            total.AppendData([0x0A]);
            total.AppendData(Hash(file.Path));
            total.AppendData([0x0A]);
        }

        return DIGEST_PREFIX + Convert.ToHexStringLower(total.GetHashAndReset());
    }

    #endregion

    #region Tools

    private static string StageForThisBuild()
    {
        var root = SolutionRoot() ?? throw new InvalidOperationException("The rig cannot find the solution root (OutWit.slnx) above the test folder.");
        var configuration = Configuration();
        var framework = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Name;
        var source = Path.Combine(root, "@Controllers", configuration);

        // The controller under test is held to the very assemblies the tests run against; the
        // others to what their projects built in this tree, in this configuration.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in NAMES)
        {
            var (assembly, project) = BUILT[name];
            expected[name] = name == NAMES[0]
                ? typeof(WitControllerCorpusModule).Assembly.Location
                : Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar), "bin", configuration, framework, assembly);
        }

        var staged = Stage(source, Path.Combine(TestContext.CurrentContext.TestDirectory, "@Rig"), NAMES, expected);

        // The model travels in the controller's module as its own assembly: the same one too.
        var model = Path.Combine(staged, NAMES[0], Path.GetFileName(typeof(CorpusTaskData).Assembly.Location));
        if (!File.Exists(model) || !Hash(model).AsSpan().SequenceEqual(Hash(typeof(CorpusTaskData).Assembly.Location)))
            throw new InvalidOperationException($"The rig cannot load {NAMES[0]}: its model assembly is not the one this build made. Rebuild the test project.");

        return staged;
    }

    private static void Copy(string source, string destination)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static byte[] Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA256.HashData(stream);
    }

    private static string Configuration()
    {
        // The configuration this test assembly was built in (Release in CI): its modules are staged under it.
        return typeof(CorpusRigModules).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .OfType<System.Reflection.AssemblyConfigurationAttribute>()
            .FirstOrDefault()?.Configuration ?? "Debug";
    }

    private static string? SolutionRoot()
    {
        var directory = TestContext.CurrentContext.TestDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "OutWit.slnx")))
                return directory;

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    #endregion

    #region Properties

    /// <summary>The folder the rigs of this test run load their modules from: staged once, from this build's modules, after every check. A module that is missing, mixed or not this build's fails the rig by name.</summary>
    public static string Folder => STAGED.Value;

    #endregion
}
