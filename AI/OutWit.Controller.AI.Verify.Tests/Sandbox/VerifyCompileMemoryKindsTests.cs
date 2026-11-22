using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// What the compile's memory estimate counts by the kind of an instruction and of what a module
/// has besides its code (VerifyCompileMemory): every family of instructions at the most any
/// measured instruction of it took, calls by the values of the type they call, loops by the checks
/// at their head, the code the engine compiles for types and escaping functions; the bounds of a
/// module's types, imports and exports; and counts that do not fit, which are unreadable.
/// </summary>
[TestFixture]
public sealed class VerifyCompileMemoryKindsTests
{
    #region Constants

    private const long MEBIBYTE = 1L << 20;

    #endregion

    #region Tests

    [Test]
    public void D7ACallCountsTheValuesOfTheTypeItCallsTest()
    {
        // A call of a function of the module, of an import and through a table: the units of
        // its kind and a unit a value of the type called, a parameter or a result.
        const int CALLS = 1000;
        var none = VerifyModuleShape.Scan(ShapeModules.Calls(0, CALLS));
        var direct = VerifyModuleShape.Scan(ShapeModules.Calls(8, CALLS));
        var imported = VerifyModuleShape.Scan(ShapeModulesKept.ImportedCalls(8, CALLS));
        var indirect = VerifyModuleShape.Scan(ShapeModulesKept.IndirectCalls(8, CALLS));
        var constants = 8L * VerifyCompileMemory.UNITS_PLAIN;
        var callee = VerifyCompileMemory.UNITS_TRAP; // its one instruction, unreachable

        Assert.Multiple(() =>
        {
            Assert.That(none.KeptUnits, Is.EqualTo(CALLS * (long)VerifyCompileMemory.UNITS_CALL + callee));
            Assert.That(direct.KeptUnits, Is.EqualTo(CALLS * VerifyCompileMemory.CallUnits(false, 8, 8) + constants + callee));
            Assert.That(imported.KeptUnits, Is.EqualTo(CALLS * VerifyCompileMemory.CallUnits(false, 8, 8) + constants), "an import has the type it is imported with");
            Assert.That(indirect.KeptUnits, Is.EqualTo(CALLS * (VerifyCompileMemory.CallUnits(true, 8, 8) + VerifyCompileMemory.UNITS_PLAIN) + constants + callee));
            Assert.That(VerifyCompileMemory.CallUnits(false, 8, 8), Is.EqualTo(VerifyCompileMemory.UNITS_CALL + 16 * VerifyCompileMemory.UNITS_PER_VALUE));
            Assert.That(indirect.LargestWork - direct.LargestWork,
                Is.EqualTo((indirect.LargestBody - direct.LargestBody) * VerifyCompileMemory.PER_BODY_BYTE + CALLS * VerifyCompileMemory.WORK_INDIRECT_CALL),
                "a call through a table takes the compiler more while it works on the function");
        });
    }

    [Test]
    public void D7EveryKindOfInstructionCountsWhatItKeepsAndTakesTest()
    {
        const int COUNT = 1000;
        var memory = (long)VerifyCompileMemory.UNITS_MEMORY;
        var plain = (long)VerifyCompileMemory.UNITS_PLAIN;

        var loads = VerifyModuleShape.Scan(ShapeModulesKept.SummedLoads(COUNT));
        var divisions = VerifyModuleShape.Scan(ShapeModulesKept.Divisions(COUNT));
        var truncations = VerifyModuleShape.Scan(ShapeModulesKept.Truncations(COUNT));
        var grows = VerifyModuleShape.Scan(ShapeModulesKept.Grows(COUNT));
        var branches = VerifyModuleShape.Scan(ShapeModules.Branches(0, COUNT));
        var arithmetic = VerifyModuleShape.Scan(ShapeModulesKept.Arithmetic(COUNT));

        Assert.Multiple(() =>
        {
            // A load from a base read first, a load and an addition each, the store of the sum.
            Assert.That(loads.KeptUnits, Is.EqualTo(2 * memory + 2 * plain + COUNT * (memory + plain)));
            Assert.That(loads.LargestWork, Is.EqualTo(VerifyCompileMemory.Work(loads.LargestBody, (COUNT + 2) * VerifyCompileMemory.WORK_MEMORY)));
            // Two values read first, a division each.
            Assert.That(divisions.KeptUnits, Is.EqualTo(2 * memory + 2 * plain + COUNT * (long)VerifyCompileMemory.UNITS_DIVISION));
            // A value read first; a conversion to a float, one back that may trap, a constant and an addition each; the store.
            Assert.That(truncations.KeptUnits, Is.EqualTo(2 * memory + 2 * plain + COUNT * (VerifyCompileMemory.UNITS_TRUNCATION + VerifyCompileMemory.UNITS_FLOAT + 2 * plain)));
            Assert.That(grows.KeptUnits, Is.EqualTo(COUNT * (VerifyCompileMemory.UNITS_ENGINE_CALL + plain)));
            Assert.That(branches.KeptUnits, Is.EqualTo(COUNT * (long)VerifyCompileMemory.UNITS_BRANCH));
            Assert.That(arithmetic.KeptUnits, Is.EqualTo(2 * memory + 2 * plain + COUNT * 4 * plain));
            Assert.That(grows.LargestWork, Is.EqualTo(VerifyCompileMemory.Work(grows.LargestBody, COUNT * VerifyCompileMemory.WORK_ENGINE_CALL)));
        });
    }

    [Test]
    public void D7AnInstructionIsReadWithAllItsImmediatesTest()
    {
        // A select with its type, and an import: what follows them is the next instruction, not
        // their bytes read as instructions (the type of a select is i64, which read as an
        // instruction would be a multiplication).
        byte[] select = [0x00, 0x42, 0x00, 0x42, 0x00, 0x41, 0x00, 0x1C, 0x01, 0x7E, 0x1A, 0x0B];
        var report = VerifyModuleShape.Scan(ShapeModules.Build([([], [])], [(0, [0x00, 0x0B]), (0, select)], 1));
        var imported = VerifyModuleShape.Scan(ShapeModulesKept.ImportedCalls(2, 10));

        Assert.Multiple(() =>
        {
            Assert.That(report.Problem, Is.Null);
            Assert.That(report.KeptUnits, Is.EqualTo(4L * VerifyCompileMemory.UNITS_PLAIN), "three constants and the select");
            Assert.That(imported.Problem, Is.Null, "the import's names are passed over whole");
            Assert.That(imported.KeptUnits, Is.EqualTo(10 * VerifyCompileMemory.CallUnits(false, 2, 2) + 2L * VerifyCompileMemory.UNITS_PLAIN));
        });
    }

    [Test]
    public void D7ALoopCountsTheChecksAtItsHeadTest()
    {
        // The engine checks the fuel and the clock at the head of every loop, each with a call out
        // of the line: 8 MB of empty loops in 32,767 functions took 1.6 GiB and 70 s on x64.
        const int COUNT = 1000;
        var loops = VerifyModuleShape.Scan(ShapeModulesKept.Loops(COUNT));
        var many = VerifyModuleShape.Measure(ShapeModulesKept.Loops(83, 32_767));

        Assert.Multiple(() =>
        {
            Assert.That(loops.KeptUnits, Is.EqualTo(COUNT * (long)VerifyCompileMemory.UNITS_LOOP));
            Assert.That(loops.LargestWork, Is.EqualTo(VerifyCompileMemory.Work(loops.LargestBody, COUNT * VerifyCompileMemory.WORK_LOOP)));
            Assert.That(many.CompileMemoryBytes, Is.GreaterThanOrEqualTo((long)(1.15 * 1.84 * 1681 * MEBIBYTE)), "over the x64 peak times the arm64 stand-in, with the margin");
            Assert.That(VerifyModuleShape.Scan(ShapeModulesKept.Loops(83, 32_767)).Problem, Is.EqualTo(VerifyModuleShape.TOO_MUCH_MEMORY));
        });
    }

    [Test]
    public void D7EveryFamilyCountsAtLeastTheMostOfItsMeasuredInstructionsTest()
    {
        // A float, a vector, a saturating conversion, a reference and a table instruction, a trap:
        // each counts its family's units, and an instruction of no family counts the most of any.
        const int COUNT = 100;
        var memory = (long)VerifyCompileMemory.UNITS_MEMORY;
        var plain = (long)VerifyCompileMemory.UNITS_PLAIN;

        // Two values read first and one stored at the end: three constants and three loads or stores.
        var around = 3 * plain + 3 * memory;
        var floats = VerifyModuleShape.Scan(ShapeModulesKept.Steps(0x7C, [0x20, 0x00, 0x20, 0x01, 0xA4, 0x21, 0x00], COUNT));
        var vectors = VerifyModuleShape.Scan(ShapeModulesKept.Steps(0x7B, [0x20, 0x00, 0xFD, 0x62, 0x21, 0x00], COUNT));
        var saturating = VerifyModuleShape.Scan(ShapeModulesKept.SaturatingTruncations(COUNT));
        var references = VerifyModuleShape.Scan(ShapeModulesKept.FunctionReferences(COUNT));
        var tables = VerifyModuleShape.Scan(ShapeModulesKept.TableReads(COUNT));
        var traps = VerifyModuleShape.Scan(ShapeModulesKept.GuardedTraps(COUNT));
        int[] families =
        [
            VerifyCompileMemory.UNITS_PLAIN, VerifyCompileMemory.UNITS_FLOAT, VerifyCompileMemory.UNITS_VECTOR, VerifyCompileMemory.UNITS_SATURATING,
            VerifyCompileMemory.UNITS_MEMORY, VerifyCompileMemory.UNITS_BRANCH, VerifyCompileMemory.UNITS_LOOP, VerifyCompileMemory.UNITS_TRAP,
            VerifyCompileMemory.UNITS_DIVISION, VerifyCompileMemory.UNITS_TRUNCATION, VerifyCompileMemory.UNITS_ENGINE_CALL
        ];
        long[] works =
        [
            VerifyCompileMemory.WORK_MEMORY, VerifyCompileMemory.WORK_DIVISION, VerifyCompileMemory.WORK_TRUNCATION, VerifyCompileMemory.WORK_LOOP,
            VerifyCompileMemory.WORK_ENGINE_CALL, VerifyCompileMemory.WORK_FLOAT, VerifyCompileMemory.WORK_VECTOR
        ];

        Assert.Multiple(() =>
        {
            Assert.That(floats.KeptUnits, Is.EqualTo(around + COUNT * (long)VerifyCompileMemory.UNITS_FLOAT));
            Assert.That(vectors.KeptUnits, Is.EqualTo(around + COUNT * (long)VerifyCompileMemory.UNITS_VECTOR));
            Assert.That(saturating.KeptUnits, Is.EqualTo(around + COUNT * (VerifyCompileMemory.UNITS_SATURATING + VerifyCompileMemory.UNITS_FLOAT + 2 * plain)));
            Assert.That(references.KeptUnits, Is.EqualTo(COUNT * (long)VerifyCompileMemory.UNITS_ENGINE_CALL));
            Assert.That(tables.KeptUnits, Is.EqualTo(COUNT * (VerifyCompileMemory.UNITS_ENGINE_CALL + plain)));
            Assert.That(traps.KeptUnits, Is.EqualTo(plain + memory + COUNT * (long)(VerifyCompileMemory.UNITS_BRANCH + VerifyCompileMemory.UNITS_TRAP)));
            Assert.That(floats.LargestWork, Is.EqualTo(VerifyCompileMemory.Work(floats.LargestBody, 3 * VerifyCompileMemory.WORK_MEMORY + COUNT * VerifyCompileMemory.WORK_FLOAT)));
            Assert.That(VerifyCompileMemory.Units(0xC5, 0), Is.EqualTo(VerifyCompileMemory.UNITS_UNCLASSIFIED), "an instruction of no family");
            Assert.That(VerifyCompileMemory.WorkOf(0xC5, 0), Is.EqualTo(VerifyCompileMemory.WORK_UNCLASSIFIED));
            Assert.That(VerifyCompileMemory.UNITS_UNCLASSIFIED, Is.EqualTo(families.Max()), "counted as the most of any family");
            Assert.That(VerifyCompileMemory.WORK_UNCLASSIFIED, Is.EqualTo(works.Max()));
        });
    }

    [Test]
    public void D7WhatTheEngineCompilesForTypesAndEscapingFunctionsIsCountedTest()
    {
        // The engine compiles code for every function type and for every function that leaves the
        // module: 600,000 types in 7 MB took 3.8 GiB; 32,767 functions of 128 parameters in a
        // table 1 GiB, where the same functions not in a table take a fifth of that.
        const int FUNCTIONS = 1000;
        var tabled = VerifyModuleShape.Scan(ShapeModulesSurface.TabledFunctions(FUNCTIONS, 8));
        var exported = VerifyModuleShape.Scan(ShapeModulesSurface.ExportedFunctions(FUNCTIONS, 8));
        var types = VerifyModuleShape.Scan(ShapeModulesSurface.WideTypes(100, 120));

        Assert.Multiple(() =>
        {
            // Two types (_start's and the functions'), the memory and _start exported, _start and the functions leaving.
            Assert.That(tabled.SurfaceBytes, Is.EqualTo(VerifyCompileMemory.Surface(2, 8, 0, 2, FUNCTIONS + 1, FUNCTIONS * 8L)));
            Assert.That(exported.SurfaceBytes, Is.EqualTo(VerifyCompileMemory.Surface(2, 8, 0, FUNCTIONS + 2, FUNCTIONS + 1, FUNCTIONS * 8L)));
            Assert.That(types.SurfaceBytes, Is.GreaterThanOrEqualTo(99 * (VerifyCompileMemory.PER_TYPE + 121 * VerifyCompileMemory.PER_TYPE_VALUE)), "99 types of 121 parameters or more");
            Assert.That(VerifyModuleShape.Measure(ShapeModulesSurface.TabledFunctions(32_767, 128)).CompileMemoryBytes,
                Is.GreaterThanOrEqualTo((long)(1.15 * 1.84 * 1023 * MEBIBYTE)), "over the x64 peak times the arm64 stand-in, with the margin");
        });
    }

    [Test]
    public void D7AModuleIsHeldToItsTypesImportsAndExportsTest()
    {
        // The compiler the controller ships has 542 types, CPython 176; CPython imports 42 functions;
        // a program built to export all its functions exports one a function.
        Assert.Multiple(() =>
        {
            Assert.That(VerifyModuleShape.Scan(ShapeModulesSurface.ManyTypes(VerifyModuleShape.MAX_TYPES)).Problem, Is.Null);
            Assert.That(VerifyModuleShape.Scan(ShapeModulesSurface.ManyTypes(VerifyModuleShape.MAX_TYPES + 1)).Problem,
                Is.EqualTo($"the module has more than {VerifyModuleShape.MAX_TYPES} function types"));
            Assert.That(VerifyModuleShape.Scan(ShapeModulesSurface.ManyTypes(600_000)).Problem,
                Is.EqualTo($"the module has more than {VerifyModuleShape.MAX_TYPES} function types"), "600,000 types took 3.8 GiB");
            Assert.That(VerifyModuleShape.Scan(ShapeModulesSurface.ManyImports(VerifyModuleShape.MAX_IMPORTS, 1)).Problem, Is.Null);
            Assert.That(VerifyModuleShape.Scan(ShapeModulesSurface.ManyImports(VerifyModuleShape.MAX_IMPORTS + 1, 1)).Problem,
                Is.EqualTo($"the module has more than {VerifyModuleShape.MAX_IMPORTS} imports"));
            // The memory and _start are exported besides.
            Assert.That(VerifyModuleShape.Scan(ShapeModulesSurface.ManyExports(VerifyModuleShape.MAX_EXPORTS - 2)).Problem, Is.Null);
            Assert.That(VerifyModuleShape.Scan(ShapeModulesSurface.ManyExports(VerifyModuleShape.MAX_EXPORTS - 1)).Problem,
                Is.EqualTo($"the module has more than {VerifyModuleShape.MAX_EXPORTS} exports"));
        });
    }

    [Test]
    public void D7AModuleWhoseCountsDoNotFitIsUnreadableTest()
    {
        // The scan does not lean on the engine's validation: a count or a length that does not fit
        // what is left, or a select of other than one type, is unreadable, and the scan ends.
        const string UNREADABLE = "the module's code cannot be read";
        byte[] twoTypes = [0x00, 0x42, 0x00, 0x42, 0x00, 0x41, 0x00, 0x1C, 0x02, 0x7E, 0x7E, 0x1A, 0x0B];
        var select = ShapeModules.Build([([], [])], [(0, [0x00, 0x0B]), (0, twoTypes)], 1);

        // An import whose module name says it is 4 GiB long.
        byte[] header = [0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00, 0x01, 0x04, 0x01, 0x60, 0x00, 0x00];
        var longName = header.Concat(new byte[] { 0x02, 0x08, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0x65, 0x00 }).ToArray();

        // A type section that says it holds a thousand million types.
        byte[] manyTypes = [0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00, 0x01, 0x06, 0x80, 0x94, 0xEB, 0xDC, 0x03, 0x60];

        Assert.Multiple(() =>
        {
            Assert.That(VerifyModuleShape.Scan(select).Problem, Is.EqualTo(UNREADABLE));
            Assert.That(VerifyModuleShape.Measure(longName).Problem, Is.EqualTo(UNREADABLE));
            Assert.That(VerifyModuleShape.Measure(manyTypes).Problem, Is.EqualTo(UNREADABLE));
        });
    }

    #endregion
}
