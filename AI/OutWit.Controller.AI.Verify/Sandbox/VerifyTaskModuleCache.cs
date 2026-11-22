using System.Security.Cryptography;
using OutWit.Controller.AI.Wasm;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Compiled modules of task-supplied programs (the raw WASI runtime) and of programs built from a
/// task's sources, keyed by content hash under the engine's compiled-code key
/// (<see cref="WasmHost.CompiledCodeKey"/>: the native library's SHA-256 and the trap mode) and
/// bounded twice: by their number and by the bytes of compiled code they hold
/// (<see cref="WasmHostModuleImage"/>). A module of a few kilobytes can compile to hundreds of
/// megabytes, so a count alone bounds nothing. The cache lives in memory with its engine and
/// persists nothing, so a module compiled by other natives or for another trap mode cannot reach
/// it; the key says so for any copy of it that would. A batch usually carries a few distinct
/// modules many times over (one candidate, many cases), so the compile is paid once per module.
/// A module is handed out as a lease; when the cache is full it evicts the least recently used
/// module that nobody holds, never one in flight (the load suite caught a wholesale clear
/// disposing a module another worker was instantiating). With every module leased the cache is
/// over its bounds until leases return, and comes back under them as they do; a module larger
/// than the byte bound by itself is not kept once nobody holds it.
/// </summary>
internal sealed class VerifyTaskModuleCache : IDisposable
{
    #region Constants

    /// <summary>Compiled task-supplied modules kept at once (idle ones are evicted past this).</summary>
    public const int DEFAULT_CAPACITY = 64;

    /// <summary>The bytes of compiled code the idle modules may hold together (they are evicted past this).</summary>
    public const long DEFAULT_BYTE_CAPACITY = 256L << 20;

    #endregion

    #region Fields

    private readonly WasmEngine m_engine;
    private readonly string m_codeKey;
    private readonly int m_capacity;
    private readonly long m_byteCapacity;
    private readonly Func<WasmModule, long, long> m_sizeOf;
    private readonly object m_lock = new();
    private readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> m_compiling = new(StringComparer.Ordinal);
    private long m_clock;
    private long m_bytes;

    #endregion

    #region Constructors

    /// <summary>
    /// A cache over an engine.
    /// </summary>
    /// <param name="engine">The engine modules are compiled on.</param>
    /// <param name="codeKey">What the engine's compiled code is valid for (<see cref="WasmHost.CompiledCodeKey"/>).</param>
    /// <param name="capacity">The most modules kept.</param>
    /// <param name="byteCapacity">The most bytes of compiled code kept.</param>
    /// <param name="sizeOf">The bytes a compiled module holds, from the module and the size it was compiled from; null for the engine's own answer.</param>
    public VerifyTaskModuleCache(WasmEngine engine, string codeKey, int capacity = DEFAULT_CAPACITY, long byteCapacity = DEFAULT_BYTE_CAPACITY,
        Func<WasmModule, long, long>? sizeOf = null)
    {
        m_engine = engine;
        m_codeKey = codeKey;
        m_capacity = Math.Max(1, capacity);
        m_byteCapacity = Math.Max(1, byteCapacity);
        m_sizeOf = sizeOf ?? WasmHostModuleImage.Bytes;
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
        var key = KeyOf(m_codeKey, bytes);

        object turn;
        lock (m_lock)
        {
            if (m_entries.TryGetValue(key, out var hit))
                return Take(key, hit);

            if (!m_compiling.TryGetValue(key, out turn!))
                m_compiling[key] = turn = new object();
        }

        // One worker compiles a module; the others that want the same one wait for it instead of
        // compiling it again beside it. Outside the cache's lock: a compile takes milliseconds
        // to seconds, and lookups of other modules should not wait on it.
        lock (turn)
        {
            lock (m_lock)
            {
                if (m_entries.TryGetValue(key, out var compiledMeanwhile))
                    return Take(key, compiledMeanwhile);
            }

            try
            {
                var compiled = WasmModule.FromBytes(m_engine, name, bytes);
                var size = Math.Max(0, m_sizeOf(compiled, bytes.Length));

                lock (m_lock)
                {
                    // Room for the newcomer first: its lease is taken before anything could evict it.
                    Evict(size, 1);
                    var entry = new Entry(compiled, size);
                    m_entries[key] = entry;
                    m_bytes += size;
                    return Take(key, entry);
                }
            }
            finally
            {
                lock (m_lock)
                {
                    m_compiling.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// The key of a module's compiled code: the engine's compiled-code key and the module's SHA-256.
    /// </summary>
    /// <param name="codeKey">The engine's compiled-code key.</param>
    /// <param name="bytes">The module bytes.</param>
    /// <returns>The key.</returns>
    public static string KeyOf(string codeKey, byte[] bytes)
    {
        return $"{codeKey}/{Convert.ToHexStringLower(SHA256.HashData(bytes))}";
    }

    /// <summary>Returns a lease; called by <see cref="VerifyTaskModuleLease.Dispose"/>.</summary>
    internal void Release(string key)
    {
        lock (m_lock)
        {
            if (!m_entries.TryGetValue(key, out var entry))
                return;

            entry.Leases--;

            // What grew past the bounds while everything was in flight goes as the leases return.
            Evict(0, 0);
        }
    }

    private VerifyTaskModuleLease Take(string key, Entry entry)
    {
        entry.Leases++;
        entry.LastUse = ++m_clock;
        return new VerifyTaskModuleLease(this, key, entry.Module);
    }

    /// <summary>
    /// Evicts idle modules, the least recently used first, until there is room for a number of
    /// modules and of bytes more within the bounds, or nothing idle is left.
    /// </summary>
    private void Evict(long incomingBytes, int incoming)
    {
        while (m_entries.Count + incoming > m_capacity || m_bytes + incomingBytes > m_byteCapacity)
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
                return; // every module is in flight; over the bounds until leases return

            m_bytes -= m_entries[victim].Bytes;
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
            m_bytes = 0;
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

    /// <summary>The bytes of compiled code the modules held right now take.</summary>
    public long Bytes
    {
        get
        {
            lock (m_lock)
            {
                return m_bytes;
            }
        }
    }

    #endregion

    #region Nested Types

    private sealed class Entry(WasmModule module, long bytes)
    {
        public WasmModule Module { get; } = module;

        public long Bytes { get; } = bytes;

        public int Leases { get; set; }

        public long LastUse { get; set; }
    }

    #endregion
}
