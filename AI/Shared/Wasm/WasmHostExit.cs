using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The guest's <c>proc_exit</c>, defined by the host for one run: it notes the status the guest
/// asked for and ends the run with a trap of the host's own, so that the run ends as an exit with
/// that status, whatever it is. WASI preview 1 has statuses below 126; wasmtime 48's own
/// <c>proc_exit</c> refuses the others with an error that is neither a trap nor an exit and carries
/// no status ("exit with invalid exit status outside of [0..126)"). A program that calls
/// <c>exit(200)</c> ended by itself, as it would on any other system: with this definition every
/// status is the guest's exit code, and the ones below 126 are what WASI's own definition gave.
/// </summary>
internal sealed class WasmHostExit
{
    #region Constants

    private const string WASI_MODULE = "wasi_snapshot_preview1";
    private const string PROC_EXIT = "proc_exit";

    #endregion

    #region Fields

    private int? m_status;

    #endregion

    #region Functions

    /// <summary>
    /// Defines the run's <c>proc_exit</c> in a linker that has WASI and allows shadowing.
    /// </summary>
    /// <param name="linker">The run's linker.</param>
    /// <returns>The run's exit, which holds the status once the guest exited.</returns>
    public static WasmHostExit Define(Linker linker)
    {
        var exit = new WasmHostExit();
        linker.DefineFunction(WASI_MODULE, PROC_EXIT, (int status) => exit.Take(status));
        return exit;
    }

    private void Take(int status)
    {
        m_status = status;

        // The binding turns an exception of a host function into a trap, which unwinds the guest.
        throw new ExitRequested(status);
    }

    #endregion

    #region Properties

    /// <summary>
    /// The status the guest asked for, or null while it has not called <c>proc_exit</c>. WASI's
    /// status is unsigned; one of 2^31 or more reads as a negative number (4294967295 is -1).
    /// </summary>
    public int? Status => m_status;

    #endregion

    #region Nested Types

    /// <summary>The way out of the guest; never seen outside a run.</summary>
    private sealed class ExitRequested(int status) : Exception($"the guest exited with status {(uint)status}");

    #endregion
}
