using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The price of compiling a module a task brought, read off its shape before the engine sees it
/// (finding C-2 of the audit of 2026-11-17): a byte costs a fixed amount, locals read inside
/// nested loops cost with the square of the locals times the depth, a branch table costs with
/// its targets times the blocks around it, and blocks nested too deep are refused. The scan
/// reads every module the controller ships and the fixtures of the tests without a complaint.
/// </summary>
[TestFixture]
public sealed class VerifyModuleShapeTests
{
    #region Constants

    private const string RUNTIMES = "@Data";
    private const string FIXTURES = "Fixtures";
    private const string COMPILER = "llvm.wasm";

    #endregion

    #region Price Tests

    [Test]
    public void C2APlainModuleCostsItsBytesTest()
    {
        var module = Convert.FromBase64String(VerifySelfCheck.HELLO_WASM_BASE64);

        var report = VerifyModuleShape.Scan(module);

        // Its one function has a body of 13 bytes, priced as the least a function is.
        var floor = (VerifyModuleShape.FUNCTION_FLOOR_BYTES - 13) * VerifyModuleShape.COMPILE_FUEL_PER_BYTE;

        Assert.Multiple(() =>
        {
            Assert.That(report.Problem, Is.Null);
            Assert.That(report.CompileFuel, Is.EqualTo(module.Length * VerifyModuleShape.COMPILE_FUEL_PER_BYTE + floor));
            Assert.That(report.LargestBody, Is.EqualTo(13));
        });
    }

    [Test]
    public void C2LocalsReadInsideNestedLoopsCostWithTheirSquareTimesTheDepthTest()
    {
        // What took the engine 1.5 s, 10.6 s and 81 s to compile.
        var thousand = VerifyModuleShape.Scan(HostileModules.NestedLoops(1000, 1000));
        var threeThousand = VerifyModuleShape.Scan(HostileModules.NestedLoops(3000, 3000));
        var wide = VerifyModuleShape.Scan(HostileModules.NestedLoops(4000, 1000));
        var shallow = VerifyModuleShape.Scan(HostileModules.NestedLoops(4000, 1));

        // What 4,000 declared locals cost by themselves, whatever the loops.
        var locals = (4000L - VerifyModuleShape.FREE_LOCALS) * VerifyModuleShape.COMPILE_FUEL_PER_LOCAL;

        Assert.Multiple(() =>
        {
            Assert.That(thousand.Problem, Is.Null, "within the nesting limit: priced, not refused");
            Assert.That(thousand.CompileFuel, Is.GreaterThan(1000L * 1000 * 1000 * VerifyModuleShape.COMPILE_FUEL_PER_LOOP_UNIT));
            Assert.That(threeThousand.Problem, Is.Not.Null, "deeper than the limit: refused");
            Assert.That(wide.CompileFuel, Is.GreaterThan(15 * thousand.CompileFuel), "four times the locals: sixteen times the price");
            Assert.That(Shape(shallow, HostileModules.NestedLoops(4000, 1)) - locals, Is.EqualTo((Shape(wide, HostileModules.NestedLoops(4000, 1000)) - locals) / 1000), "a thousandth of the depth: a thousandth of the price of the loops");
        });
    }

    [Test]
    public void C2ManyFunctionsOfThatShapeCostManyTimesAsMuchTest()
    {
        var one = VerifyModuleShape.Scan(HostileModules.NestedLoops(300, 300, 1));
        var hundred = VerifyModuleShape.Scan(HostileModules.NestedLoops(300, 300, 100));

        Assert.That(hundred.CompileFuel, Is.GreaterThan(99 * (one.CompileFuel - 100 * VerifyModuleShape.COMPILE_FUEL_PER_BYTE)));
    }

    [Test]
    public void C2AShapeWithoutLoopsCostsItsBytesOnlyTest()
    {
        var module = HostileModules.NestedBlocks(1000);

        var report = VerifyModuleShape.Scan(module);

        Assert.Multiple(() =>
        {
            Assert.That(report.Problem, Is.Null);
            Assert.That(report.CompileFuel, Is.EqualTo(module.Length * VerifyModuleShape.COMPILE_FUEL_PER_BYTE), "blocks without loops and without locals are compiled in a line");
            Assert.That(report.ShapeFuel, Is.Zero);
            Assert.That(report.DeepestNesting, Is.EqualTo(1000));
        });
    }

    #endregion

    #region Refusal Tests

    [Test]
    public void C2BlocksNestedDeeperThanTheLimitAreRefusedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VerifyModuleShape.Scan(HostileModules.NestedBlocks(VerifyModuleShape.MAX_NESTING)).Problem, Is.Null);
            Assert.That(VerifyModuleShape.Scan(HostileModules.NestedBlocks(VerifyModuleShape.MAX_NESTING + 1)).Problem,
                Is.EqualTo($"a function of the module nests blocks deeper than {VerifyModuleShape.MAX_NESTING}"));
        });
    }

    [Test]
    public void C2AnInstructionTheScanDoesNotKnowIsRefusedNotGuessedAtTest()
    {
        // 0xFE opens the atomic instructions, which the engine is configured without.
        var module = HostileModules.NestedBlocks(1);
        var body = Array.LastIndexOf(module, (byte)0x02);
        module[body] = 0xFE;

        Assert.That(VerifyModuleShape.Scan(module).Problem, Is.EqualTo("the module uses an instruction the sandbox does not take"));
    }

    [Test]
    public void C2WhatIsNoModuleIsNotReadTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VerifyModuleShape.Scan([]).Problem, Is.Not.Null);
            Assert.That(VerifyModuleShape.Scan([0, 0x61, 0x73, 0x6D, 1, 0, 0, 0, 10, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F]).Problem, Is.Not.Null, "a section longer than the module");
        });
    }

    #endregion

    #region Coverage Tests

    [Test]
    public void C2TheScanReadsEveryModuleOfTheTestsAndOfTheRuntimeSetTest()
    {
        var root = FindTestProjectRoot();
        if (root == null)
            Assert.Ignore("Test project root not found");

        var modules = Directory.EnumerateFiles(Path.Combine(root, "Sandbox", FIXTURES), "*.wasm").ToList();
        var runtimes = Path.Combine(root, RUNTIMES);
        if (Directory.Exists(runtimes))
            modules.AddRange(Directory.EnumerateFiles(runtimes, "*.wasm", SearchOption.AllDirectories));

        Assert.That(modules, Is.Not.Empty);
        var refused = new List<string>();
        foreach (var path in modules)
        {
            var module = File.ReadAllBytes(path);
            var measured = VerifyModuleShape.Measure(module);
            var report = VerifyModuleShape.Scan(module);

            TestContext.Out.WriteLine($"{Path.GetFileName(path)}: {module.Length} bytes, {measured.Functions} functions, nesting {measured.DeepestNesting}, parameters {measured.WidestParameters}, results {measured.WidestResults}, "
                                      + $"block values {measured.WidestBlock}, locals {measured.MostLocals} in a function and {measured.Locals} in all, largest body {measured.LargestBody}, branches {measured.MostBranches}, "
                                      + $"{measured.CompileFuel / 1_000_000} M fuel, {measured.CompileFuel / (double)module.Length:F0} a byte{(report.Problem == null ? "" : "; as a task's module: " + report.Problem)}");

            Assert.That(measured.Problem, Is.Null, path);
            if (report.Problem != null && module.Length <= VerifyWasmSandbox.MAX_TASK_MODULE_BYTES)
                refused.Add($"{Path.GetFileName(path)}: {report.Problem}");

            // What the bounds were set against: no module of the set, the compiler included, is wider than this.
            Assert.That(measured.WidestParameters, Is.LessThanOrEqualTo(VerifyModuleShape.MAX_PARAMETERS / 4), path);
            Assert.That(measured.WidestResults, Is.LessThanOrEqualTo(1), path);
            Assert.That(measured.WidestBlock, Is.LessThanOrEqualTo(1), path);
            Assert.That(measured.MostLocals, Is.LessThanOrEqualTo(VerifyModuleShape.FREE_LOCALS), path);
        }

        // A module a task could bring (one within the size a task's module may have: the fixtures,
        // Lua, QuickJS) is within every bound. CPython, Ruby and the compiler are several times
        // that size, the controller's own, compiled by the engine for shipped modules and never
        // scanned: the largest function of each is larger than a task's may be.
        Assert.That(refused, Is.Empty);
    }

    #endregion

    #region Tools

    /// <summary>The part of a module's price that is its shape's: the price less what its bytes cost.</summary>
    private static long Shape(VerifyModuleShapeReport report, byte[] module)
    {
        return report.CompileFuel - module.Length * VerifyModuleShape.COMPILE_FUEL_PER_BYTE;
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
