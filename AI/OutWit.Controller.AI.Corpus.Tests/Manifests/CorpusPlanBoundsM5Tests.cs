using System.Text;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Manifests;

/// <summary>
/// Audit finding M-5 on the server's side of a plan: nothing the server answers with, and nothing
/// it hands a script, grows with the manifest. A manifest wrong in every line gets a bounded list
/// of problems and a count; a manifest of more shards than a job takes is refused by name; the
/// tasks of a plan are a view of the manifest with one pipeline for all of them; and the packages
/// a round's tasks travel in are a bounded number, whatever the manifest's length.
/// </summary>
[TestFixture]
public sealed class CorpusPlanBoundsM5Tests
{
    #region Problems

    [Test]
    public void M5AManifestWrongInEveryLineGetsABoundedAnswerTest()
    {
        // 20,000 sources, each on a host the allowlist does not name, each with a long path.
        var sources = string.Join(",", Enumerable.Range(0, 20_000).Select(index => $"\"https://evil-{index}.example/{new string('p', 300)}.warc.gz\""));
        var manifest = CorpusManifestParser.Parse($$"""{"pipeline":{"version":"v","identify_language":false},"allowed_origins":["data.commoncrawl.org"],"sources":[{{sources}}]}""");

        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.Empty, "");
        var answer = plan.Errors.Concat(plan.Notes).Concat(plan.RefusedOrigins).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(plan.ErrorCount, Is.EqualTo(20_000), "every problem is counted");
            Assert.That(plan.Errors, Has.Count.EqualTo(CorpusProblems.MAX_LISTED + 1), "the first by line, and one line for the rest");
            Assert.That(plan.Errors[^1], Is.EqualTo($"{20_000 - CorpusProblems.MAX_LISTED} more problem(s) of the manifest are not listed here."));
            Assert.That(plan.Errors[0], Does.StartWith("sources[0]: 'evil-0.example' is not an allowed origin."));
            Assert.That(plan.RefusedOrigins, Has.Count.EqualTo(CorpusTaskPlanner.MAX_LISTED_HOSTS), "a bounded number of host names");
            Assert.That(plan.Tasks, Is.Empty);
            Assert.That(answer.Sum(line => line.Length), Is.LessThan(64 * 1024), "the whole answer is a few kilobytes for a manifest of megabytes");
        });
    }

    [Test]
    public void M5AProblemQuotesTheManifestWithinALengthTest()
    {
        // Sources whose URL is a megabyte long, with no extension to tell a format from: the
        // line that quotes one is not a megabyte.
        var url = $"https://data.commoncrawl.org/{new string('x', 1 << 20)}.bin";
        var manifest = CorpusManifestParser.Parse($$"""{"pipeline":{"version":"v","identify_language":false},"allowed_origins":["data.commoncrawl.org"],"sources":["{{url}}","{{url}}"]}""");

        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.Empty, "");

        Assert.Multiple(() =>
        {
            Assert.That(manifest.ErrorCount, Is.EqualTo(2));
            Assert.That(manifest.Errors[0], Does.StartWith("sources[0]: cannot tell the format of 'https://data.commoncrawl.org/xxxx"));
            Assert.That(manifest.Errors.Select(line => line.Length), Is.All.LessThanOrEqualTo(CorpusProblems.MAX_LINE_CHARS + 3));
            Assert.That(plan.Errors.Select(line => line.Length), Is.All.LessThanOrEqualTo(CorpusProblems.MAX_LINE_CHARS + 3));
        });
    }

    [Test]
    public void M5DuplicateListingsAndOutsideHostsAreNamedWithinABoundTest()
    {
        var sources = Enumerable.Range(0, 500).Select(index => $"https://lab-{index % 100}.internal.example/shard-{index % 50}.warc.wet.gz").ToList();
        var manifest = new CorpusManifest(
            new CorpusPipelineData { Version = "v", IdentifyLanguage = false },
            Enumerable.Range(0, 100).Select(index => $"lab-{index}.internal.example").ToList(),
            sources.Select(url => new CorpusSourceData { Url = url, Format = CorpusSourceFormat.Wet }).ToList(),
            []);

        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.Empty, "", new ShardOriginPolicy());

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty, "a host outside the public origins is each machine's own decision");
            Assert.That(plan.Tasks, Has.Count.EqualTo(500));
            Assert.That(plan.OutsideOrigins, Has.Count.EqualTo(CorpusTaskPlanner.MAX_LISTED_HOSTS));
            Assert.That(plan.Notes, Has.Some.Contains(" and other hosts: not a public origin"));
            Assert.That(plan.Notes.Count(note => note.Contains("is listed more than once")), Is.EqualTo(CorpusTaskPlanner.MAX_LISTED_DUPLICATES));
            Assert.That(plan.Notes, Has.Some.EndsWith("more duplicate listing(s) of the manifest are not listed here."));
            Assert.That(plan.Notes.Sum(note => note.Length), Is.LessThan(16 * 1024));
        });
    }

    #endregion

    #region The manifest

    [Test]
    public void M5AManifestOfMoreShardsThanAJobTakesIsRefusedByNameTest()
    {
        // The bound of a job is 1,048,576 shards; the same rule at a bound of 1,000, read from a
        // manifest of 1,500 that exists only as it is read.
        var json = $$"""{"pipeline":{"version":"v"},"allowed_origins":["data.commoncrawl.org"],"sources":[{{string.Join(",", Enumerable.Range(0, 1500).Select(index => $"\"https://data.commoncrawl.org/{index}.warc.wet.gz\""))}}]}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var manifest = CorpusManifestParser.Parse(stream, maxShards: 1000);

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Sources, Has.Count.EqualTo(1000), "nothing past the bound is kept");
            Assert.That(manifest.Errors, Has.Some.EqualTo("sources: the manifest names 1500 shards; a job takes at most 1000. Split the manifest into several jobs."));
            Assert.That(CorpusTaskPlanner.Plan(manifest, null, Guid.Empty, "").Tasks, Is.Empty, "a manifest past the bound makes no job");
            Assert.That(CorpusPackagePlanner.MAX_SHARDS, Is.EqualTo(1 << 20));
        });
    }

    [Test]
    public void M5AManifestReadFromAStreamIsTheManifestReadFromItsTextTest()
    {
        var json = """
            // a manifest with everything in it
            {
              "sources": [
                "https://data.commoncrawl.org/a.warc.wet.gz",
                { "url": "https://data.commoncrawl.org/b.warc.gz", "size": 1000, "sha256": "ABCDEFabcdef0123456789abcdef0123456789abcdef0123456789abcdef0123" },
                { "url": "https://data.commoncrawl.org/c.bin", "format": "wet" },
                17,
                { "url": "https://data.commoncrawl.org/d.txt" },
              ],
              "allowed_origins": ["data.commoncrawl.org"],
              "surprise": 1,
              "pipeline": { "version": "cc-text-v1", "sign": false, "quality": { "min_words": 20, "min_word": 1 }, "minhash": { "hashes": 64, "bands": 8 } },
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var streamed = CorpusManifestParser.Parse(stream);
        var whole = CorpusManifestParser.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(streamed.Errors, Is.EqualTo(whole.Errors));
            Assert.That(streamed.Errors, Is.EqualTo(new[]
            {
                "manifest: unknown key 'surprise'.",
                "pipeline.quality: unknown key 'min_word'.",
                "sources[3]: must be a URL string or an object.",
                "sources[4]: cannot tell the format of 'https://data.commoncrawl.org/d.txt' from its extension; give 'format' as 'wet' or 'warc'."
            }), "the problems in one order whatever the order of the document's keys: its keys, its pipeline, its origins, its sources");
            Assert.That(streamed.Sources.Select(source => (source.Url, source.Format, source.SizeBytes)), Is.EqualTo(new[]
            {
                ("https://data.commoncrawl.org/a.warc.wet.gz", CorpusSourceFormat.Wet, 0L),
                ("https://data.commoncrawl.org/b.warc.gz", CorpusSourceFormat.Warc, 1000L),
                ("https://data.commoncrawl.org/c.bin", CorpusSourceFormat.Wet, 0L)
            }));
            Assert.That(streamed.Sources[1].Sha256, Is.EqualTo("abcdefabcdef0123456789abcdef0123456789abcdef0123456789abcdef0123"));
            Assert.That(streamed.Pipeline.Is(whole.Pipeline), Is.True);
            Assert.That(streamed.Pipeline.Quality!.MinWords, Is.EqualTo(20));
            Assert.That(streamed.AllowedOrigins, Is.EqualTo(new[] { "data.commoncrawl.org" }));
        });
    }

    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":[],"sources":["https://h/a.warc.gz"]}""", "not valid JSON (The key 'sources' appears more than once.)")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":"https://h/a.warc.gz"}""", "sources: must be an array.")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":["https://h/a.warc.gz"]} trailing""", "not valid JSON")]
    public void M5WhatAStreamedManifestRefusesTest(string json, string problem)
    {
        Assert.That(CorpusManifestParser.Parse(json).Errors, Has.Some.Contains(problem));
    }

    #endregion

    #region The tasks and their packages

    [Test]
    public void M5TheTasksOfAPlanAreAViewNotAListTest()
    {
        var manifest = new CorpusManifest(
            new CorpusPipelineData { Version = "v", IdentifyLanguage = false, TargetLanguages = [] },
            ["data.commoncrawl.org"],
            Enumerable.Range(0, 50_000).Select(index => new CorpusSourceData { Url = $"https://data.commoncrawl.org/{index}.warc.wet.gz", Format = CorpusSourceFormat.Wet, SizeBytes = 1000 + index }).ToList(),
            []);

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.Empty, "");
        var held = GC.GetTotalMemory(forceFullCollection: true) - before;
        var issued = CorpusTaskPlanner.Issue(plan, "0.5.0").Take(3).ToList();

        // The figure is printed, not asserted: the collector's own answer moves by megabytes from
        // one platform to the next. What is asserted is the thing itself: a task is made when it
        // is asked for, and the plan keeps none.
        TestContext.Out.WriteLine($"50,000 shards: the plan holds about {held} bytes beside the manifest (at the audit's 509 bytes a task, 25 MB)");

        var last = plan.Tasks[49_999];
        var lastAgain = plan.Tasks[49_999];
        var firstOnes = plan.Tasks.Take(3).ToList();
        var firstOnesAgain = plan.Tasks.Take(3).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Has.Count.EqualTo(50_000));
            Assert.That(plan.Tasks, Is.Not.InstanceOf<List<CorpusTaskData>>().And.Not.InstanceOf<CorpusTaskData[]>(), "a view over the manifest, not a list of tasks");
            Assert.That(lastAgain, Is.Not.SameAs(last), "a task is made when it is asked for, and is not kept");
            Assert.That(firstOnes.Zip(firstOnesAgain, ReferenceEquals), Is.All.False, "nor when the tasks are read in order");
            Assert.That(plan.Tasks[49_999].TaskIndex, Is.EqualTo(49_999));
            Assert.That(plan.Tasks[49_999].Source, Is.SameAs(manifest.Sources[49_999]), "a task is the manifest's own entry");
            Assert.That(plan.Tasks[7].Pipeline, Is.SameAs(plan.Pipeline));
            Assert.That(plan.Tasks[7].DeclaredBytes, Is.EqualTo(1007), "what the manifest declares is what the task is held to");
            Assert.That(issued.Select(task => task.Ticket), Is.Unique);
            Assert.That(issued.Select(task => task.RequiredControllerVersion), Is.All.EqualTo("0.5.0"));
            Assert.That(plan.Tasks[0].Ticket, Is.Empty, "a plan gives out no ticket: the split does");
        });

        GC.KeepAlive(manifest);
    }

    [TestCase(0, 1, 0)]
    [TestCase(1, 1, 1)]
    [TestCase(24, 1, 24)]
    [TestCase(1024, 1, 1024)]
    [TestCase(1025, 2, 513)]
    [TestCase(2048, 2, 1024)]
    [TestCase(100_000, 98, 1021)]
    [TestCase(1 << 20, 1024, 1024)]
    public void M5ARoundHasABoundedNumberOfPackagesTest(int shards, int perPackage, int packages)
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusPackagePlanner.ShardsPerPackage(shards), Is.EqualTo(perPackage));
            Assert.That(CorpusPackagePlanner.Packages(shards), Is.EqualTo(packages));
            Assert.That(CorpusPackagePlanner.Packages(shards), Is.LessThanOrEqualTo(CorpusPackagePlanner.MAX_PACKAGES));
            Assert.That(CorpusPackagePlanner.ShardsPerPackage(shards), Is.LessThanOrEqualTo(CorpusPackagePlanner.MAX_PACKAGE_SHARDS));
        });
    }

    [Test]
    public void M5UpToTheBoundAShardIsAPackageOfItsOwnTest()
    {
        // A job of up to 1,024 shards is assigned and sampled shard by shard, as before packages.
        long[] sizes = [10, 0, 30, -4, 50];

        Assert.Multiple(() =>
        {
            Assert.That(CorpusPackagePlanner.PackageSums(sizes), Is.EqualTo(new long[] { 10, 0, 30, 0, 50 }));
            Assert.That(CorpusPackagePlanner.PackageSums(Enumerable.Repeat(2L, 2050).ToList()), Is.EqualTo(Enumerable.Repeat(6L, 683).Append(2L)), "past the bound: consecutive shards, three to a package here");
            Assert.That(CorpusPackagePlanner.PackageSums([]), Is.Empty);
        });
    }

    #endregion
}
