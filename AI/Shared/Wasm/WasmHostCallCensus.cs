namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The calls of one run into the host, counted by name: how many, and how many bytes they were
/// charged for; and, when asked, every call in its order with the fuel the run had left when it
/// was made. Two machines that answer a guest differently part at one call: the fuel left before
/// the next one is no longer the same. A tool for finding that call; no run needs it.
/// </summary>
internal sealed class WasmHostCallCensus
{
    #region Fields

    private readonly Dictionary<string, long> m_calls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> m_bytes = new(StringComparer.Ordinal);
    private readonly List<(string Name, ulong FuelLeft, long Bytes)>? m_trace;

    #endregion

    #region Constructors

    /// <summary>
    /// A count of calls.
    /// </summary>
    /// <param name="trace">Whether every call is kept in its order as well.</param>
    public WasmHostCallCensus(bool trace = false)
    {
        m_trace = trace ? [] : null;
    }

    #endregion

    #region Functions

    /// <summary>Counts one call.</summary>
    /// <param name="name">The call's name.</param>
    /// <param name="fuelLeft">The fuel the run had left when it made the call.</param>
    public void Call(string name, ulong fuelLeft)
    {
        m_calls[name] = m_calls.GetValueOrDefault(name) + 1;
        m_trace?.Add((name, fuelLeft, 0));
    }

    /// <summary>Counts the bytes a call was charged for.</summary>
    /// <param name="name">The call's name.</param>
    /// <param name="bytes">The bytes.</param>
    public void Bytes(string name, long bytes)
    {
        m_bytes[name] = m_bytes.GetValueOrDefault(name) + bytes;

        if (m_trace is { Count: > 0 } trace && trace[^1].Name == name)
            trace[^1] = (name, trace[^1].FuelLeft, trace[^1].Bytes + bytes);
    }

    /// <summary>
    /// Every call in its order, when the census keeps them: the name, the fuel the run had used
    /// by then of a budget, and the bytes the call was charged for.
    /// </summary>
    /// <param name="budget">The run's fuel budget.</param>
    /// <returns>Lines of the form <c>name used bytes</c>; none when the census keeps no order.</returns>
    public IReadOnlyList<string> Trace(long budget)
    {
        return m_trace == null ? [] : m_trace.Select(call => $"{call.Name} {(ulong)budget - call.FuelLeft} {call.Bytes}").ToList();
    }

    /// <summary>The count as lines, one a name, in the order of the names.</summary>
    /// <returns>Lines of the form <c>name calls bytes</c>.</returns>
    public IReadOnlyList<string> Lines()
    {
        return m_calls.Keys.Union(m_bytes.Keys)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => $"{name} {m_calls.GetValueOrDefault(name)} {m_bytes.GetValueOrDefault(name)}")
            .ToList();
    }

    #endregion

    #region Properties

    /// <summary>All the calls counted.</summary>
    public long Calls => m_calls.Values.Sum();

    /// <summary>All the bytes counted.</summary>
    public long Charged => m_bytes.Values.Sum();

    #endregion
}
