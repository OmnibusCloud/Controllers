namespace OutWit.Controller.AI.Smt.Live.Tests.Mock;

/// <summary>
/// The solver module the local runs of the tests solve with: the one the controller's tests run,
/// <c>@Data/wasm/z3-5.4.1.wasm</c> under their project (gitignored: the archive
/// <c>smt-z3-wasm.zip</c> of the workflow "AI Smt toolchain", unpacked there). A test that needs it
/// is ignored, with the reason, when it is not there, and fails on the build server, which unpacks
/// it before the tests.
/// </summary>
internal static class SmtLiveSolver
{
    #region Constants

    /// <summary>The variable the controller's locator reads the module's file from.</summary>
    public const string MODULE_VARIABLE = "WITAI_SMT_Z3";

    private const string CI_ENV = "GITHUB_ACTIONS";

    private static readonly string[] MODULE = ["AI", "OutWit.Controller.AI.Smt.Tests", "@Data", "wasm", "z3-5.4.1.wasm"];

    #endregion

    #region Functions

    /// <summary>The module's file under the repository, or null when it is not there.</summary>
    /// <returns>Its full path.</returns>
    public static string? Find()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.slnx")))
            directory = directory.Parent;

        if (directory == null)
            return null;

        var module = Path.Combine([directory.FullName, .. MODULE]);
        return File.Exists(module) ? module : null;
    }

    /// <summary>Ends the calling test or fixture when the module is not there: ignored on a developer's machine, failed on the build server.</summary>
    public static void Require()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(MODULE_VARIABLE)))
            return;

        var reason = $"the solver module is not in {string.Join("/", MODULE.SkipLast(1))}: unpack smt-z3-wasm.zip there to run the local runs";
        if (OnBuildServer)
            Assert.Fail(reason);

        Assert.Ignore(reason);
    }

    /// <summary>Ends the calling test when a prerequisite other than the module is not there: ignored on a developer's machine, failed on the build server.</summary>
    /// <param name="reason">What is missing.</param>
    public static void Missing(string reason)
    {
        if (OnBuildServer)
            Assert.Fail(reason);

        Assert.Ignore(reason);
    }

    #endregion

    #region Properties

    /// <summary>Whether the tests run on the build server.</summary>
    public static bool OnBuildServer => string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase);

    #endregion
}
