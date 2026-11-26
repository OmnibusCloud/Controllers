namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The solver module the tests run: <c>@Data/wasm/z3-5.4.1.wasm</c> under the test project
/// (gitignored), where the archive <c>smt-z3-wasm.zip</c> unpacks: the archive of the release the
/// controller's project file names, which the workflow "AI Smt toolchain" builds from its pins.
/// A test that needs it is ignored, with the reason, when it is absent - and fails on the build
/// server, which downloads the archive and holds it to the pins before the tests.
/// </summary>
internal static class SmtSolverModule
{
    #region Constants

    /// <summary>The module's place under the test project's <c>@Data</c>.</summary>
    public const string RELATIVE_PATH = "wasm/z3-5.4.1.wasm";

    /// <summary>The SHA-256 of the module: the bytes the toolchain's pins build to, and the profile pins.</summary>
    public const string SHA256 = "9b8a67e85542b5d62d30b40060dd237fcef07f794ea235483afc83eef47a1364";

    private const string CI_ENV = "GITHUB_ACTIONS";
    private const string PROJECT_FILE = "OutWit.Controller.AI.Smt.Tests.csproj";
    private const string DATA_FOLDER = "@Data";

    private static readonly Lazy<string?> PATH = new(Find);

    #endregion

    #region Functions

    /// <summary>The module's path; ignores the calling test when the module is not here (fails it on the build server).</summary>
    /// <returns>The full path.</returns>
    public static string Require()
    {
        var path = PATH.Value;
        if (path != null)
            return path;

        var reason = $"the solver module is not in the test project's {DATA_FOLDER}/{RELATIVE_PATH}: unpack smt-z3-wasm.zip (the release smt-z3-v5.4.1, or the artifact of the workflow \"AI Smt toolchain\") into {DATA_FOLDER} to run the solver's tests";
        if (OnBuildServer)
            Assert.Fail(reason);

        Assert.Ignore(reason);
        return "";
    }

    /// <summary>The test project's folder, found from the test's output folder.</summary>
    /// <returns>The folder, or null when the tests do not run under their project.</returns>
    public static string? ProjectFolder()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(System.IO.Path.Combine(directory.FullName, PROJECT_FILE)))
            directory = directory.Parent;

        return directory?.FullName;
    }

    private static string? Find()
    {
        var project = ProjectFolder();
        if (project == null)
            return null;

        var module = System.IO.Path.Combine(project, DATA_FOLDER, RELATIVE_PATH.Replace('/', System.IO.Path.DirectorySeparatorChar));
        return File.Exists(module) ? module : null;
    }

    #endregion

    #region Properties

    /// <summary>The module's path, or null when it is not here.</summary>
    public static string? Path => PATH.Value;

    /// <summary>Whether the tests run on the build server, where a missing module is a failure.</summary>
    public static bool OnBuildServer => string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase);

    #endregion
}
