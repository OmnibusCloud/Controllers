using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The TypeScript runtime: the JavaScript runtime's QuickJS with sucrase stripping the types of
/// the inline source, the result run as a module. The language's everyday features work, stdin,
/// arguments, top-level await and exit codes behave as in JavaScript, a source that does not
/// parse is reported with its line and column, an uncaught error is a RuntimeError, a runtime
/// namespace is the stated limit, and a run is reproducible to the fuel unit.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyTypeScriptRuntimeTests : VerifySandboxTestBase
{
    #region Constants

    /// <summary>Loading the transpiler is most of a hello's cost; anything near this is wrong.</summary>
    private const long HELLO_FUEL_CEILING = 1_000_000_000;

    private const string FEATURES_PROGRAM =
        "interface Shape { area(): number }\n" +
        "enum Kind { Square = 'square', Circle = 'circle' }\n" +
        "abstract class Base implements Shape {\n" +
        "  constructor(public readonly kind: Kind) {}\n" +
        "  abstract area(): number;\n" +
        "}\n" +
        "class Square extends Base {\n" +
        "  #side: number;\n" +
        "  constructor(side: number) { super(Kind.Square); this.#side = side; }\n" +
        "  area(): number { return this.#side ** 2; }\n" +
        "}\n" +
        "function largest<T extends Shape>(items: readonly T[]): T | undefined {\n" +
        "  return items.reduce<T | undefined>((best, s) => (best && best.area() >= s.area() ? best : s), undefined);\n" +
        "}\n" +
        "type Point = { x: number; y?: number };\n" +
        "const origin = { x: 0 } satisfies Point;\n" +
        "const sizes = [3, 5, 2] as const;\n" +
        "const big = largest(sizes.map((n) => new Square(n)));\n" +
        "console.log(big?.kind, big?.area(), origin.y ?? 'none', Kind.Circle);\n";

    #endregion

    #region Program Tests

    [Test]
    public void HelloRunsSilentlyTest()
    {
        var result = Run(TypeScript, TsTask("const greeting: string = 'hello';\nconsole.log(greeting);\n"));
        TestContext.Out.WriteLine($"hello: fuel {result.FuelConsumed:N0}, peak memory {result.PeakMemoryBytes:N0} bytes");

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("hello\n"));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(result.FuelConsumed, Is.LessThan(HELLO_FUEL_CEILING));
        });
    }

    [Test]
    public void TheEverydayFeaturesWorkTest()
    {
        var result = Run(TypeScript, TsTask(FEATURES_PROGRAM));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("square 25 none circle\n"));
        });
    }

    [Test]
    public void StdinArgumentsAndAwaitWorkTest()
    {
        const string program =
            "import * as std from 'qjs:std';\n" +
            "const numbers: number[] = std.in.readAsString().trim().split(/\\s+/).map(Number);\n" +
            "const total = await Promise.resolve(numbers.reduce((a, b) => a + b, 0));\n" +
            "console.log(total, scriptArgs.join('|'));\n";

        var result = Run(TypeScript, TsTask(program, args: ["-x", "two words"], stdin: "2 3\n37\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("42 -x|two words\n"));
        });
    }

    [Test]
    public void SuiteCasesAreJudgedByStdoutAndExitTest()
    {
        var suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Stdin = "2 3", ExpectedStdout = "5\n" },
                new VerifySuiteCaseData { Stdin = "10 -4", ExpectedStdout = "6\n" },
                new VerifySuiteCaseData { Stdin = "1 1", ExpectedStdout = "3\n" }
            ]
        };
        const string program = "import * as std from 'qjs:std';\nconst [a, b]: number[] = std.in.readAsString().trim().split(' ').map(Number);\nconsole.log(a + b);\n";

        var result = Run(TypeScript, TsTask(program, suite: suite));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Fail), "the third case expects a wrong sum");
    }

    [Test]
    public void ASourceThatDoesNotParseIsAnErrorWithItsPlaceTest()
    {
        var result = Run(TypeScript, TsTask("const ok = 1;\nconst x: number = ;\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Stderr, Does.StartWith("SyntaxError: ").And.Contain("main.ts").And.Contain("(2:"));
        });
    }

    [Test]
    public void AnUncaughtErrorIsARuntimeErrorTest()
    {
        var result = Run(TypeScript, TsTask("console.log('before');\nthrow new TypeError('boom');\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Stdout, Is.EqualTo("before\n"));
            Assert.That(result.Stderr, Does.StartWith("TypeError: boom"));
        });
    }

    [Test]
    public void TheExitCodeIsTheProgramsTest()
    {
        var result = Run(TypeScript, TsTask("import * as std from 'qjs:std';\nconsole.log('out');\nstd.exit(3);\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.Stdout, Is.EqualTo("out\n"));
        });
    }

    [Test]
    public void ARuntimeNamespaceIsTheStatedLimitTest()
    {
        // sucrase strips types and does not emit namespaces: the program fails, it does not run wrong.
        var result = Run(TypeScript, TsTask("namespace Geo { export const pi = 3; }\nconsole.log(Geo.pi);\n"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
    }

    [Test]
    public void AnEndlessRecursionIsARangeErrorTest()
    {
        const string program = "function f(n: number): number { return 1 + f(n + 1); }\ntry { f(0); } catch (e) { console.log('caught ' + (e as Error).name); }\n";

        var result = Run(TypeScript, TsTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("caught RangeError\n"));
        });
    }

    #endregion

    #region Determinism Tests

    [Test]
    public void ARunIsReproducibleToTheFuelUnitTest()
    {
        var first = Run(TypeScript, TsTask(FEATURES_PROGRAM));
        var second = Run(TypeScript, TsTask(FEATURES_PROGRAM));

        Assert.Multiple(() =>
        {
            Assert.That(first.Verdict, Is.EqualTo(VerifyVerdict.Pass), first.Stderr);
            Assert.That(second.Stdout, Is.EqualTo(first.Stdout));
            Assert.That(second.FuelConsumed, Is.EqualTo(first.FuelConsumed));
        });
    }

    [Test]
    public void TheRuntimeIsTheJavaScriptModuleWithItsTranspilerTest()
    {
        var descriptor = VerifyRuntimeCatalog.Find(VerifyRuntimeCatalog.TYPESCRIPT_SUCRASE_3_35_1)!;
        var javaScript = VerifyRuntimeCatalog.Find(VerifyRuntimeCatalog.QUICKJS_0_15_1)!;

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.ModulePath, Is.EqualTo(javaScript.ModulePath));
            Assert.That(descriptor.Sha256, Is.EqualTo(javaScript.Sha256));
            Assert.That(descriptor.Layers, Has.Count.EqualTo(1));
            Assert.That(TypeScript.RootPath, Is.Not.Null);
        });
    }

    #endregion
}
