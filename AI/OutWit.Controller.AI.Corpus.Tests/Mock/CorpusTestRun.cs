using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A run as the server holds it, for tests of the ledger and the documents: tasks with tickets as
/// <c>Corpus.Split</c> issues them, and results shaped as an honest node shapes them — which a
/// test then bends into whatever a dishonest one would send.
/// </summary>
internal static class CorpusTestRun
{
    #region Constants

    /// <summary>The controller version of this build: the one an accepted result carries.</summary>
    public static readonly string VERSION = ControllerBuildInfo.VERSION;

    #endregion

    #region Functions

    /// <summary>A SHA-256 of one repeated hex digit.</summary>
    public static string Hash(char digit)
    {
        return new string(digit, 64);
    }

    /// <summary>A pipeline that labels no language and signs nothing: the plainest result.</summary>
    public static CorpusPipelineData Plain()
    {
        return new CorpusPipelineData { Version = "test", IdentifyLanguage = false, Sign = false };
    }

    /// <summary>A pipeline that labels languages.</summary>
    public static CorpusPipelineData Labelled()
    {
        return new CorpusPipelineData { Version = "test", IdentifyLanguage = true, LanguageModelBlobId = Guid.NewGuid(), LanguageModelSha256 = Hash('1'), Sign = false };
    }

    /// <summary>A pipeline that signs with the defaults.</summary>
    public static CorpusPipelineData Signed()
    {
        return new CorpusPipelineData { Version = "test", IdentifyLanguage = false, Sign = true };
    }

    /// <summary>One task of a job's first round, as Corpus.Split issues it.</summary>
    public static CorpusTaskData Task(int index, CorpusPipelineData? pipeline = null)
    {
        return new CorpusTaskData
        {
            TaskIndex = index,
            Source = new CorpusSourceData { Url = $"https://data.commoncrawl.org/shard-{index}.warc.wet.gz", Format = CorpusSourceFormat.Wet },
            Pipeline = pipeline ?? Plain(),
            AllowedOrigins = ["data.commoncrawl.org"],
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = VERSION
        };
    }

    /// <summary>The tasks 0 to count - 1.</summary>
    public static List<CorpusTaskData> Tasks(int count, CorpusPipelineData? pipeline = null)
    {
        return Enumerable.Range(0, count).Select(index => Task(index, pipeline?.Clone())).ToList();
    }

    /// <summary>The same shard under a new ticket: what a later round sends out.</summary>
    public static CorpusTaskData Reissue(CorpusTaskData task)
    {
        var again = task.Clone();
        again.Ticket = CorpusTickets.New();
        return again;
    }

    /// <summary>What an honest node returns for a task it completed.</summary>
    /// <param name="task">The task.</param>
    /// <param name="content">The hex digit the content hash is made of: two results with the same digit agree on the text.</param>
    /// <param name="documentsOut">Documents in the product.</param>
    /// <param name="languages">The product's languages (a labelling pipeline); null for one language holding every document.</param>
    /// <param name="drops">Drops by rule.</param>
    /// <param name="signatures">The hex digit of the signatures hash (a signing pipeline).</param>
    public static CorpusResultData Completed(CorpusTaskData task, char content = 'a', int documentsOut = 5, (string Name, int Count)[]? languages = null, (string Name, int Count)[]? drops = null, char signatures = 'b')
    {
        var pipeline = task.Pipeline!;
        drops ??= [];
        languages ??= documentsOut > 0 ? [(pipeline.IdentifyLanguage ? "eng" : "", documentsOut)] : [];
        var inside = drops.Where(drop => ShardDropRules.StageOf(drop.Name) != ShardDropRules.STAGE_EXTRACT).Sum(drop => drop.Count);
        var beside = drops.Sum(drop => drop.Count) - inside;
        var signs = pipeline.Sign;
        var keeps = pipeline.Output == CorpusOutputMode.Full;

        return new CorpusResultData
        {
            TaskIndex = task.TaskIndex,
            Verdict = CorpusShardVerdict.Completed,
            TextBlobId = keeps ? Guid.NewGuid() : Guid.Empty,
            SignaturesBlobId = keeps && signs ? Guid.NewGuid() : Guid.Empty,
            ContentSha256 = Hash(content),
            SignaturesSha256 = signs ? Hash(signatures) : "",
            MinHash = signs ? MinHashParameters.Resolve(pipeline.MinHash).ToData() : null,
            ControllerVersion = VERSION,
            SourceUrl = task.Source!.Url,
            RuntimeTarget = "linux-x64",
            Output = pipeline.Output,
            Ticket = task.Ticket,
            Stats = new CorpusShardStatsData
            {
                RecordsIn = documentsOut + inside + beside + 3,
                DocumentsExtracted = documentsOut + inside,
                DocumentsOut = documentsOut,
                Languages = Counts(languages),
                Drops = Counts(drops),
                BytesFetched = 1000,
                BytesProduced = 300,
                FetchMs = 10,
                ProcessMs = 20
            }
        };
    }

    /// <summary>What an honest node returns for a shard that did not complete for a reason of the shard.</summary>
    public static CorpusResultData Verdict(CorpusTaskData task, CorpusShardVerdict verdict, string code)
    {
        return new CorpusResultData
        {
            TaskIndex = task.TaskIndex,
            Verdict = verdict,
            Error = code,
            ControllerVersion = VERSION,
            SourceUrl = task.Source!.Url,
            RuntimeTarget = "linux-x64",
            Output = task.Pipeline!.Output,
            Ticket = task.Ticket
        };
    }

    /// <summary>What a node returns for a shard it could not answer for a reason of its own.</summary>
    public static CorpusResultData Fault(CorpusTaskData task, string code = CorpusResultCodes.FAULT_DISK)
    {
        return Verdict(task, CorpusShardVerdict.NodeFault, code);
    }

    /// <summary>The ledger of a run: the first round, and the later rounds when given; with the sampling rate the run asked for when a test states one.</summary>
    public static CorpusLedger Ledger(IReadOnlyList<CorpusTaskData> tasks, IEnumerable<object?> executions, (IReadOnlyList<CorpusTaskData> Tasks, IEnumerable<object?> Results)? repair = null, (IReadOnlyList<CorpusTaskData> Tasks, IEnumerable<object?> Results)? dispute = null, double? samplingRate = null)
    {
        return CorpusLedger.Build(
            VERSION,
            new CorpusRound(CorpusRoundKind.First, tasks, executions.ToList()),
            repair is { } repaired ? new CorpusRound(CorpusRoundKind.Repair, repaired.Tasks, repaired.Results.ToList()) : null,
            dispute is { } disputed ? new CorpusRound(CorpusRoundKind.Dispute, disputed.Tasks, disputed.Results.ToList()) : null,
            samplingRate);
    }

    /// <summary>Named counts in the order a node writes them: by name, ordinal.</summary>
    public static List<CorpusCountData> Counts(IEnumerable<(string Name, int Count)> counts)
    {
        return counts
            .OrderBy(count => count.Name, StringComparer.Ordinal)
            .Select(count => new CorpusCountData { Name = count.Name, Count = count.Count })
            .ToList();
    }

    #endregion
}
