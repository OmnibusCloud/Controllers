using System.Reflection;
using System.Runtime.InteropServices;
using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The two engine settings the .NET binding does not expose, set through the wasmtime C API on
/// the binding's own configuration before the engine is made from it.
/// <list type="bullet">
/// <item>Traps without signals, where the host needs them. By default wasmtime lets a guest's trap
/// (an unreachable, an access outside the memory, a division by zero, a stack overflow) fault in
/// hardware and resumes the thread from its exception handler by rewriting the thread's context.
/// A Windows process with hardware-enforced stack protection (CET shadow stacks, on by default for
/// .NET 9+ executables) is killed for that (fast fail 0x30, set-context denied): one task that
/// trapped took the whole cloud client down with it. In such a process every trap becomes an
/// explicit check and the host's protection stays on. Elsewhere the hardware traps stay: explicit
/// checks cost a guest a fifth to a half of its speed. The verdicts, the outputs and the fuel are
/// the same either way (fuel counts wasm operators, not machine instructions).</item>
/// <item>The async stack size. The engine refuses a guest stack larger than its async stack,
/// whose default (2 MiB) the binding cannot change: setting it lets a guest have the stack a
/// program has on Linux (<see cref="WasmHostGuestThread.ENGINE_STACK_BYTES"/>).</item>
/// </list>
/// The binding is pinned (<see cref="WasmHostBinding.BUILT_AGAINST"/>), and these are the only calls
/// made past it; a binding that no longer has the native handle makes the host refuse to start, not
/// run unprotected.
/// </summary>
internal static class WasmHostEngineNative
{
    #region Constants

    private const string HANDLE_PROPERTY = "NativeHandle";
    private const string SIGNALS_BASED_TRAPS = "wasmtime_config_signals_based_traps_set";
    private const string ASYNC_STACK_SIZE = "wasmtime_config_async_stack_size_set";

    /// <summary>The async stack: the guest's share and a margin the engine keeps above it.</summary>
    public const ulong ASYNC_STACK_BYTES = (ulong)WasmHostGuestThread.ENGINE_STACK_BYTES + (1UL << 20);

    // PROCESS_MITIGATION_POLICY.ProcessUserShadowStackPolicy and its EnableUserShadowStack bit.
    private const int PROCESS_USER_SHADOW_STACK_POLICY = 15;
    private const uint ENABLE_USER_SHADOW_STACK = 0x1;

    #endregion

    #region Delegates

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetFlag(IntPtr config, [MarshalAs(UnmanagedType.U1)] bool value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetSize(IntPtr config, ulong size);

    #endregion

    #region Functions

    /// <summary>
    /// Sets the async stack on a configuration not yet used, and turns signal-based traps off
    /// when this process enforces shadow stacks.
    /// </summary>
    /// <param name="config">The configuration the engine will be made from.</param>
    /// <returns>True when traps are explicit checks in this process.</returns>
    /// <exception cref="InvalidOperationException">The binding has no native handle or the native library lacks the calls.</exception>
    public static bool Apply(Config config)
    {
        var handle = NativeHandleOf(config);
        var library = TheBindingsLibrary();

        Export<SetSize>(library, ASYNC_STACK_SIZE)(handle, ASYNC_STACK_BYTES);

        var explicitTraps = HostEnforcesShadowStacks();
        if (explicitTraps)
            Export<SetFlag>(library, SIGNALS_BASED_TRAPS)(handle, false);

        return explicitTraps;
    }

    /// <summary>
    /// Whether this process runs with hardware-enforced stack protection. Only Windows has it; a
    /// Windows that cannot say is taken to have it (an explicit trap is slower, a denied one kills
    /// the process).
    /// </summary>
    /// <returns>True when a hardware trap could not be resumed from in this process.</returns>
    public static bool HostEnforcesShadowStacks()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            return !GetProcessMitigationPolicy(GetCurrentProcess(), PROCESS_USER_SHADOW_STACK_POLICY, out var flags, sizeof(uint))
                   || (flags & ENABLE_USER_SHADOW_STACK) != 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    private static IntPtr TheBindingsLibrary()
    {
        // The library the binding's own calls loaded: through WasmHostNativeLibrary's resolver in
        // a cloud client's shared load context, through the module's dependency file in an isolated
        // plugin context. A load by name runs neither the resolver nor a plugin context's lookup
        // (it found nothing on a Linux client), so load by the path the resolver used or would
        // use; the operating system hands back the library already loaded.
        var path = WasmHostNativeLibrary.ResolvedPath ?? WasmHostNativeLibrary.Locate();
        return path != null
            ? NativeLibrary.Load(path)
            : NativeLibrary.Load(WasmHostNativeLibrary.LIBRARY_NAME, typeof(Wasmtime.Engine).Assembly, null);
    }

    private static IntPtr NativeHandleOf(Config config)
    {
        var property = typeof(Config).GetProperty(HANDLE_PROPERTY, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (property?.GetValue(config) is not SafeHandle handle || handle.IsInvalid)
            throw new InvalidOperationException($"Wasmtime.Config has no usable {HANDLE_PROPERTY}: the binding changed; the sandbox cannot configure its engine.");

        return handle.DangerousGetHandle();
    }

    private static T Export<T>(IntPtr library, string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(library, name, out var address))
            throw new InvalidOperationException($"the wasmtime library exports no {name}");

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out uint buffer, nuint length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    #endregion
}
