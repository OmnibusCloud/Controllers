using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Tests.Storage;

/// <summary>
/// The estimate against the deployment's limit: a job over the headroom is refused with a
/// message that says by how much and what to do; a job under it, or a deployment that states
/// no headroom, is not; the preflight's line names every part.
/// </summary>
[TestFixture]
public sealed class CorpusStorageCheckTests
{
    #region Constants

    private static readonly CorpusStorageEstimate ESTIMATE = Estimate(unsized: 0);

    #endregion

    #region Tests

    [Test]
    public void AJobOverTheHeadroomIsRefusedTest()
    {
        var limit = new CorpusStorageLimit { HeadroomBytes = 500_000_000, RetentionDays = 7, Source = "the server (BlobStorage:JobStorageHeadroomBytes)" };

        var refusal = CorpusStorageCheck.Refusal(ESTIMATE, limit);

        Assert.Multiple(() =>
        {
            Assert.That(ESTIMATE.TotalBytes, Is.EqualTo(814_250_000));
            Assert.That(refusal, Is.Not.Null);
            Assert.That(refusal, Does.StartWith("Server storage:"));
            Assert.That(refusal, Does.Contain("814250000 bytes"));
            Assert.That(refusal, Does.Contain("500000000 bytes"));
            Assert.That(refusal, Does.Contain("for 7 day(s)"));
            Assert.That(refusal, Does.Contain("BlobStorage:JobStorageHeadroomBytes"));
            Assert.That(refusal, Does.Contain("Split the manifest"));
        });
    }

    [Test]
    public void AJobUnderTheHeadroomIsNotRefusedTest()
    {
        var limit = new CorpusStorageLimit { HeadroomBytes = ESTIMATE.TotalBytes, Source = "x" };

        Assert.That(CorpusStorageCheck.Refusal(ESTIMATE, limit), Is.Null, "exactly the headroom fits");
    }

    [Test]
    public void NoHeadroomRefusesNothingTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusStorageCheck.Refusal(ESTIMATE, CorpusStorageLimit.NONE), Is.Null);
            Assert.That(CorpusStorageCheck.Describe(ESTIMATE, CorpusStorageLimit.NONE), Does.Contain("states no headroom"));
        });
    }

    [Test]
    public void ThePreflightLineNamesEveryPartTest()
    {
        var line = CorpusStorageCheck.Describe(Estimate(unsized: 2), new CorpusStorageLimit { HeadroomBytes = 1L << 40, RetentionDays = 7, Source = "the operator" });

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.StartWith("Server storage estimate 814250000 bytes (776.5 MiB) for 7 day(s)"));
            Assert.That(line, Does.Contain("text 400000000 bytes"));
            Assert.That(line, Does.Contain("signatures 250000000 bytes"));
            Assert.That(line, Does.Contain("second copies of 5 sampled shard(s) 160000000 bytes"));
            Assert.That(line, Does.Contain("documents 4000000 bytes"));
            Assert.That(line, Does.Contain("inputs 250000 bytes"));
            Assert.That(line, Does.Contain("2 source(s) without a known size are not in it"));
            Assert.That(line, Does.Contain("allows one job 1099511627776 bytes (1024.00 GiB) (the operator)"));
        });
    }

    [TestCase(0L, "0 bytes (0.0 MiB)")]
    [TestCase(1048576L, "1048576 bytes (1.0 MiB)")]
    [TestCase(1073741824L, "1073741824 bytes (1.00 GiB)")]
    public void BytesReadForAPersonTest(long bytes, string expected)
    {
        Assert.That(CorpusStorageCheck.Bytes(bytes), Is.EqualTo(expected));
    }

    #endregion

    #region Tools

    private static CorpusStorageEstimate Estimate(int unsized)
    {
        return new CorpusStorageEstimate
        {
            TextBytes = 400_000_000,
            SignatureBytes = 250_000_000,
            ReplicaBytes = 160_000_000,
            DocumentBytes = 4_000_000,
            InputBytes = 250_000,
            Documents = 240_000,
            SampledShards = 5,
            UnsizedSources = unsized
        };
    }

    #endregion
}
