namespace OutWit.Controller.AI.Smt.Live.Documents;

/// <summary>What the check of one document file came to.</summary>
/// <param name="Items">The values read: 1 for a JSON document, the rows of a JSON lines file.</param>
/// <param name="Bytes">The file's size.</param>
/// <param name="Faulty">The values (rows) with at least one fault.</param>
/// <param name="Errors">The first faults, each with its line (for JSON lines) and its JSON path.</param>
internal sealed record SmtLiveDocumentVerdict(int Items, long Bytes, int Faulty, IReadOnlyList<string> Errors)
{
    /// <summary>Whether the file holds to its schema.</summary>
    public bool IsValid => Faulty == 0 && Errors.Count == 0;
}
