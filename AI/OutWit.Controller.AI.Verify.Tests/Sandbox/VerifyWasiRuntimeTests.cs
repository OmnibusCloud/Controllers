using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The raw WASI runtime: a task's entry source is the command module itself. One test per
/// verdict path with hand-assembled modules (no interpreter, no runtime download), plus the
/// parser's base64 contract and the batch executor running without a runtimes root.
/// </summary>
[TestFixture]
public sealed class VerifyWasiRuntimeTests
{
    #region Constants

    /// <summary>
    /// The hello module as the README shows it: Hello("hello\n") assembled by <see cref="WasiModules"/>,
    /// pinned so a change to the assembler is a deliberate one.
    /// </summary>
    private const string HELLO_BASE64 =
        "AGFzbQEAAAABEANgBH9/f38Bf2ABfwBgAAACZwMWd2FzaV9zbmFwc2hvdF9wcmV2aWV3MQhmZF93cml0ZQAAFndhc2lfc25hcHNob3RfcHJldmlldzEHZmRfcmVhZAAAFndhc2lfc25hcHNob3RfcHJldmlldzEJcHJvY19leGl0AAEDAgECBQMBAAEHEwIGbWVtb3J5AgAGX3N0YXJ0AAMKDwENAEEBQQBBAUEIEAAaCwssAQBBAAsmIAAAAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABoZWxsbwo=";

    #endregion

    #region Fields

    private VerifyWasmSandbox m_sandbox = null!;
    private VerifyResolvedRuntime m_wasi = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason)
                 ?? throw new InvalidOperationException(reason);
        m_sandbox = new VerifyWasmSandbox();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        m_sandbox.Dispose();
    }

    #endregion

    #region Verdict Tests

    [Test]
    public void HelloModulePassesWithItsOutputTest()
    {
        var result = Run(Task(WasiModules.Hello("hello\n")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.Stdout, Is.EqualTo("hello\n"));
            Assert.That(result.PeakMemoryBytes, Is.EqualTo(65536));
        });
    }

    [Test]
    public void HelloModuleBytesMatchTheReferenceTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToBase64String(WasiModules.Hello("hello\n")), Is.EqualTo(HELLO_BASE64));
            Assert.That(VerifySelfCheck.HELLO_WASM_BASE64, Is.EqualTo(HELLO_BASE64), "the self-check probe and the README example are this module");
        });
    }

    [Test]
    public void EchoModuleWithSuitePassesAndFailsByOutputTest()
    {
        var suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Stdin = "ping\n", ExpectedStdout = "ping\n" },
                new VerifySuiteCaseData { Stdin = "pong\n", ExpectedStdout = "pong\n" }
            ]
        };
        var passing = Run(Task(WasiModules.Echo(), suite: suite));

        suite.Cases[1].ExpectedStdout = "PONG\n";
        var failing = Run(Task(WasiModules.Echo(), suite: suite));

        Assert.Multiple(() =>
        {
            Assert.That(passing.Verdict, Is.EqualTo(VerifyVerdict.Pass));
            Assert.That(passing.CaseResults.All(c => c.Passed), Is.True);
            Assert.That(failing.Verdict, Is.EqualTo(VerifyVerdict.Fail));
            Assert.That(failing.CaseResults[0].Passed, Is.True);
            Assert.That(failing.CaseResults[1].Passed, Is.False);
            Assert.That(failing.CaseResults[1].ActualStdout, Is.EqualTo("pong\n"));
        });
    }

    [Test]
    public void ExitCodeIsRuntimeErrorUnlessTheSuiteExpectsItTest()
    {
        var plain = Run(Task(WasiModules.Exit(7)));
        var expected = Run(Task(WasiModules.Exit(7), suite: new VerifySuiteData
        {
            Cases = [new VerifySuiteCaseData { ExpectedExitCode = 7 }]
        }));

        Assert.Multiple(() =>
        {
            Assert.That(plain.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(plain.ExitCode, Is.EqualTo(7));
            Assert.That(expected.Verdict, Is.EqualTo(VerifyVerdict.Pass));
        });
    }

    [Test]
    public void SpinningModuleIsTimeoutTest()
    {
        var result = Run(Task(WasiModules.Spin()), new VerifyLimitsData { FuelBudget = 1_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
        Assert.That(result.FuelConsumed, Is.EqualTo(1_000_000));
    }

    [Test]
    public void OutOfBoundsAccessIsARuntimeErrorNamedOnStderrTest()
    {
        // A wild pointer is the program's fault, not a refusal of memory.
        var result = Run(Task(WasiModules.OutOfBounds()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.Stderr, Does.StartWith("wasm trap: out of bounds memory access"));
        });
    }

    [Test]
    public void ABudgetStopNamesNothingTest()
    {
        // One node may stop on fuel where another stops on the clock: the outputs must agree.
        var result = Run(Task(WasiModules.Spin()), new VerifyLimitsData { FuelBudget = 1_000_000, WallTimeMs = 30_000 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    [Test]
    public void GrowIsBoundedByTheMemoryCapTest()
    {
        // 64 pages allowed: 1 + 16 + 16 + 16 = 49, the next grow would reach 65 and is refused.
        var result = Run(Task(WasiModules.GrowUntilFull()), new VerifyLimitsData { MemoryBytes = 64 * 65536 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(49));
            Assert.That(result.PeakMemoryBytes, Is.EqualTo(49 * 65536));
        });
    }

    [Test]
    public void ATableIsBoundedByItsOwnCapTest()
    {
        // A table is host memory outside the linear memory cap. The module may grow its
        // table to 64 times the sandbox's cap by its own declaration; the sandbox stops it
        // at the cap: 1,048,576 elements, sixteen steps of 65,536.
        var result = Run(Task(WasiModules.TableBomb()), new VerifyLimitsData { MemoryBytes = 64 * 65536 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo((int)(VerifyWasmSandbox.MAX_TABLE_ELEMENTS >> WasiModules.TABLE_STEP_SHIFT)));
        });
    }

    [Test]
    public void ASecondLinearMemoryIsCompileErrorTest()
    {
        // The memory cap is per linear memory: a module is given one.
        var result = Run(Task(WasiModules.TwoMemories()));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError), result.Stderr);
    }

    [Test]
    public void SixtyFourBitMemoriesAndTablesAreCompileErrorTest()
    {
        var memory = Run(Task(WasiModules.Memory64()));
        var table = Run(Task(WasiModules.Table64()));

        Assert.Multiple(() =>
        {
            Assert.That(memory.Verdict, Is.EqualTo(VerifyVerdict.CompileError), memory.Stderr);
            Assert.That(table.Verdict, Is.EqualTo(VerifyVerdict.CompileError), table.Stderr);
        });
    }

    [Test]
    public void RenumberingADescriptorIsNotImplementedTest()
    {
        // WASI errno 52 (nosys): the call exists for the linker and does nothing.
        var result = Run(Task(WasiModules.RenumberDescriptor()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(52));
        });
    }

    [Test]
    public void MissingStartIsRuntimeErrorTest()
    {
        var result = Run(Task(WasiModules.NoStart("hello\n")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(-1));
            Assert.That(result.Stderr, Does.Contain("_start"));
        });
    }

    [Test]
    public void InvalidBytesAreCompileErrorTest()
    {
        var result = Run(Task(WasiModules.Invalid()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Does.Contain("compile"));
        });
    }

    [Test]
    public void NotBase64IsCompileErrorTest()
    {
        var task = Task(WasiModules.Hello("x"));
        task.Sources[0].Content = "print('this is not a module')";

        var result = Run(task);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Does.Contain("base64"));
        });
    }

    [Test]
    public void UnknownImportIsCompileErrorTest()
    {
        var result = Run(Task(WasiModules.UnknownImport("hello\n")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Does.Contain("instantiate"));
            Assert.That(result.Stdout, Is.Empty);
        });
    }

    [Test]
    public void SameModuleRunsDeterministicallyTest()
    {
        var first = Run(Task(WasiModules.Hello("again\n")));
        var second = Run(Task(WasiModules.Hello("again\n")));

        Assert.Multiple(() =>
        {
            Assert.That(second.Stdout, Is.EqualTo(first.Stdout));
            Assert.That(second.FuelConsumed, Is.EqualTo(first.FuelConsumed));
            Assert.That(first.FuelConsumed, Is.GreaterThan(0));
        });
    }

    #endregion

    #region Pipeline Tests

    [Test]
    public void WasiRuntimeResolvesWithoutARootTest()
    {
        var resolved = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason);
        var python = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.PYTHON_3_14_6, out var pythonReason);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.Not.Null);
            Assert.That(reason, Is.Null);
            Assert.That(resolved!.Descriptor.TaskSupplied, Is.True);
            Assert.That(python, Is.Null);
            Assert.That(pythonReason, Does.Contain("root"));
        });
    }

    [Test]
    public void BatchExecutorRunsAWasiBatchWithoutARuntimesRootTest()
    {
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            Tasks = [Task(WasiModules.Hello("a\n"), 0), Task(WasiModules.Exit(3), 1), Task(WasiModules.Hello("c\n"), 2)]
        };

        var result = VerifyBatchExecutor.Execute(m_sandbox, null, batch);

        Assert.Multiple(() =>
        {
            Assert.That(result.Results.Select(r => r.Verdict), Is.EqualTo(new[] { VerifyVerdict.Pass, VerifyVerdict.RuntimeError, VerifyVerdict.Pass }));
            Assert.That(result.Results[2].Stdout, Is.EqualTo("c\n"));
            Assert.That(result.Diagnostics!.RuntimeId, Is.EqualTo(VerifyRuntimeCatalog.WASI));
            Assert.That(result.Diagnostics.RuntimeSha256, Is.Empty);
            Assert.That(result.Diagnostics.Degree, Is.GreaterThan(0));
        });
    }

    [Test]
    public void ManyDistinctModulesInOneParallelBatchAllRunTest()
    {
        // More distinct modules than the cache holds, several workers at once: a module in
        // flight is never evicted under another worker's feet (the load suite found that one).
        var count = VerifyWasmSandbox.TaskModuleCacheCapacity + 40;
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            MaxDegree = 8,
            Tasks = Enumerable.Range(0, count).Select(i => Task(WasiModules.Hello($"m{i}\n"), i, new VerifySuiteData
            {
                Cases = [new VerifySuiteCaseData { ExpectedStdout = $"m{i}\n" }]
            })).ToList()
        };

        var result = VerifyBatchExecutor.Execute(m_sandbox, null, batch);

        Assert.Multiple(() =>
        {
            Assert.That(result.Results.Select(r => r.Verdict).Distinct(), Is.EqualTo(new[] { VerifyVerdict.Pass }),
                string.Join("; ", result.Results.Where(r => r.Verdict != VerifyVerdict.Pass).Take(3).Select(r => $"{r.TaskIndex}: {r.Verdict} {r.Stderr}")));
            Assert.That(m_sandbox.TaskModuleCacheCount, Is.LessThanOrEqualTo(VerifyWasmSandbox.TaskModuleCacheCapacity));
        });
    }

    [Test]
    public void ParserAcceptsBase64AndRejectsAnythingElseForWasiTest()
    {
        var good = "{\"runtime\":\"wasi\",\"entry\":\"hello.wasm\",\"sources\":{\"hello.wasm\":\"" + HELLO_BASE64 + "\"},\"args\":[\"one\"]}";
        var bad = "{\"runtime\":\"wasi\",\"sources\":{\"hello.wasm\":\"print(1)\"}}";

        var parsed = VerifyTasksetParser.Parse(good + "\n" + bad + "\n");

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Has.Count.EqualTo(1));
            Assert.That(parsed.Tasks[0].EntryPoint, Is.EqualTo("hello.wasm"));
            Assert.That(parsed.Errors, Has.Count.EqualTo(1));
            Assert.That(parsed.Errors[0], Does.Contain("line 2").And.Contain("base64"));
        });

        var result = Run(parsed.Tasks[0]);
        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(result.Stdout, Is.EqualTo("hello\n"));
    }

    #endregion

    #region Helpers

    private static VerifyTaskData Task(byte[] module, int index = 0, VerifySuiteData? suite = null)
    {
        return new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = VerifyRuntimeCatalog.WASI,
            Sources = [new VerifySourceFileData { Name = "main.wasm", Content = Convert.ToBase64String(module) }],
            EntryPoint = "main.wasm",
            Suite = suite
        };
    }

    private VerifyResultData Run(VerifyTaskData task, VerifyLimitsData? limits = null)
    {
        return m_sandbox.Execute(m_wasi, task, VerifySandboxDefaults.Resolve(limits, null));
    }

    #endregion
}
