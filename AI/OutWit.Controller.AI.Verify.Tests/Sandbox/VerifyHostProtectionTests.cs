using System.Diagnostics;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// A guest's trap must not take its host down. On Windows a .NET 9+ executable runs with
/// hardware-enforced stack protection (CET shadow stacks) by default, and a trap the engine
/// resumed from by rewriting the thread's context killed such a process (fast fail 0x30): the
/// cloud client's worker died with every task in it. The test host of these tests is not such a
/// process, so the check runs a separate executable built the way a host is
/// (OutWit.Controller.AI.Verify.Tests.TrapHost) and reads its output.
/// </summary>
[TestFixture]
public sealed class VerifyHostProtectionTests
{
    #region Constants

    private const string HOST = "OutWit.Controller.AI.Verify.Tests.TrapHost";
    private const int TIMEOUT_MS = 180_000;

    private const int IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS = 20;
    private const int IMAGE_DLLCHARACTERISTICS_EX_CET_COMPAT = 0x1;

    #endregion

    #region Tests

    [Test]
    public void AGuestTrapLeavesTheHostRunningTest()
    {
        var (exitCode, output) = RunHost();
        TestContext.Out.WriteLine(output);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0), $"the host exited with 0x{exitCode:X8}");
            Assert.That(output, Does.Contain("unreachable RuntimeError wasm trap: unreachable"));
            Assert.That(output, Does.Contain("out-of-bounds RuntimeError wasm trap: out of bounds memory access"));
            Assert.That(output, Does.Contain("division-by-zero RuntimeError wasm trap: integer division by zero"));
            Assert.That(output, Does.Contain("stack-overflow RuntimeError wasm trap: call stack exhausted"));
        });
    }

    [Test]
    public void TheHostIsBuiltWithStackProtectionTest()
    {
        // Without the flag the test above would prove nothing on Windows.
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
            Assert.That(output, Does.Contain("shadow stacks on, traps explicit").Or.Contain("shadow stacks off, traps hardware"), output);
            if (!OperatingSystem.IsWindows())
                Assert.That(output, Does.Contain("shadow stacks off, traps hardware"), "only Windows has shadow stacks");
        });
    }

    #endregion

    #region Tools

    private static (int ExitCode, string Output) RunHost()
    {
        var path = HostPath();
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(path)
            : new ProcessStartInfo("dotnet", $"\"{Path.ChangeExtension(path, ".dll")}\"");
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
