using System.Security.Cryptography;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The raw-WASI runtime on modules a real toolchain built: one C program (wasi-sdk) and one
/// Rust program, the way a lab compiles its own. The modules are files in the repository,
/// built by the toolchain job from the sources next to it; the job rebuilds them and compares
/// the bytes, and the hashes here are the job's. The hand-assembled modules prove the edges
/// of the format; these prove that what a compiler emits (a C library, an allocator, a
/// formatter, a panic handler) runs under the sandbox as written.
/// </summary>
[TestFixture]
public sealed class VerifyWasiToolchainFixtureTests
{
    #region Constants

    private const string FIXTURES = "Fixtures";
    private const string SUM = "sum.wasm";
    private const string WORDS = "words.wasm";

    private const string SUM_SHA256 = "e1167ec86a2e6b984ba216caf68e34d251dedb56579ef87855e46dad266d7112";
    private const string WORDS_SHA256 = "a95168d0b8fae7da1732c387dd16a216c38ca1d8f58288e372ec4a2460b998eb";

    private const long MEBIBYTE = 1024 * 1024;

    #endregion

    #region Fields

    private VerifyWasmSandbox m_sandbox = null!;
    private VerifyResolvedRuntime m_wasi = null!;
    private byte[] m_sum = null!;
    private byte[] m_words = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Sandbox", FIXTURES);
        m_sum = File.ReadAllBytes(Path.Combine(folder, SUM));
        m_words = File.ReadAllBytes(Path.Combine(folder, WORDS));

        m_wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason)
                 ?? throw new InvalidOperationException(reason);
        m_sandbox = new VerifyWasmSandbox();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        m_sandbox?.Dispose();
    }

    #endregion

    #region Pin Tests

    [Test]
    public void TheFixturesAreTheToolchainJobsBytesTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(m_sum)), Is.EqualTo(SUM_SHA256));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(m_words)), Is.EqualTo(WORDS_SHA256));
        });
    }

    #endregion

    #region C Tests

    [Test]
    public void TheCProgramReadsStdinAndArgumentsTest()
    {
        var result = Run(Task(SUM, m_sum, args: ["10", "20"], stdin: "1 2 3\n4\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("6 40\n"));
        });
    }

    [Test]
    public void TheCProgramsExitCodeIsItsOwnTest()
    {
        var result = Run(Task(SUM, m_sum, args: ["--exit", "7"]));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(7));
        });
    }

    [Test]
    public void TheCProgramIsJudgedBySuiteTest()
    {
        var suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Stdin = "5 5", ExpectedStdout = "2 10\n" },
                new VerifySuiteCaseData { Stdin = "", Args = ["3"], ExpectedStdout = "1 3\n" },
                new VerifySuiteCaseData { Args = ["--exit", "4"], ExpectedExitCode = 4 }
            ]
        };

        var passing = Run(Task(SUM, m_sum, suite: suite));

        suite.Cases.Add(new VerifySuiteCaseData { Stdin = "1 1", ExpectedStdout = "2 3\n" });
        var failing = Run(Task(SUM, m_sum, suite: suite));

        Assert.Multiple(() =>
        {
            Assert.That(passing.Verdict, Is.EqualTo(VerifyVerdict.Pass), passing.Stderr);
            Assert.That(failing.Verdict, Is.EqualTo(VerifyVerdict.Fail));
        });
    }

    [Test]
    public void TheCAllocatorStopsAtTheMemoryCapTest()
    {
        // malloc returns NULL at the cap and the program reports how much it was given:
        // most of the 64 MiB, never more.
        var result = Run(Task(SUM, m_sum, args: ["--grow"]), new VerifyLimitsData { MemoryBytes = 64 * MEBIBYTE });

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(int.Parse(result.Stdout.Trim()), Is.InRange(48, 64));
            Assert.That(result.PeakMemoryBytes, Is.LessThanOrEqualTo(64 * MEBIBYTE));
        });
    }

    #endregion

    #region Rust Tests

    [Test]
    public void TheRustProgramCountsWordsTest()
    {
        var result = Run(Task(WORDS, m_words, stdin: "b a b\nc B a b\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("4 b\n2 a\n1 c\n"));
        });
    }

    [Test]
    public void ARustPanicIsARuntimeErrorWithItsMessageTest()
    {
        var result = Run(Task(WORDS, m_words, args: ["--panic"]));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.Stderr, Does.Contain("asked to"));
        });
    }

    #endregion

    #region Determinism Tests

    [Test]
    public void CompiledModulesRunTheSameTwiceTest()
    {
        var sumFirst = Run(Task(SUM, m_sum, args: ["10", "20"], stdin: "1 2 3\n4\n"));
        var sumSecond = Run(Task(SUM, m_sum, args: ["10", "20"], stdin: "1 2 3\n4\n"));
        var wordsFirst = Run(Task(WORDS, m_words, stdin: "b a b\nc B a b\n"));
        var wordsSecond = Run(Task(WORDS, m_words, stdin: "b a b\nc B a b\n"));

        Assert.Multiple(() =>
        {
            Assert.That(sumSecond.Stdout, Is.EqualTo(sumFirst.Stdout));
            Assert.That(sumSecond.FuelConsumed, Is.EqualTo(sumFirst.FuelConsumed));
            Assert.That(wordsSecond.Stdout, Is.EqualTo(wordsFirst.Stdout));
            Assert.That(wordsSecond.FuelConsumed, Is.EqualTo(wordsFirst.FuelConsumed));
        });
    }

    #endregion

    #region Tools

    private static VerifyTaskData Task(string name, byte[] module, List<string>? args = null, string? stdin = null, VerifySuiteData? suite = null)
    {
        return new VerifyTaskData
        {
            TaskIndex = 0,
            RuntimeId = VerifyRuntimeCatalog.WASI,
            Sources = [new VerifySourceFileData { Name = name, Content = Convert.ToBase64String(module) }],
            EntryPoint = name,
            Args = args ?? [],
            Stdin = stdin,
            Suite = suite
        };
    }

    private VerifyResultData Run(VerifyTaskData task, VerifyLimitsData? limits = null)
    {
        return m_sandbox.Execute(m_wasi, task, VerifySandboxDefaults.Resolve(limits, null));
    }

    #endregion
}
