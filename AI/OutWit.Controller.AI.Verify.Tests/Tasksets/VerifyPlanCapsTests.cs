using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The caps of a batch and of a package are caps (findings M-5 and H-1 of the audit of
/// 2026-11-17): a batch is cut by its tasks, by their text and by the output they may keep, so
/// no package is larger than the documents say, whatever batch size a run asks for.
/// </summary>
[TestFixture]
public sealed class VerifyPlanCapsTests
{
    #region Tests

    [Test]
    public void M5ABatchSizeAboveTheCapIsClampedTest()
    {
        // 20,000 tasks with a batch size of a million made one batch and one package of 20,000.
        var tasks = Enumerable.Range(0, 20_000).Select(index => Task(index, "print(1)")).ToList();

        var plan = VerifyTasksetPlanner.Plan(tasks, new VerifyOptionsData { BatchSize = 1_000_000 });
        var packages = VerifyPackagePlanner.Plan(plan.Batches);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Notes, Does.Contain($"batch size 1000000 clamped to {VerifyBatchExecutor.MAX_BATCH_TASKS}"));
            Assert.That(plan.Batches.Max(batch => batch.Tasks.Count), Is.LessThanOrEqualTo(VerifyBatchExecutor.MAX_BATCH_TASKS));
            Assert.That(packages.Max(package => package.Sum(batch => batch.Tasks.Count)), Is.LessThanOrEqualTo(VerifyPackagePlanner.MAX_PACKAGE_TASKS));
            Assert.That(plan.Batches.Sum(batch => batch.Tasks.Count), Is.EqualTo(20_000), "no task is lost to a cap");
        });
    }

    [Test]
    public void M5ABatchIsCutByItsTextAsWellAsByItsCountTest()
    {
        // 64 tasks of 1 MiB at the default batch size made packages of 32 MiB.
        var megabyte = new string('#', 1 << 20);
        var tasks = Enumerable.Range(0, 64).Select(index => Task(index, megabyte)).ToList();

        var plan = VerifyTasksetPlanner.Plan(tasks, null);
        var packages = VerifyPackagePlanner.Plan(plan.Batches);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Batches.Max(VerifyPackagePlanner.EstimateBytes), Is.LessThanOrEqualTo(VerifyPackagePlanner.MAX_PACKAGE_BYTES));
            Assert.That(packages.Max(package => package.Sum(VerifyPackagePlanner.EstimateBytes)), Is.LessThanOrEqualTo(VerifyPackagePlanner.MAX_PACKAGE_BYTES));
            Assert.That(plan.Batches.Sum(batch => batch.Tasks.Count), Is.EqualTo(64));
        });
    }

    [Test]
    public void M5ATaskLargerThanAPackageIsRefusedByItsIndexTest()
    {
        var tasks = new List<VerifyTaskData> { Task(0, "print(1)"), Task(7, new string('#', (int)VerifyPackagePlanner.MAX_PACKAGE_BYTES + 1)) };

        var plan = VerifyTasksetPlanner.Plan(tasks, null);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Has.Count.EqualTo(1));
            Assert.That(plan.Errors[0], Does.StartWith("task 7: its text is ").And.EndsWith($"a task may have {VerifyPackagePlanner.MAX_PACKAGE_BYTES}"));
            Assert.That(plan.Batches.Sum(batch => batch.Tasks.Count), Is.EqualTo(1), "the tasks that fit are planned; Split refuses the taskset for the one that does not");
        });
    }

    [Test]
    public void H1ABatchAndAPackageMayKeepNoMoreOutputThanTheirBudgetTest()
    {
        // Every task at the stdout ceiling: a package of 4,096 of them could keep 64 GiB.
        var limits = new VerifyLimitsData { StdoutLimitBytes = VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES, StderrLimitBytes = VerifyLimitCeilings.MAX_STDERR_LIMIT_BYTES };
        var tasks = Enumerable.Range(0, 200).Select(index => Task(index, "print(1)")).ToList();

        var plan = VerifyTasksetPlanner.Plan(tasks, new VerifyOptionsData { DefaultLimits = limits, BatchSize = 4096 });
        var packages = VerifyPackagePlanner.Plan(plan.Batches);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Batches.Max(VerifyPackagePlanner.OutputBytes), Is.LessThanOrEqualTo(VerifyTasksetPlanner.MAX_BATCH_OUTPUT_BYTES));
            Assert.That(packages.Max(package => package.Sum(VerifyPackagePlanner.OutputBytes)), Is.LessThanOrEqualTo(VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES));
            Assert.That(plan.Batches.Max(batch => batch.Tasks.Count), Is.EqualTo(3), "three tasks of 17 MiB of output fit a batch of 64");
        });
    }

    [Test]
    public void ATaskAtTheDefaultCapsLeavesTheBatchSizeAloneTest()
    {
        var tasks = Enumerable.Range(0, 100).Select(index => Task(index, "print(1)")).ToList();

        var plan = VerifyTasksetPlanner.Plan(tasks, null);

        Assert.That(plan.Batches.Select(batch => batch.Tasks.Count), Is.EqualTo(new[] { 32, 32, 32, 4 }));
    }

    [Test]
    public void ABatchNamesTheBytesItsRuntimeIsPinnedToTest()
    {
        var plan = VerifyTasksetPlanner.Plan([Task(0, "print(1)")], null);

        Assert.That(plan.Batches[0].RuntimeSha256, Is.EqualTo(OutWit.Controller.AI.Verify.Runtimes.VerifyRuntimeCatalog.Find("python-3.14.6")!.Sha256));
    }

    #endregion

    #region Tools

    private static VerifyTaskData Task(int index, string source)
    {
        return new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = "python-3.14.6",
            EntryPoint = "m.py",
            Sources = [new VerifySourceFileData { Name = "m.py", Content = source }]
        };
    }

    #endregion
}
