namespace OutWit.Controller.AI.Smt.Diagnostics;

/// <summary>
/// What the host can say about the pool an Smt run would use, as totals only: no node is named,
/// listed or told apart.
/// </summary>
/// <param name="Nodes">Nodes that can take Smt work for this submitter now.</param>
/// <param name="Measured">Of them, the nodes that have measured themselves on the reference set.</param>
/// <param name="Workers">
/// Sessions at once of the measured nodes, added up (the degree each node measured at), or null
/// when the host does not carry it for every measured node: a total over some of the nodes would
/// read as the pool's.
/// </param>
/// <param name="Rate">Problems of the reference set a second of the measured nodes, added up.</param>
internal sealed record SmtPoolSummary(int Nodes, int Measured, int? Workers, double Rate);
