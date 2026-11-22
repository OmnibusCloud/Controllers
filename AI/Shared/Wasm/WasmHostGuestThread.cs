using System.Runtime.ExceptionServices;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The threads guest code runs on. The engine lets a guest use up to <see cref="ENGINE_STACK_BYTES"/>
/// of the native call stack: 8 MiB, the stack a program has on Linux, sixteen times the engine's
/// default of 512 KiB, where a compiled program's recursion ended near ten thousand levels (the
/// engine's async stack is set to match, <see cref="WasmHostEngineNative"/>). A thread that runs a
/// guest must hold that and the host's own frames, which a pool thread or a caller's thread need
/// not: a host's workers are made here with <see cref="THREAD_STACK_BYTES"/>, and a run from any
/// other thread moves to one made for it.
/// </summary>
internal static class WasmHostGuestThread
{
    #region Constants

    /// <summary>The native stack a guest may use (the engine's maximum wasm stack).</summary>
    public const int ENGINE_STACK_BYTES = 8 << 20;

    /// <summary>The stack of a thread that runs guests: the guest's share and room for the host.</summary>
    public const int THREAD_STACK_BYTES = 16 << 20;

    private const string THREAD_NAME = "wasm-guest";

    #endregion

    #region Fields

    [ThreadStatic]
    private static bool m_isGuestThread;

    #endregion

    #region Functions

    /// <summary>
    /// Makes a thread that runs guests: its stack holds the engine's share and the host's frames.
    /// </summary>
    /// <param name="body">What the thread runs.</param>
    /// <returns>The thread, not started.</returns>
    public static Thread Create(Action body)
    {
        return new Thread(() =>
        {
            m_isGuestThread = true;
            body();
        }, THREAD_STACK_BYTES);
    }

    /// <summary>
    /// Runs guest code on a thread that can hold it: the current one when it was made by
    /// <see cref="Create"/>, otherwise a thread made for the call.
    /// </summary>
    /// <typeparam name="T">What the call returns.</typeparam>
    /// <param name="call">The call that enters the guest.</param>
    /// <returns>What the call returned.</returns>
    /// <exception cref="Exception">Whatever the call threw, rethrown on the caller's thread.</exception>
    public static T Run<T>(Func<T> call)
    {
        if (m_isGuestThread)
            return call();

        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = Create(() =>
        {
            try
            {
                result = call();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.IsBackground = true;
        thread.Name = THREAD_NAME;
        thread.Start();
        thread.Join();

        failure?.Throw();
        return result;
    }

    #endregion
}
