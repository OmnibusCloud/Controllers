namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>One row of the fetch list with its text.</summary>
/// <param name="Id">The slice's id (p000…).</param>
/// <param name="Logic">The SMT-LIB logic.</param>
/// <param name="Status">The :status the list recorded.</param>
/// <param name="Member">The path in the SMT-LIB archive.</param>
/// <param name="Text">The file's text, or null when it is missing.</param>
internal sealed record SpikeProblem(string Id, string Logic, string Status, string Member, string? Text);
