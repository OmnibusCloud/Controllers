using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Node;

/// <summary>
/// What a package leaves on a node when it ends before any of its problems is solved, through the
/// engine's own entry of the adapter of <c>Smt.SolvePackage</c>: nothing of its job's progress, and
/// no folder. A node keeps one picture of a job's share for all of the job's packages, for as long
/// as one of them is at work: a package that could not even make its work folder must not stay in
/// that picture, or the next package of the job would report beside a package that is not there.
/// No solver runs here: the packages are refused before anything is fetched.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtSolvePackageShareTests
{
    #region Constants

    /// <summary>A controller version no node runs: the package is refused by its reference alone.</summary>
    private const string ANOTHER_VERSION = "0.0.1";

    #endregion

    #region Fields

    private string m_folder = null!;

    private SmtTestBlobService m_blobs = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_share_{Guid.NewGuid():N}");
        m_blobs = new SmtTestBlobService(Path.Combine(m_folder, "blobs"));
    }

    [TearDown]
    public void TearDown()
    {
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Tests

    [Test]
    [CancelAfter(120_000)]
    public async Task APackageWhoseWorkFolderCannotBeMadeLeavesNothingOfItsJobBehindTest()
    {
        var problems = new List<SmtProblemData> { SmtGoldenPackages.Indexed(SmtSolverProblems.Check("p", SmtSolverCases.ONE_SOLUTION), 0) };
        var package = await SmtNodeRig.PublishAsync(problems, new SmtOptionsData(), m_blobs, m_folder);
        package.RequiredControllerVersion = ANOTHER_VERSION;

        var storage = new SmtTestTempStorage(Path.Combine(m_folder, "storage"));
        var job = Guid.NewGuid();

        // The first package of the job: the client's storage has no folder to give (a full disk).
        var manager = new SmtTestProcessingManager();
        storage.RefuseNext(1);
        var failed = await SmtAdapterRig.Start(package, m_blobs, manager, new WitVariableCollection(), job, storage, null);
        var before = manager.Reports.Count;

        // The next package of the same job, on the same node, when the storage gives folders again.
        var pool = new WitVariableCollection();
        var next = await SmtAdapterRig.Start(package, m_blobs, manager, pool, job, storage, null);
        var reports = manager.Reports.Skip(before).ToList();

        foreach (var (fraction, stage) in reports)
            TestContext.Out.WriteLine($"{fraction:F2} {stage}");

        Assert.Multiple(() =>
        {
            Assert.That(failed.Result, Is.EqualTo(WitProcessingResult.Failed), "the package that had no work folder fails");
            Assert.That(failed.Message, Does.Contain(SmtAdapterRig.NodeFailure), "in the fixed words of a failure of the node's own");

            Assert.That(next.Result, Is.EqualTo(WitProcessingResult.Completed), next.Message);
            Assert.That(SmtAdapterRig.Result(pool)?.FaultCode, Is.EqualTo(SmtNodeFaults.VERSION_MISMATCH), "the next package is refused for its own reason");
            Assert.That(reports.LastOrDefault(), Is.EqualTo((1.0, "1/1 problems")), "and the job's picture is that package's alone: the failed one is not counted in it");
            Assert.That(storage.Left, Is.Empty, "no work folder is left in the client's storage");
        });
    }

    #endregion
}
