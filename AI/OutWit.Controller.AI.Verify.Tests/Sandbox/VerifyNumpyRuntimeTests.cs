using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The numpy runtime: CPython 3.14.6 with numpy 2.5.4 built in, run as a start-up snapshot with
/// numpy imported. A hello and an import cost a fraction of the plain module's start; linear
/// algebra, FFT and random numbers work; the snapshot answers as the plain module beside it does,
/// its frozen process state is the task's, and unseeded random numbers follow the task's seed;
/// the 32-bit default integer is a fact a program sees; the limits hold; nothing is writable; a
/// run is reproducible to the fuel unit; a changed layer does not resolve, and a run under an
/// environment the snapshot was not made with is refused.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyNumpyRuntimeTests : VerifySandboxTestBase
{
    #region Constants

    private const string NUMPY_DIR = "python-3.14.6-numpy-2.5.4";
    private const string PLAIN_MODULE = "python-plain.wasm";
    private const string NUMPY_LAYER = "numpy-2.5.4.zip";
    private static readonly string DEMO_TASKSET = Path.Combine("OutWit.Controller.AI.Verify.Live", "tasksets", "numpy-demo.jsonl");

    /// <summary>A hello in the snapshot costs about 37 M units; the plain module's start is 120 M.</summary>
    private const long HELLO_FUEL_CEILING = 60_000_000;

    /// <summary>numpy is imported in the snapshot already; importing it in the plain module costs about 0.8 G.</summary>
    private const long IMPORT_FUEL_CEILING = 60_000_000;

    private const string LINALG_PROGRAM =
        "import numpy as np\n" +
        "a = np.array([[4.0, 1.0], [2.0, 3.0]])\n" +
        "print(np.linalg.solve(a, np.array([9.0, 13.0])).tolist())\n" +
        "print(round(float(np.linalg.det(a)), 9), np.linalg.eigvalsh(np.array([[2.0, 1.0], [1.0, 2.0]])).round(9).tolist())\n" +
        "print(np.linalg.svd(np.diag([3.0, 4.0]), compute_uv=False).tolist(), np.linalg.matrix_rank(np.ones((3, 3))))\n" +
        "print(np.round(np.fft.rfft(np.array([1.0, 0.0, -1.0, 0.0])), 9).tolist())\n" +
        "r = np.random.default_rng(7)\n" +
        "m = r.standard_normal((100, 100)) + 10 * np.eye(100)\n" +
        "print(bool(np.abs(m @ np.linalg.solve(m, np.ones(100)) - 1).max() < 1e-10))\n" +
        "try:\n" +
        "    np.linalg.inv(np.zeros((2, 2)))\n" +
        "except np.linalg.LinAlgError as error:\n" +
        "    print('LinAlgError', error)\n";

    private const string LINALG_EXPECTED =
        "[1.4, 3.4]\n" +
        "10.0 [1.0, 3.0]\n" +
        "[4.0, 3.0] 1\n" +
        "[0j, (2+0j), 0j]\n" +
        "True\n" +
        "LinAlgError Singular matrix\n";

    // The process a task sees: its environment, argv, flags, folder, clock and path.
    private const string PROCESS_PROGRAM =
        "import datetime, os, sys, time\n" +
        "print(sorted(os.environ.items()))\n" +
        "print(sys.argv, sys.orig_argv[:4], sys.orig_argv[5:])\n" +
        "print(sys.flags.safe_path, sys.flags.dont_write_bytecode, sys.flags.hash_randomization, sys.flags.isolated)\n" +
        "print(os.getcwd(), sys.path, sys.prefix)\n" +
        "print(time.time(), time.monotonic(), time.perf_counter(), time.process_time())\n" +
        "time.sleep(2.5)\n" +
        "print(time.time(), datetime.datetime.now(datetime.timezone.utc))\n";

    private const string PROCESS_EXPECTED =
        "[('PYTHONDONTWRITEBYTECODE', '1'), ('PYTHONHASHSEED', '0'), ('PYTHONHOME', '/'), ('PYTHONPATH', '/lib/numpy-2.5.4.zip'), ('PYTHONUNBUFFERED', '1')]\n" +
        "['-c', 'a', '-x'] ['python.wasm', '-P', '-B', '-c'] ['a', '-x']\n" +
        "True 1 0 0\n" +
        "/ ['/lib/numpy-2.5.4.zip', '/lib/python314.zip', '/lib/python3.14', '/lib/python3.14/lib-dynload'] /\n" +
        "0.0 0.0 0.0 0.0\n" +
        "2.5 1970-01-01 00:00:02.500000+00:00\n";

    // Every source of randomness a program may use without a seed of its own.
    private const string UNSEEDED_PROGRAM =
        "import random, secrets, uuid\n" +
        "import numpy as np\n" +
        "print(np.random.randint(0, 10**9), np.random.rand())\n" +
        "print(random.random(), random.randrange(10**9))\n" +
        "print(np.random.default_rng().integers(0, 10**9), secrets.token_hex(4), uuid.uuid4())\n";

    #endregion

    #region Program Tests

    [Test]
    public void HelloRunsSilentlyAndCheaplyTest()
    {
        var result = Run(PythonNumpy, NumpyTask("print(6*7)"));
        TestContext.Out.WriteLine($"hello: fuel {result.FuelConsumed:N0}, peak memory {result.PeakMemoryBytes:N0} bytes, {result.WallMs} ms");

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("42\n"));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(result.FuelConsumed, Is.LessThan(HELLO_FUEL_CEILING));
        });
    }

    [Test]
    public void NumpyIsImportedAlreadyTest()
    {
        const string program = "import numpy\nprint(numpy.__version__, numpy.random.__name__, numpy.linalg.__name__)";

        var snapshot = Run(PythonNumpy, NumpyTask(program));
        var plain = Run(Plain(), NumpyTask(program));
        TestContext.Out.WriteLine($"import numpy: snapshot {snapshot.FuelConsumed:N0} fuel {snapshot.WallMs} ms, plain module {plain.FuelConsumed:N0} fuel {plain.WallMs} ms");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Verdict, Is.EqualTo(VerifyVerdict.Pass), snapshot.Stderr);
            Assert.That(snapshot.Stdout, Is.EqualTo("2.5.4 numpy.random numpy.linalg\n"));
            Assert.That(snapshot.FuelConsumed, Is.LessThan(IMPORT_FUEL_CEILING));
            Assert.That(plain.Stdout, Is.EqualTo(snapshot.Stdout));
            Assert.That(plain.FuelConsumed, Is.GreaterThan(10 * snapshot.FuelConsumed), "the snapshot is what makes the import cheap");
        });
    }

    [Test]
    public void LinearAlgebraAndFftWorkTest()
    {
        var result = Run(PythonNumpy, NumpyTask(LINALG_PROGRAM));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo(LINALG_EXPECTED));
        });
    }

    [Test]
    public void TheDefaultIntegerIs32BitTest()
    {
        // wasm32 is ILP32: numpy's default integer is int32, and a large sum wraps unless the
        // program asks for 64 bits.
        const string program = "import numpy as np\nprint(np.dtype(int), np.arange(3).dtype, np.arange(1_000_000).sum(), np.arange(1_000_000, dtype=np.int64).sum())";

        var result = Run(PythonNumpy, NumpyTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("int32 int32 1783293664 499999500000\n"));
        });
    }

    [Test]
    public void SeededRandomNumbersAreNumpysOwnTest()
    {
        // The same numbers numpy gives on any platform for these seeds.
        const string program = "import numpy as np\nnp.random.seed(1)\nprint(np.random.rand(3).round(9).tolist())\nprint(np.random.default_rng(42).integers(0, 100, 5).tolist())";

        var result = Run(PythonNumpy, NumpyTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("[0.417022005, 0.720324493, 0.000114375]\n[8, 77, 65, 43, 43]\n"));
        });
    }

    [Test]
    public void UnseededRandomNumbersFollowTheTasksSeedTest()
    {
        // The snapshot imported numpy, random and secrets while it was made; each task seeds what
        // they hold again from its own stream, as a fresh interpreter would.
        var first = Run(PythonNumpy, NumpyTask(UNSEEDED_PROGRAM, seed: 1));
        var again = Run(PythonNumpy, NumpyTask(UNSEEDED_PROGRAM, seed: 1));
        var other = Run(PythonNumpy, NumpyTask(UNSEEDED_PROGRAM, seed: 2));
        TestContext.Out.WriteLine($"seed 1: {first.Stdout}seed 2: {other.Stdout}");

        var firstValues = first.Stdout.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var otherValues = other.Stdout.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(first.Verdict, Is.EqualTo(VerifyVerdict.Pass), first.Stderr);
            Assert.That(again.Stdout, Is.EqualTo(first.Stdout), "the same seed gives the same numbers");
            Assert.That(otherValues, Has.Length.EqualTo(firstValues.Length));
            Assert.That(Enumerable.Range(0, firstValues.Length).Where(i => firstValues[i] == otherValues[i]), Is.Empty,
                "another seed gives other numbers, from every source");
        });
    }

    [Test]
    public void TheFrozenProcessIsTheTasksTest()
    {
        // The snapshot holds the environment, argv, flags and modules of the moment it was made;
        // what a task sees is its own: the sandbox's environment, its argv, the clock at the
        // pinned start, and the clock moving only when the program waits.
        var result = Run(PythonNumpy, NumpyTask(PROCESS_PROGRAM, args: ["a", "-x"]));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo(PROCESS_EXPECTED));
        });
    }

    [TestCase(PROCESS_PROGRAM)]
    [TestCase(LINALG_PROGRAM)]
    [TestCase("import numpy as np\nnp.linalg.inv(np.zeros((2, 2)))")]
    [TestCase("import sys\nprint('partial')\nsys.exit(3)")]
    [TestCase("import sys, numpy as np\nprint('numpy' in sys.modules, 'np' in dir(), 'numpy' in dir(), __name__)")]
    [TestCase("    print('indented, as CPython 3.14 dedents -c')\n    print(1 / 0)")]
    public void TheSnapshotAnswersAsThePlainModuleTest(string program)
    {
        // Byte for byte, tracebacks included. What differs is what the snapshot holds already:
        // numpy, numpy.random, numpy.linalg, numpy.fft and what they import are in sys.modules
        // before the task starts (not in its namespace), and unseeded random numbers, which
        // follow the task's seed in both but from another place in its stream.
        var task = NumpyTask(program, args: ["a", "-x"], seed: 5);

        var snapshot = Run(PythonNumpy, task);
        var plain = Run(Plain(), task);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Verdict, Is.EqualTo(plain.Verdict), snapshot.Stderr);
            Assert.That(snapshot.ExitCode, Is.EqualTo(plain.ExitCode));
            Assert.That(snapshot.Stdout, Is.EqualTo(plain.Stdout));
            Assert.That(snapshot.Stderr, Is.EqualTo(plain.Stderr));
        });
    }

    [Test]
    public void TheDemoTasksetGivesTheVerdictsItNamesTest()
    {
        // The taskset the fleet proof runs (verify-live run numpy-demo.jsonl): ten programs that
        // pass, a wrong answer, a crash and a loop that never ends.
        var path = Path.GetFullPath(Path.Combine(RuntimesRoot, "..", "..", "..", DEMO_TASKSET));
        var parsed = VerifyTasksetParser.Parse(File.ReadAllText(path));
        Assert.That(parsed.Errors, Is.Empty);

        var results = parsed.Tasks.Select(task => Run(PythonNumpy, task)).ToList();
        foreach (var result in results.Where(result => result.Verdict != VerifyVerdict.Pass))
            TestContext.Out.WriteLine($"task {result.TaskIndex}: {result.Verdict} {result.Stderr}");

        var expected = Enumerable.Repeat(VerifyVerdict.Pass, 10)
            .Concat([VerifyVerdict.Fail, VerifyVerdict.RuntimeError, VerifyVerdict.Timeout]);
        Assert.That(results.Select(result => result.Verdict), Is.EqualTo(expected));
    }

    #endregion

    #region Determinism Tests

    [Test]
    public void ARunIsReproducibleToTheFuelUnitTest()
    {
        const string program =
            "import numpy as np\n" +
            "r = np.random.default_rng(3)\n" +
            "m = r.standard_normal((60, 60))\n" +
            "print(repr(float(np.linalg.det(m))), np.sort(r.integers(0, 1000, 20)).tolist())\n" +
            "print(np.random.rand(), np.random.default_rng().random())\n";

        var first = Run(PythonNumpy, NumpyTask(program, seed: 9));
        var second = Run(PythonNumpy, NumpyTask(program, seed: 9));

        Assert.Multiple(() =>
        {
            Assert.That(first.Verdict, Is.EqualTo(VerifyVerdict.Pass), first.Stderr);
            Assert.That(second.Stdout, Is.EqualTo(first.Stdout));
            Assert.That(second.FuelConsumed, Is.EqualTo(first.FuelConsumed));
            Assert.That(second.PeakMemoryBytes, Is.EqualTo(first.PeakMemoryBytes));
        });
    }

    #endregion

    #region Containment Tests

    [Test]
    public void NothingIsWritableTest()
    {
        const string program =
            "import io, os, tempfile\n" +
            "import numpy as np\n" +
            "for attempt in (lambda: open('/out.txt', 'w'), lambda: np.save('/a.npy', np.arange(3)), lambda: tempfile.mkstemp(), lambda: os.mkdir('/d'), lambda: open('/lib/numpy-2.5.4.zip', 'r+b')):\n" +
            "    try:\n" +
            "        attempt()\n" +
            "        print('written')\n" +
            "    except OSError as error:\n" +
            "        print(type(error).__name__)\n" +
            "buffer = io.BytesIO()\n" +
            "np.save(buffer, np.arange(3))\n" +
            "buffer.seek(0)\n" +
            "print(np.load(buffer).tolist())\n";

        var result = Run(PythonNumpy, NumpyTask(program));
        TestContext.Out.WriteLine(result.Stdout);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Does.Not.Contain("written"));
            Assert.That(result.Stdout, Does.EndWith("[0, 1, 2]\n"), "numpy's own files work in memory");
        });
    }

    [Test]
    public void NoThreadsTest()
    {
        const string program = "import threading\ntry:\n    threading.Thread(target=print).start()\nexcept RuntimeError as error:\n    print(error)";

        var result = Run(PythonNumpy, NumpyTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("can't start new thread\n"));
        });
    }

    [Test]
    public void AnArrayPastTheMemoryCapIsMemoryExceededTest()
    {
        const string program = "import numpy as np\na = np.ones((3000, 3000))\nb = a * 2\nc = a + b\nprint(c.sum())";

        var result = Run(PythonNumpy, NumpyTask(program), new VerifyLimitsData { MemoryBytes = 64L * 1024 * 1024 });
        TestContext.Out.WriteLine($"{result.Verdict} exit={result.ExitCode} stderr={result.Stderr}");

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.MemoryExceeded));
    }

    [Test]
    public void AnInfiniteLoopIsBoundedByFuelTest()
    {
        var result = Run(PythonNumpy, NumpyTask("while True:\n    pass"), new VerifyLimitsData { FuelBudget = 2_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    [Test]
    public void ARunUnderAnotherEnvironmentIsRefusedTest()
    {
        // The snapshot holds os.environ and sys.path; a runtime wired with another PYTHONPATH
        // would run a program against a state it does not describe, so the snapshot refuses it.
        var layers = PythonNumpy.Descriptor.Layers;
        var rewired = PythonNumpy with
        {
            Descriptor = PythonNumpy.Descriptor with { Layers = [.. layers, layers[^1] with { PythonPath = "/lib/another.zip" }] }
        };

        var result = Run(rewired, NumpyTask("print(1)"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(result.Stderr, Does.Contain("the environment differs"));
        });
    }

    #endregion

    #region Pin Tests

    [Test]
    public void AChangedLayerFailsThePinTest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"witai_numpy_pin_{Guid.NewGuid():N}");
        try
        {
            var runtimeDir = Path.Combine(root, NUMPY_DIR);
            CopyFolder(Path.Combine(RuntimesRoot, NUMPY_DIR), runtimeDir);
            var layer = Path.Combine(runtimeDir, "lib", NUMPY_LAYER);
            var bytes = File.ReadAllBytes(layer);
            bytes[bytes.Length / 2] ^= 0x01;
            File.WriteAllBytes(layer, bytes);

            var resolved = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.PYTHON_3_14_6_NUMPY_2_5_4, out var reason);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.Null);
                Assert.That(reason, Does.Contain(NUMPY_LAYER).And.Contain("SHA-256"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    #endregion

    #region Tools

    /// <summary>The same runtime with the module before its snapshot (shipped beside it for debugging).</summary>
    private VerifyResolvedRuntime Plain()
    {
        return PythonNumpy with { ModulePath = Path.Combine(PythonNumpy.RootPath!, PLAIN_MODULE) };
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    #endregion
}
