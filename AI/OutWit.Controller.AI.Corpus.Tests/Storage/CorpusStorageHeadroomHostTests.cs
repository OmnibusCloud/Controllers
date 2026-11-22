using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Storage;

/// <summary>
/// Where the controller learns the deployment's headroom: the server's blob service by
/// reflection (the members proposed for the server), the operator's environment variable, the
/// smaller of the two; a host that has neither, or fails to answer, states nothing.
/// </summary>
[TestFixture]
public sealed class CorpusStorageHeadroomHostTests
{
    #region Tests

    [Test]
    public void AHostWithoutTheMembersStatesNothingTest()
    {
        var limit = Ask(new PlainBlobService(), null);

        Assert.Multiple(() =>
        {
            Assert.That(limit.IsStated, Is.False);
            Assert.That(limit.HeadroomBytes, Is.Zero);
            Assert.That(limit.RetentionDays, Is.Zero);
            Assert.That(limit.Source, Is.Empty);
        });
    }

    [Test]
    public void NoBlobServiceStatesNothingTest()
    {
        Assert.That(Ask(null, null).IsStated, Is.False);
    }

    [Test]
    public void TheServerStatesItsHeadroomAndWindowTest()
    {
        var limit = Ask(new StatingBlobService { Headroom = 50L << 30, RetentionDays = 7 }, null);

        Assert.Multiple(() =>
        {
            Assert.That(limit.HeadroomBytes, Is.EqualTo(50L << 30));
            Assert.That(limit.RetentionDays, Is.EqualTo(7));
            Assert.That(limit.Source, Does.Contain(CorpusStorageHeadroomHost.SERVER_SETTING));
        });
    }

    [Test]
    public void TheOperatorsVariableStatesItWhenTheServerDoesNotTest()
    {
        var limit = Ask(new PlainBlobService(), "1073741824");

        Assert.Multiple(() =>
        {
            Assert.That(limit.HeadroomBytes, Is.EqualTo(1L << 30));
            Assert.That(limit.Source, Does.Contain(CorpusStorageHeadroomHost.HEADROOM_VARIABLE));
        });
    }

    [Test]
    public void TheSmallerOfTheTwoHoldsTest()
    {
        var serverSmaller = Ask(new StatingBlobService { Headroom = 1000, RetentionDays = 3 }, "5000");
        var operatorSmaller = Ask(new StatingBlobService { Headroom = 9000, RetentionDays = 3 }, "5000");

        Assert.Multiple(() =>
        {
            Assert.That(serverSmaller.HeadroomBytes, Is.EqualTo(1000));
            Assert.That(serverSmaller.Source, Does.Contain(CorpusStorageHeadroomHost.SERVER_SETTING));
            Assert.That(operatorSmaller.HeadroomBytes, Is.EqualTo(5000));
            Assert.That(operatorSmaller.Source, Does.Contain(CorpusStorageHeadroomHost.HEADROOM_VARIABLE));
            Assert.That(operatorSmaller.RetentionDays, Is.EqualTo(3));
        });
    }

    [Test]
    public void AServerThatFailsToAnswerHasStatedNothingTest()
    {
        var limit = Ask(new FailingBlobService(), null);

        Assert.That(limit.IsStated, Is.False);
    }

    [Test]
    public void AServerThatStatesZeroHasStatedNothingTest()
    {
        var limit = Ask(new StatingBlobService { Headroom = 0, RetentionDays = 7 }, "");

        Assert.Multiple(() =>
        {
            Assert.That(limit.IsStated, Is.False);
            Assert.That(limit.RetentionDays, Is.EqualTo(7), "the window is reported even without a headroom");
        });
    }

    [TestCase("1073741824", 1073741824L)]
    [TestCase(" 42 ", 42L)]
    [TestCase("0", 0L)]
    [TestCase("-5", 0L)]
    [TestCase("1e9", 0L)]
    [TestCase("10GB", 0L)]
    [TestCase("1,000", 0L)]
    [TestCase("", 0L)]
    [TestCase(null, 0L)]
    public void TheVariableIsAWholeNumberOfBytesTest(string? text, long expected)
    {
        Assert.That(CorpusStorageHeadroomHost.ParseBytes(text), Is.EqualTo(expected));
    }

    #endregion

    #region Tools

    private static CorpusStorageLimit Ask(IWitBlobService? blobService, string? variable)
    {
        // Not an async test on purpose (see VerifyCapabilitiesTests): the question completes synchronously here.
        var host = new CorpusStorageHeadroomHost(blobService, name => name == CorpusStorageHeadroomHost.HEADROOM_VARIABLE ? variable : null);
        return host.GetAsync().GetAwaiter().GetResult();
    }

    #endregion

    #region Types

    private class PlainBlobService : IWitBlobService
    {
        public Task<string> GetLocalPathAsync(Guid blobId) => throw new NotSupportedException();

        public Task<Guid> UploadFileAsync(string localFilePath) => throw new NotSupportedException();

        public Task<Guid> UploadBytesAsync(byte[] data, string fileName) => throw new NotSupportedException();
    }

    private sealed class StatingBlobService : PlainBlobService
    {
        public Task<long> GetJobStorageHeadroomBytesAsync() => Task.FromResult(Headroom);

        public long Headroom { get; init; }

        public int JobBlobRetentionDays => RetentionDays;

        public int RetentionDays { get; init; }
    }

    private sealed class FailingBlobService : PlainBlobService
    {
        public Task<long> GetJobStorageHeadroomBytesAsync() => Task.FromException<long>(new IOException("the store is unreachable"));

        public int JobBlobRetentionDays => throw new InvalidOperationException("no configuration");
    }

    #endregion
}
