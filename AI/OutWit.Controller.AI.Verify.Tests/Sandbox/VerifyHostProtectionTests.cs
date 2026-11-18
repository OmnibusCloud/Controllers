using System.Diagnostics;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// A guest's trap must not take its host down. On Windows a .NET 9+ executable runs with
/// hardware-enforced stack protection (CET shadow stacks) by default, and a trap the engine
/// resumed from by rewriting the thread's context killed such a process (fast fail 0x30): the
/// cloud client's worker died with every task in it. The test host of these tests is not such a
/// process, so the check runs a separate executable built the way a host is
/// (OutWit.Controller.AI.Verify.Tests.TrapHost) and reads its output.
/// <para>
/// Whether that executable really runs under shadow stacks depends on the machine: the
/// processor and the system must enforce them. Where they do not, the proof of survival under
/// stack protection does not run, and its test says so by being skipped with that reason
/// rather than passing (audit finding V:M-7). The explicit-trap mode itself is asked for in a
/// test of its own, so it is exercised on every machine.
/// </para>
/// <para>
/// Nothing fails a Windows run for a machine without shadow stacks: the build server's Windows
/// runners are not one kind of machine. What a run owes instead is to say which case it was, in
/// one line of its log that a release check can look for (<see cref="ProofLine"/>): a release is
/// cut from a commit whose Windows job says <c>stack-protection proof: shadow stacks on</c>.
/// </para>
/// </summary>
[TestFixture]
public sealed class VerifyHostProtectionTests
{
    #region Constants

    private const string HOST = "OutWit.Controller.AI.Verify.Tests.TrapHost";
    private const int TIMEOUT_MS = 180_000;

    private const string SHADOW_STACKS_ON = "shadow stacks on";
    private const string SHADOW_STACKS_OFF = "shadow stacks off";
    private const string TRAPS_EXPLICIT = "traps explicit";

    private const string PROOF_LINE = "stack-protection proof: ";

    // Worded so that a search for "shadow stacks on" does not find it: the reason used to read
    // "WITHOUT shadow stacks on this machine", and a run without them answered such a search.
    private const string NOT_PROVEN =
        "the trap host ran WITHOUT shadow stacks: this machine (its processor or its system) does not enforce them, " +
        "and that a host under hardware stack protection survives a guest's trap was NOT proven here. " +
        "The explicit-trap mode such a host runs in is still exercised (M7ExplicitTrapsGiveTheVerdictsOfHardwareTrapsTest).";
    private const string SUMMARY_ENV = "GITHUB_STEP_SUMMARY";

    private const int IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS = 20;
    private const int IMAGE_DLLCHARACTERISTICS_EX_CET_COMPAT = 0x1;

    #endregion

    #region Tests

    [Test]
    public void AGuestTrapLeavesTheHostRunningTest()
    {
        var run = RunHost();
        TestContext.Out.WriteLine(run.Output);

        AssertEveryTrapIsAVerdict(run);
    }

    [Test]
    public void M7AHostUnderStackProtectionSurvivesAGuestTrapTest()
    {
        // The proof the page for contributors promises: a process with hardware-enforced stack
        // protection is not killed by a guest's trap. It is a proof only where the process really
        // runs under that protection. The test above passes with it off as well, and used to be
        // the only one: on a machine without it nothing said that the proof had not run.
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("hardware-enforced stack protection (CET shadow stacks) is Windows': the proof does not run on this system");

        var run = RunHost();
        TestContext.Out.WriteLine(run.Output);

        // The one line that says which case this run was, written before anything is asserted:
        // a release check looks for it in the Windows job's log.
        var enforced = run.Output.Contains(SHADOW_STACKS_ON);
        StateTheCase(ProofLine(enforced));

        if (!enforced)
        {
            Assert.That(run.Output, Does.Contain(SHADOW_STACKS_OFF), run.Output);
            Assert.Ignore(NOT_PROVEN);
        }

        Assert.That(run.Output, Does.Contain(TRAPS_EXPLICIT), "a host with shadow stacks takes traps as explicit checks");
        AssertEveryTrapIsAVerdict(run);
    }

    [Test]
    public void C5TheLineARunStatesItsCaseInIsFixedTest()
    {
        // A release check looks for these words in the Windows job's log; they are not to drift.
        Assert.Multiple(() =>
        {
            Assert.That(ProofLine(shadowStacks: true), Is.EqualTo("stack-protection proof: shadow stacks on (a host under hardware stack protection ran the guests' traps)"));
            Assert.That(ProofLine(shadowStacks: false), Is.EqualTo("stack-protection proof: shadow stacks off (NOT proven on this machine: it does not enforce them)"));
            Assert.That(ProofLine(shadowStacks: false), Does.Not.Contain(SHADOW_STACKS_ON), "the line of a run without them is not found by a search for the other");
            Assert.That(NOT_PROVEN, Does.Not.Contain(SHADOW_STACKS_ON), "nor is the reason the proof is skipped with");
        });
    }

    [Test]
    public void M7ExplicitTrapsGiveTheVerdictsOfHardwareTrapsTest()
    {
        // The mode a host under stack protection runs in, asked for on any machine: the build
        // server's Windows may not enforce shadow stacks, and the explicit checks would never run
        // there. The same four guests, the same verdicts, words and fuel as in the mode the
        // process picks for itself.
        var chosen = RunHost();
        var forced = RunHost("--explicit-traps");
        TestContext.Out.WriteLine(chosen.Output);
        TestContext.Out.WriteLine(forced.Output);

        Assert.Multiple(() =>
        {
            Assert.That(forced.Output, Does.Contain(TRAPS_EXPLICIT));
            AssertEveryTrapIsAVerdict(forced);
            Assert.That(TrapLines(forced.Output), Is.EqualTo(TrapLines(chosen.Output)), "verdict, fuel and words of every trap, in both modes");
        });
    }

    [Test]
    public void TheHostIsBuiltWithStackProtectionTest()
    {
        // Without the flag the stack-protection proof would prove nothing on Windows.
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("hardware-enforced stack protection is a Windows apphost flag");

        Assert.That(IsCetCompatible(HostPath()), Is.True, "the trap host must be CET-compatible, as a .NET 10 host application is");
    }

    [Test]
    public void TrapsAreExplicitOnlyWhereTheHostNeedsItTest()
    {
        // Explicit checks cost a guest speed: a host without shadow stacks keeps hardware traps.
        var (_, output) = RunHost();

        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain($"{SHADOW_STACKS_ON}, {TRAPS_EXPLICIT}").Or.Contain($"{SHADOW_STACKS_OFF}, traps hardware"), output);
            if (!OperatingSystem.IsWindows())
                Assert.That(output, Does.Contain($"{SHADOW_STACKS_OFF}, traps hardware"), "only Windows has shadow stacks");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// The line a Windows run states its case in: whether the trap host ran under hardware
    /// stack protection, so whether the proof of survival was made.
    /// </summary>
    /// <param name="shadowStacks">True when the trap host said that shadow stacks are enforced.</param>
    /// <returns>The line, without a line end.</returns>
    public static string ProofLine(bool shadowStacks)
    {
        return shadowStacks
            ? $"{PROOF_LINE}{SHADOW_STACKS_ON} (a host under hardware stack protection ran the guests' traps)"
            : $"{PROOF_LINE}{SHADOW_STACKS_OFF} (NOT proven on this machine: it does not enforce them)";
    }

    /// <summary>
    /// Writes the line where a release check finds it: the run's log, once, and the job's summary
    /// on the build server.
    /// </summary>
    private static void StateTheCase(string line)
    {
        // Progress goes to the log as it is written, whatever becomes of the test afterwards
        // (a skipped test's own output is not always shown).
        TestContext.Progress.WriteLine(line);

        var summary = Environment.GetEnvironmentVariable(SUMMARY_ENV);
        if (string.IsNullOrEmpty(summary))
            return;

        try
        {
            File.AppendAllText(summary, line + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The summary is a convenience; the log has the line.
        }
    }

    private static void AssertEveryTrapIsAVerdict((int ExitCode, string Output) run)
    {
        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.EqualTo(0), $"the host exited with 0x{run.ExitCode:X8}");
            Assert.That(run.Output, Does.Match(@"unreachable RuntimeError \d+ wasm trap: unreachable"));
            Assert.That(run.Output, Does.Match(@"out-of-bounds RuntimeError \d+ wasm trap: out of bounds memory access"));
            Assert.That(run.Output, Does.Match(@"division-by-zero RuntimeError \d+ wasm trap: integer division by zero"));
            Assert.That(run.Output, Does.Match(@"stack-overflow RuntimeError \d+ wasm trap: call stack exhausted"));
        });
    }

    /// <summary>The lines of the four guests: everything the host printed but its first line, which names the mode.</summary>
    private static string[] TrapLines(string output)
    {
        return output.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0 && !line.StartsWith("running", StringComparison.Ordinal)).ToArray();
    }

    private static (int ExitCode, string Output) RunHost(params string[] arguments)
    {
        var path = HostPath();
        var start = OperatingSystem.IsWindows() ? new ProcessStartInfo(path) : new ProcessStartInfo("dotnet");
        if (!OperatingSystem.IsWindows())
            start.ArgumentList.Add(Path.ChangeExtension(path, ".dll"));
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("the trap host did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TIMEOUT_MS))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("the trap host did not finish");
        }

        return (process.ExitCode, output.Result + error.Result);
    }

    private static string HostPath()
    {
        // bin/<configuration>/net10.0 of this project → the host project's same folder.
        var testDirectory = TestContext.CurrentContext.TestDirectory;
        var framework = Path.GetFileName(testDirectory);
        var configuration = Path.GetFileName(Path.GetDirectoryName(testDirectory)!);
        var aiRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", "..", "..", ".."));
        var file = OperatingSystem.IsWindows() ? $"{HOST}.exe" : $"{HOST}.dll";
        var path = Path.Combine(aiRoot, HOST, "bin", configuration!, framework, file);

        Assert.That(File.Exists(path), Is.True, $"the trap host is built with the tests: {path}");
        return path;
    }

    private static bool IsCetCompatible(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        var stream = reader.BaseStream;

        stream.Position = 0x3C;
        var peHeader = reader.ReadInt32();
        stream.Position = peHeader + 6;
        var sections = reader.ReadUInt16();
        stream.Position = peHeader + 20;
        var optionalSize = reader.ReadUInt16();
        var optional = peHeader + 24;
        stream.Position = optional;
        var dataDirectories = reader.ReadUInt16() == 0x20B ? optional + 112 : optional + 96;

        // The debug directory is data directory 6.
        stream.Position = dataDirectories + 6 * 8;
        var debugRva = reader.ReadUInt32();
        var debugSize = reader.ReadUInt32();
        var debugOffset = RvaToOffset(reader, optional + optionalSize, sections, debugRva);
        if (debugOffset < 0)
            return false;

        for (var entry = 0; entry < debugSize / 28; entry++)
        {
            stream.Position = debugOffset + entry * 28 + 12;
            var type = reader.ReadInt32();
            stream.Position = debugOffset + entry * 28 + 24;
            var dataOffset = reader.ReadUInt32();
            if (type != IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS)
                continue;

            stream.Position = dataOffset;
            return (reader.ReadInt32() & IMAGE_DLLCHARACTERISTICS_EX_CET_COMPAT) != 0;
        }

        return false;
    }

    private static long RvaToOffset(BinaryReader reader, long sectionTable, int sections, uint rva)
    {
        for (var section = 0; section < sections; section++)
        {
            reader.BaseStream.Position = sectionTable + section * 40 + 8;
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();
            if (rva >= virtualAddress && rva < virtualAddress + virtualSize)
                return rawPointer + (rva - virtualAddress);
        }

        return -1;
    }

    #endregion
}
