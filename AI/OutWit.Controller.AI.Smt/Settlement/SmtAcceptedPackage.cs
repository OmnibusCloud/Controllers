using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>A result package the host took: the package of problems it answers and its rows.</summary>
/// <param name="Unit">The package of problems.</param>
/// <param name="Rows">The package's rows, one a problem.</param>
/// <param name="Bound">Whether the platform told the host it was returned for a task of that package (<see cref="SmtResultBindings"/>).</param>
internal sealed record SmtAcceptedPackage(SmtTaskPackageData Unit, List<SmtCopy> Rows, bool Bound);
