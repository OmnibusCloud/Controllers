namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>Where a package's place stands in the node's budget of sessions.</summary>
internal enum SmtSessionBudgetPlaceState
{
    /// <summary>The package waits to be let in.</summary>
    Waiting,

    /// <summary>The package is let in: it is checked and opened, or its problems are handed out and running.</summary>
    Open,

    /// <summary>The package has left the budget.</summary>
    Left
}
