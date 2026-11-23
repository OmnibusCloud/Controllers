using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The controller modules a test rig loads: a named set, laid out in a folder of the rig's own,
/// each checked to be what this build made. Every controller of the repository stages its module
/// into one folder (<c>@Controllers/&lt;configuration&gt;</c> at the solution root); a rig that
/// loaded that folder would load whatever lay there, a module of another controller or one an
/// earlier build left. A rig names the modules it needs; only those are copied, and a module whose
/// assembly is not byte for byte the one this build produced is a failure that names it.
/// </summary>
internal static class SmtRigModules
{
    #region Constants

    /// <summary>The Smt controller's module.</summary>
    public const string SMT = "smt.module";

    /// <summary>The module Smt depends on.</summary>
    public const string VARIABLES = "variables.module";

    /// <summary>The module of Grid.ForEach and Grid.Replicas.</summary>
    public const string GRID = "grid.module";

    /// <summary>What a package needs to go through the engine to in-process nodes, and nothing else.</summary>
    public static readonly IReadOnlyList<string> ENGINE = [SMT, VARIABLES, GRID];

    /// <summary>What a host needs to load the Smt controller alone.</summary>
    public static readonly IReadOnlyList<string> CONTROLLER = [SMT, VARIABLES];

    private const string MANIFEST = "controller.json";
    private const string ASSEMBLY_NAME = "assemblyName";
    private const string RIG_FOLDER = "@Rig";
    private const string STAGED_FOLDER = "@Controllers";
    private const string SOLUTION_FILE = "OutWit.slnx";

    private static readonly IReadOnlyDictionary<string, string> PROJECTS = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SMT] = Path.Combine("AI", "OutWit.Controller.AI.Smt"),
        [VARIABLES] = Path.Combine("Variables", "OutWit.Controller.Variables"),
        [GRID] = Path.Combine("Grid", "OutWit.Controller.Grid")
    };

    #endregion

    #region Functions

    /// <summary>
    /// Lays out a folder of the rig's own under the test output with exactly the named modules of
    /// this build. Ignores the test locally when the build staged them not; fails it on the build
    /// server.
    /// </summary>
    /// <param name="name">The folder's name; an earlier folder of that name is replaced.</param>
    /// <param name="modules">The modules to load.</param>
    /// <returns>The folder.</returns>
    public static string Stage(string name, IReadOnlyList<string> modules)
    {
        var root = SolutionRoot();
        var staged = root == null ? null : Path.Combine(root, STAGED_FOLDER, Configuration);
        if (staged == null || modules.Any(module => !File.Exists(Path.Combine(staged, module, MANIFEST))))
            Missing($"the build staged no {string.Join(", ", modules)} under '{staged}'");

        foreach (var module in modules)
        {
            var problem = Problem(staged!, module, root!, TestContext.CurrentContext.TestDirectory);
            if (problem != null)
                Assert.Fail(problem);
        }

        var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, RIG_FOLDER, name);
        TryDelete(folder);
        foreach (var module in modules)
            Copy(Path.Combine(staged!, module), Path.Combine(folder, module), ownFilesOnly: module == SMT);

        return folder;
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

    /// <summary>
    /// Ends a fixture or a test for a prerequisite it does not have: skipped on a developer's
    /// machine, a failure on the build server, where its absence would hide every test behind it.
    /// </summary>
    /// <param name="reason">What is missing.</param>
    [DoesNotReturn]
    public static void Missing(string reason)
    {
        if (SmtSolverModule.OnBuildServer)
            Assert.Fail(reason);

        Assert.Ignore(reason);
        throw new InvalidOperationException(reason);
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

    private static string? Problem(string stagedRoot, string module, string solutionRoot, string testOutput)
    {
        var folder = Path.Combine(stagedRoot, module);
        string? assemblyName;
        using (var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, MANIFEST))))
            assemblyName = document.RootElement.TryGetProperty(ASSEMBLY_NAME, out var name) ? name.GetString() : null;

        if (string.IsNullOrEmpty(assemblyName))
            return $"{module} has a manifest without an assembly name";

        var assembly = Path.Combine(folder, assemblyName + ".dll");
        if (!File.Exists(assembly))
            return $"{module} holds no {assemblyName}.dll";

        var references = new List<(string Path, string What)>();
        if (module == SMT)
            references.Add((Path.Combine(testOutput, assemblyName + ".dll"), "the controller the tests run against"));

        references.Add((Path.Combine(solutionRoot, PROJECTS[module], "bin", Configuration, Path.GetFileName(testOutput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), assemblyName + ".dll"), "its project's build output"));

        var staged = Sha256(assembly);
        foreach (var (path, what) in references)
        {
            if (!File.Exists(path))
                return $"{module} cannot be checked: {what} is not at '{path}' (build the test project)";

            if (!string.Equals(Sha256(path), staged, StringComparison.Ordinal))
                return $"the staged {module} is stale: its {assemblyName}.dll is not {what} ('{path}'). Build the test project again.";
        }

        return null;
    }

    private static void Copy(string source, string destination, bool ownFilesOnly)
    {
        Directory.CreateDirectory(destination);

        // The Smt module's folders (natives, the solver module) are not its own files: a rig that
        // needs them puts in the ones it means to test.
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

    #endregion

    #region Properties

    /// <summary>The configuration this test assembly was built with (Release on the build server).</summary>
    public static string Configuration =>
        typeof(SmtRigModules).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

    #endregion
}
