using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The configuration of the engine every store of a host shares. NaN canonicalization pins float
/// bit patterns across platforms; fuel and epoch give the CPU and wall-clock envelopes. Beyond
/// that the engine runs what a guest built for wasm32-wasi needs and nothing more: one linear
/// memory with 32-bit addresses (the store's cap is per memory, and 64-bit tables are what the
/// table-size panic GHSA-p8xm-42r7-89xg of wasmtime 44.0.0 needed), no shared memory, no GC heap,
/// no exceptions (on by default since wasmtime 47, the subject of four advisories since), no
/// components; relaxed SIMD in its deterministic mode, so a module that uses it computes the same
/// bits on every CPU. A guest may use 8 MiB of the native call stack, as a program does on Linux;
/// every run is on a thread that holds it (<see cref="WasmHostGuestThread"/>). Traps are explicit
/// checks, not hardware faults, where the host process enforces hardware stack protection, so such
/// a process survives a guest's trap (<see cref="WasmHostEngineNative"/>).
/// <para>
/// The configuration is the binding's first call into the native library, the call that loads it:
/// the natives guard (<see cref="WasmHostNatives"/>) asks right after it, before anything else of
/// the library is used.
/// </para>
/// </summary>
internal static class WasmHostEngineConfig
{
    #region Functions

    /// <summary>
    /// Makes the deterministic configuration, with the engine settings the binding does not expose
    /// already applied through the C API.
    /// </summary>
    /// <param name="explicitTraps">True when traps are explicit checks in this process.</param>
    /// <returns>A configuration not yet used, to make one engine from.</returns>
    /// <exception cref="InvalidOperationException">No native library was found, the one loaded is not the pinned one, or the binding has no native handle or the library lacks the calls.</exception>
    public static Config Create(out bool explicitTraps)
    {
        var config = NewConfig();
        try
        {
            WasmHostNatives.ThrowIfUnavailable();

            config
                .WithMaximumStackSize(WasmHostGuestThread.ENGINE_STACK_BYTES)
                .WithFuelConsumption(true)
                .WithEpochInterruption(true)
                .WithCraneliftNaNCanonicalization(true)
                .WithMemory64(false)
                .WithMultiMemory(false)
                .WithWasmThreads(false)
                .WithGc(false)
                .WithExceptions(false)
                .WithComponentModel(false)
                .WithRelaxedSIMD(true, true);

            explicitTraps = WasmHostEngineNative.Apply(config);
            return config;
        }
        catch
        {
            config.Dispose();
            throw;
        }
    }

    private static Config NewConfig()
    {
        try
        {
            return new Config();
        }
        catch (DllNotFoundException exception)
        {
            // Every resolver and handler of the process refused or found nothing: this controller's
            // own library is missing or another one, and no other controller's copy had the pinned one.
            throw new InvalidOperationException(WasmHostNatives.NotLoaded(exception), exception);
        }
    }

    #endregion
}
