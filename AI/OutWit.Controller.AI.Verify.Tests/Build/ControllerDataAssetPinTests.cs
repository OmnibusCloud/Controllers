using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests.Build;

/// <summary>
/// What the shared build files demand of a declared data asset before they write it into a
/// controller's manifest (<c>ControllerValidateDataAssets</c> in
/// <c>Build/OutWit.Controller.Manifest.targets</c>): a SHA-256 of 64 lower-case hex digits that
/// is not the all-zero hash of a template, and a size, the length in bytes, a whole number
/// above zero. A placeholder or a hash of another form would be written into
/// <c>controller.json</c> and fail at every consumer, whose resolver and whose publish guard
/// compare exactly that form; an asset without its size could not be held to it. Held on a
/// controller project of its own (<see cref="ControllerDataAssetProbe"/>).
/// </summary>
[TestFixture]
public sealed class ControllerDataAssetPinTests
{
    #region Constants

    // The SHA-256 of "abc" and its length: a pin of the right form.
    private const string PINNED = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string SIZE = "3";

    // The all-zero hash a template carries until the asset tool writes the archive's own.
    private const string TEMPLATE = "0000000000000000000000000000000000000000000000000000000000000000";

    private const string SHA_REFUSED = "ControllerDataAsset '" + ControllerDataAssetProbe.ASSET + "' must have a Sha256 of 64 lower-case hex digits";
    private const string TEMPLATE_REFUSED = "ControllerDataAsset '" + ControllerDataAssetProbe.ASSET + "' has the all-zero Sha256 of a template";
    private const string SIZE_REFUSED = "ControllerDataAsset '" + ControllerDataAssetProbe.ASSET + "' must have a Size";

    #endregion

    #region Fields

    private ControllerDataAssetProbe m_probe = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var root = VerifyRigModules.SolutionRoot();
        if (root == null)
            VerifyRigModules.Missing("Solution root not found");

        m_probe = new ControllerDataAssetProbe(root);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_probe?.Dispose();
    }

    #endregion

    #region Tests

    [TestCase(PINNED, SIZE)]
    [TestCase(PINNED, "1")]
    [TestCase("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "999999999999999999")]
    [TestCase("0000000000000000000000000000000000000000000000000000000000000001", "1")]
    public void AnAssetPinnedByItsHashAndItsSizeIsWrittenIntoTheManifestTest(string sha256, string size)
    {
        var (code, output) = m_probe.Run(sha256, size);

        Assert.That(code, Is.Zero, "the manifest of a pinned asset is written:\n" + output);
        var manifest = File.ReadAllText(m_probe.Manifest);
        Assert.Multiple(() =>
        {
            Assert.That(manifest, Does.Contain($"\"sha256\": \"{sha256}\""), "the hash as declared");
            Assert.That(manifest, Does.Contain($"\"size\": {size},"), "the size as declared");
        });
    }

    [TestCase("TBD", TestName = "{m}(a placeholder)")]
    [TestCase("ABCDEF", TestName = "{m}(six hex digits)")]
    [TestCase("", TestName = "{m}(empty)")]
    [TestCase("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", TestName = "{m}(upper case)")]
    [TestCase("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a", TestName = "{m}(63 digits)")]
    [TestCase("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad0", TestName = "{m}(65 digits)")]
    [TestCase("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ag", TestName = "{m}(a letter that is not hex)")]
    [TestCase("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", TestName = "{m}(with a prefix)")]
    public void AHashThatIsNotSixtyFourLowerCaseHexDigitsIsRefusedTest(string sha256)
    {
        var (code, output) = m_probe.Run(sha256, SIZE);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Not.Zero, $"'{sha256}' is not a SHA-256 in the form a consumer compares, and the build went on:\n" + output);
            Assert.That(output, Does.Contain(SHA_REFUSED), "the build says which asset and why");
            Assert.That(File.Exists(m_probe.Manifest), Is.False, "no manifest is written for a refused asset");
        });
    }

    [TestCase(SIZE, TestName = "{m}(with a size)")]
    [TestCase("1", TestName = "{m}(with a size of one)")]
    public void TheAllZeroHashOfATemplateIsRefusedTest(string size)
    {
        var (code, output) = m_probe.Run(TEMPLATE, size);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Not.Zero, "the all-zero hash of a template pins no archive, and the build went on:\n" + output);
            Assert.That(output, Does.Contain(TEMPLATE_REFUSED), "the build says which asset and why");
            Assert.That(File.Exists(m_probe.Manifest), Is.False, "no manifest is written for a refused asset");
        });
    }

    [TestCase(null, TestName = "{m}(no Size)")]
    [TestCase("", TestName = "{m}(empty)")]
    [TestCase("0", TestName = "{m}(zero)")]
    [TestCase("-3", TestName = "{m}(below zero)")]
    [TestCase("12a", TestName = "{m}(not a number)")]
    [TestCase("1.5", TestName = "{m}(not whole)")]
    [TestCase("1e6", TestName = "{m}(an exponent)")]
    [TestCase("0123", TestName = "{m}(a leading zero)")]
    [TestCase("1000000000000000000", TestName = "{m}(nineteen digits)")]
    public void AnAssetWithoutItsSizeInBytesIsRefusedTest(string? size)
    {
        var (code, output) = m_probe.Run(PINNED, size);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Not.Zero, $"a size of '{size ?? "(none)"}' cannot hold an asset to its length, and the build went on:\n" + output);
            Assert.That(output, Does.Contain(SIZE_REFUSED), "the build says which asset and why");
            Assert.That(File.Exists(m_probe.Manifest), Is.False, "no manifest is written for a refused asset");
        });
    }

    #endregion
}
