namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The thread an evaluation runs on. Terms are walked by calls, one level of nesting a call, so
/// how deep an evaluation may go is a matter of the stack it runs on; a caller's thread or a
/// pool thread has whatever stack it happens to have. The evaluator therefore makes a thread
/// of its own for each validation, with a stack sized for the nesting ceiling it counts
/// against: the count ends an evaluation long before the stack does, and the same input ends
/// at the same level on every machine.
/// </summary>
internal static class SmtEvaluationThread
{
    #region Constants

    /// <summary>
    /// The stack one level of nesting may take. Measured on every shape that nests (a term in
    /// a term, a connective, a let, a call, a symbol defined by another, a function behind an
    /// array, a value in a value), a level takes up to 1.4 kilobytes in a debug build and 1.0
    /// in an optimized one; this is three times the larger, for other processors and runtimes.
    /// </summary>
    public const int BYTES_PER_LEVEL = 4096;

    /// <summary>The stack under the first level: the thread's own frames and the margin the runtime keeps free.</summary>
    public const int BASE_BYTES = 1 << 20;

    private const string THREAD_NAME = "smt-evaluation";

    #endregion

    #region Functions

    /// <summary>
    /// The stack of a thread that may nest to a ceiling.
    /// </summary>
    /// <param name="depth">The nesting ceiling.</param>
    /// <returns>The size of the stack, in bytes.</returns>
    public static int StackBytes(int depth)
    {
        var ceiling = Math.Clamp(depth, 1, SmtEvaluationBudget.DEPTH_CEILING);
        return BASE_BYTES + ceiling * BYTES_PER_LEVEL;
    }

    /// <summary>
    /// Runs an evaluation on a thread made for it and waits for it.
    /// </summary>
    /// <typeparam name="T">What the evaluation gives.</typeparam>
    /// <param name="evaluation">The evaluation; it lets no exception out.</param>
    /// <param name="depth">The nesting ceiling the evaluation counts against.</param>
    /// <returns>What the evaluation gave.</returns>
    public static T Run<T>(Func<T> evaluation, int depth)
    {
        T result = default!;
        var thread = new Thread(() => result = evaluation(), StackBytes(depth))
        {
            IsBackground = true,
            Name = THREAD_NAME
        };

        thread.Start();
        thread.Join();
        return result;
    }

    #endregion
}
