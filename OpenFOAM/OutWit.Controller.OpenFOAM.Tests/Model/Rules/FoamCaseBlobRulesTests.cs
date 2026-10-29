using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

/// <summary>
/// A blob holds the bytes its file reference describes - the size and the
/// SHA-256 the initiator measured - or the variant is refused with the file,
/// what was found and what was expected.
/// </summary>
[TestFixture]
public class FoamCaseBlobRulesTests
{
    private string m_root = null!;

    [SetUp]
    public void Setup()
    {
        m_root = OpenFOAMTestPaths.CreateScratch("foam-blob");
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_root);
    }

    #region Tools

    private string Store(string text)
    {
        var path = Path.Combine(m_root, $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, text);
        return path;
    }

    private static FoamFileRefData Reference(string relativePath, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new FoamFileRefData { RelativePath = relativePath, BlobId = Guid.NewGuid(), Size = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
    }

    #endregion

    #region Blob Tests

    [Test]
    public async Task TheDescribedBytesPassWhateverTheCaseOfTheDigestTest()
    {
        var path = Store("application simpleFoam;\n");
        var file = Reference("system/controlDict", "application simpleFoam;\n");

        Assert.That(await FoamCaseBlobRules.ValidateAsync(file, path), Is.Null);
        file.Sha256 = file.Sha256.ToUpperInvariant();
        Assert.That(await FoamCaseBlobRules.ValidateAsync(file, path), Is.Null, "the digest is compared as hex, not as text");
    }

    [Test]
    public async Task OtherBytesAreRefusedWithWhatWasFoundAndWhatWasExpectedTest()
    {
        var path = Store("application pisoFoam;\n");
        var file = Reference("system/controlDict", "application simpleFoam;\n");
        var (size, sha256) = await FoamCaseBlobRules.DescribeAsync(path);

        var finding = await FoamCaseBlobRules.ValidateAsync(file, path);

        Assert.That(size, Is.EqualTo(22));
        Assert.That(finding, Is.EqualTo(
            $"system/controlDict: the blob's bytes are not the ones the task describes (size 22, sha256 {sha256}; expected size 24, sha256 {file.Sha256}) - the case changed after it was read; open it again and submit it again."));
    }

    [Test]
    public async Task TheSameSizeWithOtherBytesIsRefusedTooTest()
    {
        var path = Store("endTime 100;\n");
        var file = Reference("system/controlDict", "endTime 200;\n");

        var finding = await FoamCaseBlobRules.ValidateAsync(file, path);

        Assert.That(finding, Does.StartWith("system/controlDict: the blob's bytes are not the ones the task describes (size 13, sha256 "));
        Assert.That(finding, Does.Contain($"; expected size 13, sha256 {file.Sha256})"));
    }

    #endregion
}
