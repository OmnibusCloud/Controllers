namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>A package of results a node wrote to a file.</summary>
/// <param name="Path">The package file, complete.</param>
/// <param name="Rows">The rows in it.</param>
internal sealed record SmtWrittenResults(string Path, int Rows);
