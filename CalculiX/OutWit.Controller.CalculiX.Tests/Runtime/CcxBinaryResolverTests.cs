using OutWit.Controller.CalculiX.Runtime;

namespace OutWit.Controller.CalculiX.Tests.Runtime;

[TestFixture]
public class CcxBinaryResolverTests
{
    #region Resolution Tests

    [Test]
    public void CurrentRuntimeFolderIsKnownOnSupportedPlatformsTest()
    {
        // The build/test fleet runs only on the three supported platforms, so
        // the mapping must resolve here — and to the asset extraction
        // vocabulary (windows-x64/linux-x64/macos-arm64), not to RIDs.
        var folder = CcxBinaryResolver.ResolveCurrentRuntimeFolder();

        Assert.That(folder, Is.Not.Null);
        Assert.That(folder, Is.AnyOf("windows-x64", "linux-x64", "macos-arm64"));
    }

    [Test]
    public void ResolveReturnsNullWithoutBundledSolverTest()
    {
        // A bare assembly path with no ccx/ folder next to it: the resolver
        // reports absence instead of throwing — the adapter turns that into
        // its own loud error.
        var probe = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "controller.dll");

        Assert.That(CcxBinaryResolver.Resolve(probe), Is.Null);
    }

    #endregion
}
