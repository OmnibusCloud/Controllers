using System.Runtime.InteropServices;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

// Runs one guest of each trap kind in the sandbox and prints "<kind> <verdict>" per line. A host
// whose process a trap kills prints nothing past "running" and exits with the fault's code.

var wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason)
           ?? throw new InvalidOperationException(reason);

using var sandbox = new VerifyWasmSandbox();
Console.WriteLine($"running, shadow stacks {(ShadowStacks.Enforced() ? "on" : "off")}, traps {(sandbox.ExplicitTraps ? "explicit" : "hardware")}");
foreach (var (kind, body) in TrapModules.BODIES)
{
    var task = new VerifyTaskData
    {
        RuntimeId = VerifyRuntimeCatalog.WASI,
        EntryPoint = $"{kind}.wasm",
        Sources = [new VerifySourceFileData { Name = $"{kind}.wasm", Content = Convert.ToBase64String(TrapModules.Build(body)) }]
    };
    var result = sandbox.Execute(wasi, task, VerifySandboxDefaults.Resolve(null, null));
    Console.WriteLine($"{kind} {result.Verdict} {result.Stderr.Split('\n')[0]}");
}

return 0;

/// <summary>Hand-assembled modules: one exported memory and a <c>_start</c> whose body traps.</summary>
internal static class TrapModules
{
    /// <summary>Each trap kind's function body (locals, then the code, then <c>end</c>).</summary>
    public static readonly (string Kind, byte[] Body)[] BODIES =
    [
        ("unreachable", [0x00, 0x00, 0x0B]),
        ("out-of-bounds", [0x00, 0x41, 0xF0, 0xFF, 0xFF, 0xFF, 0x07, 0x28, 0x02, 0x00, 0x1A, 0x0B]),
        ("division-by-zero", [0x00, 0x41, 0x01, 0x41, 0x00, 0x6D, 0x1A, 0x0B]),
        ("stack-overflow", [0x00, 0x10, 0x00, 0x0B])
    ];

    private static readonly byte[] HEAD =
    [
        0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
        0x01, 0x04, 0x01, 0x60, 0x00, 0x00,
        0x03, 0x02, 0x01, 0x00,
        0x05, 0x03, 0x01, 0x00, 0x01,
        0x07, 0x13, 0x02, 0x06, 0x6D, 0x65, 0x6D, 0x6F, 0x72, 0x79, 0x02, 0x00, 0x06, 0x5F, 0x73, 0x74, 0x61, 0x72, 0x74, 0x00, 0x00
    ];

    /// <summary>The module around one function body.</summary>
    /// <param name="body">The body: locals, code, end.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Build(byte[] body)
    {
        return [.. HEAD, 0x0A, (byte)(body.Length + 2), 0x01, (byte)body.Length, .. body];
    }
}

/// <summary>Whether Windows enforces user-mode shadow stacks in this process.</summary>
internal static class ShadowStacks
{
    /// <summary>Asks the process mitigation policy (ProcessUserShadowStackPolicy).</summary>
    /// <returns>True when the EnableUserShadowStack bit is set.</returns>
    public static bool Enforced()
    {
        return OperatingSystem.IsWindows() && GetProcessMitigationPolicy(GetCurrentProcess(), 15, out var flags, sizeof(uint)) && (flags & 0x1) != 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out uint buffer, nuint length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
