using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// How the ledger takes what came back under a task's ticket: each value is checked against the
/// server's own task and becomes an execution, a fault of the node or a rejection with its
/// reason (<see cref="CorpusLedger"/>'s summary states the rules). Nothing here reads a run.
/// </summary>
internal static class CorpusLedgerIntake
{
    #region Functions

    /// <summary>Checks every value under a task's ticket and adds it to the shard's entries of the round.</summary>
    /// <param name="slot">The task with what came back under its ticket.</param>
    /// <param name="entries">The shard's entries of the round.</param>
    /// <param name="tally">Receives what is dropped.</param>
    /// <param name="controllerVersion">The server's controller version: the only one a result may carry.</param>
    public static void Take(CorpusRoundSlot slot, List<CorpusLedgerEntry> entries, CorpusLedgerTally tally, string controllerVersion)
    {
        foreach (var value in slot.Values)
        {
            switch (value.Kind)
            {
                case CorpusLedgerEntryKind.Execution when value.Result != null:
                    entries.Add(Entry(value.Result, slot.Task, tally, controllerVersion));
                    break;
                case CorpusLedgerEntryKind.Fault when CorpusResultCodes.IsFault(value.Code):
                    entries.Add(new CorpusLedgerEntry(CorpusLedgerEntryKind.Fault, null, value.Code));
                    break;
                case CorpusLedgerEntryKind.Rejected when CorpusLedger.REJECTION_REASONS.Contains(value.Code):
                    tally.Reject(value.Code);
                    entries.Add(new CorpusLedgerEntry(CorpusLedgerEntryKind.Rejected, null, value.Code));
                    break;
                default:
                    tally.Reject(CorpusLedger.REJECTED_UNREADABLE);
                    entries.Add(new CorpusLedgerEntry(CorpusLedgerEntryKind.Rejected, null, CorpusLedger.REJECTED_UNREADABLE));
                    break;
            }
        }
    }

    private static CorpusLedgerEntry Entry(CorpusResultData result, CorpusTaskData task, CorpusLedgerTally tally, string controllerVersion)
    {
        var reason = Reason(result, task, controllerVersion);
        if (reason != null)
        {
            tally.Reject(reason);
            return new CorpusLedgerEntry(CorpusLedgerEntryKind.Rejected, null, reason);
        }

        return result.Verdict == CorpusShardVerdict.NodeFault
            ? new CorpusLedgerEntry(CorpusLedgerEntryKind.Fault, result, result.Error)
            : new CorpusLedgerEntry(CorpusLedgerEntryKind.Execution, result, "");
    }

    /// <summary>
    /// Why a value is dropped, or null for one that is taken. A node shaped the value, so whatever
    /// a check throws over it is that value's: it is unreadable, and the run goes on.
    /// </summary>
    private static string? Reason(CorpusResultData result, CorpusTaskData task, string controllerVersion)
    {
        try
        {
            return !CorpusResultCheck.IsWellFormed(result) ? CorpusLedger.REJECTED_UNREADABLE
                : result.TaskIndex != task.TaskIndex || !string.Equals(result.SourceUrl, task.Source?.Url ?? "", StringComparison.Ordinal) ? CorpusLedger.REJECTED_CONTENT
                : !string.Equals(result.ControllerVersion, controllerVersion, StringComparison.Ordinal) ? CorpusLedger.REJECTED_VERSION
                : !Fits(result, task) ? CorpusLedger.REJECTED_CONTENT
                : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return CorpusLedger.REJECTED_UNREADABLE;
        }
    }

    /// <summary>Whether a checked result is one the task's pipeline can have produced.</summary>
    private static bool Fits(CorpusResultData result, CorpusTaskData task)
    {
        var pipeline = task.Pipeline;
        if (pipeline == null)
            return result.Verdict != CorpusShardVerdict.Completed;

        if (result.Output != pipeline.Output)
            return false;

        if (result.Verdict != CorpusShardVerdict.Completed)
            return true;

        var keeps = pipeline.Output == CorpusOutputMode.Full;
        if ((result.TextBlobId != Guid.Empty) != keeps || (result.SignaturesBlobId != Guid.Empty) != (keeps && pipeline.Sign))
            return false;

        if (pipeline.Sign != (result.MinHash != null))
            return false;

        if (result.MinHash != null && MinHashParameters.Resolve(result.MinHash) != MinHashParameters.Resolve(pipeline.MinHash))
            return false;

        // More bytes than the node was to accept for the shard: the cap, or the size it was declared with.
        var stats = result.Stats!;
        if (stats.BytesFetched > ShardLimits.FetchCap(task))
            return false;

        // The PDF stage runs on a WARC shard of a pipeline that has it on, and nowhere else.
        if (stats.Pdf != null && (task.Source?.Format != CorpusSourceFormat.Warc || !pipeline.ExtractPdf))
            return false;

        if (!pipeline.IdentifyLanguage)
            return stats.UnsupportedLanguages.Count == 0 && stats.Languages.All(language => language.Name.Length == 0);

        if (stats.Languages.Any(language => language.Name.Length == 0))
            return false;

        return pipeline.TargetLanguages.Count == 0 || stats.Languages.All(language => pipeline.TargetLanguages.Contains(language.Name, StringComparer.Ordinal));
    }

    #endregion
}
