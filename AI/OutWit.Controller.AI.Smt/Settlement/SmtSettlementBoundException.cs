namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// A run the host does not settle because what it would keep of it on its own disk passes a bound
/// (<see cref="SmtSettlementBounds"/>): the activity fails with the bound's name and its number,
/// and the disk is not filled. The words say sizes only: no machine, no folder, no package.
/// </summary>
internal sealed class SmtSettlementBoundException : Exception
{
    #region Constants

    /// <summary>The name of the bound of the rows the nodes returned.</summary>
    public const string ROWS = "smt_rows_bound";

    /// <summary>The name of the bound of the full models a ledger holds.</summary>
    public const string MODELS = "smt_models_bound";

    #endregion

    #region Constructors

    /// <summary>
    /// A run past a bound.
    /// </summary>
    /// <param name="name">The bound's name: <see cref="ROWS"/> or <see cref="MODELS"/>.</param>
    /// <param name="bound">The bound, bytes.</param>
    /// <param name="what">What passed it, in fixed words.</param>
    public SmtSettlementBoundException(string name, long bound, string what)
        : base($"Smt: {name}: {what} pass the {bound} bytes the settlement of one run keeps; the run is not settled.")
    {
        Name = name;
        Bound = bound;
    }

    #endregion

    #region Properties

    /// <summary>The bound's name.</summary>
    public string Name { get; }

    /// <summary>The bound, bytes.</summary>
    public long Bound { get; }

    #endregion
}
