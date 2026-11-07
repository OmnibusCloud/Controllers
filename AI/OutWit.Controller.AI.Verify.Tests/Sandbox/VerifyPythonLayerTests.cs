using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The Python bytecode layers: the standard library loads from python314.zip (no directory
/// scans, no prefix warning, a cheaper start than the source tree), the sympy layer is on
/// the runtime that names it and absent from the one that does not, a sympy run is
/// deterministic, and a tampered layer never resolves.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyPythonLayerTests : VerifySandboxTestBase
{
    #region Constants

    /// <summary>What a hello cost with the standard library as a source tree (0.2.x); the layer must beat it.</summary>
    private const long SOURCE_TREE_HELLO_FUEL = 157_000_000;

    private const string SYMPY_PROGRAM =
        "import sympy\n" +
        "x = sympy.symbols('x')\n" +
        "print(sympy.solve(x**2 - 4, x))\n" +
        "print(sympy.integrate(sympy.sin(x) * x, x))\n" +
        "print(sympy.factorint(2**32 + 1))\n";

    private const string SYMPY_EXPECTED = "[-2, 2]\n-x*cos(x) + sin(x)\n{641: 1, 6700417: 1}\n";

    private const string MPMATH_PROGRAM =
        "import mpmath\n" +
        "mpmath.mp.dps = 50\n" +
        "print(mpmath.pi)\n" +
        "print(mpmath.quad(lambda t: mpmath.exp(-t*t), [-mpmath.inf, mpmath.inf]))\n";

    private const string MPMATH_EXPECTED =
        "3.1415926535897932384626433832795028841971693993751\n" +
        "1.7724538509055160272981674833411451827975494561224\n";

    #endregion

    #region Layer Tests

    [Test]
    public void StandardLibraryLoadsFromTheBytecodeZipTest()
    {
        var result = Run(Python, PythonTask("import sys, json\nprint(sys.path[0])\nprint(json.__file__)\nprint(sys.prefix)"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
            Assert.That(result.Stdout, Is.EqualTo("/lib/python314.zip\n/lib/python314.zip/json/__init__.pyc\n/\n"));
        });
    }

    [Test]
    public void HelloStartsSilentlyAndCheaperThanTheSourceTreeTest()
    {
        var result = Run(Python, PythonTask("print('hello')"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
            Assert.That(result.Stdout, Is.EqualTo("hello\n"));
            Assert.That(result.Stderr, Is.Empty, "no 'platform dependent libraries' warning with the prefix pinned");
            Assert.That(result.FuelConsumed, Is.LessThan(SOURCE_TREE_HELLO_FUEL));
        });
    }

    [Test]
    public void SympySolvesIntegratesAndFactorsTest()
    {
        var result = Run(PythonSympy, SympyTask(SYMPY_PROGRAM));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo(SYMPY_EXPECTED));
        });
    }

    [Test]
    public void MpmathComputesToFiftyDigitsTest()
    {
        var result = Run(PythonSympy, SympyTask(MPMATH_PROGRAM));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo(MPMATH_EXPECTED));
        });
    }

    [Test]
    public void SympyRunIsDeterministicTest()
    {
        var first = Run(PythonSympy, SympyTask(SYMPY_PROGRAM));
        var second = Run(PythonSympy, SympyTask(SYMPY_PROGRAM));

        Assert.Multiple(() =>
        {
            Assert.That(second.Stdout, Is.EqualTo(first.Stdout));
            Assert.That(second.FuelConsumed, Is.EqualTo(first.FuelConsumed));
            Assert.That(second.PeakMemoryBytes, Is.EqualTo(first.PeakMemoryBytes));
        });
    }

    [Test]
    public void SympyIsNotOnTheStandardLibraryRuntimeTest()
    {
        var result = Run(Python, PythonTask("import sympy"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.Stderr, Does.Contain("ModuleNotFoundError"));
        });
    }

    [Test]
    public void SuiteOverASympyCheckerPassesTest()
    {
        var checker = "import sys, sympy\nexpr, value = sys.stdin.read().split('=')\nprint('OK' if sympy.sympify(expr) == int(value) else 'MISMATCH')";
        var suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Stdin = "7 * 6 = 42", ExpectedStdout = "OK\n" },
                new VerifySuiteCaseData { Stdin = "2 + 2 = 5", ExpectedStdout = "MISMATCH\n" }
            ]
        };

        var result = Run(PythonSympy, SympyTask(checker, suite));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
    }

    #endregion

    #region Pin Tests

    [Test]
    public void TamperedLayerFailsItsPinTest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"witai_layer_{Guid.NewGuid():N}");
        try
        {
            var pythonDir = Path.Combine(root, "python-3.14.6");
            Directory.CreateDirectory(Path.Combine(pythonDir, "lib"));
            File.Copy(Path.Combine(RuntimesRoot, "python-3.14.6", "python.wasm"), Path.Combine(pythonDir, "python.wasm"));

            var layer = File.ReadAllBytes(Path.Combine(RuntimesRoot, "python-3.14.6", "lib", "python314.zip"));
            layer[layer.Length / 2] ^= 0x01;
            File.WriteAllBytes(Path.Combine(pythonDir, "lib", "python314.zip"), layer);

            var resolved = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.PYTHON_3_14_6, out var reason);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.Null);
                Assert.That(reason, Does.Contain("layer").And.Contain("SHA-256"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void MissingLayerIsUnavailableTest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"witai_nolayer_{Guid.NewGuid():N}");
        try
        {
            var pythonDir = Path.Combine(root, "python-3.14.6");
            Directory.CreateDirectory(Path.Combine(pythonDir, "lib"));
            File.Copy(Path.Combine(RuntimesRoot, "python-3.14.6", "python.wasm"), Path.Combine(pythonDir, "python.wasm"));
            File.Copy(Path.Combine(RuntimesRoot, "python-3.14.6", "lib", "python314.zip"), Path.Combine(pythonDir, "lib", "python314.zip"));

            var stdlibOnly = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.PYTHON_3_14_6, out _);
            var withSympy = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.PYTHON_3_14_6_SYMPY_1_14_0, out var reason);

            Assert.Multiple(() =>
            {
                Assert.That(stdlibOnly, Is.Not.Null);
                Assert.That(withSympy, Is.Null);
                Assert.That(reason, Does.Contain("layer not found").And.Contain("sympy-1.14.0.zip"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    #endregion

    #region Helpers

    private static VerifyTaskData SympyTask(string code, VerifySuiteData? suite = null)
    {
        var task = PythonTask(code, suite: suite);
        task.RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6_SYMPY_1_14_0;
        return task;
    }

    #endregion
}
