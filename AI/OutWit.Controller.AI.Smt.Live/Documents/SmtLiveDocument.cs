namespace OutWit.Controller.AI.Smt.Live.Documents;

/// <summary>One document a job hands out as a blob.</summary>
/// <param name="Variable">The script's variable that names the blob.</param>
/// <param name="File">The file it is saved as.</param>
/// <param name="Schema">The file of its schema in the controller's <c>docs/</c>.</param>
/// <param name="Lines">Whether it is JSON lines (one value a line) rather than one JSON value.</param>
internal sealed record SmtLiveDocument(string Variable, string File, string Schema, bool Lines);
