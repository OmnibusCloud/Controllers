using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The memory estimate against what the engine's compiler was measured to take (wasmtime 48.0.8,
/// one thread, one compile in a fresh process, the peak over the process's memory before it: the
/// larger of the resident and the private memory). Each shape carries its peaks on an x64 Windows
/// node in its two trap modes (signals, and explicit checks as a host with hardware stack
/// protection has them) and on an arm64 macOS node. Where the arm64 node did not run a shape, it
/// is taken to need what the x64 node took with signals times the ratio the arm64 node showed for
/// the closest shape it did run (the same calls, in one function or in many), or times the largest
/// ratio it showed for any shape. The estimate must cover the largest with 15 % to spare. The measured
/// shapes are the ones the estimate was fitted to; the test holds the formula to them, not to
/// every module the engine accepts.
/// </summary>
[TestFixture]
public sealed class VerifyCompileMemoryCoverageTests
{
    #region Constants

    private const double MEBIBYTE = 1024.0 * 1024.0;

    private const double MARGIN = 1.15;

    #endregion

    #region Fields

    private static readonly Peak[] PEAKS =
    [
        new("kept-additions-18000x1", () => ShapeModulesKept.Additions(18000, 1), 22.3, null, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-additions-18000x16", () => ShapeModulesKept.Additions(18000, 16), 22.4, null, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-arithmetic-12000x1", () => ShapeModulesKept.Arithmetic(12000, 1), 27.6, 27.0, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-arithmetic-12000x16", () => ShapeModulesKept.Arithmetic(12000, 16), 43.4, 41.6, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-branches-0x1000x1", () => ShapeModules.Branches(0, 1000, 1), 8.7, 8.7, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-branches-0x1000x128", () => ShapeModules.Branches(0, 1000, 128), 18.8, 18.4, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-calls-0x65000x1", () => ShapeModules.Calls(0, 65000, 1), 216.7, 218.5, null, 1.31), // arm64: like plain-calls-0x20000
        new("kept-calls-0x65000x16", () => ShapeModules.Calls(0, 65000, 16), 308.3, 310.4, null, 1.37), // arm64: like plain-calls-0x20000, and the calls of eight values in 16 and 64 functions
        new("kept-calls-0x65000x64", () => ShapeModules.Calls(0, 65000, 64), 608.0, null, null, 1.37), // arm64: like plain-calls-0x20000, and the calls of eight values in 16 and 64 functions
        new("kept-calls-1x65000x1", () => ShapeModules.Calls(1, 65000, 1), 250.2, null, null, 1.27), // arm64: like calls-1x20000
        new("kept-calls-1x65000x16", () => ShapeModules.Calls(1, 65000, 16), 366.4, null, null, 1.37), // arm64: like calls-1x20000, and the calls of eight values in 16 and 64 functions
        new("kept-calls-2x65000x1", () => ShapeModules.Calls(2, 65000, 1), 258.6, null, null, 1.39), // arm64: like calls-2x20000
        new("kept-calls-2x65000x16", () => ShapeModules.Calls(2, 65000, 16), 383.0, null, null, 1.39), // arm64: like calls-2x20000, and the calls of eight values in 16 and 64 functions
        new("kept-calls-4x65000x1", () => ShapeModules.Calls(4, 65000, 1), 351.1, null, null, 1.22), // arm64: like calls-4x20000
        new("kept-calls-4x65000x16", () => ShapeModules.Calls(4, 65000, 16), 474.7, null, null, 1.37), // arm64: like calls-4x20000, and the calls of eight values in 16 and 64 functions
        new("kept-calls-8x65000x1", () => ShapeModules.Calls(8, 65000, 1), 607.8, 598.4, 656.3, 0), // arm64: measured
        new("kept-calls-8x65000x16", () => ShapeModules.Calls(8, 65000, 16), 750.4, 738.8, 1020.3, 0), // arm64: measured
        new("kept-callsof-0-8x13000x1", () => ShapeModules.CallsOf(0, 8, 13000, 1), 84.6, null, null, 1.32), // arm64: like callsof-0-8x5000
        new("kept-callsof-0-8x13000x16", () => ShapeModules.CallsOf(0, 8, 13000, 16), 115.8, null, null, 1.37), // arm64: like callsof-0-8x5000, and the calls of eight values in 16 and 64 functions
        new("kept-callsof-1-8x14000x1", () => ShapeModules.CallsOf(1, 8, 14000, 1), 117.3, null, null, 1.21), // arm64: like callsof-1-8x5000
        new("kept-callsof-1-8x14000x16", () => ShapeModules.CallsOf(1, 8, 14000, 16), 148.9, null, null, 1.37), // arm64: like callsof-1-8x5000, and the calls of eight values in 16 and 64 functions
        new("kept-callsof-8-0x7000x1", () => ShapeModules.CallsOf(8, 0, 7000, 1), 55.7, null, null, 0.97), // arm64: like callsof-8-0x5000
        new("kept-callsof-8-0x7000x16", () => ShapeModules.CallsOf(8, 0, 7000, 16), 71.2, null, null, 1.37), // arm64: like callsof-8-0x5000, and the calls of eight values in 16 and 64 functions
        new("kept-callsof-8-1x8000x1", () => ShapeModules.CallsOf(8, 1, 8000, 1), 59.7, 58.1, null, 0.98), // arm64: like callsof-8-1x5000
        new("kept-callsof-8-1x8000x16", () => ShapeModules.CallsOf(8, 1, 8000, 16), 80.0, 77.3, null, 1.37), // arm64: like callsof-8-1x5000, and the calls of eight values in 16 and 64 functions
        new("kept-callsof-8-1x8000x64", () => ShapeModules.CallsOf(8, 1, 8000, 64), 159.7, null, null, 1.37), // arm64: like callsof-8-1x5000, and the calls of eight values in 16 and 64 functions
        new("kept-divisions-18000x1", () => ShapeModulesKept.Divisions(18000, 1), 63.2, 480.6, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-divisions-18000x16", () => ShapeModulesKept.Divisions(18000, 16), 95.2, 672.3, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-divisions-18000x64", () => ShapeModulesKept.Divisions(18000, 64), null, 1432.6, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-grows-26000x1", () => ShapeModulesKept.Grows(26000, 1), 84.7, 86.3, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-grows-26000x16", () => ShapeModulesKept.Grows(26000, 16), 124.8, 134.5, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-imported-0x65000x1", () => ShapeModulesKept.ImportedCalls(0, 65000, 1), 239.3, 239.4, null, 1.31), // arm64: like plain-calls-0x20000
        new("kept-imported-0x65000x16", () => ShapeModulesKept.ImportedCalls(0, 65000, 16), 314.7, 315.8, null, 1.37), // arm64: like plain-calls-0x20000, and the calls of eight values in 16 and 64 functions
        new("kept-imported-0x65000x64", () => ShapeModulesKept.ImportedCalls(0, 65000, 64), 486.8, null, null, 1.37), // arm64: like plain-calls-0x20000, and the calls of eight values in 16 and 64 functions
        new("kept-imported-8x65000x1", () => ShapeModulesKept.ImportedCalls(8, 65000, 1), 630.3, null, null, 1.07), // arm64: like calls-8x65000
        new("kept-imported-8x65000x16", () => ShapeModulesKept.ImportedCalls(8, 65000, 16), 739.2, null, null, 1.37), // arm64: like calls-8x65000, and the calls of eight values in 16 and 64 functions
        new("kept-indirect-0x26000x1", () => ShapeModulesKept.IndirectCalls(0, 26000, 1), 727.0, 1386.9, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-indirect-0x26000x16", () => ShapeModulesKept.IndirectCalls(0, 26000, 16), 910.6, 1878.2, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-indirect-0x26000x64", () => ShapeModulesKept.IndirectCalls(0, 26000, 64), 1587.4, null, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-indirect-8x26000x1", () => ShapeModulesKept.IndirectCalls(8, 26000, 1), 914.7, 1662.4, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-indirect-8x26000x16", () => ShapeModulesKept.IndirectCalls(8, 26000, 16), 1163.5, null, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-loads-21000x1", () => ShapeModulesKept.Loads(21000, 1), 18.0, null, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-loads-21000x16", () => ShapeModulesKept.Loads(21000, 16), 17.5, null, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-stores-18000x1", () => ShapeModulesKept.Stores(18000, 1), 17.0, null, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-stores-18000x16", () => ShapeModulesKept.Stores(18000, 16), 16.2, null, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-sumloads-10000x1", () => ShapeModulesKept.SummedLoads(10000, 1), 35.1, 287.5, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-sumloads-10000x16", () => ShapeModulesKept.SummedLoads(10000, 16), 49.9, 353.4, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-sumloads-10000x64", () => ShapeModulesKept.SummedLoads(10000, 64), null, 562.8, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-truncations-14000x1", () => ShapeModulesKept.Truncations(14000, 1), 80.8, 612.4, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-truncations-14000x16", () => ShapeModulesKept.Truncations(14000, 16), 182.0, 867.4, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("kept-truncations-14000x64", () => ShapeModulesKept.Truncations(14000, 64), null, 1838.3, null, 1.84), // arm64: the largest ratio of any shape it ran
        new("blocks-1x2048", () => ShapeModules.Blocks(1, 2048, 1), 9.2, 9.2, 11.8, 0), // arm64: measured
        new("blocks-2x2048", () => ShapeModules.Blocks(2, 2048, 1), 17.4, 17.4, 20.8, 0), // arm64: measured
        new("blocks-4x2048", () => ShapeModules.Blocks(4, 2048, 1), 35.1, 34.8, 39.0, 0), // arm64: measured
        new("blocks-8x2048", () => ShapeModules.Blocks(8, 2048, 1), 71.6, 71.8, 75.2, 0), // arm64: measured
        new("blocks-8x2048x8", () => ShapeModules.Blocks(8, 2048, 8), 75.4, 75.4, 76.5, 0), // arm64: measured
        new("body-131072", () => ShapeModules.Body(131072), 0.1, 0.1, 0.1, 0), // arm64: measured
        new("branches-0x16000", () => ShapeModules.Branches(0, 16000, 1), 135.4, 135.5, 160.1, 0), // arm64: measured
        new("branches-0x32000", () => ShapeModules.Branches(0, 32000, 1), 270.4, 270.7, 299.9, 0), // arm64: measured
        new("branches-0x4000", () => ShapeModules.Branches(0, 4000, 1), 34.0, 34.0, 41.6, 0), // arm64: measured
        new("branches-0x4000x8", () => ShapeModules.Branches(0, 4000, 8), 43.1, 42.7, 50.2, 0), // arm64: measured
        new("branches-0x8000", () => ShapeModules.Branches(0, 8000, 1), 67.9, 68.0, 81.2, 0), // arm64: measured
        new("branches-8x16000", () => ShapeModules.Branches(8, 16000, 1), 136.2, 136.0, 161.2, 0), // arm64: measured
        new("branches-8x32000", () => ShapeModules.Branches(8, 32000, 1), 271.9, 272.0, 310.4, 0), // arm64: measured
        new("branches-8x4000", () => ShapeModules.Branches(8, 4000, 1), 34.2, 34.3, 42.2, 0), // arm64: measured
        new("branches-8x8000", () => ShapeModules.Branches(8, 8000, 1), 68.5, 68.5, 83.1, 0), // arm64: measured
        new("calls-1x20000", () => ShapeModules.Calls(1, 20000, 1), 88.8, 90.0, 114.0, 0), // arm64: measured
        new("calls-2x20000", () => ShapeModules.Calls(2, 20000, 1), 92.6, 92.7, 129.8, 0), // arm64: measured
        new("calls-4x20000", () => ShapeModules.Calls(4, 20000, 1), 128.1, 128.9, 158.7, 0), // arm64: measured
        new("calls-6x20000", () => ShapeModules.Calls(6, 20000, 1), 157.1, 156.6, 164.3, 0), // arm64: measured
        new("calls-8x20000", () => ShapeModules.Calls(8, 20000, 1), 180.8, 183.8, 210.1, 0), // arm64: measured
        new("calls-8x60000", () => ShapeModules.Calls(8, 60000, 1), 583.9, 565.0, 618.3, 0), // arm64: measured
        new("calls-8x65000", () => ShapeModules.Calls(8, 65000, 1), 608.2, 598.7, 656.3, 0), // arm64: measured
        new("calls-8x65000x16", () => ShapeModules.Calls(8, 65000, 16), 750.7, null, 1020.3, 0), // arm64: measured
        new("calls-8x65000x64", () => ShapeModules.Calls(8, 65000, 64), 1382.4, null, 1892.8, 0), // arm64: measured
        new("callsof-0-4x5000", () => ShapeModules.CallsOf(0, 4, 5000, 1), 25.3, 25.3, 33.3, 0), // arm64: measured
        new("callsof-0-8x5000", () => ShapeModules.CallsOf(0, 8, 5000, 1), 36.1, 36.1, 47.6, 0), // arm64: measured
        new("callsof-1-8x5000", () => ShapeModules.CallsOf(1, 8, 5000, 1), 40.6, 40.8, 49.3, 0), // arm64: measured
        new("callsof-8-0x5000", () => ShapeModules.CallsOf(8, 0, 5000, 1), 39.9, 40.9, 38.9, 0), // arm64: measured
        new("callsof-8-1x5000", () => ShapeModules.CallsOf(8, 1, 5000, 1), 40.5, 41.2, 40.7, 0), // arm64: measured
        new("callsof-8-8x5000", () => ShapeModules.CallsOf(8, 8, 5000, 1), 45.2, 46.0, 57.0, 0), // arm64: measured
        new("chain-16000", () => ShapeModules.Chain(16000), 143.9, 143.9, 129.4, 0), // arm64: measured
        new("chain-18000", () => ShapeModules.Chain(18000), 164.9, 165.1, 144.0, 0), // arm64: measured
        new("chain-8000", () => ShapeModules.Chain(8000), 72.2, 72.5, 70.0, 0), // arm64: measured
        new("combined-at-bounds", () => AtThreeBounds(), 612.7, 603.7, 836.3, 0), // arm64: measured
        new("fixture-sum", () => Fixture("sum.wasm"), 9.6, 14.2, 13.0, 0), // arm64: measured
        new("fixture-words", () => Fixture("words.wasm"), 7.0, 10.5, 14.3, 0), // arm64: measured
        new("hello", () => Hello(), 0.3, 0.3, 0.3, 0), // arm64: measured
        new("live-1000x10000", () => ShapeModules.Live(1000, 10000, 1), 118.4, 118.4, 189.8, 0), // arm64: measured
        new("live-1000x2000", () => ShapeModules.Live(1000, 2000, 1), 29.5, 29.8, 46.0, 0), // arm64: measured
        new("live-1000x5000", () => ShapeModules.Live(1000, 5000, 1), 60.6, 60.5, 112.8, 0), // arm64: measured
        new("live-100x20000", () => ShapeModules.Live(100, 20000, 1), 177.5, 177.6, 182.6, 0), // arm64: measured
        new("live-100x400x200", () => ShapeModules.Live(100, 400, 200), 21.4, 21.7, 15.2, 0), // arm64: measured
        new("live-125x1250", () => ShapeModules.Live(125, 1250, 1), 14.0, 14.1, 20.7, 0), // arm64: measured
        new("live-128x128x500", () => ShapeModules.Live(128, 128, 500), 26.1, 26.9, 24.3, 0), // arm64: measured
        new("live-128x512x100", () => ShapeModules.Live(128, 512, 100), 15.5, 15.6, 14.2, 0), // arm64: measured
        new("live-16x64x2000", () => ShapeModules.Live(16, 64, 2000), 33.2, 33.6, 32.0, 0), // arm64: measured
        new("live-2000x1000", () => ShapeModules.Live(2000, 1000, 1), 24.0, 24.1, 34.3, 0), // arm64: measured
        new("live-2000x20000", () => ShapeModules.Live(2000, 20000, 1), 283.4, 282.8, 519.4, 0), // arm64: measured
        new("live-250x2500", () => ShapeModules.Live(250, 2500, 1), 24.3, 24.3, 40.0, 0), // arm64: measured
        new("live-256x1024x30", () => ShapeModules.Live(256, 1024, 30), 18.6, 18.7, 21.9, 0), // arm64: measured
        new("live-32x128x1000", () => ShapeModules.Live(32, 128, 1000), 38.6, 38.7, 27.2, 0), // arm64: measured
        new("live-4000x500", () => ShapeModules.Live(4000, 500, 1), 28.2, 28.2, 34.5, 0), // arm64: measured
        new("live-500x5000", () => ShapeModules.Live(500, 5000, 1), 48.2, 48.4, 82.9, 0), // arm64: measured
        new("live-64x256x500", () => ShapeModules.Live(64, 256, 500), 36.9, 35.9, 25.4, 0), // arm64: measured
        new("locals-10000x0", () => ShapeModules.Locals(10000, 0), 57.0, 58.0, 74.0, 0), // arm64: measured
        new("locals-32767x0", () => ShapeModules.Locals(32767, 0), 189.1, 191.0, 239.8, 0), // arm64: measured
        new("plain-calls-0x20000", () => ShapeModules.Calls(0, 20000), 74.7, 75.1, 97.5, 0), // arm64: measured
    ];

    #endregion

    #region Tests

    [Test]
    public void D7TheEstimateCoversWhatEveryMeasuredShapeTookTest()
    {
        var uncovered = new List<string>();
        foreach (var peak in PEAKS)
        {
            var module = peak.Module();
            var report = VerifyModuleShape.Measure(module);
            var needed = MARGIN * MEBIBYTE * peak.Needed();
            if (report.CompileMemoryBytes < needed)
                uncovered.Add($"{peak.Shape}: estimate {report.CompileMemoryBytes / MEBIBYTE:F0} MiB, needed {needed / MEBIBYTE:F0} MiB");
        }

        Assert.That(uncovered, Is.Empty, "the estimate does not cover what these shapes took, with the margin:\n" + string.Join("\n", uncovered));
    }

    [Test]
    public void D7EveryShapeThatWasMeasuredIsInTheTableTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PEAKS.Select(peak => peak.Shape).Distinct().Count(), Is.EqualTo(PEAKS.Length));
            Assert.That(PEAKS.Count(peak => peak.Arm64 != null), Is.GreaterThanOrEqualTo(55), "the shapes the arm64 node ran");
            Assert.That(PEAKS.Where(peak => peak.Shape.StartsWith("kept-", StringComparison.Ordinal)).Count(), Is.GreaterThanOrEqualTo(40), "the shapes of what a compile keeps");
        });
    }

    #endregion

    #region Tools

    private static byte[] Hello()
    {
        return Convert.FromBase64String(VerifySelfCheck.HELLO_WASM_BASE64);
    }

    private static byte[] Fixture(string name)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.Controller.AI.Verify.Tests.csproj")))
            directory = directory.Parent;

        var path = directory == null
            ? Path.Combine(TestContext.CurrentContext.TestDirectory, "Sandbox", "Fixtures", name)
            : Path.Combine(directory.FullName, "Sandbox", "Fixtures", name);
        return File.ReadAllBytes(path);
    }

    /// <summary>A module at three bounds at once: 32,768 functions, a body of 65,000 calls that pass eight values, eight results through 2,048 blocks.</summary>
    private static byte[] AtThreeBounds()
    {
        const byte I64 = 0x7E;
        var eight = Enumerable.Repeat(I64, 8).ToArray();

        var calls = new List<byte> { 0x00 };
        for (var i = 0; i < 8; i++)
            calls.AddRange([0x42, 0x00]);
        for (var i = 0; i < 65_000; i++)
            calls.AddRange([0x10, 0x01]);
        for (var i = 0; i < 8; i++)
            calls.Add(0x1A);
        calls.Add(0x0B);

        var blocks = new List<byte> { 0x00 };
        for (var i = 0; i < 2048; i++)
            blocks.AddRange([0x02, 0x02]);
        for (var i = 0; i < 8; i++)
            blocks.AddRange([0x42, 0x00]);
        for (var i = 0; i < 2048; i++)
            blocks.Add(0x0B);
        for (var i = 0; i < 8; i++)
            blocks.Add(0x1A);
        blocks.Add(0x0B);

        var functions = new List<(int Type, byte[] Body)> { (0, [0x00, 0x0B]), (1, [0x00, 0x00, 0x0B]), (0, calls.ToArray()), (0, blocks.ToArray()) };
        while (functions.Count < VerifyModuleShape.MAX_FUNCTIONS)
            functions.Add((0, [0x00, 0x0B]));

        return ShapeModules.Build([([], []), (eight, eight), ([], eight)], functions, 1);
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// One measured shape: how to build it and what its compile took, MiB, on each node (null where
    /// it did not run), and for a shape the arm64 node did not run, the ratio it is taken to need over x64.
    /// </summary>
    private sealed record Peak(string Shape, Func<byte[]> Module, double? X64, double? X64Explicit, double? Arm64, double Arm64Factor)
    {
        public double Needed()
        {
            var x64 = Math.Max(X64 ?? 0, X64Explicit ?? 0);
            var arm64 = Arm64 ?? Arm64Factor * (X64 ?? 0);
            return Math.Max(x64, arm64);
        }
    }

    #endregion
}
