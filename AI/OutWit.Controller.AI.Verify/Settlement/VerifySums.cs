namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// Sums of what nodes reported. A node's number is held to a range before it is taken, but a
/// sum of numbers a node chose is never allowed to throw or to wrap: it stops at the largest
/// value its type holds.
/// </summary>
internal static class VerifySums
{
    #region Functions

    /// <summary>Adds two numbers, neither negative, and stops at the largest value.</summary>
    /// <param name="left">A number, 0 or more.</param>
    /// <param name="right">A number, 0 or more.</param>
    /// <returns>The sum, or <see cref="long.MaxValue"/> when it does not fit.</returns>
    public static long Add(long left, long right)
    {
        left = Math.Max(0, left);
        right = Math.Max(0, right);

        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    /// <summary>The fuel the executions of a batch reported, together.</summary>
    /// <param name="executions">The executions.</param>
    /// <returns>The sum, stopped at the largest value.</returns>
    public static long Fuel(IEnumerable<VerifyExecution> executions)
    {
        long total = 0;
        foreach (var execution in executions)
            total = Add(total, execution.Fuel);

        return total;
    }

    /// <summary>The wall time the executions of a batch reported, together.</summary>
    /// <param name="executions">The executions.</param>
    /// <returns>The sum in milliseconds, stopped at the largest value.</returns>
    public static long WallMs(IEnumerable<VerifyExecution> executions)
    {
        long total = 0;
        foreach (var execution in executions)
            total = Add(total, execution.WallMs);

        return total;
    }

    #endregion
}
