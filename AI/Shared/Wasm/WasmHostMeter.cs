using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// What one run pays for its calls into the host, when the run asks for that
/// (<see cref="WasmHostRunRequest.ChargeHostCalls"/>): every call a fixed amount
/// (<see cref="CALL_FUEL"/>), and a unit a byte the call moves, as the engine charges a guest's
/// own bulk copies. The fuel is the run's: a call the budget does not hold ends the run as out of
/// fuel, like an instruction it does not hold. Without it a program could keep the host busy for
/// almost nothing: fuel counts the guest's instructions, and a call is one instruction however
/// long the host works for it.
/// <para>
/// The meter also holds how one of the host's calls ended the run, so that the end is known by
/// what the host did and never by the words of an error.
/// </para>
/// </summary>
internal sealed class WasmHostMeter
{
    #region Constants

    /// <summary>
    /// What any call into the host costs, in fuel, before the bytes it moves. A thousand units
    /// are half a microsecond at the rate a busy loop burns fuel on the slowest node: about what
    /// the host's own part of most calls takes (the way in and out, the look at the guest's
    /// memory, the way to WASI's own call and back), measured at 0.1 to 0.6 microseconds on a
    /// desktop of 2025. Not of every call: three file calls take far longer on Windows
    /// (<c>path_filestat_get</c>, <c>path_open</c> and <c>fd_filestat_get</c>, 45 to 190
    /// microseconds each where measured), so a program that loops on them burns some 7 million
    /// units a second there, and the run's wall-clock backstop ends it before its fuel does: an
    /// end of the node's clock, which gives no verdict, never a wrong one.
    /// </summary>
    public const long CALL_FUEL = 1000;

    #endregion

    #region Fields

    private readonly bool m_charges;
    private readonly WasmHostCallCensus? m_census;

    #endregion

    #region Constructors

    /// <summary>
    /// The meter of one run.
    /// </summary>
    /// <param name="charges">Whether the run's calls into the host cost fuel.</param>
    /// <param name="census">Where every call is counted by its name, or null.</param>
    public WasmHostMeter(bool charges, WasmHostCallCensus? census = null)
    {
        m_charges = charges;
        m_census = census;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Takes the price of one call from the run's fuel, and ends the run when it does not hold it.
    /// </summary>
    /// <param name="caller">The guest's call.</param>
    /// <param name="name">The call's name.</param>
    /// <exception cref="WasmHostRunEnded">The run is out of fuel.</exception>
    public void Call(Caller caller, string name)
    {
        m_census?.Call(name, caller.Fuel);
        Take(caller, CALL_FUEL);
    }

    /// <summary>
    /// Takes the price of the bytes a call moves from the run's fuel, a unit a byte, and ends the
    /// run when it does not hold it.
    /// </summary>
    /// <param name="caller">The guest's call.</param>
    /// <param name="name">The call's name.</param>
    /// <param name="bytes">The bytes read, written or filled.</param>
    /// <exception cref="WasmHostRunEnded">The run is out of fuel.</exception>
    public void Bytes(Caller caller, string name, long bytes)
    {
        if (bytes <= 0)
            return;

        m_census?.Bytes(name, bytes);
        Take(caller, bytes);
    }

    /// <summary>
    /// Notes how one of the host's calls ends the run and gives the way out of the guest.
    /// </summary>
    /// <param name="verdict">How the run ends.</param>
    /// <returns>The exception to throw from the host's call.</returns>
    public WasmHostRunEnded Ended(WasmHostVerdict verdict)
    {
        End = verdict;
        return new WasmHostRunEnded(verdict);
    }

    /// <summary>
    /// Notes that the epoch deadline stopped the run inside one of the host's calls: seen there as
    /// a trap with its code, before the engine wraps it in the error of the guest's call.
    /// </summary>
    public void Interrupted()
    {
        End ??= WasmHostVerdict.Interrupted;
    }

    private void Take(Caller caller, long fuel)
    {
        if (!m_charges)
            return;

        var left = caller.Fuel;
        if ((ulong)fuel > left)
        {
            caller.Fuel = 0;
            throw Ended(WasmHostVerdict.OutOfFuel);
        }

        caller.Fuel = left - (ulong)fuel;
    }

    #endregion

    #region Properties

    /// <summary>Whether the run's calls into the host cost fuel.</summary>
    public bool Charges => m_charges;

    /// <summary>How one of the host's calls ended the run (out of fuel, the output limit, the deadline), or null when none did.</summary>
    public WasmHostVerdict? End { get; private set; }

    #endregion
}
