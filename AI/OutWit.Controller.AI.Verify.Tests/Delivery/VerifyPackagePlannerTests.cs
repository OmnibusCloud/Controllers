using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Delivery;

namespace OutWit.Controller.AI.Verify.Tests.Delivery;

/// <summary>
/// The package plan: one batch per package while a run is small (the split between nodes
/// stays as fine as the batches), about a thousand packages once it is large, a batch never
/// split, the order kept, and the caps on a package's tasks and bytes respected.
/// </summary>
[TestFixture]
public sealed class VerifyPackagePlannerTests
{
    #region Constants

    // Tasks that print a line: 16 KiB of stdout and of stderr, 48 KiB a task by the package's count.
    private static readonly VerifyLimitsData SMALL_OUTPUT = new() { StdoutLimitBytes = 16 * 1024, StderrLimitBytes = 16 * 1024 };

    #endregion

    #region Count Tests

    [Test]
    public void ASmallRunKeepsOneBatchPerPackageTest()
    {
        var batches = Batches(count: 158, tasksPerBatch: 64);

        var packages = VerifyPackagePlanner.Plan(batches);

        Assert.That(packages, Has.Count.EqualTo(158));
        Assert.That(packages.All(package => package.Count == 1), Is.True);
    }

    [Test]
    public void ALargeRunIsCutIntoAboutTheTargetTest()
    {
        // The million-task run of the scale proofs: 7,813 batches of 128, of tasks that keep
        // little output (the output a package may keep is a cap of its own: the next tests).
        var batches = Batches(count: 7813, tasksPerBatch: 128, limits: SMALL_OUTPUT);

        var packages = VerifyPackagePlanner.Plan(batches);

        TestContext.Out.WriteLine($"{batches.Count} batches in {packages.Count} packages, {packages[0].Count} batches in the first");
        Assert.Multiple(() =>
        {
            Assert.That(packages.Count, Is.InRange(VerifyPackagePlanner.TARGET_PACKAGES, VerifyPackagePlanner.TARGET_PACKAGES * 5 / 4));
            Assert.That(packages.Sum(package => package.Count), Is.EqualTo(batches.Count));
            Assert.That(packages.Max(package => package.Sum(batch => batch.Tasks.Count)), Is.LessThanOrEqualTo(VerifyPackagePlanner.MAX_PACKAGE_TASKS));
        });
    }

    [Test]
    public void AHugeRunStopsAtTheTaskCapTest()
    {
        // Ten million tasks: the target would ask for almost ten thousand tasks a package.
        var batches = Batches(count: 78125, tasksPerBatch: 128, shared: true, limits: SMALL_OUTPUT);

        var packages = VerifyPackagePlanner.Plan(batches);

        Assert.That(packages.Max(package => package.Sum(batch => batch.Tasks.Count)), Is.EqualTo(VerifyPackagePlanner.MAX_PACKAGE_TASKS));
    }

    [Test]
    public void H1APackageClosesAtTheOutputItsTasksMayKeepTest()
    {
        // At the default caps a task may keep 576 KiB (its stdout cap twice and its stderr cap),
        // so the 256 MiB a package may keep close it at 455 tasks: three batches of 128. A node
        // never holds a result package larger than that, whatever the run's size.
        var perTask = VerifyPackagePlanner.OutputBytes(VerifySandboxDefaults.Resolve(null, null));
        var batches = Batches(count: 7813, tasksPerBatch: 128, shared: true);

        var packages = VerifyPackagePlanner.Plan(batches);

        Assert.Multiple(() =>
        {
            Assert.That(perTask, Is.EqualTo(576 * 1024));
            Assert.That(VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES / perTask, Is.EqualTo(455));
            Assert.That(packages.Max(package => package.Sum(VerifyPackagePlanner.OutputBytes)), Is.LessThanOrEqualTo(VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES));
            Assert.That(packages.Max(package => package.Sum(batch => batch.Tasks.Count)), Is.EqualTo(3 * 128));
            Assert.That(packages.Sum(package => package.Count), Is.EqualTo(batches.Count));
        });
    }

    #endregion

    #region Shape Tests

    [Test]
    public void BatchesKeepTheirOrderAndAreNeverSplitTest()
    {
        var batches = Batches(count: 5000, tasksPerBatch: 100);

        var flattened = VerifyPackagePlanner.Plan(batches).SelectMany(package => package).ToList();

        Assert.That(flattened, Has.Count.EqualTo(batches.Count));
        Assert.That(flattened.Select((batch, index) => ReferenceEquals(batch, batches[index])).All(same => same), Is.True);
    }

    [Test]
    public void LargeTasksCloseAPackageByBytesTest()
    {
        // Four batches of 3 MB each, in a run large enough to group batches: no package passes the cap.
        var filler = Batches(count: 4000, tasksPerBatch: 512, shared: true);
        var heavy = Enumerable.Range(0, 4).Select(_ => Batch(tasks: 3, sourceLength: 1024 * 1024)).ToList();

        var packages = VerifyPackagePlanner.Plan([.. heavy, .. filler]);

        Assert.That(packages.Take(2).All(package => package.Sum(VerifyPackagePlanner.EstimateBytes) <= VerifyPackagePlanner.MAX_PACKAGE_BYTES), Is.True);
        Assert.That(packages[0].Count, Is.EqualTo(2), "two 3 MB batches fit under the 8 MB cap, the third does not");
    }

    [Test]
    public void ABatchLargerThanTheCapStillMakesAPackageTest()
    {
        var giant = Batch(tasks: 1, sourceLength: (int)VerifyPackagePlanner.MAX_PACKAGE_BYTES + 1);

        var packages = VerifyPackagePlanner.Plan([giant, Batch(tasks: 1, sourceLength: 10)]);

        Assert.That(packages, Has.Count.EqualTo(2));
    }

    [Test]
    public void NoBatchesNoPackagesTest()
    {
        Assert.That(VerifyPackagePlanner.Plan([]), Is.Empty);
    }

    #endregion

    #region Tools

    private static List<VerifyTaskBatchData> Batches(int count, int tasksPerBatch, bool shared = false, VerifyLimitsData? limits = null)
    {
        // "shared" reuses one task list for every batch: the plan reads counts and sizes only,
        // and millions of task objects would be a test of the garbage collector.
        var template = Batch(tasksPerBatch, sourceLength: 20);

        return Enumerable.Range(0, count)
            .Select(_ =>
            {
                var batch = shared ? new VerifyTaskBatchData { RuntimeId = template.RuntimeId, Tasks = template.Tasks } : Batch(tasksPerBatch, sourceLength: 20);
                batch.DefaultLimits = limits;
                return batch;
            })
            .ToList();
    }

    private static VerifyTaskBatchData Batch(int tasks, int sourceLength)
    {
        var content = new string('x', sourceLength);

        return new VerifyTaskBatchData
        {
            RuntimeId = "python-3.14.6",
            Tasks = Enumerable.Range(0, tasks)
                .Select(index => new VerifyTaskData
                {
                    TaskIndex = index,
                    RuntimeId = "python-3.14.6",
                    EntryPoint = "m.py",
                    Sources = [new VerifySourceFileData { Name = "m.py", Content = content }]
                })
                .ToList()
        };
    }

    #endregion
}
