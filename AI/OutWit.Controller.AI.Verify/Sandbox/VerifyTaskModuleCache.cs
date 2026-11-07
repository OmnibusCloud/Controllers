using System.Security.Cryptography;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Compiled modules of task-supplied programs (the raw WASI runtime), keyed by content
/// hash and bounded: a batch usually carries a few distinct modules many times over (one
/// candidate, many cases), so the compile is paid once per module, and the cache is
/// cleared wholesale when it grows past its cap — a cache, not a registry.
/// </summary>
internal sealed class VerifyTaskModuleCache : IDisposable
{
    #region Constants

    public const int DEFAULT_CAPACITY = 64;

    #endregion

    #region Fields

    private readonly WasmEngine m_engine;
    private readonly int m_capacity;
    private readonly object m_lock = new();
    private readonly Dictionary<string, WasmModule> m_modules = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    public VerifyTaskModuleCache(WasmEngine engine, int capacity = DEFAULT_CAPACITY)
    {
        m_engine = engine;
        m_capacity = Math.Max(1, capacity);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Returns the compiled module for the bytes, compiling on a miss.
    /// </summary>
    /// <param name="name">The module name wasmtime reports in traps (the entry file name).</param>
    /// <param name="bytes">The module bytes.</param>
    /// <returns>The compiled module, owned by the cache.</returns>
    /// <exception cref="WasmtimeException">The bytes are not a valid module.</exception>
    public WasmModule GetOrCompile(string name, byte[] bytes)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(bytes));

        lock (m_lock)
        {
            if (m_modules.TryGetValue(key, out var cached))
                return cached;

            var module = WasmModule.FromBytes(m_engine, name, bytes);

            if (m_modules.Count >= m_capacity)
                Clear();

            m_modules[key] = module;
            return module;
        }
    }

    private void Clear()
    {
        foreach (var module in m_modules.Values)
            module.Dispose();

        m_modules.Clear();
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        lock (m_lock)
        {
            Clear();
        }
    }

    #endregion

    #region Properties

    public int Count
    {
        get
        {
            lock (m_lock)
            {
                return m_modules.Count;
            }
        }
    }

    #endregion
}
