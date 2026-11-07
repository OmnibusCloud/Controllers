using System.Runtime.InteropServices;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tests.Runtimes;

/// <summary>
/// The module-aware native resolver. The cloud client loads controllers into the shared load
/// context, where the module's deps.json is never read: only this resolver finds wasmtime under
/// the module's runtimes/&lt;rid&gt;/native/ folder, so its path logic is pinned here.
/// </summary>
[TestFixture]
public sealed class VerifyNativeLibraryTests
{
    #region Naming Tests

    [Test]
    public void FileNameFollowsThePlatformTest()
    {
        var expected = OperatingSystem.IsWindows() ? "wasmtime.dll"
            : OperatingSystem.IsMacOS() ? "libwasmtime.dylib"
            : "libwasmtime.so";

        Assert.That(VerifyNativeLibrary.FileName, Is.EqualTo(expected));
    }

    [Test]
    public void CandidateRidsStartSpecificAndEndPortableTest()
    {
        var rids = VerifyNativeLibrary.CandidateRids();

        Assert.Multiple(() =>
        {
            Assert.That(rids[0], Is.EqualTo(RuntimeInformation.RuntimeIdentifier));
            Assert.That(rids[^1], Is.EqualTo(VerifyNativeLibrary.PortableRid));
            Assert.That(rids, Is.Unique);
            Assert.That(VerifyNativeLibrary.PortableRid, Does.Match("^(win|linux|osx)-(x64|x86|arm64|arm)$"));
        });
    }

    #endregion

    #region Locate Tests

    [Test]
    public void LocateFindsTheLibraryUnderThePortableRidTest()
    {
        var root = CreateRoot();
        try
        {
            var native = Path.Combine(root, "runtimes", VerifyNativeLibrary.PortableRid, "native");
            Directory.CreateDirectory(native);
            var file = Path.Combine(native, VerifyNativeLibrary.FileName);
            File.WriteAllBytes(file, [0]);

            Assert.That(VerifyNativeLibrary.Locate(root), Is.EqualTo(file));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void LocateReturnsNullWithoutTheLibraryTest()
    {
        var root = CreateRoot();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(VerifyNativeLibrary.Locate(root), Is.Null);
                Assert.That(VerifyNativeLibrary.Locate(null), Is.Null);
                Assert.That(VerifyNativeLibrary.Locate(""), Is.Null);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    #endregion

    #region Registration Tests

    [Test]
    public void RegisteredResolverLetsTheEngineLoadTest()
    {
        VerifyNativeLibrary.EnsureRegistered();
        VerifyNativeLibrary.EnsureRegistered();

        var located = VerifyNativeLibrary.Locate();
        Assert.That(located, Is.Not.Null, "the test bin carries the package's runtimes/<rid>/native library");
        Assert.That(located, Does.Exist);

        Assert.DoesNotThrow(() =>
        {
            using var engine = new Wasmtime.Engine();
        });
    }

    #endregion

    #region Tools

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"witai_native_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    #endregion
}
