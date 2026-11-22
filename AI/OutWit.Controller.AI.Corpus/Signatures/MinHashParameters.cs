using System.Globalization;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Signatures;

/// <summary>
/// The MinHash parameters a pipeline signs with, resolved: the hash count, the shingle size and the
/// band layout of the near-duplicate merge, with the controller's defaults where the pipeline says
/// nothing (or 0). Signatures made with different parameters cannot be compared, so the parameters
/// travel with the signatures (the result, the report, the blob's header) and the merge refuses to
/// mix them.
/// </summary>
/// <param name="Hashes">Hash functions per signature.</param>
/// <param name="ShingleWords">Words per shingle.</param>
/// <param name="Bands">LSH bands of the merge; divides <paramref name="Hashes"/>.</param>
public sealed record MinHashParameters(int Hashes, int ShingleWords, int Bands)
{
    #region Constants

    /// <summary>The defaults: 128 hashes over 5-word shingles, 16 bands of 8 rows.</summary>
    public static readonly MinHashParameters DEFAULT = new(MinHashSigner.HASHES, MinHashSigner.SHINGLE_WORDS, MinHashSigner.BANDS);

    #endregion

    #region Functions

    /// <summary>The parameters a pipeline's settings resolve to: each 0 (or a missing setting) is the default.</summary>
    /// <param name="data">The pipeline's settings; null = every default.</param>
    /// <returns>The parameters (not checked: see <see cref="Problem"/>).</returns>
    public static MinHashParameters Resolve(CorpusMinHashData? data)
    {
        if (data == null)
            return DEFAULT;

        return new MinHashParameters(
            data.Hashes > 0 ? data.Hashes : DEFAULT.Hashes,
            data.ShingleWords > 0 ? data.ShingleWords : DEFAULT.ShingleWords,
            data.Bands > 0 ? data.Bands : DEFAULT.Bands);
    }

    /// <summary>Why a pipeline's settings cannot be used, named for the manifest; null when they can.</summary>
    /// <param name="data">The pipeline's settings; null = every default.</param>
    /// <returns>The refusal, or null.</returns>
    public static string? Problem(CorpusMinHashData? data)
    {
        if (data == null)
            return null;

        if (data.Hashes < 0 || data.Hashes > MinHashSigner.MAX_HASHES)
            return $"'hashes' must be an integer from 1 to {MinHashSigner.MAX_HASHES} (0 or absent: {DEFAULT.Hashes}), not {data.Hashes}.";

        if (data.ShingleWords < 0 || data.ShingleWords > MinHashSigner.MAX_SHINGLE_WORDS)
            return $"'shingle_words' must be an integer from 1 to {MinHashSigner.MAX_SHINGLE_WORDS} (0 or absent: {DEFAULT.ShingleWords}), not {data.ShingleWords}.";

        var resolved = Resolve(data);
        if (data.Bands < 0 || data.Bands > resolved.Hashes)
            return $"'bands' must be an integer from 1 to the hash count, {resolved.Hashes} (0 or absent: {DEFAULT.Bands}), not {data.Bands}.";

        if (resolved.Hashes % resolved.Bands != 0)
            return $"'bands' ({resolved.Bands}{(data.Bands == 0 ? ", the default" : "")}) must divide 'hashes' ({resolved.Hashes}): a band is hashes / bands rows of the signature.";

        return null;
    }

    /// <summary>The parameters as they travel in a result: every value resolved.</summary>
    /// <returns>The data.</returns>
    public CorpusMinHashData ToData()
    {
        return new CorpusMinHashData { Hashes = Hashes, ShingleWords = ShingleWords, Bands = Bands };
    }

    /// <summary>The parameters for a person: "128 hashes over 5-word shingles, 16 bands of 8 rows".</summary>
    /// <returns>The text.</returns>
    public string Describe()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Hashes} hashes over {ShingleWords}-word shingles, {Bands} bands of {RowsPerBand} rows");
    }

    #endregion

    #region Properties

    /// <summary>Whether these are the defaults: a signature made with them is the one Corpus 0.2.x made.</summary>
    public bool IsDefault => this == DEFAULT;

    /// <summary>Rows of the signature per LSH band.</summary>
    public int RowsPerBand => Bands > 0 ? Hashes / Bands : 0;

    /// <summary>Bytes of one document's signature.</summary>
    public int SignatureBytes => Hashes * sizeof(ulong);

    #endregion
}
