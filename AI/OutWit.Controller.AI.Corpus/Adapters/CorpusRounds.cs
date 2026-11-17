using System.Collections;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

/// <summary>
/// The host-side step the round-aware activities share: read the script's task and result
/// collections of each round from the pool and build the server's ledger over them. The values of
/// the result collections are whatever the nodes returned; nothing of them is read here.
/// </summary>
internal static class CorpusRounds
{
    #region Functions

    /// <summary>Builds the ledger over the rounds the script has run.</summary>
    /// <param name="pool">The script's variables.</param>
    /// <param name="tasks">The job's tasks.</param>
    /// <param name="executions">What the first round returned.</param>
    /// <param name="repairs">The repair round's tasks; null when the caller is before that round.</param>
    /// <param name="repaired">What the repair round returned.</param>
    /// <param name="disputes">The dispute round's tasks; null when the caller is before that round.</param>
    /// <param name="resolved">What the dispute round returned.</param>
    /// <returns>The ledger.</returns>
    /// <exception cref="InvalidOperationException">The job's tasks or the first round's results are not in the pool.</exception>
    public static CorpusLedger Ledger(IWitVariablesCollection pool, IWitReference? tasks, IWitReference? executions, IWitReference? repairs = null, IWitReference? repaired = null, IWitReference? disputes = null, IWitReference? resolved = null)
    {
        var first = CorpusRound.Of(CorpusRoundKind.First, Required(pool, tasks, "Tasks"), Required(pool, executions, "Executions"));

        return CorpusLedger.Build(
            ControllerBuildInfo.VERSION,
            first,
            Round(pool, CorpusRoundKind.Repair, repairs, repaired),
            Round(pool, CorpusRoundKind.Dispute, disputes, resolved));
    }

    /// <summary>A later round: null when the script names no task collection for it (the round has not run).</summary>
    private static CorpusRound? Round(IWitVariablesCollection pool, CorpusRoundKind kind, IWitReference? tasks, IWitReference? results)
    {
        if (tasks == null || !pool.TryGetObject(tasks, out var tasksObject) || tasksObject is not IEnumerable taskItems)
            return null;

        // An empty round returns nothing: a result collection that was never set is an empty one.
        var resultItems = results != null && pool.TryGetObject(results, out var resultsObject) ? resultsObject as IEnumerable : null;

        return CorpusRound.Of(kind, taskItems, resultItems);
    }

    private static IEnumerable Required(IWitVariablesCollection pool, IWitReference? reference, string name)
    {
        if (reference == null || !pool.TryGetObject(reference, out var value) || value is not IEnumerable items)
            throw new InvalidOperationException($"Failed to get parameter '{name}'.");

        return items;
    }

    #endregion
}
