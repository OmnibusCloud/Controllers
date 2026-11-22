using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Scripts;

/// <summary>
/// Anonymity (rule R-5, audit findings M-1 and L-4): everything a lab can fetch from a run is
/// scanned for what only a node or the server's operator knows. The runs are the unkind ones:
/// through the engine rig, a manifest of a good shard beside garbage, a refused request, a
/// missing file, a wrong hash and a shard of hostile pages, with one of the three nodes lying and
/// writing its own paths and names into every field it can reach; then a run that fails before a
/// node is asked. The documents are the report, the integrity document, the dedup document, the
/// capabilities, the diagnostics, the preflight and the text of a failed job.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed partial class CorpusLeakA5Tests : CorpusScriptTestBase
{
    #region Constants

    private const string WET_FIXTURE = CorpusFixtures.WET;
    private const string WARC_FIXTURE = CorpusFixtures.WARC;
    private const string MODEL_FIXTURE = CorpusFixtures.PACK;

    /// <summary>What the lying node writes wherever a result has room for text.</summary>
    private const string PLANTED = "LEAKED-BY-A-NODE";

    #endregion

    #region Fields

    private string m_hostile = null!;
    private CorpusTestOrigin m_hostileOrigin = null!;
    private Guid m_modelBlob;
    private readonly Dictionary<string, string> m_documents = new(StringComparer.Ordinal);
    private readonly List<string> m_tickets = [];

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_hostile = Path.Combine(Path.GetTempPath(), $"witai_corpus_a5_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_hostile);
        await File.WriteAllBytesAsync(Path.Combine(m_hostile, "hostile.warc.gz"), HostileShard());
        m_hostileOrigin = new CorpusTestOrigin(m_hostile);
        m_modelBlob = await UploadFixtureAsync(MODEL_FIXTURE);

        await RunHostileAsync();
        await RunFailedAsync();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        NodesManager.Tamper = null;
        m_hostileOrigin?.Dispose();
        CorpusTestFolders.Delete(m_hostile);
    }

    #endregion

    #region Tests

    [Test]
    public void A5TheRunsWereUnkindTest()
    {
        // The scan below means something only if the documents hold what went wrong.
        using var report = JsonDocument.Parse(m_documents["report"]);
        using var integrity = JsonDocument.Parse(m_documents["integrity"]);
        var verdicts = report.RootElement.GetProperty("shards").EnumerateArray().Select(shard => $"{shard.GetProperty("verdict").GetString()}:{shard.GetProperty("error").GetString()}").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(verdicts, Is.EqualTo(new[] { "Completed:", "Completed:", "Completed:", "Malformed:not_warc", "FetchFailed:http_403", "FetchFailed:http_404", "FetchFailed:hash_mismatch" }));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("rejected_results").GetInt32(), Is.GreaterThan(0), "the lying node's results were dropped");
            Assert.That(report.RootElement.GetProperty("shards")[2].GetProperty("stats").GetProperty("drops").EnumerateObject().Select(drop => drop.Name),
                Is.SupersetOf(new[] { "html_refused", "html_too_complex", "unsupported_charset" }), "the hostile pages dropped, each by its rule");
            Assert.That(m_documents["failed job"], Has.Length.GreaterThan(20), "the failed job says something");
            Assert.That(m_documents["batch refusals"].Split('\n'), Has.All.Matches<string>(line => line.Length > 20), "each refusal has its sentence");
            Assert.That(m_documents["refused job"], Does.Contain("cannot run the PDF stage"), "the job no node took says why, in the node's fixed sentence");
            Assert.That(m_documents.Keys, Is.SupersetOf(new[] { "report", "integrity", "dedup", "capabilities", "diagnostics", "preflight", "failed job", "refused job", "batch refusals" }));
        });
    }

    [Test]
    public void A5NoDocumentCarriesAPathAnAccountOrAMachineTest()
    {
        var leaks = new List<string>();
        foreach (var (name, text) in m_documents)
        {
            foreach (var (what, value) in Secrets())
            {
                if (text.Contains(value, StringComparison.OrdinalIgnoreCase) || text.Contains(JsonEscaped(value), StringComparison.OrdinalIgnoreCase))
                    leaks.Add($"{name}: {what}");
            }

            foreach (var (what, pattern) in new[] { ("a drive path", DrivePath()), ("a path of a home or temp folder", UnixPath()), ("an exception's text", ExceptionText()) })
            {
                var match = pattern.Match(text);
                if (match.Success)
                    leaks.Add($"{name}: {what} ('{Around(text, match.Index)}')");
            }
        }

        Assert.That(leaks, Is.Empty);
    }

    [Test]
    public void A5NoDocumentCarriesANodeOrATicketTest()
    {
        var nodes = NodesManager.CompatibleNodes.Select(node => node.NodeId).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(m_tickets, Has.Count.GreaterThanOrEqualTo(7), "a ticket for every shard, and for every shard of a later round");
            foreach (var (name, text) in m_documents)
            {
                foreach (var node in nodes)
                    Assert.That(text, Does.Not.Contain(node.ToString("D")).And.Not.Contain(node.ToString("N")), $"{name}: a node's id");

                foreach (var ticket in m_tickets)
                    Assert.That(text, Does.Not.Contain(ticket), $"{name}: a ticket");

                Assert.That(text, Does.Not.Contain(PLANTED), $"{name}: what a node wrote into its result");
            }
        });
    }

    [Test]
    public void A5TheDiagnosticsCountTheOperatorsHostsAndNameNoneTest()
    {
        // The test machine's owner lists 127.0.0.1 (CorpusTestEnvironment): the server's own list
        // went to whoever ran the diagnostics (L-4).
        using var diagnostics = JsonDocument.Parse(m_documents["diagnostics"]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.RootElement.GetProperty("MachineOriginCount").GetInt32(), Is.EqualTo(1));
            Assert.That(diagnostics.RootElement.GetProperty("MachineOrigins").GetArrayLength(), Is.Zero);
            Assert.That(m_documents["diagnostics"], Does.Not.Contain("127.0.0.1"));
            Assert.That(m_documents["capabilities"], Does.Not.Contain("127.0.0.1"));
        });
    }

    #endregion

    #region Tools

    private async Task RunHostileAsync()
    {
        var wet = Origin.ShardUrl(WET_FIXTURE);
        var manifest = $$"""
            {"pipeline":{"version":"a5"},"allowed_origins":["{{Origin.Host}}"],"sources":[
              "{{wet}}",
              "{{Origin.ShardUrl(WARC_FIXTURE)}}",
              "{{m_hostileOrigin.ShardUrl("hostile.warc.gz")}}",
              {"url":"{{Origin.BaseUrl}}garbage","format":"wet"},
              {"url":"{{Origin.BaseUrl}}forbidden","format":"wet"},
              "{{m_hostileOrigin.ShardUrl("missing.warc.wet.gz")}}",
              {"url":"{{wet}}?again","sha256":"{{new string('0', 64)}}"}
            ]}
            """;
        var manifestBlob = await UploadAsync(manifest, "a5.json");

        // The liar bends every result it returns, the way a node before this release wrote them
        // and worse: its paths, its account and its machine wherever a result has a string.
        var liar = NodesManager.CompatibleNodes[0].NodeId;
        var secret = $"Access to the path '{Path.Combine(Path.GetTempPath(), Environment.UserName, "text.jsonl.zst")}' is denied. {PLANTED} {Environment.MachineName}";
        NodesManager.Tamper = (nodeId, result) =>
        {
            if (nodeId != liar)
                return;

            switch (result.TaskIndex % 4)
            {
                case 0:
                    result.Error = secret;
                    break;
                case 1:
                    result.NodeName = $"{PLANTED} {Environment.MachineName}";
                    result.NodeId = liar;
                    break;
                case 2:
                    result.RuntimeTarget = secret;
                    break;
                default:
                    result.SourceUrl = $"file:///{Path.GetTempPath()}{PLANTED}";
                    result.ControllerVersion = $"{PLANTED} {Environment.UserName}";
                    break;
            }
        };

        try
        {
            var process = Engine.Compile(LoadScript("CorpusProcess.wit"));
            var status = await Engine.ScheduleAndWaitAsync(process, manifestBlob, m_modelBlob, new CorpusOptionsData { SamplingRate = 1.0 });
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

            var reportBlob = (Guid)process.Variables["report"].Value!;
            m_documents["report"] = Encoding.UTF8.GetString(await ReadBytesAsync(reportBlob));
            m_documents["integrity"] = Encoding.UTF8.GetString(await ReadBytesAsync((Guid)process.Variables["integrity"].Value!));
            m_documents["job status"] = Describe(status);
            foreach (var name in new[] { "tasks", "repairs", "disputes" })
            {
                // The tickets of every round: the packages' own and the ones of the shards inside them.
                if (process.Variables[name].Value is IEnumerable packages)
                {
                    m_tickets.AddRange(packages.OfType<CorpusTaskPackageData>().Select(package => package.Ticket));
                    m_tickets.AddRange(TasksOf(packages).Select(task => task.Ticket));
                }
            }

            var dedup = Engine.Compile(LoadScript("CorpusDedup.wit"));
            var dedupStatus = await Engine.ScheduleAndWaitAsync(dedup, reportBlob, new CorpusOptionsData());
            Assert.That(dedupStatus.Result, Is.EqualTo(WitProcessingResult.Completed), dedupStatus.ToString());
            m_documents["dedup"] = Encoding.UTF8.GetString(await ReadBytesAsync((Guid)dedup.Variables["dedup"].Value!));
        }
        finally
        {
            NodesManager.Tamper = null;
        }

        var diagnostics = Engine.Compile(LoadScript("CorpusRuntimeDiagnostics.wit"));
        var diagnosticsStatus = await Engine.ScheduleAndWaitAsync(diagnostics);
        Assert.That(diagnosticsStatus.Result, Is.EqualTo(WitProcessingResult.Completed), diagnosticsStatus.ToString());
        m_documents["diagnostics"] = JsonSerializer.Serialize((CorpusRuntimeDiagnosticsData)diagnostics.Variables["result"].Value!);
        m_documents["capabilities"] = Encoding.UTF8.GetString(await ReadBytesAsync((Guid)diagnostics.Variables["capabilities"].Value!));

        var preflight = Engine.Compile(LoadScript("CorpusPreflight.wit"));
        await Engine.ScheduleAndWaitAsync(preflight, manifestBlob, m_modelBlob, new CorpusOptionsData { SamplingRate = 1.0 });
        m_documents["preflight"] = JsonSerializer.Serialize((CorpusPreflightData)preflight.Variables["result"].Value!);

        // What a node says when it refuses a batch: the platform shows the last one when no node takes the work.
        m_documents["batch refusals"] = string.Join('\n', new[]
        {
            ShardBatchGuard.Refusal(new CorpusTaskData { RequiredControllerVersion = "9.9.9" }, "0.4.0", () => null, () => true),
            ShardBatchGuard.Refusal(new CorpusTaskData { RequiredControllerVersion = $"{Path.GetTempPath()}{PLANTED}" }, "0.4.0", () => null, () => true),
            ShardBatchGuard.Refusal(Warc(), "0.4.0", () => $"the module at {Path.GetTempPath()} is missing for {Environment.UserName}", () => true),
            ShardBatchGuard.Refusal(Warc(), "0.4.0", () => null, () => false),
            ShardBatchGuard.Check(Warc(), "0.4.0", () => throw new IOException($"Access to the path '{Path.GetTempPath()}{PLANTED}' is denied for {Environment.UserName}."), () => true)
        }.Select(refusal => refusal == null ? "" : $"{refusal.Code}: {refusal.Message}"));
    }

    private async Task RunFailedAsync()
    {
        // A job that never reaches a node: a source on a host the manifest does not allow.
        var manifest = $$"""{"pipeline":{"version":"a5"},"allowed_origins":["data.commoncrawl.org"],"sources":["{{Origin.ShardUrl(WET_FIXTURE)}}"]}""";
        var process = Engine.Compile(LoadScript("CorpusProcess.wit"));
        var status = await Engine.ScheduleAndWaitAsync(process, await UploadAsync(manifest, "a5-failed.json"), m_modelBlob, new CorpusOptionsData());
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        m_documents["failed job"] = Describe(status);

        // A job no node takes: every node is without its PDF sandbox, for a reason that names
        // its own folders and account. What the lab reads is the last node's refusal.
        PdfSandboxHost.Override(null, $"no module in {Path.GetTempPath()} for {Environment.UserName} {PLANTED}");
        try
        {
            var refused = Engine.Compile(LoadScript("CorpusProcess.wit"));
            var refusal = await Engine.ScheduleAndWaitAsync(refused, await UploadAsync(Manifest(WARC_FIXTURE), "a5-refused.json"), m_modelBlob, new CorpusOptionsData());
            Assert.That(refusal.Result, Is.EqualTo(WitProcessingResult.Failed));
            m_documents["refused job"] = Describe(refusal);
        }
        finally
        {
            PdfSandboxHost.ClearOverride();
            ShardBatchGuard.Clear();
        }
    }

    private static CorpusTaskData Warc()
    {
        return new CorpusTaskData
        {
            RequiredControllerVersion = "0.4.0",
            Source = new CorpusSourceData { Url = "https://data.commoncrawl.org/x.warc.gz", Format = CorpusSourceFormat.Warc, SizeBytes = 1 << 20 },
            Pipeline = new CorpusPipelineData()
        };
    }

    /// <summary>A WARC shard of pages written to hurt: an unsupported charset, nesting hidden from the pre-scan two ways, and prose between them.</summary>
    private static byte[] HostileShard()
    {
        var prose = "<html><body><p>" + WarcSynth.Paragraphs(new Random(5), 400) + "</p></body></html>";
        var utf7Body = Encoding.ASCII.GetBytes(prose);
        var utf7Head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-7\r\nContent-Length: {utf7Body.Length}\r\n\r\n");

        return WarcSynth.Shard(
        [
            WarcSynth.HtmlResponseRecord("https://a.example/good", prose),
            WarcSynth.Record("response", "https://a.example/utf7", "application/http; msgtype=response", [.. utf7Head, .. utf7Body]),
            WarcSynth.HtmlResponseRecord("https://a.example/comment", "<html><body><!-->" + string.Concat(Enumerable.Repeat("<div>", 40_000)) + "text-->"),
            WarcSynth.HtmlResponseRecord("https://a.example/end-tags", "<html><body>" + string.Concat(Enumerable.Repeat("<div></x>", 29_000)) + "text"),
            WarcSynth.HtmlResponseRecord("https://a.example/good-again", prose)
        ]);
    }

    /// <summary>What only this machine knows: its account, its name, its folders.</summary>
    private IEnumerable<(string What, string Value)> Secrets()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        (string What, string Value)[] candidates =
        [
            ("the account", Environment.UserName),
            ("the machine", Environment.MachineName),
            ("the temp folder", Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ("the profile folder", profile),
            ("the folder of the binaries", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ("the repository's folder", SolutionRoot),
            ("the hostile shards' folder", m_hostile)
        ];

        // A name of one or two characters says nothing by being found.
        return candidates.Where(candidate => candidate.Value is { Length: >= 3 });
    }

    private static string JsonEscaped(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    private static string Around(string text, int index)
    {
        var from = Math.Max(0, index - 30);
        return text.Substring(from, Math.Min(text.Length - from, 90)).ReplaceLineEndings(" ");
    }

    /// <summary>A drive letter, a colon and a separator (C:\, c:/), once or JSON-escaped; a URL's scheme is longer than a letter.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:(\\\\?|/)[A-Za-z0-9_.$~-]", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePath();

    /// <summary>The roots a path of a Linux or macOS node starts with.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9.:/])/(home|Users|tmp|var|private|root|usr|etc|mnt|opt)/", RegexOptions.CultureInvariant)]
    private static partial Regex UnixPath();

    /// <summary>What an exception's text looks like: its type's name, a stack frame, the file system's own words.</summary>
    [GeneratedRegex(@"[A-Za-z]Exception\b|\n\s+at [A-Za-z]|is denied|HResult|Could not find|being used by another process", RegexOptions.CultureInvariant)]
    private static partial Regex ExceptionText();

    #endregion
}
