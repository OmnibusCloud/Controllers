using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// A compiled task-supplied module on loan from <see cref="VerifyTaskModuleCache"/>: the
/// module stays alive while the lease is held; disposing the lease lets the cache evict it.
/// </summary>
internal sealed class VerifyTaskModuleLease : IDisposable
{
    #region Fields

    private readonly VerifyTaskModuleCache m_cache;
    private readonly string m_key;
    private bool m_disposed;

    #endregion

    #region Constructors

    internal VerifyTaskModuleLease(VerifyTaskModuleCache cache, string key, WasmModule module)
    {
        m_cache = cache;
        m_key = key;
        Module = module;
    }

    #endregion

    #region IDisposable

    /// <summary>Returns the module to the cache.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_cache.Release(m_key);
    }

    #endregion

    #region Properties

    /// <summary>The compiled module, valid while the lease is held.</summary>
    public WasmModule Module { get; }

    #endregion
}
