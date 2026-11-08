using System.Security.Cryptography;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Compiled modules of task-supplied programs (the raw WASI runtime), keyed by content
/// hash and bounded: a batch usually carries a few distinct modules many times over (one
/// candidate, many cases), so the compile is paid once per module. A module is handed out
/// as a lease; when the cache is full it evicts the least recently used module that nobody
/// holds, never one in flight — the load suite caught a wholesale clear disposing a module
/// another worker was instantiating. With every slot leased the cache grows past its cap
/// until leases return.
/// </summary>
internal sealed class VerifyTaskModuleCache : IDisposable
{
    #region Constants

    /// <summary>Compiled task-supplied modules kept at once (idle ones are evicted past this).</summary>
    public const int DEFAULT_CAPACITY = 64;

    #endregion

    #region Fields

    private readonly WasmEngine m_engine;
    private readonly int m_capacity;
    private readonly object m_lock = new();
    private readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);
    private long m_clock;

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
    /// Leases the compiled module for the bytes, compiling on a miss. The lease keeps the
    /// module alive until disposed.
    /// </summary>
    /// <param name="name">The module name wasmtime reports in traps (the entry file name).</param>
    /// <param name="bytes">The module bytes.</param>
    /// <returns>The lease; dispose it when the run is over.</returns>
    /// <exception cref="WasmtimeException">The bytes are not a valid module.</exception>
    public VerifyTaskModuleLease Lease(string name, byte[] bytes)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(bytes));

        lock (m_lock)
        {
            if (m_entries.TryGetValue(key, out var hit))
                return Take(key, hit);
        }

        // Compile outside the lock: a compile takes milliseconds to seconds and the other
        // workers' lookups should not wait on it.
        var compiled = WasmModule.FromBytes(m_engine, name, bytes);

        lock (m_lock)
        {
            if (m_entries.TryGetValue(key, out var raced))
            {
                compiled.Dispose();
                return Take(key, raced);
            }

            EvictIdle();
            var entry = new Entry(compiled);
            m_entries[key] = entry;
            return Take(key, entry);
        }
    }

    /// <summary>Returns a lease; called by <see cref="VerifyTaskModuleLease.Dispose"/>.</summary>
    internal void Release(string key)
    {
        lock (m_lock)
        {
            if (m_entries.TryGetValue(key, out var entry))
                entry.Leases--;
        }
    }

    private VerifyTaskModuleLease Take(string key, Entry entry)
    {
        entry.Leases++;
        entry.LastUse = ++m_clock;
        return new VerifyTaskModuleLease(this, key, entry.Module);
    }

    private void EvictIdle()
    {
        while (m_entries.Count >= m_capacity)
        {
            string? victim = null;
            var oldest = long.MaxValue;
            foreach (var (key, entry) in m_entries)
            {
                if (entry.Leases == 0 && entry.LastUse < oldest)
                {
                    oldest = entry.LastUse;
                    victim = key;
                }
            }

            if (victim == null)
                return; // every module is in flight; grow until leases return

            m_entries[victim].Module.Dispose();
            m_entries.Remove(victim);
        }
    }

    #endregion

    #region IDisposable

    /// <summary>Releases every compiled module the cache holds; no lease may be outstanding.</summary>
    public void Dispose()
    {
        lock (m_lock)
        {
            foreach (var entry in m_entries.Values)
                entry.Module.Dispose();

            m_entries.Clear();
        }
    }

    #endregion

    #region Properties

    /// <summary>Compiled modules held right now.</summary>
    public int Count
    {
        get
        {
            lock (m_lock)
            {
                return m_entries.Count;
            }
        }
    }

    #endregion

    #region Nested Types

    private sealed class Entry(WasmModule module)
    {
        public WasmModule Module { get; } = module;

        public int Leases { get; set; }

        public long LastUse { get; set; }
    }

    #endregion
}
