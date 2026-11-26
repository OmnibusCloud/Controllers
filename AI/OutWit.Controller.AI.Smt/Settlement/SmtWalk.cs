namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The check of one copy of a row under its package's budget: its models walked in order, each
/// checked or taken from a copy of the same digest that was checked already, until the row ends
/// (every model, a failed one, a fault of the evaluator) or the package has no steps left.
/// </summary>
/// <param name="Check">What the walk gave: the row's class and reason as far as it went.</param>
/// <param name="Models">How many models it walked.</param>
/// <param name="Ended">Whether it reached the row's end: false when the package's budget stopped it.</param>
internal sealed record SmtWalk(SmtModelCheck Check, int Models, bool Ended)
{
    #region Functions

    /// <summary>
    /// The walk that says the most of a row of which two walks were made (two copies of one digest,
    /// each under its own package): a failed one, else one that reached the row's end, else the one
    /// that went further. Walks of one row agree as far as both went, so the order the walks are
    /// taken in does not change the answer.
    /// </summary>
    /// <param name="left">A walk, or null.</param>
    /// <param name="right">Another walk.</param>
    /// <returns>The walk that says the most.</returns>
    public static SmtWalk Better(SmtWalk? left, SmtWalk right)
    {
        if (left == null)
            return right;

        var leftRank = Rank(left);
        var rightRank = Rank(right);
        if (leftRank != rightRank)
            return rightRank > leftRank ? right : left;

        return right.Models > left.Models ? right : left;
    }

    private static int Rank(SmtWalk walk)
    {
        return walk.Check.IsFailed ? 2 : walk.Ended ? 1 : 0;
    }

    #endregion
}
