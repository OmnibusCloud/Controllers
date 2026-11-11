using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests.Delivery;

/// <summary>
/// Tasks and results as blobs, end to end without a sandbox: the host stores packages and
/// hands out references, a node fetches and reads its package, uploads results, and the host
/// reads them back. A transfer that fails is tried again; a blob that is not the package its
/// reference names is refused.
/// </summary>
[TestFixture]
public sealed class VerifyPackageTransferTests
{
    #region Fields

    private string m_storagePath = null!;

    private VerifyTestBlobService m_blobs = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_storagePath = Path.Combine(Path.GetTempPath(), $"witai_verify_packages_{Guid.NewGuid():N}");
        m_blobs = new VerifyTestBlobService(m_storagePath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_storagePath))
            Directory.Delete(m_storagePath, recursive: true);
    }

    #endregion

    #region Delivery Tests

    [Test]
    public async Task TasksTravelAsBlobsAndTheReferencesCarryTheCountsTest()
    {
        var batches = Enumerable.Range(0, 5).Select(index => TaskBatch(firstIndex: index * 4, count: 4)).ToList();

        var packages = await VerifyTaskPackages.PublishAsync(batches, m_blobs, NullLogger.Instance, "test");

        Assert.That(packages, Has.Count.EqualTo(5), "a small run: one batch per package");
        Assert.Multiple(() =>
        {
            Assert.That(packages.All(package => package.TasksBlobId != Guid.Empty), Is.True);
            Assert.That(packages.Sum(package => package.TaskCount), Is.EqualTo(20));
            Assert.That(packages.All(package => package.BatchCount == 1), Is.True);
            Assert.That(packages.All(package => package.SizeBytes > 0 && package.Sha256.Length == 64), Is.True);
        });

        var path = await VerifyPackageTransfer.FetchAsync(m_blobs, packages[2], NullLogger.Instance, CancellationToken.None);
        var tasks = await VerifyPackageTransfer.ReadTasksAsync(m_blobs, packages[2], path, NullLogger.Instance, CancellationToken.None);

        Assert.That(new FileInfo(path).Length, Is.EqualTo(packages[2].SizeBytes));
        Assert.That(tasks.Single().Is(batches[2]), Is.True);
    }

    [Test]
    public async Task ResultsTravelBackAsBlobsTest()
    {
        var executed = new List<VerifyResultBatchData> { ResultBatch(0, 3), ResultBatch(3, 2) };

        var first = await VerifyPackageTransfer.UploadResultsAsync(m_blobs, [executed[0]], NullLogger.Instance, CancellationToken.None);
        var second = await VerifyPackageTransfer.UploadResultsAsync(m_blobs, [executed[1]], NullLogger.Instance, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.TaskCount, Is.EqualTo(3));
            Assert.That(first.BatchCount, Is.EqualTo(1));
            Assert.That(second.ResultsBlobId, Is.Not.EqualTo(first.ResultsBlobId));
        });

        // The host reads whatever came back, in whatever order.
        var loaded = await VerifyResultPackages.LoadAsync(new[] { second, first }, m_blobs);

        Assert.That(loaded.SelectMany(batch => batch.Results).Select(result => result.TaskIndex).Order(), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
    }

    #endregion

    #region Failure Tests

    [Test]
    public async Task ADownloadThatFailsIsTriedAgainTest()
    {
        var package = (await VerifyTaskPackages.PublishAsync([TaskBatch(0, 2)], m_blobs, NullLogger.Instance, "test")).Single();
        var flaky = new VerifyFlakyBlobService(m_blobs, failures: 1);

        var path = await VerifyPackageTransfer.FetchAsync(flaky, package, NullLogger.Instance, CancellationToken.None);

        Assert.That(File.Exists(path), Is.True);
        Assert.That(flaky.Attempts, Is.EqualTo(2));
    }

    [Test]
    public void ACancelledJobLeavesTheRetryWaitTest()
    {
        var package = new VerifyTaskPackageData { TasksBlobId = Guid.NewGuid(), TaskCount = 1 };
        var flaky = new VerifyFlakyBlobService(m_blobs, failures: int.MaxValue);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        Assert.That(
            async () => await VerifyPackageTransfer.FetchAsync(flaky, package, NullLogger.Instance, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ABlobThatIsNotThePackageIsRefusedTest()
    {
        var package = (await VerifyTaskPackages.PublishAsync([TaskBatch(0, 2)], m_blobs, NullLogger.Instance, "test")).Single();
        var path = await VerifyPackageTransfer.FetchAsync(m_blobs, package, NullLogger.Instance, CancellationToken.None);

        var wrongHash = package.Clone();
        wrongHash.Sha256 = new string('0', 64);
        var wrongCount = package.Clone();
        wrongCount.TaskCount = 3;

        Assert.Multiple(() =>
        {
            Assert.That(async () => await VerifyPackageTransfer.ReadTasksAsync(m_blobs, wrongHash, path, NullLogger.Instance, CancellationToken.None),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(async () => await VerifyPackageTransfer.ReadTasksAsync(m_blobs, wrongCount, path, NullLogger.Instance, CancellationToken.None),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("3"));
        });
    }

    [Test]
    public async Task APackageTheCacheDroppedIsFetchedAgainTest()
    {
        var package = (await VerifyTaskPackages.PublishAsync([TaskBatch(0, 2)], m_blobs, NullLogger.Instance, "test")).Single();

        var tasks = await VerifyPackageTransfer.ReadTasksAsync(m_blobs, package, Path.Combine(m_storagePath, "gone.wvp"), NullLogger.Instance, CancellationToken.None);

        Assert.That(tasks.Single().Tasks, Has.Count.EqualTo(2));
    }

    [Test]
    public void AResultPackageWithoutABlobFailsTheCollectionTest()
    {
        Assert.That(
            async () => await VerifyResultPackages.LoadAsync(new[] { new VerifyResultPackageData() }, m_blobs),
            Throws.TypeOf<InvalidDataException>());
    }

    #endregion

    #region Tools

    private static VerifyTaskBatchData TaskBatch(int firstIndex, int count)
    {
        return new VerifyTaskBatchData
        {
            RuntimeId = "python-3.14.6",
            Tasks = Enumerable.Range(firstIndex, count)
                .Select(index => new VerifyTaskData
                {
                    TaskIndex = index,
                    RuntimeId = "python-3.14.6",
                    EntryPoint = "m.py",
                    Sources = [new VerifySourceFileData { Name = "m.py", Content = $"print({index})" }]
                })
                .ToList()
        };
    }

    private static VerifyResultBatchData ResultBatch(int firstIndex, int count)
    {
        return new VerifyResultBatchData
        {
            Results = Enumerable.Range(firstIndex, count)
                .Select(index => new VerifyResultData { TaskIndex = index, Verdict = VerifyVerdict.Pass, Stdout = $"{index}\n" })
                .ToList()
        };
    }

    #endregion
}
