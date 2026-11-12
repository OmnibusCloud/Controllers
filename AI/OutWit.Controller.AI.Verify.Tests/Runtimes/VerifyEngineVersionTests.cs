using System.Security.Cryptography;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tests.Runtimes;

/// <summary>
/// The engine provenance names: the binding's version and the native library's, the latter
/// recognised by its hash among the libraries the controller ships. The table of shipped
/// libraries stays in step with the asset tool: on the build server, where natives.sh lays
/// them out under @Data/natives before the tests, every one of them must be in it, and the
/// process must be running on one.
/// </summary>
[TestFixture]
public sealed class VerifyEngineVersionTests
{
    #region Constants

    private const string CI_ENV = "GITHUB_ACTIONS";

    private static readonly (string Rid, string Library)[] NATIVES =
    [
        ("win-x64", "wasmtime.dll"),
        ("linux-x64", "libwasmtime.so"),
        ("osx-arm64", "libwasmtime.dylib")
    ];

    #endregion

    #region Naming Tests

    [Test]
    public void AShippedLibraryIsNamedByItsVersionTest()
    {
        var (hash, version) = VerifyEngineVersion.SHIPPED_NATIVES.First();

        Assert.That(VerifyEngineVersion.NativeVersionOf(hash), Is.EqualTo(version));
    }

    [Test]
    public void AnyOtherLibraryIsUnpinnedWithTheStartOfItsHashTest()
    {
        var hash = new string('0', 64);

        Assert.Multiple(() =>
        {
            Assert.That(VerifyEngineVersion.NativeVersionOf(hash), Is.EqualTo("unpinned 000000000000"));
            Assert.That(VerifyEngineVersion.NativeVersionOf(null), Is.EqualTo("unpinned (not found)"));
        });
    }

    [Test]
    public void TheDescriptionCarriesBothVersionsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VerifyEngineVersion.Format("48.0.2.0", "48.0.8"), Is.EqualTo("48.0.2.0 (native 48.0.8)"));
            Assert.That(VerifyEngineVersion.Current, Does.Match(@"^\d+\.\d+\.\d+\.\d+ \(native .+\)$"));
        });
    }

    #endregion

    #region Sync Tests

    [Test]
    public void TheShippedLibrariesAreTheOnesTheAssetToolBuildsTest()
    {
        var root = FindTestProjectRoot();
        var natives = root == null ? null : Path.Combine(root, "@Data", "natives");
        var onBuildServer = string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase);

        if (natives == null || !Directory.Exists(natives))
        {
            if (onBuildServer)
                Assert.Fail("the build server lays out @Data/natives before the tests");

            Assert.Ignore("no @Data/natives here: run toolchain/natives.sh @Data/natives to check the table");
        }

        var pinned = PinnedNativeVersion(root!);

        Assert.Multiple(() =>
        {
            foreach (var (rid, library) in NATIVES)
            {
                using var stream = File.OpenRead(Path.Combine(natives, rid, library));
                var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
                Assert.That(VerifyEngineVersion.SHIPPED_NATIVES.TryGetValue(hash, out var version), Is.True, $"{rid}/{library} {hash} is not in the table");
                Assert.That(version, Is.EqualTo(pinned), $"{rid}/{library}");
            }

            Assert.That(VerifyEngineVersion.SHIPPED_NATIVES.Count, Is.EqualTo(NATIVES.Length), "one entry a platform, no stale ones");

            // The build server replaces the package's library with the shipped one before the tests.
            if (onBuildServer)
                Assert.That(VerifyEngineVersion.Current, Does.EndWith($"(native {pinned})"));
        });
    }

    #endregion

    #region Tools

    private static string PinnedNativeVersion(string testProjectRoot)
    {
        var pins = File.ReadAllText(Path.Combine(testProjectRoot, "..", "OutWit.Controller.AI.Verify.Assets", "toolchain", "pins.env"));
        var match = Regex.Match(pins, @"^WASMTIME_NATIVE_VERSION=(?<version>[0-9.]+)\s*$", RegexOptions.Multiline);
        Assert.That(match.Success, Is.True, "pins.env names the native version");
        return match.Groups["version"].Value;
    }

    private static string? FindTestProjectRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.Controller.AI.Verify.Tests.csproj")))
            directory = directory.Parent;

        return directory?.FullName;
    }

    #endregion
}
