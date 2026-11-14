using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>A defined function: its parameter names and sorts, its result sort (null for a :named term) and its body.</summary>
/// <param name="Parameters">The parameter names.</param>
/// <param name="Sorts">The parameter sorts.</param>
/// <param name="Result">The result sort, or null when not stated.</param>
/// <param name="Body">The body.</param>
internal sealed record SmtDefinition(List<string> Parameters, List<SExpr> Sorts, SExpr? Result, SExpr Body);
