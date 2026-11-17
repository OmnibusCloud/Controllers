using System.Collections;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// One round as the server holds it: the tasks it made (each with the ticket of that round) and
/// whatever came back for them, exactly as it came — the values are a node's, so they are kept as
/// objects until <see cref="CorpusLedger"/> has checked each one.
/// </summary>
/// <param name="Kind">Which round.</param>
/// <param name="Tasks">The tasks the server made for the round.</param>
/// <param name="Results">What the nodes returned, in the order the platform handed it over.</param>
public sealed record CorpusRound(CorpusRoundKind Kind, IReadOnlyList<CorpusTaskData> Tasks, IReadOnlyList<object?> Results)
{
    #region Functions

    /// <summary>A round from two script variables: collections of anything.</summary>
    /// <param name="kind">Which round.</param>
    /// <param name="tasks">The task collection; null when the script has none.</param>
    /// <param name="results">The result collection; null when the script has none.</param>
    /// <returns>The round; a value of the task collection that is no task is left out.</returns>
    public static CorpusRound Of(CorpusRoundKind kind, IEnumerable? tasks, IEnumerable? results)
    {
        return new CorpusRound(
            kind,
            tasks?.OfType<CorpusTaskData>().ToList() ?? [],
            results?.Cast<object?>().ToList() ?? []);
    }

    #endregion
}
