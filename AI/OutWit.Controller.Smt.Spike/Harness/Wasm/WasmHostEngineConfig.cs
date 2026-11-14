using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The SM-0 spike's copy of the shared host's engine configuration (AI/Shared/Wasm), identical
/// but for one flag: the WebAssembly exception-handling proposal is on, because Z3 is C++ that
/// throws (parse errors, the resource limit, cancellation) and wasi-sdk compiles its exceptions to
/// wasm exceptions. Everything else is the shared host's deterministic configuration: fuel and
/// epoch, NaN canonicalization, one 32-bit memory, no threads, no GC, no components,
/// deterministic relaxed SIMD, the 8 MiB guest stack, explicit traps under CET shadow stacks.
/// <para>
/// For a production controller this would be an engine option of the shared host (a host built
/// with or without exceptions), not a second copy of the file.
/// </para>
/// </summary>
internal static class WasmHostEngineConfig
{
    #region Functions

    /// <summary>
    /// Makes the deterministic configuration with wasm exceptions on, with the engine settings
    /// the binding does not expose already applied through the C API.
    /// </summary>
    /// <param name="explicitTraps">True when traps are explicit checks in this process.</param>
    /// <returns>A configuration not yet used, to make one engine from.</returns>
    /// <exception cref="InvalidOperationException">The binding has no native handle or the native library lacks the calls.</exception>
    public static Config Create(out bool explicitTraps)
    {
        var config = new Config()
            .WithMaximumStackSize(WasmHostGuestThread.ENGINE_STACK_BYTES)
            .WithFuelConsumption(true)
            .WithEpochInterruption(true)
            .WithCraneliftNaNCanonicalization(true)
            .WithMemory64(false)
            .WithMultiMemory(false)
            .WithWasmThreads(false)
            .WithGc(false)
            .WithExceptions(true)
            .WithComponentModel(false)
            .WithRelaxedSIMD(true, true);

        explicitTraps = WasmHostEngineNative.Apply(config);
        return config;
    }

    #endregion
}
