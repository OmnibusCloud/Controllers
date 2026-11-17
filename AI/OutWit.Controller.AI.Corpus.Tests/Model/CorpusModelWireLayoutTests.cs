using System.Text.Json;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Tests.Model;

/// <summary>
/// Freezes the wire layout of every MemoryPack DTO (the repo's append-only wire discipline), member
/// by member: the order, the name, the type and the default of each. Audit finding L-1: the guard
/// used to count members, so two members swapped or one retyped passed it. A failing case means a
/// member was inserted, reordered, renamed, retyped, removed, or given another default — a wire
/// break for a deployed fleet and for every application built against the package. New members
/// go at the END with the next order value; then the baseline moves with the Model's version.
/// <para>
/// The same finding showed what the wire does with a difference in members, and nothing held it:
/// a member the sender does not have arrives as its type's default, not as the property's
/// initializer, and a value with a member more than the reader has is refused. The tests below
/// hold both, so that the rule they imply stays in sight: the Model goes out before anything that
/// writes it, and an appended member starts at its type's default.
/// </para>
/// </summary>
[TestFixture]
public sealed class CorpusModelWireLayoutTests
{
    #region Constants

    /// <summary>Set to a file path to have the current layout written there (to move the baseline on purpose).</summary>
    private const string WRITE_BASELINE_VARIABLE = "WITAI_CORPUS_WRITE_WIRE_BASELINE";

    /// <summary>
    /// The members whose initializer is not their type's default, by type: all of them of the
    /// first layouts, before the rule. A reader that meets a sender without one of them reads
    /// false where a new instance holds true.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> GRANDFATHERED = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [nameof(CorpusPipelineData)] = [nameof(CorpusPipelineData.IdentifyLanguage), nameof(CorpusPipelineData.FilterQuality), nameof(CorpusPipelineData.Sign), nameof(CorpusPipelineData.CleanLines), nameof(CorpusPipelineData.ExtractPdf)]
    };

    #endregion

    #region Layout Tests

    [Test]
    public void L1TheWireLayoutIsTheBaselinesMemberByMemberTest()
    {
        var root = SolutionRoot();
        var path = Path.Combine(root, "AI", "OutWit.Controller.AI.Corpus.Tests", "Model", "wire-layout.baseline.json");
        var current = CorpusModelWireLayout.Describe(ModelVersion(root));

        if (Environment.GetEnvironmentVariable(WRITE_BASELINE_VARIABLE) is { Length: > 0 } target)
            File.WriteAllText(target, current + "\n");

        using var baseline = JsonDocument.Parse(File.ReadAllBytes(path));
        var expected = baseline.RootElement.GetProperty("types").EnumerateObject()
            .ToDictionary(type => type.Name, type => type.Value.EnumerateArray().Select(member => member.GetString()!).ToList(), StringComparer.Ordinal);
        var actual = CorpusModelWireLayout.Types()
            .ToDictionary(type => type.Name, type => CorpusModelWireLayout.Members(type).Select(member => member.ToString()).ToList(), StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys), "a MemoryPack DTO was added to the Model or removed from it: the baseline lists every one");
            foreach (var (type, members) in actual)
            {
                if (expected.TryGetValue(type, out var pinned))
                    Assert.That(members, Is.EqualTo(pinned), $"{type}: a member's order, name, type or default differs from the published layout. Append only; then update wire-layout.baseline.json and bump the Model <Version> in the same commit. The current layout:\n{current}");
            }
        });
    }

    [Test]
    public void L1MemberOrdersAreContiguousFromZeroTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var type in CorpusModelWireLayout.Types())
            {
                var members = CorpusModelWireLayout.Members(type);
                Assert.That(members.Select(member => member.Order), Is.EqualTo(Enumerable.Range(0, members.Count)), $"{type.Name}: orders must be contiguous from 0");
            }
        });
    }

    [Test]
    public void PublishedLayoutBaselineMatchesTheModelVersionTest()
    {
        // The baseline says which layout the published Model version carries. A layout that
        // differs from it means a wire change: update the baseline AND bump the Model's <Version>
        // in the same commit, then publish the Model before the controller depends on it.
        var root = SolutionRoot();
        using var baseline = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "AI", "OutWit.Controller.AI.Corpus.Tests", "Model", "wire-layout.baseline.json")));

        Assert.That(ModelVersion(root), Is.EqualTo(baseline.RootElement.GetProperty("modelVersion").GetString()), "the Model csproj version and the baseline's modelVersion must agree");
    }

    [Test]
    public void L1ANewMembersInitializerIsItsTypesDefaultTest()
    {
        // What a reader gets for a member its sender does not have is the type's default. A
        // member appended with another initializer would read differently from an older sender
        // than from a new instance: only the members of the first layouts may.
        Assert.Multiple(() =>
        {
            foreach (var type in CorpusModelWireLayout.Types())
            {
                var allowed = GRANDFATHERED.GetValueOrDefault(type.Name) ?? [];
                var other = CorpusModelWireLayout.Members(type)
                    .Where(member => member.Type is "Boolean" or "Int32" or "Int64" or "Double" && member.Default is not ("false" or "0"))
                    .Select(member => member.Name)
                    .ToList();

                Assert.That(other, Is.EquivalentTo(allowed), $"{type.Name}: a value member starts at something else than its type's default");
            }
        });
    }

    #endregion

    #region What the wire does with a difference

    [Test]
    public void L1AMemberAnOlderSenderDoesNotHaveArrivesAsItsTypesDefaultTest()
    {
        // A pipeline as a sender of the nine-member layout wrote it (Corpus 0.1.x): the image of
        // today's, less its last three members. ExtractPdf reads false there, though a new
        // instance holds true.
        var pipeline = new CorpusPipelineData { Version = "v" };
        var image = MemoryPackSerializer.Serialize(pipeline);
        var older = Older(image, membersLeft: 9, bytesCut: sizeof(bool) + 1 + sizeof(int));

        var back = MemoryPackSerializer.Deserialize<CorpusPipelineData>(older)!;

        // And a task as a server before the declared size wrote it: the claim is 0, "no size of its own".
        var task = new CorpusTaskData { TaskIndex = 3, Ticket = new string('a', 32), RequiredControllerVersion = "0.4.0", DeclaredBytes = 77 };
        var before = Older(MemoryPackSerializer.Serialize(task), membersLeft: 7, bytesCut: sizeof(long));
        var read = MemoryPackSerializer.Deserialize<CorpusTaskData>(before)!;

        Assert.Multiple(() =>
        {
            Assert.That(new CorpusPipelineData().ExtractPdf, Is.True, "the initializer");
            Assert.That(back.Version, Is.EqualTo("v"));
            Assert.That(back.CleanLines, Is.True, "the ninth member was sent");
            Assert.That(back.ExtractPdf, Is.False, "the tenth was not: it is the type's default, not the initializer");
            Assert.That(read.TaskIndex, Is.EqualTo(3));
            Assert.That(read.RequiredControllerVersion, Is.EqualTo("0.4.0"));
            Assert.That(read.DeclaredBytes, Is.Zero);
        });
    }

    [Test]
    public void L1AValueWithAMemberMoreThanTheReaderHasIsRefusedTest()
    {
        // A sender ahead of the reader: the image states one member more than the type has. The
        // reader does not skip what it does not know; it refuses the value. So the Model goes out
        // to every reader (the server, the application) before anything writes the new member.
        var image = MemoryPackSerializer.Serialize(new CorpusCountData { Name = "eng", Count = 3 });
        var newer = image.Concat(new byte[sizeof(int)]).ToArray();
        newer[0]++;

        Assert.Throws<MemoryPackSerializationException>(() => MemoryPackSerializer.Deserialize<CorpusCountData>(newer));
    }

    #endregion

    #region Round Trips

    [Test]
    public void StatsRoundTripWithTheUnsupportedBreakdownTest()
    {
        var stats = new CorpusShardStatsData
        {
            RecordsIn = 3,
            Drops = [new CorpusCountData { Name = "language_unsupported", Count = 2 }],
            UnsupportedLanguages = [new CorpusCountData { Name = "past_limit:spa", Count = 1 }, new CorpusCountData { Name = "swe", Count = 1 }]
        };

        var back = MemoryPackSerializer.Deserialize<CorpusShardStatsData>(MemoryPackSerializer.Serialize(stats))!;
        var other = stats.Clone();
        other.UnsupportedLanguages[1].Count = 2;

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(stats), Is.True);
            Assert.That(back.UnsupportedLanguages.Select(count => count.Name), Is.EqualTo(new[] { "past_limit:spa", "swe" }));
            Assert.That(stats.Clone().Is(stats), Is.True);
            Assert.That(other.Is(stats), Is.False, "the breakdown is part of the value");
        });
    }

    [Test]
    public void TaskRoundTripsThroughMemoryPackAndIsTest()
    {
        var task = new CorpusTaskData
        {
            TaskIndex = 7,
            Source = new CorpusSourceData { Url = "https://data.commoncrawl.org/crawl-data/x.warc.wet.gz", Format = CorpusSourceFormat.Wet, SizeBytes = 123 },
            Pipeline = new CorpusPipelineData { Version = "cc-text-v1", LanguageModelSha256 = "abc", TargetLanguages = ["eng", "deu"], Quality = new CorpusQualityData { MinWords = 50 } },
            AllowedOrigins = ["data.commoncrawl.org"],
            MaxShardBytes = 1L << 30,
            DeclaredBytes = 123
        };

        var bytes = MemoryPackSerializer.Serialize(task);
        var back = MemoryPackSerializer.Deserialize<CorpusTaskData>(bytes)!;
        var other = task.Clone();
        other.DeclaredBytes = 124;

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(task), Is.True);
            Assert.That(task.Clone().Is(task), Is.True);
            Assert.That(other.Is(task), Is.False, "the declared size is part of the value");
            Assert.That(back.Pipeline!.TargetLanguages, Is.EqualTo(new[] { "eng", "deu" }));
            Assert.That(ReferenceEquals(task.Clone().Pipeline, task.Pipeline), Is.False);
        });
    }

    [Test]
    public void ThePackageReferencesRoundTripAndCompareByValueTest()
    {
        var package = new CorpusTaskPackageData
        {
            TasksBlobId = Guid.NewGuid(),
            Sha256 = new string('a', 64),
            SizeBytes = 1234,
            ShardCount = 98,
            ShardBytes = 6_000_000_000,
            LargestShardBytes = 70_000_000,
            NeedsPdfSandbox = true,
            Ticket = new string('b', 32),
            RequiredControllerVersion = "0.5.0",
            UnsizedShards = 2
        };
        var answer = new CorpusResultPackageData { Ticket = package.Ticket, ResultsBlobId = Guid.NewGuid(), Fault = "" };
        var fault = new CorpusResultPackageData { Ticket = package.Ticket, Fault = "tasks" };

        var packageBack = MemoryPackSerializer.Deserialize<CorpusTaskPackageData>(MemoryPackSerializer.Serialize(package))!;
        var answerBack = MemoryPackSerializer.Deserialize<CorpusResultPackageData>(MemoryPackSerializer.Serialize(answer))!;
        var changed = package.Clone();
        changed.UnsizedShards = 3;

        Assert.Multiple(() =>
        {
            Assert.That(packageBack.Is(package), Is.True);
            Assert.That(package.Clone().Is(package), Is.True);
            Assert.That(changed.Is(package), Is.False);
            Assert.That(answerBack.Is(answer), Is.True);
            Assert.That(fault.Clone().Is(fault), Is.True);
            Assert.That(fault.Is(answer), Is.False);
            Assert.That(MemoryPackSerializer.Serialize(package), Has.Length.LessThan(512), "a reference is a few hundred bytes whatever its package holds");
        });
    }

    #endregion

    #region Tools

    /// <summary>The image of a value as a sender with fewer members wrote it: the member count in its first byte lowered and the bytes of the last members cut.</summary>
    private static byte[] Older(byte[] image, byte membersLeft, int bytesCut)
    {
        var older = image[..^bytesCut];
        older[0] = membersLeft;
        return older;
    }

    private static string ModelVersion(string root)
    {
        var csproj = File.ReadAllText(Path.Combine(root, "AI", "OutWit.Controller.AI.Corpus.Model", "OutWit.Controller.AI.Corpus.Model.csproj"));
        return System.Text.RegularExpressions.Regex.Match(csproj, @"<Version>([^<]+)</Version>").Groups[1].Value;
    }

    private static string SolutionRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "OutWit.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("The solution root (OutWit.slnx) was not found above the test folder.");
    }

    #endregion
}
