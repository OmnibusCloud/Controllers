using System.Diagnostics;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// What a task's module is held to before the engine compiles it (finding N-2 of the audit of
/// 2026-11-18): the shapes the audit measured, with the modules it used. A module of a few
/// kilobytes took the engine half a minute or eight gigabytes and was priced like a hello; here
/// each is refused by a bound with fixed words, or priced by what it costs, before anything is
/// compiled.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyCompileBoundsTests
{
    #region Constants

    private const byte I64 = 0x7E;

    /// <summary>What the empty _start of a module of <see cref="ShapeModules"/> is raised by: a body of two bytes priced as the least a function is.</summary>
    private const long START_FLOOR = (VerifyModuleShape.FUNCTION_FLOOR_BYTES - 2) * VerifyModuleShape.COMPILE_FUEL_PER_BYTE;

    #endregion

    #region Fields

    private VerifyWasmSandbox m_sandbox = null!;
    private VerifyResolvedRuntime m_wasi = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void SetUp()
    {
        m_wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason) ?? throw new InvalidOperationException(reason);
        m_sandbox = new VerifyWasmSandbox();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_sandbox?.Dispose();
    }

    #endregion

    #region Refusal Tests

    [Test]
    public void N2TheModulesTheAuditMeasuredAreRefusedBeforeTheyAreCompiledTest()
    {
        // 10,061 bytes that took 5.3 s and 8.3 GiB; 9,067 bytes that took 30 s and 3 GiB; 160,058
        // bytes that took 67 s; 8,068 bytes of a thousand values carried by a thousand branches.
        var modules = new (string What, byte[] Module, string Problem)[]
        {
            ("1,000 results through 2,000 nested blocks", ShapeModules.Blocks(1000, 2000), $"a function type of the module has more than {VerifyModuleShape.MAX_RESULTS} results"),
            ("1,000 values through 2,000 calls", ShapeModules.Calls(1000, 2000), $"a function type of the module has more than {VerifyModuleShape.MAX_PARAMETERS} parameters"),
            ("20,000 functions of 50,000 locals", ShapeModules.Locals(20_000, 50_000), $"a function of the module has more than {VerifyModuleShape.MAX_LOCALS} locals"),
            ("1,000 values over 1,000 branches", ShapeModules.Branches(1000, 1000), $"a function type of the module has more than {VerifyModuleShape.MAX_RESULTS} results")
        };

        var limits = VerifyLimitCeilings.Effective(new VerifyLimitsData { FuelBudget = long.MaxValue }, null);

        // The fixture's sandbox is shared: what another test of it compiled is not this test's.
        var compiledBefore = m_sandbox.TaskModuleCacheCount;
        var clock = Stopwatch.StartNew();

        Assert.Multiple(() =>
        {
            foreach (var (what, module, problem) in modules)
            {
                var result = m_sandbox.Execute(m_wasi, Wasi(module), limits);

                Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError), what);
                Assert.That(result.Stderr, Is.EqualTo(problem), what);
            }

            Assert.That(m_sandbox.TaskModuleCacheCount, Is.EqualTo(compiledBefore), "none of them reached the engine's compiler");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "the largest budget buys none of these compiles");
        });
    }

    [Test]
    public void N2AFunctionTypeIsHeldToItsParametersAndResultsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VerifyModuleShape.Scan(ShapeModules.WideType(VerifyModuleShape.MAX_PARAMETERS, VerifyModuleShape.MAX_RESULTS)).Problem, Is.Null, "at the bounds");
            Assert.That(VerifyModuleShape.Scan(ShapeModules.WideType(VerifyModuleShape.MAX_PARAMETERS + 1, 0)).Problem,
                Is.EqualTo($"a function type of the module has more than {VerifyModuleShape.MAX_PARAMETERS} parameters"));
            Assert.That(VerifyModuleShape.Scan(ShapeModules.WideType(0, VerifyModuleShape.MAX_RESULTS + 1)).Problem,
                Is.EqualTo($"a function type of the module has more than {VerifyModuleShape.MAX_RESULTS} results"));
        });
    }

    [Test]
    public void N2ABlockIsHeldToTheValuesItPassesTest()
    {
        // Within what a function type may have, and too wide for a block: five values in, five out.
        Assert.Multiple(() =>
        {
            Assert.That(VerifyModuleShape.Scan(BlockOf(4, 4)).Problem, Is.Null, "eight values together");
            Assert.That(VerifyModuleShape.Scan(BlockOf(5, 5)).Problem,
                Is.EqualTo($"a block of the module takes and gives more than {VerifyModuleShape.MAX_BLOCK_VALUES} values"));
            Assert.That(VerifyModuleShape.Scan(ShapeModules.Blocks(VerifyModuleShape.MAX_BLOCK_VALUES, 16)).Problem, Is.Null);
        });
    }

    [Test]
    public void N2AFunctionIsHeldToItsLocalsAndAModuleToTheLocalsOfItsFunctionsTest()
    {
        var most = (int)(VerifyModuleShape.MAX_MODULE_LOCALS / VerifyModuleShape.MAX_LOCALS);

        Assert.Multiple(() =>
        {
            Assert.That(VerifyModuleShape.Scan(ShapeModules.Locals(1, VerifyModuleShape.MAX_LOCALS)).Problem, Is.Null);
            Assert.That(VerifyModuleShape.Scan(ShapeModules.Locals(1, VerifyModuleShape.MAX_LOCALS + 1)).Problem,
                Is.EqualTo($"a function of the module has more than {VerifyModuleShape.MAX_LOCALS} locals"));
            Assert.That(VerifyModuleShape.Scan(ShapeModules.Locals(most, VerifyModuleShape.MAX_LOCALS)).Problem, Is.Null);
            Assert.That(VerifyModuleShape.Scan(ShapeModules.Locals(most + 1, VerifyModuleShape.MAX_LOCALS)).Problem,
                Is.EqualTo($"the functions of the module declare more than {VerifyModuleShape.MAX_MODULE_LOCALS} locals together"));
        });
    }

    [Test]
    public void N2AModuleIsHeldToTheNumberOfItsFunctionsTest()
    {
        // The module has one function besides the ones asked for: its _start.
        Assert.Multiple(() =>
        {
            Assert.That(VerifyModuleShape.Scan(ShapeModules.Locals(VerifyModuleShape.MAX_FUNCTIONS - 1, 0)).Problem, Is.Null);
            Assert.That(VerifyModuleShape.Scan(ShapeModules.Locals(VerifyModuleShape.MAX_FUNCTIONS, 0)).Problem,
                Is.EqualTo($"the module has more than {VerifyModuleShape.MAX_FUNCTIONS} functions"));
        });
    }

    [Test]
    public void N2AFunctionIsHeldToTheSizeOfItsBodyTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VerifyModuleShape.Scan(ShapeModules.Body(VerifyModuleShape.MAX_BODY_BYTES)).Problem, Is.Null);
            Assert.That(VerifyModuleShape.Scan(ShapeModules.Body(VerifyModuleShape.MAX_BODY_BYTES + 1)).Problem,
                Is.EqualTo($"a function of the module is larger than {VerifyModuleShape.MAX_BODY_BYTES >> 10} KiB"));
        });
    }

    #endregion

    #region Price Tests

    [Test]
    public void N2TheBranchesOfAFunctionCostTheirSquarePastWhatAnOrdinaryFunctionHasTest()
    {
        // 32,000 branches took 6.6 s where their bytes paid for 0.06 s.
        var few = VerifyModuleShape.Scan(ShapeModules.Chain(VerifyModuleShape.FREE_BRANCHES));
        var many = VerifyModuleShape.Scan(ShapeModules.Chain(8_000));
        var twice = VerifyModuleShape.Scan(ShapeModules.Chain(16_000));
        var carried = VerifyModuleShape.Scan(ShapeModules.Branches(VerifyModuleShape.MAX_BLOCK_VALUES, 8_000));

        Assert.Multiple(() =>
        {
            Assert.That(few.ShapeFuel - START_FLOOR, Is.Zero, "the branches of an ordinary function are in the price of its bytes");
            Assert.That(many.ShapeFuel - START_FLOOR, Is.EqualTo(8_000L * 8_000 * VerifyModuleShape.COMPILE_FUEL_PER_BRANCH_UNIT));
            Assert.That(twice.ShapeFuel - START_FLOOR, Is.EqualTo(4 * (many.ShapeFuel - START_FLOOR)), "twice the branches: four times the price");
            Assert.That(carried.ShapeFuel, Is.GreaterThanOrEqualTo(many.ShapeFuel), "branches that carry values cost no less");
            Assert.That(many.MostBranches, Is.EqualTo(8_000));
            Assert.That(twice.Problem, Is.Null);
        });
    }

    [Test]
    public void N2LocalsLiveAcrossBranchesCostTheirProductTest()
    {
        // The audit's 106,788 bytes: 2,000 values live across 20,000 branches, 31 s for a price of 0.11 s.
        var module = ShapeModules.Live(2000, 20_000);
        var report = VerifyModuleShape.Scan(module);
        var small = VerifyModuleShape.Scan(ShapeModules.Live(64, 128));

        // The function has its parameter and 2,000 locals of its own.
        var pairs = 2001L * 20_000 - VerifyModuleShape.FREE_LIVE_UNITS;
        var branches = 20_000L * 20_000;
        var locals = 2001L - VerifyModuleShape.FREE_LOCALS;

        Assert.Multiple(() =>
        {
            Assert.That(module, Has.Length.EqualTo(106_788), "the audit's module, to the byte");
            Assert.That(report.Problem, Is.Null);
            Assert.That(report.ShapeFuel - START_FLOOR, Is.EqualTo(pairs * VerifyModuleShape.COMPILE_FUEL_PER_LIVE_UNIT
                                                                   + branches * VerifyModuleShape.COMPILE_FUEL_PER_BRANCH_UNIT
                                                                   + locals * VerifyModuleShape.COMPILE_FUEL_PER_LOCAL));
            Assert.That(report.CompileFuel, Is.GreaterThan(30 * module.Length * VerifyModuleShape.COMPILE_FUEL_PER_BYTE), "thirty times what its bytes cost");
            Assert.That(small.ShapeFuel - START_FLOOR, Is.Zero, "64 locals across 128 branches: an ordinary function");
        });
    }

    [Test]
    public void N2ABlockThatPassesValuesCostsItsValuesTimesTheBlocksAroundItTest()
    {
        var one = VerifyModuleShape.Scan(ShapeModules.Blocks(1, VerifyModuleShape.MAX_NESTING));
        var wide = VerifyModuleShape.Scan(ShapeModules.Blocks(VerifyModuleShape.MAX_BLOCK_VALUES, VerifyModuleShape.MAX_NESTING));
        var units = (long)VerifyModuleShape.MAX_BLOCK_VALUES * VerifyModuleShape.MAX_NESTING * (VerifyModuleShape.MAX_NESTING + 1) / 2;

        Assert.Multiple(() =>
        {
            Assert.That(one.ShapeFuel - START_FLOOR, Is.Zero, "a block of one value is what a compiler for C writes");
            Assert.That(wide.ShapeFuel - START_FLOOR, Is.EqualTo(units * VerifyModuleShape.COMPILE_FUEL_PER_BLOCK_UNIT));
            Assert.That(wide.WidestBlock, Is.EqualTo(VerifyModuleShape.MAX_BLOCK_VALUES));
        });
    }

    [Test]
    public void N2DeclaredLocalsCostPastWhatAnOrdinaryFunctionHasTest()
    {
        var free = VerifyModuleShape.Scan(ShapeModules.Locals(100, VerifyModuleShape.FREE_LOCALS));
        var priced = VerifyModuleShape.Scan(ShapeModules.Locals(100, VerifyModuleShape.MAX_LOCALS));

        Assert.Multiple(() =>
        {
            Assert.That(Floor(free, 101), Is.Zero, "the locals of an ordinary function are in the price of its bytes");
            Assert.That(Floor(priced, 101), Is.EqualTo(100L * (VerifyModuleShape.MAX_LOCALS - VerifyModuleShape.FREE_LOCALS) * VerifyModuleShape.COMPILE_FUEL_PER_LOCAL));
        });
    }

    [Test]
    public void N2AFunctionIsPricedAsAFewBytesAtTheLeastTest()
    {
        // 400,000 empty functions, four bytes each, took 33 s and 2.2 GiB.
        const int FUNCTIONS = 10_000;
        var module = ShapeModules.Locals(FUNCTIONS, 0);
        var report = VerifyModuleShape.Scan(module);

        // Each body is two bytes: no locals, and the end.
        var floor = (FUNCTIONS + 1L) * (VerifyModuleShape.FUNCTION_FLOOR_BYTES - 2) * VerifyModuleShape.COMPILE_FUEL_PER_BYTE;

        Assert.Multiple(() =>
        {
            Assert.That(report.ShapeFuel, Is.EqualTo(floor));
            Assert.That(report.Functions, Is.EqualTo(FUNCTIONS + 1));
            Assert.That(report.CompileFuel, Is.GreaterThan(5 * module.Length * VerifyModuleShape.COMPILE_FUEL_PER_BYTE));
        });
    }

    [Test]
    public void N2AnOrdinaryProgramPaysForItsBytesAndLittleElseTest()
    {
        // The fixtures are programs a C compiler wrote: none of the new prices is for them.
        var root = FindTestProjectRoot();
        if (root == null)
            Assert.Ignore("Test project root not found");

        var modules = Directory.EnumerateFiles(Path.Combine(root, "Sandbox", "Fixtures"), "*.wasm").ToList();
        Assert.That(modules, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (var path in modules)
            {
                var module = File.ReadAllBytes(path);
                var report = VerifyModuleShape.Scan(module);

                TestContext.Out.WriteLine($"{Path.GetFileName(path)}: {module.Length} bytes, {report.Functions} functions, price {report.CompileFuel}, of which the shape {report.ShapeFuel}");
                Assert.That(report.Problem, Is.Null, path);
                Assert.That(report.ShapeFuel, Is.LessThan(report.CompileFuel / 20), path);
            }
        });
    }

    [Test]
    public void N2AShapeTheBudgetDoesNotPayForIsOutOfFuelWithoutACompileTest()
    {
        // 18,000 branches in one function of 126 KB: over a second of compiling, and a price of
        // 1.5e9 units. A budget of a thousand million does not hold it.
        var module = ShapeModules.Chain(18_000);
        var price = VerifyModuleShape.Scan(module).CompileFuel;
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = 1_000_000_000 }, null);

        var clock = Stopwatch.StartNew();
        var result = m_sandbox.Execute(m_wasi, Wasi(module), limits);

        Assert.Multiple(() =>
        {
            Assert.That(price, Is.GreaterThan(limits.FuelBudget));
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
            Assert.That(result.FuelConsumed, Is.EqualTo(limits.FuelBudget));
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)), "nothing was compiled");
        });
    }

    #endregion

    #region Tools

    /// <summary>A module whose one function has a block of a function type: values in, values out.</summary>
    private static byte[] BlockOf(int parameters, int results)
    {
        var body = new List<byte> { 0x00 };
        for (var i = 0; i < parameters; i++)
            body.AddRange([0x42, 0x00]);

        // block (type 1): drops what it took and gives constants.
        body.AddRange([0x02, 0x01]);
        for (var i = 0; i < parameters; i++)
            body.Add(0x1A);

        for (var i = 0; i < results; i++)
            body.AddRange([0x42, 0x00]);

        body.Add(0x0B);
        for (var i = 0; i < results; i++)
            body.Add(0x1A);

        body.Add(0x0B);

        return ShapeModules.Build(
            [([], []), (Enumerable.Repeat(I64, parameters).ToArray(), Enumerable.Repeat(I64, results).ToArray())],
            [(0, [0x00, 0x0B]), (0, body.ToArray())], 1);
    }

    /// <summary>
    /// The shape's price without what the smallest functions are raised to: _start has two bytes,
    /// every other function of the module the same few (the group of its locals and the end).
    /// </summary>
    private static long Floor(VerifyModuleShapeReport report, int functions)
    {
        return report.ShapeFuel
               - START_FLOOR
               - (functions - 1L) * (VerifyModuleShape.FUNCTION_FLOOR_BYTES - report.LargestBody) * VerifyModuleShape.COMPILE_FUEL_PER_BYTE;
    }

    private static VerifyTaskData Wasi(byte[] module)
    {
        return new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            EntryPoint = "main.wasm",
            Sources = [new VerifySourceFileData { Name = "main.wasm", Content = Convert.ToBase64String(module) }]
        };
    }

    private static string? FindTestProjectRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.Controller.AI.Verify.Tests.csproj")))
            directory = directory.Parent;

        return directory?.FullName;
    }

    #endregion
}
