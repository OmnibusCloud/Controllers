using System.Runtime.InteropServices;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Live.Local;

/// <summary>
/// What a local run says of the machine it ran on: the operating system and its version, the
/// platform and the architecture, the logical cores and the memory, as the runtime sees them;
/// never a name of the machine, a user or a path. The memory is read the way the node's degree
/// rule reads it (<see cref="SmtSandboxDegree.AvailableBytes()"/>), right after a collection, so
/// the view is fresh.
/// </summary>
/// <param name="Os">windows, macos, linux.</param>
/// <param name="OsVersion">The operating system's version as the runtime reports it.</param>
/// <param name="Platform">The portable runtime identifier: win-x64, linux-x64, osx-arm64.</param>
/// <param name="Architecture">The machine's architecture as the runtime reports it: x64, arm64.</param>
/// <param name="Cores">The logical processors.</param>
/// <param name="MemoryBytes">The memory the process may use in all; 0 when the runtime cannot say.</param>
/// <param name="AvailableBytes">The memory available on the machine when it was read; 0 when the runtime cannot say.</param>
internal sealed record SmtLiveMachine(string Os, string OsVersion, string Platform, string Architecture, int Cores, long MemoryBytes, long AvailableBytes)
{
    #region Functions

    /// <summary>This machine now.</summary>
    /// <returns>The description.</returns>
    public static SmtLiveMachine Current()
    {
        GC.Collect();
        var info = GC.GetGCMemoryInfo();

        var os = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : OperatingSystem.IsLinux() ? "linux"
            : RuntimeInformation.OSDescription.Split(' ', 2)[0].ToLowerInvariant();

        return new SmtLiveMachine(os, Environment.OSVersion.Version.ToString(), WasmHostNativeLibrary.PortableRid,
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), Environment.ProcessorCount,
            Math.Max(0, info.TotalAvailableMemoryBytes), SmtSandboxDegree.AvailableBytes(info.TotalAvailableMemoryBytes, info.MemoryLoadBytes));
    }

    #endregion
}
