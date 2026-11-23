namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>The texts of what Smt.Settle writes of a run: the four documents a lab reads and the ledger's section of full models.</summary>
/// <param name="Results">The results, one JSON line a row of the set.</param>
/// <param name="Report">The report.</param>
/// <param name="Integrity">The integrity document.</param>
/// <param name="Provenance">The provenance.</param>
/// <param name="Models">The full models of the rows the results cut short, one JSON line such a row.</param>
internal sealed record SmtSettledDocuments(string Results, string Report, string Integrity, string Provenance, string Models);
