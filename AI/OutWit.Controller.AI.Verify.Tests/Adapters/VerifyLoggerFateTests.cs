using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Adapters;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Tests.Mock;
using OutWit.Controller.AI.Verify.Tests.Sandbox;
using OutWit.Controller.AI.Verify.Variables;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Adapters;

/// <summary>
/// A logging call never decides a package's fate (hardening batch C, item C6), proven where it
/// went wrong on a node: <c>Verify.ExecutePackage</c> run through the engine's own entry of an
/// adapter, several packages at once as a client starts a share, on a host whose logger throws
/// and on one whose logger is not thread safe. Every package completes with its results, and a
/// package that fails for a reason of the node's own still says so in the fixed words: the
/// logger's exception, whose text can name a folder of the node, travels nowhere.
/// <para>
/// The programs are raw WASI modules, so the fixture needs no language runtimes: only the
/// sandbox itself.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyLoggerFateTests
{
    #region Constants

    private const int PACKAGES = 8;
    private const int TASKS_A_PACKAGE = 3;

    private const string PACKAGE_VARIABLE = "package";
    private const string RESULT_VARIABLE = "result";

    private const string FIXED_WORDS = "Verify: this node could not run the package for a reason of its own (its log says which)";
    private const string LOGGER_EXCEPTION = nameof(IndexOutOfRangeException);

    private static readonly TimeSpan LINE_TIME = TimeSpan.FromMilliseconds(40);

    #endregion

    #region Fields

    private string m_storagePath = null!;

    private VerifyTestBlobService m_blobs = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_storagePath = Path.Combine(Path.GetTempPath(), $"witai_verify_logger_{Guid.NewGuid():N}");
        m_blobs = new VerifyTestBlobService(m_storagePath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_storagePath))
            Directory.Delete(m_storagePath, recursive: true);
    }

    #endregion

    #region Fate Tests

    [Test]
    public async Task C6ParallelPackagesCompleteOnAHostWhoseLoggerThrowsTest()
    {
        var logger = new VerifyThrowingLogger();
        var packages = await PublishAsync(PACKAGES);

        var outcomes = await RunTogetherAsync(packages, logger);

        Assert.That(logger.Calls, Is.GreaterThan(0), "the packages logged, and the logger threw every time");
        await AssertEveryPackageCompletedAsync(packages, outcomes);
    }

    [Test]
    public async Task C6ParallelPackagesCompleteOnAHostWhoseLoggerIsNotThreadSafeTest()
    {
        // Every package is a job of its own, so each writes its delivery line as its blob
        // arrives, and the blobs of a share arrive together. The logger takes forty milliseconds
        // over a line: without the lock those lines meet inside it, and it throws as the
        // client's list did.
        var logger = new VerifyUnlockedLogger(LINE_TIME);
        var packages = await PublishAsync(PACKAGES);

        var outcomes = await RunTogetherAsync(packages, logger);

        Assert.Multiple(() =>
        {
            Assert.That(logger.Overlaps, Is.Zero, "two packages were inside the host's logger at once");
            Assert.That(logger.Lines, Has.Count.GreaterThanOrEqualTo(PACKAGES), "every package wrote its lines");
        });
        await AssertEveryPackageCompletedAsync(packages, outcomes);
    }

    [Test]
    public async Task C6AFailingPackageStillReportsTheFixedWordsTest()
    {
        // One package of the share is not the package its reference names: a failure of the
        // node's own. On a host whose logger throws, the adapter's line about it throws, and the
        // engine's own line in its catch block throws again: the logger's exception used to be
        // what the server was told.
        var logger = new VerifyThrowingLogger();
        var packages = await PublishAsync(3);
        var broken = (VerifyTaskPackageData)packages[1].Clone();
        broken.Sha256 = new string('0', 64);

        var outcomes = await RunTogetherAsync([packages[0], broken, packages[2]], logger);

        var failed = outcomes[1].Status;
        Assert.Multiple(() =>
        {
            Assert.That(failed.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(failed.Message, Does.Contain(FIXED_WORDS));
            Assert.That(failed.Message, Does.Not.Contain(VerifyThrowingLogger.TEXT), "the logger's exception text left the node");
            Assert.That(failed.Message, Does.Not.Contain(LOGGER_EXCEPTION), "the logger's exception left the node");
            Assert.That(failed.Message, Does.Not.Contain(m_storagePath), "a folder of the node left it");
            Assert.That(outcomes[1].Pool.TryGetValue(RESULT_VARIABLE, out VerifyResultPackageData? none) && none != null, Is.False, "a failed package returns nothing");
        });

        await AssertEveryPackageCompletedAsync([packages[0], packages[2]], [outcomes[0], outcomes[2]]);
    }

    [Test]
    public async Task C6AFailingPackageSaysTheSameOnAHostWhoseLoggerWorksTest()
    {
        // The control: the words are the adapter's, not something the guard made up.
        var packages = await PublishAsync(1);
        var broken = (VerifyTaskPackageData)packages[0].Clone();
        broken.Sha256 = new string('0', 64);

        var outcomes = await RunTogetherAsync([broken], NullLogger.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(outcomes[0].Status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(outcomes[0].Status.Message, Does.Contain(FIXED_WORDS));
        });
    }

    #endregion

    #region Tools

    private async Task<List<VerifyTaskPackageData>> PublishAsync(int packages)
    {
        // A small run is cut one batch a package.
        var batches = Enumerable.Range(0, packages).Select(package => new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            Tasks = Enumerable.Range(package * TASKS_A_PACKAGE, TASKS_A_PACKAGE).Select(HelloTask).ToList()
        }).ToList();

        var published = await VerifyTaskPackages.PublishAsync(batches, m_blobs, NullLogger.Instance, "test");
        Assert.That(published, Has.Count.EqualTo(packages));
        return published;
    }

    private static VerifyTaskData HelloTask(int index)
    {
        return new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = VerifyRuntimeCatalog.WASI,
            EntryPoint = "hello.wasm",
            Sources = [new VerifySourceFileData { Name = "hello.wasm", Content = Convert.ToBase64String(WasiModules.Hello(Expected(index))) }]
        };
    }

    private static string Expected(int index)
    {
        return $"task {index}\n";
    }

    /// <summary>Runs the packages at once through the engine's entry of the adapter, each as a job of its own.</summary>
    private async Task<List<(IWitProcessingStatus Status, WitVariableCollection Pool)>> RunTogetherAsync(IReadOnlyList<VerifyTaskPackageData> packages, ILogger logger)
    {
        IWitProcessingAdapter adapter = new WitActivityAdapterVerifyExecutePackage(new VerifyTestProcessingManager(), m_blobs, new VerifyNoServices(), logger);
        var engineId = Guid.NewGuid();

        var runs = packages.Select(package =>
        {
            var pool = new WitVariableCollection();
            pool.Add(new WitVariableVerifyTaskPackage(PACKAGE_VARIABLE, package));
            pool.Add(new WitVariableVerifyResultPackage(RESULT_VARIABLE));

            var activity = new WitActivityVerifyExecutePackage { Package = (WitReference)PACKAGE_VARIABLE };
            activity.SetReturnReference(RESULT_VARIABLE);

            // A logger's exception that leaves the adapter leaves this call too: the test would
            // end here with it, as the node's share did.
            return (Run: Task.Run(() => adapter.Process(engineId, Guid.NewGuid(), activity, null, pool, reportProgress: false)), Pool: pool);
        }).ToList();

        var outcomes = new List<(IWitProcessingStatus, WitVariableCollection)>();
        foreach (var (run, pool) in runs)
            outcomes.Add((await run, pool));

        return outcomes;
    }

    private async Task AssertEveryPackageCompletedAsync(IReadOnlyList<VerifyTaskPackageData> packages, IReadOnlyList<(IWitProcessingStatus Status, WitVariableCollection Pool)> outcomes)
    {
        Assert.That(outcomes, Has.Count.EqualTo(packages.Count));

        for (var index = 0; index < packages.Count; index++)
        {
            var (status, pool) = outcomes[index];
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"package {index}: {status.Message}");

            Assert.That(pool.TryGetValue(RESULT_VARIABLE, out VerifyResultPackageData? result) && result != null, Is.True, $"package {index} returned no result package");
            Assert.That(result!.Ticket, Is.EqualTo(packages[index].Ticket), $"package {index} answers under its own ticket");
            Assert.That(result.TaskCount, Is.EqualTo(TASKS_A_PACKAGE));

            var results = VerifyResultPackageReader.Read(await m_blobs.GetLocalPathAsync(result.ResultsBlobId), packages[index].BatchCount, packages[index].TaskCount)
                .SelectMany(batch => batch.Results)
                .OrderBy(task => task.TaskIndex)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(results, Has.Count.EqualTo(TASKS_A_PACKAGE), $"package {index}: a result for every task");
                Assert.That(results.Select(task => task.Verdict), Is.All.EqualTo(VerifyVerdict.Pass), $"package {index}");
                Assert.That(results.Select(task => task.Stdout), Is.EqualTo(results.Select(task => Expected(task.TaskIndex))), $"package {index}: each task's own output");
            });
        }
    }

    #endregion
}
