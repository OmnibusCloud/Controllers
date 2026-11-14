using OutWit.Controller.AI.Corpus.Pdf;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The PDF extractor module the tests run: <c>@Data/wasm/pdftext.wasm</c> under the test project
/// (gitignored), where the data asset <c>corpus-pdftext-wasm.zip</c> extracts as it does beside
/// the controller. A test that needs it is ignored, with the reason, when it is absent — and fails
/// on the build server, which fetches the asset before the tests.
/// </summary>
public static class PdfTestModule
{
    #region Constants

    private const string CI_ENV = "GITHUB_ACTIONS";
    private const string PROJECT_FILE = "OutWit.Controller.AI.Corpus.Tests.csproj";

    #endregion

    #region Fields

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

        var reason = $"the PDF extractor module is not in the test project's @Data/{PdfModuleLocator.RelativePath}: extract corpus-pdftext-wasm.zip (release pdftext-v1.0.0) into @Data to run the PDF tests";
        if (string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase))
            Assert.Fail(reason);

        Assert.Ignore(reason);
        return "";
    }

    private static string? Find()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(System.IO.Path.Combine(directory.FullName, PROJECT_FILE)))
            directory = directory.Parent;

        if (directory == null)
            return null;

        var module = System.IO.Path.Combine(directory.FullName, "@Data", PdfStageLimits.MODULE_FOLDER, PdfStageLimits.MODULE_FILE);
        return File.Exists(module) ? module : null;
    }

    #endregion

    #region Properties

    /// <summary>The module's path, or null when it is not here.</summary>
    public static string? Path => PATH.Value;

    #endregion
}
