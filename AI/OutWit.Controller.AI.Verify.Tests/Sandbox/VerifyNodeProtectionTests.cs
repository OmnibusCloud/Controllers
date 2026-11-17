using System.Diagnostics;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// What a hostile task cannot do to the node that runs it. Each test is one finding of the audit
/// of 2026-11-17 (its id is in the test's name) with the input the audit used: the test fails on
/// the code the audit read and passes here.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyNodeProtectionTests : VerifySandboxTestBase
{
    #region Constants

    private const long MEBIBYTE = 1024 * 1024;

    #endregion

    #region Fields

    private VerifyResolvedRuntime m_wasi = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void SetUp()
    {
        m_wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason) ?? throw new InvalidOperationException(reason);
    }

    #endregion

    #region C-1 Tests

    [Test]
    public void C1AFloodOfStdoutIsCutAtTheWriteAndEndsTheRunTest()
    {
        // The audit's module: 48 writes of 64 MiB each, about 600 units of fuel. Read back after
        // the run it was 3 GiB on the disk and an exception out of the batch.
        var task = Wasi(HostileModules.Flood(1, 48));
        var limits = VerifySandboxDefaults.Resolve(null, null);

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();
        VerifyResultData result = null!;
        var peakOnDisk = PeakFolderBytes(Sandbox.IoDirectory, () => result = Sandbox.Execute(m_wasi, task, limits));
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.OutputExceeded));
            Assert.That(result.Fault, Is.Empty);
            Assert.That(result.StdoutTruncated, Is.True);
            Assert.That(result.Stdout, Has.Length.EqualTo(limits.StdoutLimitBytes), "what is kept is the cap, to the byte");
            Assert.That(peakOnDisk, Is.LessThan(MEBIBYTE), "nothing of the flood reaches the disk");
            Assert.That(allocated, Is.LessThan(64 * MEBIBYTE), "and the host does not read gigabytes to keep kilobytes");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "the run ends at the write that passes the cap");
            Assert.That(result.FuelConsumed, Is.LessThan(limits.FuelBudget), "by the cap, not by the budget");
        });
    }

    [Test]
    public void C1AFloodOfStderrIsDroppedAtTheWriteAndTheRunGoesOnTest()
    {
        var task = Wasi(HostileModules.Flood(2, 8));
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = 2_000_000_000 }, null);

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        VerifyResultData result = null!;
        var peakOnDisk = PeakFolderBytes(Sandbox.IoDirectory, () => result = Sandbox.Execute(m_wasi, task, limits));
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), "stderr never fails a task by itself");
            Assert.That(result.StderrTruncated, Is.True);
            Assert.That(result.Stderr, Has.Length.EqualTo(limits.StderrLimitBytes));
            Assert.That(peakOnDisk, Is.LessThan(MEBIBYTE));
            Assert.That(allocated, Is.LessThan(64 * MEBIBYTE));
        });
    }

    [Test]
    public void C1AWriteCostsFuelSoAFloodOfStderrEndsByTheBudgetTest()
    {
        // 48 writes of 64 MiB are three thousand million bytes: more than this budget holds.
        var task = Wasi(HostileModules.Flood(2, 48));
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = 1_000_000_000 }, null);

        var result = Sandbox.Execute(m_wasi, task, limits);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
            Assert.That(result.FuelConsumed, Is.EqualTo(limits.FuelBudget));
        });
    }

    [Test]
    public void C1TheCompilersProductIsCappedAtTheWriteTest()
    {
        // An object file of 80 MiB: the compiler's stdout is its product, and went to the disk whole.
        var task = new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.CLANG_22_1_0,
            EntryPoint = "main.c",
            Sources = [new VerifySourceFileData { Name = "main.c", Content = "char big[80 << 20] = {1};\nint main(void) { return big[0] - 1; }\n" }]
        };

        VerifyResultData result = null!;
        var peakOnDisk = PeakFolderBytes(Sandbox.IoDirectory, () => result = Sandbox.Execute(Clang, task, VerifySandboxDefaults.Resolve(null, null)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Is.EqualTo($"clang produced more than {VerifyClangBuild.PRODUCT_LIMIT_BYTES >> 20} MiB"));
            Assert.That(peakOnDisk, Is.LessThanOrEqualTo(VerifyClangBuild.PRODUCT_LIMIT_BYTES + MEBIBYTE), "the product stops growing at its cap");
        });
    }

    #endregion

    #region C-2 Tests

    [Test]
    public void C2AModuleWhoseShapeCostsMoreThanItsBudgetIsNeverCompiledTest()
    {
        // The audit's module at N = 1000: 6.9 KB that took the engine 1.5 s and 189 MiB to compile,
        // outside every limit, and then passed. Its limits were fuel 1,000,000. (At N = 2000 and
        // 4000, 10.6 s and 81 s, the nesting alone refuses it: the next test.)
        var task = Wasi(HostileModules.NestedLoops(1000, 1000));
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = 1_000_000 }, null);

        var clock = Stopwatch.StartNew();
        var result = Sandbox.Execute(m_wasi, task, limits);
        var deeper = Sandbox.Execute(m_wasi, Wasi(HostileModules.NestedLoops(4000, 4000)), limits);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout), "the compile is work for the task: a budget that does not hold it is out of fuel");
            Assert.That(result.FuelConsumed, Is.EqualTo(limits.FuelBudget));
            Assert.That(deeper.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(deeper.Stderr, Does.StartWith("a function of the module nests blocks deeper than"));
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)), "nothing was compiled");
        });
    }

    [Test]
    public void C2TheLargestBudgetDoesNotBuyACompileOfMinutesTest()
    {
        // Within the nesting limit, and by the same law two minutes of compiling: 8,000 locals
        // read inside 1,000 loops. What it costs is more than a task may ask for.
        var task = Wasi(HostileModules.NestedLoops(8000, 1000));
        var limits = VerifyLimitCeilings.Effective(new VerifyLimitsData { FuelBudget = long.MaxValue }, null);

        var clock = Stopwatch.StartNew();
        var result = Sandbox.Execute(m_wasi, task, limits);

        Assert.Multiple(() =>
        {
            Assert.That(limits.FuelBudget, Is.EqualTo(VerifyLimitCeilings.MAX_FUEL_BUDGET));
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
        });
    }

    [Test]
    public void C2CompilingATaskModuleTakesTheWorkersThreadAndNoOtherTest()
    {
        // A module of thousands of small functions was compiled on every core of the node by
        // the engine's own threads, which no degree counts and no priority lowers.
        var task = Wasi(HostileModules.ManyFunctions(3000, 40));
        var limits = VerifySandboxDefaults.Resolve(null, null);
        var process = Process.GetCurrentProcess();

        process.Refresh();
        var cpuBefore = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        var result = Sandbox.Execute(m_wasi, task, limits);
        clock.Stop();
        process.Refresh();
        var cpu = process.TotalProcessorTime - cpuBefore;

        TestContext.Out.WriteLine($"wall {clock.Elapsed.TotalMilliseconds:F0} ms, processor {cpu.TotalMilliseconds:F0} ms, fuel {result.FuelConsumed}");
        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(cpu, Is.LessThan(clock.Elapsed * 1.6 + TimeSpan.FromMilliseconds(400)), "one thread's worth of processor time");
            Assert.That(result.FuelConsumed, Is.GreaterThan(task.Sources[0].Content.Length / 4 * 3 * VerifyModuleShape.COMPILE_FUEL_PER_BYTE - 1), "the compile is in the task's fuel");
        });
    }

    [Test]
    public void C2BlocksNestedDeeperThanTheLimitAreRefusedByNameTest()
    {
        var limits = VerifySandboxDefaults.Resolve(null, null);

        var within = Sandbox.Execute(m_wasi, Wasi(HostileModules.NestedBlocks(VerifyModuleShape.MAX_NESTING)), limits);
        var beyond = Sandbox.Execute(m_wasi, Wasi(HostileModules.NestedBlocks(VerifyModuleShape.MAX_NESTING + 1)), limits);

        Assert.Multiple(() =>
        {
            Assert.That(within.Verdict, Is.EqualTo(VerifyVerdict.Pass), within.Stderr);
            Assert.That(beyond.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(beyond.Stderr, Is.EqualTo($"a function of the module nests blocks deeper than {VerifyModuleShape.MAX_NESTING}"));
        });
    }

    [Test]
    public void C2AModuleLargerThanATaskMayBringIsRefusedBeforeItIsReadTest()
    {
        var task = Wasi(new byte[VerifyWasmSandbox.MAX_TASK_MODULE_BYTES + 1]);

        var result = Sandbox.Execute(m_wasi, task, VerifySandboxDefaults.Resolve(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Is.EqualTo($"the module is larger than {VerifyWasmSandbox.MAX_TASK_MODULE_BYTES >> 20} MiB"));
        });
    }

    #endregion

    #region H-1 Tests

    [Test]
    public void H1TheCasesOfASuiteShareTheTasksStdoutCapTest()
    {
        // Every case kept its own stdout up to the cap: a suite of 2,000 cases of a program that
        // prints 8 MiB held 16 GiB. Here: 40 cases of 100,000 bytes under a cap of 256 KiB.
        var suite = new VerifySuiteData { Cases = Enumerable.Range(0, 40).Select(_ => new VerifySuiteCaseData()).ToList() };
        var limits = VerifySandboxDefaults.Resolve(null, null);

        var result = Sandbox.Execute(m_wasi, Wasi(HostileModules.Print(100_000), suite: suite), limits);
        var kept = result.CaseResults.Sum(item => (long)item.ActualStdout.Length);

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo(limits.StdoutLimitBytes), "the cases together keep the task's cap, to the byte");
            Assert.That(result.CaseResults.Take(2).Select(item => item.Verdict), Is.All.EqualTo(VerifyVerdict.Pass));
            Assert.That(result.CaseResults.Skip(2).Select(item => item.Verdict), Is.All.EqualTo(VerifyVerdict.OutputExceeded));
            Assert.That(result.CaseResults.Skip(3).Select(item => item.ActualStdout), Is.All.Empty, "a case that comes after the cap is spent keeps nothing");
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.OutputExceeded));
            Assert.That(result.CaseResults, Has.Count.EqualTo(40), "every case is still reported");
        });
    }

    [Test]
    public void H1APackagesResultsGoToAFileBatchByBatchTest()
    {
        // A node kept every result of a package in memory until the upload.
        var package = new VerifyTaskPackageData { Ticket = OutWit.Controller.AI.Verify.Delivery.VerifyTickets.New(), TaskCount = 6 };
        var batches = Enumerable.Range(0, 3).Select(batch => new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            Tasks = [Wasi(HostileModules.Print(1000), batch * 2), Wasi(HostileModules.Print(2000), batch * 2 + 1)]
        }).ToList();

        var written = OutWit.Controller.AI.Verify.Delivery.VerifyPackageRunner.Run(package, batches, () => Sandbox, null, null,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
        try
        {
            var read = OutWit.Controller.AI.Verify.Delivery.VerifyResultPackageReader.Read(written.Path, 3, 6).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(Path.GetDirectoryName(written.Path), Is.EqualTo(Sandbox.IoDirectory), "in the sandbox's own folder, the client's on a node");
                Assert.That((written.Results, written.Batches), Is.EqualTo((6, 3)));
                Assert.That(read.Select(batch => batch.Ticket), Is.All.EqualTo(package.Ticket), "every batch names the package it answers");
                Assert.That(read.SelectMany(batch => batch.Results).Select(result => result.Stdout.Length), Is.EqualTo(new[] { 1000, 2000, 1000, 2000, 1000, 2000 }));
            });
        }
        finally
        {
            File.Delete(written.Path);
        }
    }

    #endregion

    #region H-3 Tests

    [TestCase("random_get", -8, 64, TestName = "H3AWildPointerToRandomGetIsBadAddressTest(pointer -8)")]
    [TestCase("random_get", 0, -1, TestName = "H3AWildPointerToRandomGetIsBadAddressTest(length -1)")]
    [TestCase("random_get", 65530, 64, TestName = "H3AWildPointerToRandomGetIsBadAddressTest(past the end)")]
    public void H3AWildPointerToRandomGetIsBadAddressTest(string function, int first, int second)
    {
        // The host indexed the guest's memory with the guest's pointer; the range error left
        // Execute as an exception and took the batch with it.
        var result = Sandbox.Execute(m_wasi, Wasi(HostileModules.ExitWith(function, first, second)), VerifySandboxDefaults.Resolve(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(21), "WASI's errno for a bad address, which the program exits with");
            Assert.That(result.Fault, Is.Empty, "the program's own doing: a verdict");
        });
    }

    [TestCase(-8)]
    [TestCase(65536)]
    [TestCase(65532)]
    public void H3AWildPointerToTheClockIsBadAddressTest(int pointer)
    {
        var result = Sandbox.Execute(m_wasi, Wasi(HostileModules.ClockTo(pointer)), VerifySandboxDefaults.Resolve(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(21));
        });
    }

    [Test]
    public void H3OneCandidateWithAWildPointerDoesNotTakeItsBatchDownTest()
    {
        // The audit's batch: a C candidate between two good ones.
        const string good = "#include <stdio.h>\nint main(void) { puts(\"ok\"); return 0; }\n";
        const string wild = "#include <wasi/api.h>\nint main(void){ return (int)__wasi_random_get((unsigned char*)0xfffffff0u, 64); }\n";
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.CLANG_22_1_0,
            Tasks = new[] { good, wild, good }.Select((source, index) => new VerifyTaskData
            {
                TaskIndex = index,
                RuntimeId = VerifyRuntimeCatalog.CLANG_22_1_0,
                EntryPoint = "main.c",
                Sources = [new VerifySourceFileData { Name = "main.c", Content = source }]
            }).ToList()
        };

        var results = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch).Results;

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(result => result.Verdict), Is.EqualTo(new[] { VerifyVerdict.Pass, VerifyVerdict.RuntimeError, VerifyVerdict.Pass }));
            Assert.That(results[1].ExitCode, Is.EqualTo(21));
            Assert.That(results[0].Stdout, Is.EqualTo("ok\n"));
        });
    }

    [Test]
    public void H3WhatTheNodeCannotDoForATaskIsThatTasksResultWithoutAVerdictTest()
    {
        // The node's own trouble (here: the folder its working files go to cannot be made) left
        // Execute as an exception. It is the task's result now, with a code and nothing else.
        using var sandbox = new VerifyWasmSandbox();
        Directory.Delete(sandbox.IoDirectory, recursive: true);
        File.WriteAllText(sandbox.IoDirectory, "a file where the folder should be");
        try
        {
            var batch = new VerifyTaskBatchData { RuntimeId = VerifyRuntimeCatalog.WASI, Tasks = [Wasi(HostileModules.Print(10), 0), Wasi(HostileModules.Print(10), 1)] };
            var faults = new List<Exception>();

            var results = VerifyBatchExecutor.Execute(sandbox, null, batch, null, CancellationToken.None, (_, fault) => { lock (faults) faults.Add(fault); }).Results;

            Assert.Multiple(() =>
            {
                Assert.That(results.Select(result => result.Verdict), Is.All.EqualTo(VerifyVerdict.NoResult));
                Assert.That(results.Select(result => result.Fault), Is.All.EqualTo(VerifyFault.NODE_FAULT));
                Assert.That(results.Select(result => result.Stderr), Is.All.Empty, "the node's own words stay on the node");
                Assert.That(faults, Has.Count.EqualTo(2), "and go to its log");
            });
        }
        finally
        {
            File.Delete(sandbox.IoDirectory);
        }
    }

    #endregion

    #region M-1 Tests

    [Test]
    public void M1ARunTheClockEndsHasNoVerdictTest()
    {
        // The wall clock ended a run as Timeout: a verdict that depended on the machine.
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = VerifyLimitCeilings.MAX_FUEL_BUDGET }, null);
        Sandbox.BackstopMs = 300;
        try
        {
            var result = Sandbox.Execute(m_wasi, Wasi(HostileModules.Spin()), limits);

            Assert.Multiple(() =>
            {
                Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.NoResult));
                Assert.That(result.Fault, Is.EqualTo(VerifyFault.WALL_CLOCK));
                Assert.That(result.FuelConsumed, Is.Zero, "a fault carries no cost that could pass for a budget stop");
            });
        }
        finally
        {
            Sandbox.BackstopMs = null;
        }
    }

    [Test]
    public void M1ABusyLoopEndsByItsFuelWhateverWallTimeTheTaskAsksForTest()
    {
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = 1_000_000_000, WallTimeMs = 1 }, null);

        var result = Sandbox.Execute(m_wasi, Wasi(HostileModules.Spin()), limits);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout), "Timeout means out of fuel");
            Assert.That(result.FuelConsumed, Is.EqualTo(limits.FuelBudget));
        });
    }

    [Test]
    public void M1AWallTimeInTheLimitsEndsNothingTest()
    {
        // One millisecond of wall time: on the code the audit read this hello was a Timeout.
        var result = Run(Python, PythonTask("print('hello')"), new VerifyLimitsData { WallTimeMs = 1 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
    }

    [Test]
    public void M1ACallIntoTheHostCostsFuelByTheBytesItMovesTest()
    {
        // Twelve fills of 250 MiB with random bytes: five seconds of the host's processor for 133
        // units of fuel, and no clock could interrupt them.
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = 1_000_000_000 }, null);

        var result = Sandbox.Execute(m_wasi, Wasi(HostileModules.RandomFlood(12)), limits);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout), "three thousand million bytes do not fit a budget of one thousand million");
            Assert.That(result.FuelConsumed, Is.EqualTo(limits.FuelBudget));
        });
    }

    [Test]
    public void M1TheBackstopIsSizedFromTheFuelBudgetTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VerifySandboxDefaults.DEFAULT_FUEL_BUDGET, Is.EqualTo(20_000_000_000), "ten seconds of a busy loop at the reference rate");
            Assert.That(VerifyLimitCeilings.MAX_FUEL_BUDGET, Is.EqualTo(240_000_000_000), "two minutes of one");
            Assert.That(VerifyBackstop.WallMs(VerifySandboxDefaults.DEFAULT_FUEL_BUDGET), Is.EqualTo(50_000));
            Assert.That(VerifyBackstop.WallMs(VerifyLimitCeilings.MAX_FUEL_BUDGET), Is.EqualTo(490_000));
            Assert.That(VerifyBackstop.WallMs(0), Is.EqualTo(VerifyBackstop.BASE_MS));
            Assert.That(VerifyBackstop.WallMs(long.MaxValue), Is.EqualTo(int.MaxValue));
        });
    }

    #endregion

    #region M-2 Tests

    [Test]
    public void M2ANodeHoldsABatchToTheCeilingsWhateverTheBatchSaysTest()
    {
        // A batch that did not come from Split: memory 3 GiB, stdout 64 MiB, a degree of 4,096.
        // It ran as asked: 2.5 GiB of memory, 20 MiB of stdout returned whole.
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6,
            MaxDegree = 4096,
            DefaultLimits = new VerifyLimitsData { MemoryBytes = 3L << 30, StdoutLimitBytes = 64 << 20, FuelBudget = VerifyLimitCeilings.MAX_FUEL_BUDGET * 10 },
            Tasks =
            [
                PythonTask("import sys\nsys.stdout.write('x' * (20 << 20))\n", 0),
                PythonTask("x = bytearray(2040 << 20)\nprint(len(x))\n", 1)
            ]
        };

        var executed = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch);

        Assert.Multiple(() =>
        {
            Assert.That(executed.Results[0].Verdict, Is.EqualTo(VerifyVerdict.OutputExceeded));
            Assert.That(executed.Results[0].Stdout, Has.Length.EqualTo(VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES), "the stdout ceiling, not what the batch asked for");
            Assert.That(executed.Results[1].Verdict, Is.EqualTo(VerifyVerdict.MemoryExceeded), "the memory ceiling: an interpreter and 2,040 MiB do not fit 2 GiB");
            Assert.That(executed.Diagnostics!.Degree, Is.LessThanOrEqualTo(Environment.ProcessorCount));
        });
    }

    [Test]
    public void M2TheSingleTaskActivityIsGoneTest()
    {
        // Verify.Execute took a task inline and returned its result inline, outside the package
        // caps, the execution slot and the job's cancellation.
        var activities = typeof(VerifyWasmSandbox).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributesData())
            .Where(attribute => attribute.AttributeType.Name == "ActivityAttribute")
            .Select(attribute => attribute.ConstructorArguments[0].Value as string)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(activities, Does.Contain("Verify.ExecutePackage"));
            Assert.That(activities, Does.Not.Contain("Verify.Execute"));
        });
    }

    [Test]
    public void M2ANodeRefusesAPackageOutsideWhatANodeTakesTest()
    {
        var oversized = new VerifyTaskBatchData { RuntimeId = VerifyRuntimeCatalog.WASI, Tasks = Enumerable.Range(0, VerifyBatchExecutor.MAX_BATCH_TASKS + 1).Select(index => Wasi([], index)).ToList() };
        var greedy = new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            DefaultLimits = new VerifyLimitsData { StdoutLimitBytes = VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES },
            Tasks = Enumerable.Range(0, 8).Select(index => Wasi([], index)).ToList()
        };

        Assert.Multiple(() =>
        {
            Assert.That(() => VerifyBatchExecutor.EnsureRunnable(null, oversized), Throws.TypeOf<VerifyBatchRefusedException>().With.Message.Contains("no more than 4096"));
            Assert.That(() => OutWit.Controller.AI.Verify.Delivery.VerifyPackageRunner.Prepare([greedy], () => Sandbox, null),
                Throws.TypeOf<VerifyBatchRefusedException>().With.Message.EqualTo("a batch may keep more output than a node takes"));
        });
    }

    #endregion

    #region Tools

    private static VerifyTaskData Wasi(byte[] module, int index = 0, VerifySuiteData? suite = null, long seed = 0)
    {
        return new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = VerifyRuntimeCatalog.WASI,
            EntryPoint = "main.wasm",
            Sources = [new VerifySourceFileData { Name = "main.wasm", Content = Convert.ToBase64String(module) }],
            Suite = suite,
            RandomSeed = seed
        };
    }

    /// <summary>The most bytes a folder held at any moment while an action ran, as seen every few milliseconds.</summary>
    private static long PeakFolderBytes(string folder, Action action)
    {
        long peak = 0;
        using var done = new ManualResetEventSlim(false);
        var watcher = new Thread(() =>
        {
            while (!done.Wait(5))
                peak = Math.Max(peak, FolderBytes(folder));
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        watcher.Start();

        try
        {
            action();
        }
        finally
        {
            done.Set();
            watcher.Join();
        }

        return Math.Max(peak, FolderBytes(folder));
    }

    private static long FolderBytes(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Exists ? file.Length : 0)
                : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that went between the listing and the look.
            return 0;
        }
    }

    #endregion
}
