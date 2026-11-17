using System.Collections;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// Opens the packages a job's variables refer to, and writes packages as a node or the server
/// would: a test reads what travelled (the tasks behind a task package, the results behind an
/// answer) and makes the answers a dishonest node would send.
/// </summary>
internal static class CorpusTestPackages
{
    #region Functions

    /// <summary>The tasks of a task package.</summary>
    /// <param name="blobs">Holds the package's blob.</param>
    /// <param name="package">The package's reference.</param>
    /// <returns>The tasks, in the order written.</returns>
    public static List<CorpusTaskData> Tasks(IWitBlobService blobs, CorpusTaskPackageData package)
    {
        return CorpusPackageCodec.ReadTasks(blobs.GetLocalPathAsync(package.TasksBlobId).GetAwaiter().GetResult(), CorpusPackagePlanner.MAX_PACKAGE_SHARDS);
    }

    /// <summary>The tasks of every task package of a job variable.</summary>
    /// <param name="blobs">Holds the packages' blobs.</param>
    /// <param name="packages">The variable's value: a collection of task package references (anything else is left out).</param>
    /// <returns>The tasks, package after package.</returns>
    public static List<CorpusTaskData> Tasks(IWitBlobService blobs, object? packages)
    {
        return packages is IEnumerable items
            ? items.OfType<CorpusTaskPackageData>().SelectMany(package => Tasks(blobs, package)).ToList()
            : [];
    }

    /// <summary>The results of an answer; none for an answer that carries a fault.</summary>
    /// <param name="blobs">Holds the answer's blob.</param>
    /// <param name="answer">The answer.</param>
    /// <returns>The results, in the order written.</returns>
    public static List<CorpusResultData> Results(IWitBlobService blobs, CorpusResultPackageData answer)
    {
        if (answer.ResultsBlobId == Guid.Empty)
            return [];

        return CorpusPackageCodec.ReadResults(blobs.GetLocalPathAsync(answer.ResultsBlobId).GetAwaiter().GetResult(), CorpusPackagePlanner.MAX_PACKAGE_SHARDS)
            .OfType<CorpusResultData>()
            .ToList();
    }

    /// <summary>The results of every answer of a job variable.</summary>
    /// <param name="blobs">Holds the answers' blobs.</param>
    /// <param name="answers">The variable's value: a collection of answers (anything else is left out).</param>
    /// <returns>The results, answer after answer.</returns>
    public static List<CorpusResultData> Results(IWitBlobService blobs, object? answers)
    {
        return answers is IEnumerable items
            ? items.OfType<CorpusResultPackageData>().SelectMany(answer => Results(blobs, answer)).ToList()
            : [];
    }

    /// <summary>An answer as a node writes it: the results in a blob, under the package's ticket.</summary>
    /// <param name="blobs">Receives the blob.</param>
    /// <param name="ticket">The ticket of the task package the answer is for.</param>
    /// <param name="results">The results.</param>
    /// <returns>The answer.</returns>
    public static CorpusResultPackageData Answer(IWitBlobService blobs, string ticket, IEnumerable<CorpusResultData> results)
    {
        using var buffer = new MemoryStream();
        CorpusPackageCodec.WriteResults(buffer, results);

        return new CorpusResultPackageData
        {
            Ticket = ticket,
            ResultsBlobId = blobs.UploadBytesAsync(buffer.ToArray(), CorpusPackageCodec.RESULTS_FILE_NAME).GetAwaiter().GetResult()
        };
    }

    /// <summary>A round's task packages as the server writes them.</summary>
    /// <param name="blobs">Receives the blobs.</param>
    /// <param name="tasks">The round's tasks, in shard order.</param>
    /// <returns>The packages' references.</returns>
    public static List<CorpusTaskPackageData> Packages(IWitBlobService blobs, IReadOnlyList<CorpusTaskData> tasks)
    {
        return tasks.Count == 0 ? [] : CorpusTaskPackages.WriteAsync(blobs, tasks, tasks.Count).GetAwaiter().GetResult();
    }

    #endregion
}
