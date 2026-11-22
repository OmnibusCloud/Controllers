namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>One head of a result's image (<see cref="CorpusTestImage"/>): a number the decoder reads to know what follows.</summary>
/// <param name="Name">What the head belongs to, e.g. <c>source_url</c>, <c>drops</c>, <c>drops[0].name.chars</c>.</param>
/// <param name="Kind">What the head counts.</param>
/// <param name="Offset">Where it stands in the image.</param>
internal sealed record CorpusTestImageHead(string Name, CorpusTestImageHeadKind Kind, int Offset)
{
    #region Functions

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Name} ({Kind} at {Offset})";
    }

    #endregion
}
