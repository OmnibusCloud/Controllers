using System.Security.Cryptography;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests;

/// <summary>
/// The solver module as a data asset of the controller: the project pins the archive of the
/// release that holds it (the archive the toolchain's pins build to, by its SHA-256 and its size)
/// and extracts it at the module's root; the module the build stages declares it so; and the
/// archive fetched from its release, held to the pin, puts the module the profile pins where the
/// controller looks for it. The archive is kept under the tests' <c>@Data/.assets</c> between runs
/// (cached on the build server); a machine that cannot reach the release skips the last test, the
/// build server fails it.
/// </summary>
[TestFixture]
public sealed class SmtDataAssetTests
{
    #region Constants

    private const string ASSET = "smt-z3-wasm";
    private const string URI = "gh-release://OmnibusCloud/Controllers/smt-z3-v5.4.1/smt-z3-wasm.zip";
    private const string ROOT = ".";
    private const string ANY_PLATFORM = "any";
    private const long MODULE_SIZE = 30_204_160;

    #endregion

    #region Tests

    [Test]
    public void TheProjectPinsTheArchiveTheToolchainBuildsTest()
    {
        var root = SmtRigModules.SolutionRoot()!;
        var pins = Pins(root);
        var asset = SmtPackagedAssets.Project(Path.Combine(root, "AI", "OutWit.Controller.AI.Smt", "OutWit.Controller.AI.Smt.csproj")).Single(one => one.Id == ASSET);

        Assert.Multiple(() =>
        {
            Assert.That(asset.Uri, Is.EqualTo(URI), "the archive of the asset release of the solver");
            Assert.That(asset.Sha256, Is.EqualTo(pins["SMT_Z3_WASM_SHA256"]), "the archive the toolchain's pins build to");
            Assert.That(asset.Size, Is.EqualTo(long.Parse(pins["SMT_Z3_WASM_SIZE"])));
            Assert.That(asset.RuntimeIdentifier, Is.EqualTo(ANY_PLATFORM), "the same bytes on every platform");
            Assert.That(asset.ExtractTo, Is.EqualTo(ROOT), "the archive holds wasm/: extracted at the module's root");
            Assert.That(pins["Z3_MODULE_SHA256"], Is.EqualTo(SmtSolverModule.SHA256), "the module the archive holds is the one the profile pins");
        });
    }

    [Test]
    public void TheStagedModuleDeclaresTheArchiveTest()
    {
        var staged = SmtRigModules.StagedRoot();
        var module = staged == null ? null : Path.Combine(staged, SmtRigModules.SMT);
        if (module == null || !File.Exists(Path.Combine(module, "controller.json")))
            SmtRigModules.Missing($"the build staged no {SmtRigModules.SMT} under '{staged}'");

        var project = SmtPackagedAssets.Project(Path.Combine(SmtRigModules.SolutionRoot()!, "AI", "OutWit.Controller.AI.Smt", "OutWit.Controller.AI.Smt.csproj")).Single(one => one.Id == ASSET);
        var declared = SmtPackagedAssets.Declared(module!).Single(one => one.Id == ASSET);

        Assert.That(declared, Is.EqualTo(project), "the manifest a node reads says what the project pins");
    }

    [Test]
    public void TheArchiveOfTheReleasePutsThePinnedModuleWhereTheControllerLooksTest()
    {
        var root = SmtRigModules.SolutionRoot()!;
        var asset = SmtPackagedAssets.Project(Path.Combine(root, "AI", "OutWit.Controller.AI.Smt", "OutWit.Controller.AI.Smt.csproj")).Single(one => one.Id == ASSET);
        var module = Path.Combine(TestContext.CurrentContext.TestDirectory, "@Rig", "asset", Guid.NewGuid().ToString("N"));

        try
        {
            try
            {
                SmtPackagedAssets.Resolve(asset, module, SmtPackagedAssets.CacheFolder(root));
            }
            catch (HttpRequestException exception)
            {
                SmtRigModules.Missing($"the archive could not be fetched from {SmtPackagedAssets.Url(asset)}: {exception.Message}");
            }

            var solver = Path.Combine(module, SmtSolverModule.RELATIVE_PATH);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(solver), Is.True, $"the module is at {SmtSolverModule.RELATIVE_PATH}");
                Assert.That(new FileInfo(solver).Length, Is.EqualTo(MODULE_SIZE));
                Assert.That(Sha256(solver), Is.EqualTo(SmtSolverModule.SHA256), "the module the profile pins, byte for byte");
                Assert.That(File.Exists(Path.Combine(module, "wasm", "LICENSE-z3.txt")), Is.True, "with the solver's licence");
                Assert.That(File.Exists(Path.Combine(module, "wasm", "THIRD-PARTY-NOTICES-z3.txt")), Is.True, "and its notices");
            });
        }
        finally
        {
            SmtRigModules.TryDelete(module);
        }
    }

    #endregion

    #region Tools

    private static Dictionary<string, string> Pins(string root)
    {
        return File.ReadAllLines(Path.Combine(root, "AI", "OutWit.Controller.AI.Smt.Assets", "toolchain", "pins.env"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line[0] != '#' && line.Contains('='))
            .Select(line => line.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1].Trim('"'), StringComparer.Ordinal);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    #endregion
}
