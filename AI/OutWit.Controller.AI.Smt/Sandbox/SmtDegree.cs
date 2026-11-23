namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// How many sessions a package runs at once on a machine, and why (<see cref="SmtSandboxDegree"/>).
/// </summary>
/// <param name="Degree">The sessions at once; 0 when not one fits in the memory.</param>
/// <param name="Reason">What decided it: <c>cores</c>, <c>cap</c>, <c>memory</c>, <c>no_memory</c>.</param>
/// <param name="Cores">The logical cores counted.</param>
/// <param name="AvailableBytes">The available memory counted; 0 when the machine could not say.</param>
/// <param name="UsableBytes">The share of it the sessions may claim together; 0 when the memory was not counted.</param>
/// <param name="SessionBytes">The memory one session may take.</param>
internal sealed record SmtDegree(int Degree, string Reason, int Cores, long AvailableBytes, long UsableBytes, long SessionBytes);
