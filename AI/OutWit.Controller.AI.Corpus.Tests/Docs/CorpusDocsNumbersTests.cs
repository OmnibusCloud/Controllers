using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OutWit.Controller.AI.Corpus.Adapters;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Json;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Docs;

/// <summary>
/// The documents against the code, for everything in them that the code can state itself: the
/// versions, the schema names, the bundled script, the activities, every fixed word a document
/// may carry, the limits, and the numbers of the demo kit's preflight. A statement here is built
/// from the constants and looked for in the document as written, so a constant that moves without
/// its sentence fails the build on the three platforms. (What a sentence means cannot be
/// generated; that part of "documents equal to the code" is reading.)
/// </summary>
[TestFixture]
public sealed class CorpusDocsNumbersTests
{
    #region Constants

    private const string CONTROLLER = "OutWit.Controller.AI.Corpus";
    private const string PACK_FIXTURE = CorpusFixtures.PACK;
    private const double KIT_SAMPLING = 0.25;

    private const long KIB = 1024;
    private const long MIB = 1024 * KIB;
    private const long GIB = 1024 * MIB;

    private static readonly CultureInfo EN = CultureInfo.GetCultureInfo("en-US");

    #endregion

    #region Versions And Names

    [Test]
    public void TheDocumentsNameTheVersionsOfThisBuildTest()
    {
        var controller = CorpusTestRun.VERSION;
        var model = typeof(CorpusTaskData).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        var scripts = XDocument.Parse(CorpusRepositoryFiles.ReadText("AI", $"{CONTROLLER}.Scripts", $"{CONTROLLER}.Scripts.csproj")).Descendants("Version").Single().Value;

        Assert.Multiple(() =>
        {
            Assert.That(Readme, Does.Contain($"**Status: {controller} —"));
            Assert.That(Reference, Does.Contain($"Version described: **Corpus {controller}** (Model {model}, Scripts {scripts})."));
            Assert.That(Reference, Does.Contain($"| The controller | {controller} |"));
            Assert.That(Reference, Does.Contain($"| The wire types | {model} |"));
            Assert.That(Reference, Does.Contain($"| The scripts | {scripts} |"));
            Assert.That(Reference, Does.Contain($"**What changed for an application in {controller}**"));
            Assert.That(ModelReadme, Does.Contain($"**{model}**"), "the wire types' own page names its version");
            Assert.That(KitExpected, Does.Contain($"controller {controller},"));
        });
    }

    [Test]
    public void TheDocumentsNameTheSchemasTheWritersWriteTest()
    {
        string[] current = [CorpusReportWriter.SCHEMA, CorpusIntegrityWriter.SCHEMA, CorpusDedupWriter.SCHEMA, CorpusCapabilitiesWriter.SCHEMA];

        Assert.Multiple(() =>
        {
            foreach (var schema in current)
            {
                Assert.That(Readme, Does.Contain($"`{schema}`"), schema);
                Assert.That(Reference, Does.Contain($"`{schema}` |"), $"{schema} in the table of what comes back");
            }

            // A page that has no history names no schema but the current ones.
            foreach (var (name, page) in new[] { ("README.md", Readme), ("EXPECTED.md", KitExpected), ("SCRIPT.md", KitScript), ("check.py", KitCheck) })
            {
                var named = Regex.Matches(page, "corpus-(report|integrity|dedup|capabilities)/[0-9]+").Select(match => match.Value).Distinct().ToList();
                Assert.That(named, Is.SubsetOf(current), name);
            }
        });
    }

    [Test]
    public void TheScriptInTheDocumentsIsTheBundledScriptTest()
    {
        var bundled = Script(CorpusRepositoryFiles.ReadText("AI", CONTROLLER, "Scripts", "CorpusProcess.wit"));

        Assert.Multiple(() =>
        {
            Assert.That(bundled, Does.StartWith("Job:CorpusProcess(Blob:manifest, Blob:languageModel, CorpusOptions:opts) {"));
            Assert.That(Script(Block(ReadmeLines, "Job:CorpusProcess(")), Is.EqualTo(bundled), "README.md");
            Assert.That(Script(Block(ReferenceLines, "Job:CorpusProcess(")), Is.EqualTo(bundled), "docs/capabilities.md");
        });
    }

    [Test]
    public void EveryActivityIsInTheDocumentsAndNoOtherTest()
    {
        var activities = typeof(WitControllerCorpusModule).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributesData())
            .Where(attribute => attribute.AttributeType.Name == "ActivityAttribute" && attribute.ConstructorArguments.Count > 0)
            .Select(attribute => (string)attribute.ConstructorArguments[0].Value!)
            .Order(StringComparer.Ordinal)
            .ToList();

        var inReadme = Regex.Matches(Readme, "`(Corpus\\.[A-Z][A-Za-z]+)`").Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToList();
        var inReference = Regex.Matches(Reference, "`(Corpus\\.[A-Z][A-Za-z]+)`").Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(activities, Has.Count.EqualTo(12), string.Join(", ", activities));
            Assert.That(Readme, Does.Contain($"wired into {Spelled(activities.Count)} activities"));
            Assert.That(inReadme, Is.EqualTo(activities), "README.md names every activity and none that is not one");

            // The reference also says which activity is gone, where it lists what changed.
            Assert.That(inReference.Except(["Corpus.ProcessShard"]), Is.EqualTo(activities), "docs/capabilities.md");
            foreach (var activity in activities)
            {
                Assert.That(Readme, Does.Contain($"| `{activity}` | "), $"{activity} in README's table");
                Assert.That(Reference, Does.Contain($"| `{activity}` | "), $"{activity} in the reference's table");
            }
        });
    }

    [Test]
    public void EveryFixedWordADocumentMayCarryIsInTheReferenceTest()
    {
        var verdictCodes = Enum.GetValues<CorpusShardVerdict>().Where(verdict => verdict != CorpusShardVerdict.NodeFault).SelectMany(CorpusResultCodes.CodesOf);
        var words = verdictCodes
            .Concat(CorpusResultCodes.FAULTS)
            .Concat(CorpusLedger.REJECTION_REASONS)
            .Concat(CorpusLedger.NO_RESULT_REASONS)
            .Concat(CorpusLedger.DISPUTE_OUTCOMES)
            .Concat(CorpusSampling.STATES)
            .Concat(ShardBatchRefusedException.CODES)
            .Concat(CorpusDedupExclusion.REASONS)
            .Concat(ShardDropRules.ALL.Select(rule => rule.Rule))
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(words, Has.Count.GreaterThan(70));
            foreach (var word in words)
            {
                // The reference writes the six repeated n-gram rules as the first "to" the last.
                if (Regex.IsMatch(word, "^duplicate_ngram_chars_[6-9]$"))
                    continue;

                Assert.That(Reference, Does.Contain($"`{word}`"), word);
            }

            foreach (var verdict in Enum.GetNames<CorpusShardVerdict>().Where(name => name != nameof(CorpusShardVerdict.NodeFault)))
                Assert.That(Reference, Does.Contain($"| `{verdict}` | "), verdict);
        });
    }

    #endregion

    #region Limits

    [Test]
    public void TheLimitsInTheDocumentsAreTheConstantsTest()
    {
        var slack = Fraction(ShardLimits.DECLARED_SLACK_DIVISOR);
        var slackMin = $"{ShardLimits.DECLARED_SLACK_MIN_BYTES / MIB} MiB";
        var allowance = Fraction(CorpusTrafficEstimate.RETRY_ALLOWANCE_DIVISOR);
        var manifestBytes = $"{CorpusManifestParser.MAX_BYTES / MIB} MiB";
        var shards = N(CorpusPackagePlanner.MAX_SHARDS);
        var value = $"{CorpusJsonStream.MAX_VALUE_BYTES / MIB} MiB";
        var probe = $"{ShardSizeProbe.CONCURRENCY} at a time, {(int)ShardSizeProbe.TIMEOUT.TotalSeconds} s each";
        var report = $"{CorpusDedupInput.MAX_REPORT_BYTES / GIB} GiB";
        var perShard = N(CorpusDedupInput.MAX_REPORT_BYTES / CorpusPackagePlanner.MAX_SHARDS);

        Assert.Multiple(() =>
        {
            // The reference's table of limits.
            Assert.That(Reference, Does.Contain($"| shard on the wire | {CorpusTaskPlanner.MAX_SHARD_BYTES / GIB} GiB (`MaxShardBytes` may lower it); a shard with a declared size, that size and {slack} more, {slackMin} at least (more: `Oversized`, `{CorpusResultCodes.OVERSIZED_DECLARED}`) |"));
            Assert.That(Reference, Does.Contain($"| manifest | {manifestBytes}; {shards} sources; {value} a value; the first {CorpusProblems.MAX_LISTED} problems listed ({CorpusProblems.MAX_LINE_CHARS} characters each), {CorpusTaskPlanner.MAX_LISTED_HOSTS} hosts named |"));
            Assert.That(Reference, Does.Contain($"| packages | {N(CorpusPackagePlanner.MAX_PACKAGES)} a round; {N(CorpusPackagePlanner.MAX_PACKAGE_SHARDS)} shards a package; a frame of {CorpusPackageCodec.MAX_JOB_FRAME_BYTES / KIB} KiB for what the tasks share, {CorpusPackageCodec.MAX_TASK_FRAME_BYTES / KIB} KiB a task, {CorpusPackageCodec.MAX_RESULT_FRAME_BYTES / KIB} KiB a result |"));
            Assert.That(Reference, Does.Contain($"| size probes | {ShardSizeProbe.MAX_PROBES} sources drawn at random (half without a size, half with one), {probe} |"));
            Assert.That(Reference, Does.Contain($"| traffic allowance | {allowance} of the fetches |"));
            Assert.That(Reference, Does.Contain($"a report of {report} at most ({perShard} bytes for each shard a job may have)"));
            Assert.That(Reference, Does.Contain($"`{CorpusResultCodes.OVERSIZED}`, `{CorpusResultCodes.OVERSIZED_DECLARED}`, `{CorpusResultCodes.OVERSIZED_UNPACKED}`, `{CorpusResultCodes.OVERSIZED_PRODUCT}` |"));
            Assert.That(Reference, Does.Contain($"| a document's root (a manifest, a report given to `Corpus.Dedup`) | {CorpusJsonStream.MAX_ROOT_KEYS} keys"));
            Assert.That(Reference, Does.Contain($"| a shard's product | {N(ShardProductLimits.DOCUMENTS_PER_MIB)} documents for every fetched MiB, {N(ShardProductLimits.MIN_DOCUMENTS)} at least, and twice the bytes fetched in compressed text, {ShardProductLimits.MIN_TEXT_BYTES / MIB} MiB at least"));
            Assert.That(Reference, Does.Contain($"| runs per shard | {N(PdfStageLimits.SHARD_RUNS)} PDFs of one shard"));
            Assert.That(Readme, Does.Contain($"| runs per shard | {N(PdfStageLimits.SHARD_RUNS)} |"));
            Assert.That(Readme, Does.Contain($"judged in full only up to {N(ShardQualityRules.MAX_WORDS)} words ({N(ShardQualityRules.MAX_LETTERS)} letters of a script without spaces)"));
            Assert.That(ShardProductLimits.TEXT_PER_BYTE, Is.EqualTo(2), "the documents write it as twice");

            // The same numbers where the README states them.
            Assert.That(Readme, Does.Contain($"({manifestBytes} and {shards} shards at most)"), "the split's row");
            Assert.That(Readme, Does.Contain($"| a manifest | {manifestBytes}, {shards} sources, {value} a value, read a source at a time, {CorpusJsonStream.MAX_ROOT_KEYS} keys at its root;"));
            Assert.That(Readme, Does.Contain($"| a shard on the wire, with a declared size | the size it was declared with and {slack} more, {slackMin} at least"));
            Assert.That(Readme, Does.Contain($"up to {ShardSizeProbe.MAX_PROBES} shards, {Spelled(ShardSizeProbe.CONCURRENCY)} at a time, {(int)ShardSizeProbe.TIMEOUT.TotalSeconds} s each"));
            Assert.That(Readme, Does.Contain($"| a report given to `Corpus.Dedup` | {report} ({perShard} bytes for each shard a job may have)"));
            Assert.That(Readme, Does.Contain($"A round has at most {N(CorpusPackagePlanner.MAX_PACKAGES)} packages."));
            Assert.That(Readme, Does.Contain($"({CorpusPackagePlanner.ShardsPerPackage(100_000)} for a hundred thousand shards)"));
            Assert.That(Readme, Does.Contain($"A frame is bounded ({CorpusPackageCodec.MAX_JOB_FRAME_BYTES / KIB} KiB the shared one, {CorpusPackageCodec.MAX_TASK_FRAME_BYTES / KIB} KiB a task, {CorpusPackageCodec.MAX_RESULT_FRAME_BYTES / KIB} KiB a result)"));
            Assert.That(Readme, Does.Contain($"The task packages ({N(CorpusStorageRatios.PACKAGE_BASE_BYTES)} bytes and {CorpusStorageRatios.TASK_PACKAGE_BYTES_PER_SHARD} a shard) and the result packages of every execution ({N(CorpusStorageRatios.PACKAGE_BASE_BYTES)} bytes and {CorpusStorageRatios.RESULT_PACKAGE_BYTES_PER_SHARD} a shard;"));
            Assert.That(Reference, Does.Contain($"The task packages ({N(CorpusStorageRatios.PACKAGE_BASE_BYTES)} bytes and {CorpusStorageRatios.TASK_PACKAGE_BYTES_PER_SHARD} a shard) and the result packages of every execution ({N(CorpusStorageRatios.PACKAGE_BASE_BYTES)} bytes and {CorpusStorageRatios.RESULT_PACKAGE_BYTES_PER_SHARD} a shard) are counted"));

            // What a round opens of a package's answers: one more than it asked for.
            Assert.That(Readme, Does.Contain($"({Spelled(CorpusLedger.FIRST_ROUND_COPIES + 1)} in the first round, {Spelled(CorpusLedger.REPAIR_COPIES + 1)} in the repair round, {Spelled(CorpusLedger.DISPUTE_COPIES + 1)} in the dispute round)"));
        });
    }

    [Test]
    public void TheStorageTableInTheDocumentsIsTheMeasuredTableTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var row in CorpusStorageRatios.TABLE)
            {
                var line = $"| {(row.Format == CorpusSourceFormat.Wet ? "WET" : "WARC")} | {(row.CleanLines ? "on" : "off")} | {(row.FilterQuality ? "on" : "off")} | {row.TextPerByte.ToString("0.00", CultureInfo.InvariantCulture)} | {row.DocumentsPerMib.ToString("0", CultureInfo.InvariantCulture)} |";
                Assert.That(Readme, Does.Contain(line), "README.md");
                Assert.That(Reference, Does.Contain(line), "docs/capabilities.md");
            }
        });
    }

    #endregion

    #region Demo Kit

    [Test]
    public void TheKitsPreflightNumbersAreWhatThePreflightGivesTest()
    {
        // The kit's own manifest and the reference pack through the step Split and Preflight
        // share, at the sampling rate the kit's script runs with. The manifest declares every
        // size, so nothing here depends on an origin; its line ends are read as line feeds, as
        // the repository holds them.
        var manifestText = CorpusRepositoryFiles.ReadText("AI", $"{CONTROLLER}.Live", "demo", "01-common-crawl", "manifest.json");
        var manifest = CorpusManifestParser.Parse(manifestText);
        var pack = CorpusFixtures.PathOf(PACK_FIXTURE);
        var packBytes = new FileInfo(pack).Length;
        var facts = CorpusLanguageModelFacts.Load(pack);
        var options = new CorpusOptionsData { SamplingRate = KIT_SAMPLING };
        var inputs = Encoding.UTF8.GetByteCount(manifestText) + packBytes;

        var plan = CorpusTaskPlanner.Plan(manifest, options, Guid.NewGuid(), ShardLanguageModels.Hash(pack), languageModel: facts);
        var sizes = ShardSizeProbe.Unasked(manifest.Sources);
        var estimates = CorpusPlanning.Estimate(new CorpusPlanned(plan, manifest, options, inputs, facts, packBytes), sizes);
        var traffic = estimates.Traffic;
        var storage = estimates.Storage;

        var fullPipeline = plan.Pipeline.Clone();
        fullPipeline.Output = CorpusOutputMode.Full;
        var full = CorpusStorageEstimator.Estimate(manifest.Sources, fullPipeline, KIT_SAMPLING, inputs, CorpusTaskPlanner.ShardCap(options), facts.MeasuredThresholds);
        var largest = manifest.Sources.Max(source => source.SizeBytes);

        TestContext.Out.WriteLine(traffic.Describe());
        TestContext.Out.WriteLine(traffic.DescribeCeiling(sizes.Unsized, CorpusTaskPlanner.ShardCap(options)));
        TestContext.Out.WriteLine(traffic.DescribeRounds());
        TestContext.Out.WriteLine(CorpusStorageCheck.Describe(storage, CorpusStorageLimit.NONE));
        TestContext.Out.WriteLine($"full: {CorpusStorageCheck.Bytes(full.TotalBytes)}; documents {storage.DocumentBytes}, packages {storage.PackageBytes}, inputs {storage.InputBytes}; disk {ShardDisk.Needed(ShardLimits.DeclaredCap(largest))}");

        Assert.Multiple(() =>
        {
            Assert.That(plan.ErrorCount, Is.Zero);
            Assert.That(manifest.Sources, Has.Count.EqualTo(24));
            Assert.That(sizes.Unsized, Is.Zero, "the kit declares every size");

            // The expected result page: the lines as the preflight writes them, and the numbers.
            Assert.That(KitExpected, Does.Contain($"`{traffic.Describe()}`"), "the traffic line");
            Assert.That(KitExpected, Does.Contain($"`{traffic.DescribeRounds()}`"), "the line on what the estimate leaves out");
            Assert.That(KitExpected, Does.Contain($"{N(traffic.CeilingBytes)} bytes"), "the ceiling");
            Assert.That(KitExpected, Does.Contain($"| Traffic at `--sampling 0.25` | {N(traffic.TotalBytes)} bytes: fetches {N(traffic.FetchBytes)} (every shard once, {N(traffic.OnceBytes)}, the manifest's sizes, and the second fetch of the {traffic.SampledShards} largest shards, sampled for integrity, {N(traffic.CopyBytes)}), uploads {N(traffic.UploadBytes)}"));
            Assert.That(KitExpected, Does.Contain($"an allowance of {N(traffic.RetryBytes)}"));
            Assert.That(KitExpected, Does.Contain($"| Server storage | {N(storage.TotalBytes)} bytes at `--sampling 0.25`: the report and the integrity document ({N(storage.DocumentBytes)}), the task and result packages ({N(storage.PackageBytes)}), the manifest and the pack ({N(storage.InputBytes)}). The same run with `\"output\": \"full\"`: {N(full.TotalBytes)} bytes"));
            Assert.That(KitExpected, Does.Contain($"| Node disk | {N(ShardDisk.Needed(ShardLimits.DeclaredCap(largest)))} bytes free for each shard a node holds (the largest shard, {N(largest)} bytes, at the most a node fetches it as: {N(ShardLimits.DeclaredCap(largest))} bytes"));

            // The script page and the reference repeat the headline numbers.
            Assert.That(KitScript, Does.Contain($"{N(traffic.TotalBytes)} bytes"));
            Assert.That(KitScript, Does.Contain(N(traffic.FetchBytes)));
            Assert.That(KitScript, Does.Contain(N(traffic.CeilingBytes)));
            Assert.That(KitScript, Does.Contain(N(ShardDisk.Needed(ShardLimits.DeclaredCap(largest)))));
            Assert.That(Reference, Does.Contain($"| Fetches at sampling 0.25 | {N(traffic.FetchBytes)} bytes: every file once ({N(traffic.OnceBytes)}) and the six largest a second time ({N(traffic.CopyBytes)}); the nodes accept {N(traffic.CeilingBytes)} at the most |"));
            Assert.That(Reference, Does.Contain($"| Traffic estimate at sampling 0.25 | {N(traffic.TotalBytes)} bytes: the fetches, uploads of {N(traffic.UploadBytes)} (the result packages: nothing else leaves a node) and an allowance of {N(traffic.RetryBytes)};"));
            Assert.That(Reference, Does.Contain($"({N(packBytes)} bytes)"));
            Assert.That(Reference, Does.Contain($"| Server storage | {N(storage.TotalBytes)} bytes at sampling 0.25 ({Rounded(full.TotalBytes)} for the same run in full) |"));
        });
    }

    #endregion

    #region Tools

    /// <summary>A fenced block of a page: the one that starts with a given line.</summary>
    private static string Block(string page, string firstLine)
    {
        var start = page.IndexOf("```\n" + firstLine, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"a block that starts with '{firstLine}'");

        start += 4;
        var end = page.IndexOf("\n```", start, StringComparison.Ordinal);
        return page[start..end];
    }

    /// <summary>A text with every run of white space as one space.</summary>
    private static string Flat(string text)
    {
        return Regex.Replace(text, @"\s+", " ");
    }

    /// <summary>A script as its statements: comments out, every run of white space one space.</summary>
    private static string Script(string text)
    {
        return Regex.Replace(Regex.Replace(text, "~[^~]*~", " "), "\\s+", " ").Trim();
    }

    private static string N(long value)
    {
        return value.ToString("N0", EN);
    }

    /// <summary>A size as the storage line rounds it: "1.15 GiB".</summary>
    private static string Rounded(long bytes)
    {
        var text = CorpusStorageCheck.Bytes(bytes);
        return text[(text.IndexOf('(') + 1)..^1];
    }

    /// <summary>A share as the documents spell it.</summary>
    private static string Fraction(int divisor)
    {
        return divisor switch
        {
            8 => "an eighth",
            10 => "a tenth",
            _ => throw new InvalidOperationException($"The documents have no word for 1/{divisor}: write it, and its case here.")
        };
    }

    /// <summary>A small count as the documents spell it.</summary>
    private static string Spelled(int count)
    {
        return count switch
        {
            2 => "two",
            3 => "three",
            4 => "four",
            11 => "eleven",
            12 => "twelve",
            _ => throw new InvalidOperationException($"The documents have no word for {count}: write it, and its case here.")
        };
    }

    #endregion

    #region Properties

    /// <summary>The controller's README, every run of white space as one space (a paragraph may be wrapped anew without a test noticing).</summary>
    private static string Readme => Flat(ReadmeLines);

    /// <summary>The README as it is written, line by line: for its fenced blocks.</summary>
    private static string ReadmeLines => CorpusRepositoryFiles.ReadText("AI", CONTROLLER, "README.md");

    /// <summary>The reference (docs/capabilities.md), every run of white space as one space.</summary>
    private static string Reference => Flat(ReferenceLines);

    /// <summary>The reference as it is written, line by line: for its fenced blocks.</summary>
    private static string ReferenceLines => CorpusRepositoryFiles.ReadText("AI", CONTROLLER, "docs", "capabilities.md");

    private static string ModelReadme => Flat(CorpusRepositoryFiles.ReadText("AI", $"{CONTROLLER}.Model", "README.md"));

    private static string KitExpected => Flat(CorpusRepositoryFiles.ReadText("AI", $"{CONTROLLER}.Live", "demo", "01-common-crawl", "EXPECTED.md"));

    private static string KitScript => Flat(CorpusRepositoryFiles.ReadText("AI", $"{CONTROLLER}.Live", "demo", "01-common-crawl", "SCRIPT.md"));

    private static string KitCheck => Flat(CorpusRepositoryFiles.ReadText("AI", $"{CONTROLLER}.Live", "demo", "01-common-crawl", "check.py"));

    #endregion
}
