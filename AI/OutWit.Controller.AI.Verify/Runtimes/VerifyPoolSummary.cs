namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// What the host can say about the pool a Verify run would use, as totals only: no node is
/// named, listed or told apart.
/// </summary>
/// <param name="Nodes">Nodes that can take Verify work for this submitter now.</param>
/// <param name="Measured">Of them, the nodes that have measured themselves on the reference workload.</param>
/// <param name="Workers">Concurrent sandboxes of the measured nodes, added up (the degree each node measured at).</param>
/// <param name="Rate">Reference tasks per second of the measured nodes, added up.</param>
public sealed record VerifyPoolSummary(int Nodes, int Measured, int Workers, double Rate);
