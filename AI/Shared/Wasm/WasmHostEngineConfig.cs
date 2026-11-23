using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The configuration of the engine every store of a host shares. NaN canonicalization pins float
/// bit patterns across platforms; fuel and epoch give the CPU and wall-clock envelopes. Beyond
/// that the engine runs what a guest built for wasm32-wasi needs and nothing more: one linear
/// memory with 32-bit addresses (the store's cap is per memory, and 64-bit tables are what the
/// table-size panic GHSA-p8xm-42r7-89xg of wasmtime 44.0.0 needed), no shared memory, no GC heap
/// and no typed function references (a value type is one byte in every module the engine takes),
/// no exceptions (on by default since wasmtime 47, the subject of four advisories since), no
/// components; relaxed SIMD in its deterministic mode, so a module that uses it computes the same
/// bits on every CPU. A guest may use 8 MiB of the native call stack, as a program does on Linux;
/// every run is on a thread that holds it (<see cref="WasmHostGuestThread"/>). Traps are explicit
/// checks, not hardware faults, where the host process enforces hardware stack protection, so such
/// a process survives a guest's trap (<see cref="WasmHostEngineNative"/>). An engine for modules a
/// task brought compiles on the calling thread alone: the engine's own compile threads are neither
/// the caller's workers nor counted by anything the caller bounds.
/// <para>
/// The configuration is the binding's first call into the native library, the call that loads it:
/// the natives guard (<see cref="WasmHostNatives"/>) asks right after it, before anything else of
/// the library is used.
/// </para>
/// <para>
/// One setting can be asked for, and is off unless it is: WebAssembly exceptions (the standard
/// proposal: tags, <c>throw</c>, <c>try_table</c>). It is for a module the controller itself ships
/// and pins by hash (a C++ program that throws), never for a module a task brought. The four
/// advisories were about exceptions and the machinery under them: fuel that a callee had used
/// given back at a catch (GHSA-m63x-6p34-q65x), the heap of the exception objects corrupted
/// (GHSA-cfhf-m2cr-62wj, GHSA-hw8m-q44c-ggrf), a module's tags misaligning the host's own records
/// (GHSA-p79w-pv2r-29qp). The pinned native library is past all four; a task's module has no use
/// for exceptions, and what is off cannot be reached. The GC proposal and the typed function
/// references stay off in such an engine as in every other. Its exception objects live in a heap
/// the store manages beside the linear memory: host memory, which the store's cap of a memory
/// bounds as it bounds the linear memory, each on its own. A guest that keeps its exceptions is
/// refused more at the cap, and that refusal is an error of the engine
/// (<see cref="WasmHostVerdict.EngineError"/>), neither a trap nor an exit.
/// </para>
/// </summary>
internal static class WasmHostEngineConfig
{
    #region Functions

    /// <summary>
    /// Makes the deterministic configuration, with the engine settings the binding does not expose
    /// already applied through the C API.
    /// </summary>
    /// <param name="parallelCompilation">Whether a compile may use threads of the engine's own besides the caller's.</param>
    /// <param name="forceExplicitTraps">True to make traps explicit checks whatever the process; false leaves it to the process (a process with shadow stacks always has them).</param>
    /// <param name="explicitTraps">True when traps are explicit checks in the engine made from the configuration.</param>
    /// <returns>A configuration not yet used, to make one engine from.</returns>
    /// <exception cref="InvalidOperationException">No native library was found, the one loaded is not the pinned one, or the binding has no native handle or the library lacks the calls.</exception>
    public static Config Create(bool parallelCompilation, bool forceExplicitTraps, out bool explicitTraps)
    {
        return Create(parallelCompilation, forceExplicitTraps, exceptions: false, out explicitTraps);
    }

    /// <summary>
    /// Makes the deterministic configuration and says whether the engine takes WebAssembly
    /// exceptions: the one setting an engine made here can differ in from every other.
    /// </summary>
    /// <param name="parallelCompilation">Whether a compile may use threads of the engine's own besides the caller's.</param>
    /// <param name="forceExplicitTraps">True to make traps explicit checks whatever the process; false leaves it to the process (a process with shadow stacks always has them).</param>
    /// <param name="exceptions">True for an engine that takes the exceptions proposal: for a module the controller ships and pins by hash, never for one a task brought.</param>
    /// <param name="explicitTraps">True when traps are explicit checks in the engine made from the configuration.</param>
    /// <returns>A configuration not yet used, to make one engine from.</returns>
    /// <exception cref="InvalidOperationException">No native library was found, the one loaded is not the pinned one, or the binding has no native handle or the library lacks the calls.</exception>
    public static Config Create(bool parallelCompilation, bool forceExplicitTraps, bool exceptions, out bool explicitTraps)
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
                .WithFunctionReferences(false)
                .WithExceptions(exceptions)
                .WithComponentModel(false)
                .WithRelaxedSIMD(true, true)
                .WithParallelCompilation(parallelCompilation);

            explicitTraps = WasmHostEngineNative.Apply(config, forceExplicitTraps);
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
