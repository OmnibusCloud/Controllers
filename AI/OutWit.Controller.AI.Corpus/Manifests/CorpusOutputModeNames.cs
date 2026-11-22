using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// The names of the output modes as the manifest, the report and the capabilities document write
/// them: <c>full</c> and <c>stats_and_hashes</c>.
/// </summary>
public static class CorpusOutputModeNames
{
    #region Constants

    /// <summary>Everything stays on the server: text, signatures, statistics, hashes, documents.</summary>
    public const string FULL = "full";

    /// <summary>The statistics, the content hashes and the documents stay on the server; the text and the signatures do not.</summary>
    public const string STATS_AND_HASHES = "stats_and_hashes";

    /// <summary>Every name, the default first.</summary>
    public static readonly IReadOnlyList<string> ALL = [FULL, STATS_AND_HASHES];

    #endregion

    #region Functions

    /// <summary>The name of a mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>Its name.</returns>
    public static string Name(CorpusOutputMode mode)
    {
        return mode == CorpusOutputMode.StatsAndHashes ? STATS_AND_HASHES : FULL;
    }

    /// <summary>The mode a name stands for.</summary>
    /// <param name="name">The name, exactly as written.</param>
    /// <param name="mode">The mode, when the name is one.</param>
    /// <returns>True when the name is a mode's.</returns>
    public static bool TryParse(string? name, out CorpusOutputMode mode)
    {
        switch (name)
        {
            case FULL:
                mode = CorpusOutputMode.Full;
                return true;
            case STATS_AND_HASHES:
                mode = CorpusOutputMode.StatsAndHashes;
                return true;
            default:
                mode = CorpusOutputMode.Full;
                return false;
        }
    }

    #endregion
}
