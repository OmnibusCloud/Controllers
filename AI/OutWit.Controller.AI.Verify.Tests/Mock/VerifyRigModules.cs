using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// The controller modules a test rig loads: a named set, laid out in a folder of the rig's own,
/// each checked to be what this build made.
/// <para>
/// Every controller of the repository stages its module into one folder
/// (<c>@Controllers/&lt;configuration&gt;</c> at the solution root), and a rig that loaded that
/// folder loaded whatever lay there: a module of another controller (a staged
/// <c>corpus.module</c> failed every Verify fixture's set-up for a service it wanted and the rig
/// did not have), or a module left by an earlier build, tested in place of the code under test.
/// A rig now names the modules it needs; only those are copied, and a module whose assembly is
/// not byte for byte the one this build produced is a failure that names it, never a silent pick.
/// </para>
/// </summary>
internal static class VerifyRigModules
{
    #region Constants

    /// <summary>The Verify controller's module.</summary>
    public const string VERIFY = "verify.module";

    /// <summary>The module Verify depends on.</summary>
    public const string VARIABLES = "variables.module";

    /// <summary>The module of Grid.ForEach and Grid.Replicas, which the bundled scripts use.</summary>
    public const string GRID = "grid.module";

    /// <summary>The module of the flow constructs the bundled scripts use.</summary>
    public const string SPECIAL = "special.module";

    /// <summary>What the bundled scripts need to run through the engine, and nothing else.</summary>
    public static readonly IReadOnlyList<string> SCRIPTS = [VERIFY, VARIABLES, GRID, SPECIAL];

    /// <summary>What a host needs to load the Verify controller alone.</summary>
    public static readonly IReadOnlyList<string> CONTROLLER = [VERIFY, VARIABLES];

    private const string MANIFEST = "controller.json";
    private const string ASSEMBLY_NAME = "assemblyName";
    private const string RIG_FOLDER = "@Rig";
    private const string STAGED_FOLDER = "@Controllers";
    private const string SOLUTION_FILE = "OutWit.slnx";
    private const string CI_ENV = "GITHUB_ACTIONS";

    // Where each module's project is, from the solution root: its build output is what the module must hold.
    private static readonly IReadOnlyDictionary<string, string> PROJECTS = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [VERIFY] = Path.Combine("AI", "OutWit.Controller.AI.Verify"),
        [VARIABLES] = Path.Combine("Variables", "OutWit.Controller.Variables"),
        [GRID] = Path.Combine("Grid", "OutWit.Controller.Grid"),
        [SPECIAL] = Path.Combine("Special", "OutWit.Controller.Special")
    };

    private static readonly Lazy<string> SCRIPT_RIG = new(() => Stage("scripts", SCRIPTS));

    #endregion

    #region Functions

    /// <summary>
    /// The folder of the script rig's modules, laid out once a test process. Ignores the fixture
    /// locally when the build staged no modules; on the build server that is a failure.
    /// </summary>
    /// <returns>The folder to give the engine.</returns>
    public static string ScriptRig()
    {
        return SCRIPT_RIG.Value;
    }

    /// <summary>
    /// Lays out a folder of the rig's own under the test output with exactly the named modules of
    /// this build.
    /// </summary>
    /// <param name="name">The folder's name; an earlier folder of that name is replaced.</param>
    /// <param name="modules">The modules to load.</param>
    /// <returns>The folder.</returns>
    public static string Stage(string name, IReadOnlyList<string> modules)
    {
        var staged = StagedRoot();
        if (staged == null || modules.Any(module => !File.Exists(Path.Combine(staged, module, MANIFEST))))
        {
            var reason = $"the build staged no {string.Join(", ", modules)} under '{staged}'";
            if (OnTheBuildServer)
                Assert.Fail(reason);

            Assert.Ignore(reason);
        }

        var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, RIG_FOLDER, name);
        return Stage(staged!, folder, modules, SolutionRoot(), TestContext.CurrentContext.TestDirectory);
    }

    /// <summary>
    /// Lays out a folder with exactly the named modules of a staged root, each checked first.
    /// </summary>
    /// <param name="stagedRoot">The folder the builds stage their modules into.</param>
    /// <param name="folder">The folder to make; replaced when it exists.</param>
    /// <param name="modules">The modules to copy.</param>
    /// <param name="solutionRoot">The solution root, where the modules' projects are.</param>
    /// <param name="testOutput">The test project's output, which holds the controller under test.</param>
    /// <returns>The folder.</returns>
    public static string Stage(string stagedRoot, string folder, IReadOnlyList<string> modules, string? solutionRoot, string testOutput)
    {
        foreach (var module in modules)
        {
            var problem = Problem(stagedRoot, module, solutionRoot, testOutput);
            if (problem != null)
                Assert.Fail(problem);
        }

        TryDelete(folder);
        foreach (var module in modules)
            Copy(Path.Combine(stagedRoot, module), Path.Combine(folder, module), ownFilesOnly: module == VERIFY);

        return folder;
    }

    /// <summary>
    /// What is wrong with a staged module, or null when it is what this build made: its manifest
    /// is there, and its controller assembly is byte for byte the one in the project's build
    /// output (for the Verify module also the one the tests themselves run against).
    /// </summary>
    /// <param name="stagedRoot">The folder the builds stage their modules into.</param>
    /// <param name="module">The module's folder name.</param>
    /// <param name="solutionRoot">The solution root, or null when it was not found.</param>
    /// <param name="testOutput">The test project's output.</param>
    /// <returns>The problem in words that name the module, or null.</returns>
    public static string? Problem(string stagedRoot, string module, string? solutionRoot, string testOutput)
    {
        var folder = Path.Combine(stagedRoot, module);
        var manifest = Path.Combine(folder, MANIFEST);
        if (!File.Exists(manifest))
            return $"{module} is not staged under '{stagedRoot}'";

        string? assemblyName;
        using (var document = JsonDocument.Parse(File.ReadAllBytes(manifest)))
            assemblyName = document.RootElement.TryGetProperty(ASSEMBLY_NAME, out var name) ? name.GetString() : null;

        if (string.IsNullOrEmpty(assemblyName))
            return $"{module} has a manifest without an assembly name";

        var assembly = Path.Combine(folder, assemblyName + ".dll");
        if (!File.Exists(assembly))
            return $"{module} holds no {assemblyName}.dll";

        var references = new List<(string Path, string What)>();
        if (module == VERIFY)
            references.Add((Path.Combine(testOutput, assemblyName + ".dll"), "the controller the tests run against"));

        if (solutionRoot != null && PROJECTS.TryGetValue(module, out var project))
            references.Add((Path.Combine(solutionRoot, project, "bin", Configuration, Framework(testOutput), assemblyName + ".dll"), "its project's build output"));

        if (references.Count == 0)
            return $"{module} is not a module this rig knows how to check";

        var staged = Sha256(assembly);
        foreach (var (path, what) in references)
        {
            if (!File.Exists(path))
                return $"{module} cannot be checked: {what} is not at '{path}' (build the test project)";

            if (!string.Equals(Sha256(path), staged, StringComparison.Ordinal))
                return $"the staged {module} is stale: its {assemblyName}.dll is not {what} ('{path}'). Build the test project again; a rig never runs a module another build left.";
        }

        return null;
    }

    /// <summary>The folder every build of this configuration stages its modules into, or null when the solution root is not found.</summary>
    /// <returns>The folder.</returns>
    public static string? StagedRoot()
    {
        var root = SolutionRoot();
        return root == null ? null : Path.Combine(root, STAGED_FOLDER, Configuration);
    }

    /// <summary>The solution root above the test output, or null.</summary>
    /// <returns>The folder that holds the solution file.</returns>
    public static string? SolutionRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, SOLUTION_FILE)))
            directory = directory.Parent;

        return directory?.FullName;
    }

    /// <summary>Removes a folder of a rig; best-effort (a library loaded from it stays mapped while the process lives).</summary>
    /// <param name="folder">The folder.</param>
    public static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // overwritten by the next rig of that name
        }
    }

    private static void Copy(string source, string destination, bool ownFilesOnly)
    {
        Directory.CreateDirectory(destination);

        // The Verify module's folders (natives, language runtimes) are not its own files: a rig
        // that needs them puts in the ones it means to test.
        var option = ownFilesOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
        foreach (var file in Directory.GetFiles(source, "*", option))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Framework(string testOutput)
    {
        return Path.GetFileName(testOutput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    #endregion

    #region Properties

    /// <summary>The configuration this test assembly was built with (Release on the build server).</summary>
    public static string Configuration =>
        typeof(VerifyRigModules).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

    /// <summary>Whether the tests run on the build server, where a missing prerequisite is a failure and not a skip.</summary>
    public static bool OnTheBuildServer =>
        string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase);

    #endregion
}
