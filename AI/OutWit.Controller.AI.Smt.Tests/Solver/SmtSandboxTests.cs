using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The sandbox and its module: a file that is not the pinned module is refused before a byte of
/// it is compiled, a machine without the module says so in fixed words that name no path of
/// the machine, the module is found by the variable or beside the controller, and the
/// process-wide sandbox is one sandbox, compiled once.
/// </summary>
[TestFixture]
public sealed class SmtSandboxTests
{
    #region Fields

    private string m_folder = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_folder);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (m_folder != null && Directory.Exists(m_folder))
            Directory.Delete(m_folder, recursive: true);
    }

    #endregion

    #region Module Tests

    [Test]
    public void AFileOfAnotherSizeIsRefusedBeforeAnythingIsCompiledTest()
    {
        var path = Path.Combine(m_folder, "short.wasm");
        File.WriteAllBytes(path, [0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00]);

        var exception = Assert.Throws<InvalidDataException>(() => new SmtSandbox(path).Dispose());

        Assert.That(exception!.Message, Is.EqualTo($"the solver module has 8 bytes, not the pinned {SmtProfile.MODULE_SIZE}"));
    }

    [Test]
    public void AFileOfTheRightSizeAndOtherBytesIsRefusedByItsHashTest()
    {
        // The size is the module's, so only the hash can tell: zeros hash to something else.
        var path = Path.Combine(m_folder, "zeros.wasm");
        using (var file = File.Create(path))
            file.SetLength(SmtProfile.MODULE_SIZE);

        var exception = Assert.Throws<InvalidDataException>(() => new SmtSandbox(path).Dispose());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.StartWith("the solver module hashes to "));
            Assert.That(exception.Message, Does.EndWith($", not the pinned {SmtProfile.MODULE_SHA256}"));
        });
    }

    [Test]
    [Category("Solver")]
    public void TheRealModuleWithOneByteChangedIsRefusedByItsHashTest()
    {
        var bytes = File.ReadAllBytes(SmtSolverModule.Require());
        bytes[^1] ^= 0x01;
        var path = Path.Combine(m_folder, "flipped.wasm");
        File.WriteAllBytes(path, bytes);

        var exception = Assert.Throws<InvalidDataException>(() => new SmtSandbox(path).Dispose());

        Assert.That(exception!.Message, Does.StartWith("the solver module hashes to "));
    }

    [Test]
    public void AMachineWithoutTheModuleSaysSoInFixedWordsTest()
    {
        var sandbox = SmtSandboxHost.Create(null, null, out var problem);

        Assert.Multiple(() =>
        {
            Assert.That(sandbox, Is.Null);
            Assert.That(problem, Is.EqualTo("the solver module (wasm/z3-5.4.1.wasm) is not in the controller's folder"));
        });
    }

    [Test]
    public void AModuleThatIsNotThePinnedOneIsSaidWithoutAPathOfTheMachineTest()
    {
        var path = Path.Combine(m_folder, "other.wasm");
        File.WriteAllBytes(path, [1, 2, 3]);

        var sandbox = SmtSandboxHost.Create(path, null, out var problem);

        Assert.Multiple(() =>
        {
            Assert.That(sandbox, Is.Null);
            Assert.That(problem, Is.EqualTo($"the solver's sandbox cannot start: InvalidDataException: the solver module has 3 bytes, not the pinned {SmtProfile.MODULE_SIZE}"));
            Assert.That(problem, Does.Not.Contain(m_folder));
        });
    }

    [Test]
    public void AModuleFileThatCannotBeReadIsSaidByTheKindOfFailureAloneTest()
    {
        var path = Path.Combine(m_folder, "missing", "z3.wasm");

        var sandbox = SmtSandboxHost.Create(path, null, out var problem);

        Assert.Multiple(() =>
        {
            Assert.That(sandbox, Is.Null);
            Assert.That(problem, Is.EqualTo("the solver's sandbox cannot start: DirectoryNotFoundException reading the module or loading the engine library"));
            Assert.That(problem, Does.Not.Contain(m_folder), "the exception's own text names the file: it is not passed on");
        });
    }

    #endregion

    #region Scope Tests

    [Test]
    public void AProcessWithoutTheModuleMakesNoScopeForItsSandboxTest()
    {
        // A node without the module asks for its sandbox again at every package and at every
        // benchmark: an attempt that the cheap checks end leaves nothing in the client's storage.
        var storage = new SmtTestTempStorage(Path.Combine(m_folder, "storage-without-module"));

        var problem = SmtSolverRig.MakeAndClose(null, storage);

        Assert.Multiple(() =>
        {
            Assert.That(problem, Is.EqualTo("the solver module (wasm/z3-5.4.1.wasm) is not in the controller's folder"));
            Assert.That(storage.Made, Is.Zero, "no scope is made for a sandbox the cheap checks already refuse");
            Assert.That(storage.Left, Is.Empty);
        });
    }

    [Test]
    public void ASandboxThatCouldNotBeMadeGivesItsScopeBackTest()
    {
        // A module with other bytes passes the cheap checks and is refused when it is read: the
        // scope that was made for the sandbox's files goes back, at every attempt.
        var storage = new SmtTestTempStorage(Path.Combine(m_folder, "storage-other-module"));
        var path = Path.Combine(m_folder, "other-bytes.wasm");
        File.WriteAllBytes(path, [1, 2, 3]);

        var problems = Enumerable.Range(0, 3).Select(_ => SmtSolverRig.MakeAndClose(path, storage)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.All.StartsWith("the solver's sandbox cannot start: InvalidDataException"));
            Assert.That(storage.Made, Is.EqualTo(3), "a scope an attempt");
            Assert.That(storage.Left, Is.Empty, "and none of them left behind");
        });
    }

    [Test]
    [Category("Solver")]
    public void ASandboxThatWasMadeKeepsItsOwnFilesInItsScopeOfTheHostsStorageTest()
    {
        var storage = new SmtTestTempStorage(Path.Combine(m_folder, "storage-real-module"));
        IReadOnlyList<string> open = [];

        var problem = SmtSolverRig.MakeAndClose(SmtSolverModule.Require(), storage, () => open = storage.Left);

        Assert.Multiple(() =>
        {
            Assert.That(problem, Is.Null);
            Assert.That(storage.Made, Is.EqualTo(1));
            Assert.That(open, Has.Count.EqualTo(1), "the sandbox's own files have one scope while it is open");
            Assert.That(open, Has.All.StartsWith(storage.RootPath), "inside the host's storage, which on a contributor's machine is the client's folder");
            Assert.That(storage.Left, Is.Empty, "and the scope goes with the sandbox");
        });
    }

    #endregion

    #region Locator Tests

    [Test]
    public void TheModuleIsLookedForUnderTheVariableTheTestsSetTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SmtModuleLocator.OVERRIDE_VARIABLE, Is.EqualTo(SmtTestEnvironment.MODULE_VARIABLE));
            Assert.That(SmtModuleLocator.RelativePath, Is.EqualTo(SmtSolverModule.RELATIVE_PATH));
        });
    }

    [Test]
    [NonParallelizable]
    public void TheVariableNamesTheModulesFileAndAFileThatIsNotThereIsNoModuleTest()
    {
        var before = Environment.GetEnvironmentVariable(SmtModuleLocator.OVERRIDE_VARIABLE);
        var present = Path.Combine(m_folder, "present.wasm");
        File.WriteAllBytes(present, [0]);
        try
        {
            Environment.SetEnvironmentVariable(SmtModuleLocator.OVERRIDE_VARIABLE, present);
            var found = SmtModuleLocator.Locate();

            Environment.SetEnvironmentVariable(SmtModuleLocator.OVERRIDE_VARIABLE, Path.Combine(m_folder, "absent.wasm"));
            var missing = SmtModuleLocator.Locate();

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.EqualTo(Path.GetFullPath(present)));
                Assert.That(missing, Is.Null, "the variable is not a hint: when it is set, nothing else is looked at");
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(SmtModuleLocator.OVERRIDE_VARIABLE, before);
        }
    }

    #endregion

    #region Process Tests

    [Test]
    [Category("Solver")]
    public void TheProcessHasOneSandboxCompiledOnceTest()
    {
        SmtSolverModule.Require();

        var first = SmtSandboxHost.Get(out var problem);
        var second = SmtSandboxHost.Get(out _);

        Assert.That(first, Is.Not.Null, problem);
        TestContext.Out.WriteLine($"the solver module compiled in {first!.CompileMs} ms in this process; explicit trap checks: {first.ExplicitTraps}");
        Assert.Multiple(() =>
        {
            Assert.That(problem, Is.Null);
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.CompileMs, Is.GreaterThan(0), "how long the compile took is kept, for the node's diagnostics");
            Assert.That(first.CompiledCodeKey, Does.EndWith("-exceptions"), "the solver's engine takes WebAssembly exceptions, and its compiled code is keyed so");
        });
    }

    #endregion
}
