using System.Collections;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

/// <summary>
/// The host-side steps the round-aware activities share: read the references to each round's task
/// packages and answers from the pool, open the round over its blobs and build the server's ledger
/// over them; write a new round's tasks into packages. The pool holds references only (a bounded
/// number a round); the tasks and the results are read from the blobs one package at a time.
/// </summary>
internal static class CorpusRounds
{
    #region Functions

    /// <summary>Builds the ledger over the rounds the script has run.</summary>
    /// <param name="blobs">Resolves the rounds' blobs.</param>
    /// <param name="pool">The script's variables.</param>
    /// <param name="tasks">The job's task packages.</param>
    /// <param name="executions">What the first round returned.</param>
    /// <param name="repairs">The repair round's task packages; null when the caller is before that round.</param>
    /// <param name="repaired">What the repair round returned.</param>
    /// <param name="disputes">The dispute round's task packages; null when the caller is before that round.</param>
    /// <param name="resolved">What the dispute round returned.</param>
    /// <returns>The ledger.</returns>
    /// <exception cref="InvalidOperationException">The job's task packages or the first round's answers are not in the pool, or are not the ones this controller wrote.</exception>
    public static async Task<CorpusLedger> LedgerAsync(IWitBlobService blobs, IWitVariablesCollection pool, IWitReference? tasks, IWitReference? executions, IWitReference? repairs = null, IWitReference? repaired = null, IWitReference? disputes = null, IWitReference? resolved = null)
    {
        var first = await CorpusRoundSourceBlobs.OpenAsync(CorpusRoundKind.First, Items(Required(pool, tasks, "Tasks")), Items(Required(pool, executions, "Executions")), blobs);
        var repair = await RoundAsync(blobs, pool, CorpusRoundKind.Repair, repairs, repaired);
        var dispute = await RoundAsync(blobs, pool, CorpusRoundKind.Dispute, disputes, resolved);

        // The first reading of the run, for its counts: off the caller's thread.
        return await Task.Run(() => CorpusLedger.Build(ControllerBuildInfo.VERSION, first, repair, dispute));
    }

    /// <summary>Writes a round's tasks into task packages.</summary>
    /// <param name="blobs">Receives the packages.</param>
    /// <param name="tasks">The round's tasks, in shard order, made as they are asked for.</param>
    /// <param name="count">How many there are.</param>
    /// <returns>The packages' references.</returns>
    public static Task<List<CorpusTaskPackageData>> PackagesAsync(IWitBlobService blobs, IEnumerable<CorpusTaskData> tasks, int count)
    {
        return count == 0 ? Task.FromResult(new List<CorpusTaskPackageData>()) : CorpusTaskPackages.WriteAsync(blobs, tasks, count);
    }

    /// <summary>
    /// Runs a host-side step over the run's blobs. A failure of the server's own files leaves the
    /// activity in fixed words: the failure's text names the server's folders, and what an
    /// activity throws is shown to the lab.
    /// </summary>
    /// <typeparam name="T">What the step returns.</typeparam>
    /// <param name="activity">The activity's name, for the message.</param>
    /// <param name="step">The step.</param>
    /// <returns>What the step returned.</returns>
    /// <exception cref="InvalidOperationException">The server could not read or write the run's blobs.</exception>
    public static async Task<T> HostAsync<T>(string activity, Func<Task<T>> step)
    {
        try
        {
            return await step();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidOperationException($"{activity}: the server could not read or write the run's blobs.");
        }
    }

    #endregion

    #region Tools

    /// <summary>A later round: null when the script names no task collection for it (the round has not run).</summary>
    private static async Task<CorpusRoundSourceBlobs?> RoundAsync(IWitBlobService blobs, IWitVariablesCollection pool, CorpusRoundKind kind, IWitReference? tasks, IWitReference? results)
    {
        if (tasks == null || !pool.TryGetObject(tasks, out var tasksObject) || tasksObject is not IEnumerable taskItems)
            return null;

        // An empty round returns nothing: an answer collection that was never set is an empty one.
        var resultItems = results != null && pool.TryGetObject(results, out var resultsObject) ? resultsObject as IEnumerable : null;

        return await CorpusRoundSourceBlobs.OpenAsync(kind, Items(taskItems), resultItems == null ? null : Items(resultItems), blobs);
    }

    private static IEnumerable Required(IWitVariablesCollection pool, IWitReference? reference, string name)
    {
        if (reference == null || !pool.TryGetObject(reference, out var value) || value is not IEnumerable items)
            throw new InvalidOperationException($"Failed to get parameter '{name}'.");

        return items;
    }

    private static IEnumerable<object?> Items(IEnumerable items)
    {
        return items.Cast<object?>();
    }

    #endregion
}
