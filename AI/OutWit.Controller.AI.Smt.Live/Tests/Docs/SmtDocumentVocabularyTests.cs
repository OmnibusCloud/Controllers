using System.Text.Json;
using OutWit.Cloud.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Docs;

/// <summary>
/// The types the model package publishes into the platform's job document vocabulary, through the
/// platform's own reader of a request (<c>JobRequestParser</c>) and the door's rules
/// (<see cref="SmtLiveDocumentDoor"/>): the options travel in a request document, member by member,
/// a member left out being its default; a document with a member the options do not have, or a
/// value of another type, is refused as the door refuses a document that is not of its type's
/// shape, before any job; the preflight and the diagnostics read back as value documents. A host
/// without .NET submits and reads through these, so what it may send is held here.
/// </summary>
[TestFixture]
public sealed class SmtDocumentVocabularyTests
{
    #region Constants

    private const string PAGE = "AI/OutWit.Controller.AI.Smt/docs/application.md";
    private const string SCHEMA = "AI/OutWit.Controller.AI.Smt.Model/Documents/smt.schema.json";

    private const string OPTIONS = "smt.options@1";
    private const string PREFLIGHT = "smt.preflight@1";
    private const string DIAGNOSTICS = "smt.diagnostics@1";
    private const string COUNT = "smt.count@1";
    private const string REFUSAL = "smt.refusal@1";

    /// <summary>How the door refuses a value that is not of its type's shape.</summary>
    private const string NOT_THE_SHAPE = "does not match the published shape of 'smt.options@1'";

    #endregion

    #region Vocabulary Tests

    [Test]
    public void TheModelPackagePublishesFiveTypesTest()
    {
        var published = SmtLiveDocumentDoor.Published();
        using var schema = JsonDocument.Parse(Read(SCHEMA));
        var page = Read(PAGE).Replace("\n", " ", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.EquivalentTo(new Dictionary<string, Type>
            {
                [OPTIONS] = typeof(SmtOptionsData),
                [PREFLIGHT] = typeof(SmtPreflightData),
                [DIAGNOSTICS] = typeof(SmtRuntimeDiagnosticsData),
                [COUNT] = typeof(SmtCountData),
                [REFUSAL] = typeof(SmtRefusalData)
            }), "the options, the preflight, the diagnostics, and the two types the preflight is made of");

            Assert.That(schema.RootElement.GetProperty("x-oc-vocabulary").GetProperty("types").EnumerateArray().Select(type => type.GetString()),
                Is.EquivalentTo(published.Keys), "the vocabulary the package carries for a host without .NET names the same types");

            foreach (var (id, type) in published)
                Assert.That(page, Does.Contain($"`{id}` (`{type.Name}`)"), $"the page names {id} and its type");
        });
    }

    [Test]
    public void TheQueryIsAWordOfTheDocumentsTest()
    {
        // The vocabulary says the query by the words a problem set and the documents say it.
        using var schema = JsonDocument.Parse(Read(SCHEMA));

        Assert.Multiple(() =>
        {
            Assert.That(schema.RootElement.GetProperty("$defs").GetProperty(nameof(SmtQuery)).GetProperty("enum").EnumerateArray().Select(word => word.GetString()),
                Is.EqualTo(new[] { "check", "enumerate", "prove" }));
            Assert.That(Options("{\"query\": \"check\"}").Query, Is.EqualTo(SmtQuery.Check));
            Assert.That(Options("{\"query\": \"enumerate\"}").Query, Is.EqualTo(SmtQuery.Enumerate));
            Assert.That(Options("{\"query\": \"prove\"}").Query, Is.EqualTo(SmtQuery.Prove));
        });
    }

    #endregion

    #region Options Tests

    [Test]
    public void EveryOptionTravelsInADocumentTest()
    {
        var options = Options("""
            {
              "profile": "z3-5.4.1",
              "query": "enumerate",
              "maxModels": 5,
              "seed": 7,
              "resourceLimit": 2000000,
              "hardCapSeconds": 30,
              "memoryMegabytes": 512,
              "samplingRate": 0.25
            }
            """);

        var expected = new SmtOptionsData
        {
            Profile = "z3-5.4.1",
            Query = SmtQuery.Enumerate,
            MaxModels = 5,
            Seed = 7,
            ResourceLimit = 2_000_000,
            HardCapSeconds = 30,
            MemoryMegabytes = 512,
            SamplingRate = 0.25
        };

        Assert.Multiple(() =>
        {
            Assert.That(options.Is(expected), Is.True, $"every option as the document gives it: {options}");
            Assert.That(SmtOptionsRules.Check(options), Is.Empty);
        });
    }

    [Test]
    public void AMemberLeftOutIsItsDefaultTest()
    {
        var none = Options("{}");
        var one = Options("{\"hardCapSeconds\": 30}");
        var nullProfile = Options("{\"profile\": null}");

        Assert.Multiple(() =>
        {
            Assert.That(none.Is(new SmtOptionsData()), Is.True, "an empty document is a new SmtOptionsData: the defaults, the sampling rate's among them");
            Assert.That(none.SamplingRate, Is.EqualTo(SmtOptionsRules.DEFAULT_SAMPLING_RATE));
            Assert.That(one.Is(new SmtOptionsData { HardCapSeconds = 30 }), Is.True);
            Assert.That(nullProfile.Profile, Is.Null.Or.Empty, "a profile of null is no profile: the controller's, as an empty one is");
            Assert.That(SmtOptionsRules.Check(nullProfile), Is.Empty);
        });
    }

    [Test]
    public void NullOptionsAreNoDocumentTest()
    {
        // null in place of the document: the run's options are null, which a run takes as the defaults.
        var request = Request("{\"kind\": \"null\"}");

        Assert.That(JobRequestParser.Parse(request, SmtLiveDocumentDoor.Resolve).Parameters!.GetValues()[1], Is.Null);
    }

    [TestCase("hard_cap_s")]
    [TestCase("hardCapSecond")]
    [TestCase("fuel")]
    public void AMemberTheOptionsDoNotHaveIsRefusedTest(string member)
    {
        // Left unread, the member would leave its option at the default without a word: a run under
        // other options than its lab asked for.
        var request = Request($"{{\"kind\": \"document\", \"type\": \"{OPTIONS}\", \"value\": {{\"{member}\": 30}}}}");

        Assert.Multiple(() =>
        {
            Assert.That(() => JobRequestParser.Validate(request), Throws.Nothing, "the request is well formed: a client forwards it");
            Assert.That(() => JobRequestParser.Parse(request, SmtLiveDocumentDoor.Resolve), Throws.TypeOf<JobRequestFormatException>().With.Message.Contains(NOT_THE_SHAPE).And.Message.Contains(member),
                "and the server's door refuses it, naming the member");
        });
    }

    [TestCase("\"hardCapSeconds\": \"30\"")]
    [TestCase("\"maxModels\": 1.5")]
    [TestCase("\"samplingRate\": true")]
    [TestCase("\"profile\": 5")]
    [TestCase("\"query\": \"solve\"")]
    public void AValueOfAnotherTypeIsRefusedTest(string member)
    {
        var request = Request($"{{\"kind\": \"document\", \"type\": \"{OPTIONS}\", \"value\": {{{member}}}}}");

        Assert.That(() => JobRequestParser.Parse(request, SmtLiveDocumentDoor.Resolve), Throws.TypeOf<JobRequestFormatException>().With.Message.Contains(NOT_THE_SHAPE));
    }

    [Test]
    public void ANumberOutOfItsRangePassesTheDoorAndTheRunRefusesItTest()
    {
        // The door reads the shape; the range is the run's, with the sentence of the option table.
        var options = Options("{\"hardCapSeconds\": 1000}");

        Assert.Multiple(() =>
        {
            Assert.That(options.HardCapSeconds, Is.EqualTo(1_000));
            Assert.That(SmtOptionsRules.Check(options), Is.EqualTo(new[] { "'hard_cap_s' must be between 1 and 600" }));
        });
    }

    #endregion

    #region Value Document Tests

    [Test]
    public void ThePreflightReadsBackAsADocumentTest()
    {
        var preflight = new SmtPreflightData
        {
            Runnable = true,
            Rows = 5,
            Accepted = 4,
            Refused = 1,
            BytesRead = 598,
            Sha256 = new string('e', 64),
            RefusalCodes = [SmtCountData.Of("command_not_allowed", 1)],
            Refusals = [new SmtRefusalData { File = "push", Line = 1, Command = "push", Code = "command_not_allowed", Reason = "incremental scripts, unsat cores and proofs are not supported", Row = 6 }],
            Logics = [SmtCountData.Of("QF_LIA", 4)],
            Queries = [SmtCountData.Of("check", 2), SmtCountData.Of("enumerate", 1), SmtCountData.Of("prove", 1)],
            Expected = [SmtCountData.Of("none", 2), SmtCountData.Of("sat", 1), SmtCountData.Of("unsat", 1)],
            Licenses = [SmtCountData.Of("", 4)],
            OptionProblems = ["'seed' must be between 0 and 2147483647"],
            Profile = "z3-5.4.1",
            ResourceLimit = 150_000_000,
            HardCapSeconds = 60,
            Fuel = 300_000_000_000,
            MemoryMegabytes = 1_024,
            MaxModels = 2,
            SamplingRate = 1,
            SessionMemoryBytes = 2_885_681_152,
            UpperBoundSeconds = 240,
            Packages = 1,
            ProblemsPerPackage = 16,
            Messages = ["The upper bound is the taken problems times the hard cap."]
        };

        var document = SmtLiveDocumentDoor.ValueDocument(preflight)!;
        using var parsed = JsonDocument.Parse(document);
        var value = parsed.RootElement.GetProperty("value");

        Assert.Multiple(() =>
        {
            Assert.That(parsed.RootElement.GetProperty("kind").GetString(), Is.EqualTo("document"));
            Assert.That(parsed.RootElement.GetProperty("type").GetString(), Is.EqualTo(PREFLIGHT));
            Assert.That(value.GetProperty("sessionMemoryBytes").GetInt64(), Is.EqualTo(2_885_681_152), "a member by its name in camel case");
            Assert.That(value.GetProperty("refusals")[0].GetProperty("code").GetString(), Is.EqualTo("command_not_allowed"), "a refusal as an object of its own type");
            Assert.That(value.GetProperty("logics")[0].GetProperty("count").GetInt32(), Is.EqualTo(4), "a count as an object of its own type");
            Assert.That(((SmtPreflightData)SmtLiveDocumentDoor.Read(document)).Is(preflight), Is.True, "and the document reads back whole");
        });
    }

    [Test]
    public void TheDiagnosticsReadBackAsADocumentTest()
    {
        var diagnostics = new SmtRuntimeDiagnosticsData
        {
            ControllerVersion = "1.0.0",
            ProfileId = "z3-5.4.1",
            SolverVersion = "5.4.1",
            ModuleFile = "wasm/z3-5.4.1.wasm",
            ModuleSha256 = new string('9', 64),
            ModuleSize = 30_204_160,
            ModulePresent = true,
            ModuleCompiled = true,
            CompileMs = 4_210,
            WasmtimeBinding = "48.0.2",
            NativesVersion = "48.0.8",
            NativesSha256 = new string('d', 64),
            NativesPinned = true,
            TrapMode = "explicit",
            Platform = "linux-x64",
            LogicalCores = 16,
            AvailableMemoryBytes = 34_359_738_368,
            SessionMemoryBytes = 2_885_681_152,
            Degree = 8,
            DegreeReason = "memory"
        };

        var document = SmtLiveDocumentDoor.ValueDocument(diagnostics)!;
        using var parsed = JsonDocument.Parse(document);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.RootElement.GetProperty("type").GetString(), Is.EqualTo(DIAGNOSTICS));
            Assert.That(parsed.RootElement.GetProperty("value").GetProperty("degreeReason").GetString(), Is.EqualTo("memory"));
            Assert.That(((SmtRuntimeDiagnosticsData)SmtLiveDocumentDoor.Read(document)).Is(diagnostics), Is.True, "the document reads back whole");
        });
    }

    [Test]
    public void ATypeThePackageDoesNotPublishHasNoDocumentTest()
    {
        // A node's row and its packages never travel as documents: the door answers them with its failure.
        Assert.Multiple(() =>
        {
            Assert.That(SmtLiveDocumentDoor.ValueDocument(new SmtResultData()), Is.Null);
            Assert.That(SmtLiveDocumentDoor.ValueDocument(new SmtTaskPackageData()), Is.Null);
            Assert.That(() => SmtLiveDocumentDoor.Resolve("smt.result@1", JsonDocument.Parse("{}").RootElement),
                Throws.TypeOf<JobRequestFormatException>().With.Message.Contains("is not published by any installed controller"));
        });
    }

    #endregion

    #region Tools

    /// <summary>The options a request document of SmtRun carries as a document of their type, as the server's door reads them.</summary>
    private static SmtOptionsData Options(string value)
    {
        var values = JobRequestParser.Parse(Request($"{{\"kind\": \"document\", \"type\": \"{OPTIONS}\", \"value\": {value}}}"), SmtLiveDocumentDoor.Resolve).Parameters!.GetValues();
        Assert.That(values[1], Is.TypeOf<SmtOptionsData>(), "the options as their type");
        return (SmtOptionsData)values[1]!;
    }

    /// <summary>A request document of SmtRun: a set's blob and the given parameter for the options.</summary>
    private static string Request(string options)
    {
        return $"{{\"schemaVersion\": 1, \"script\": \"SmtRun\", \"parameters\": [{{\"kind\": \"uuid\", \"value\": \"{Guid.NewGuid()}\"}}, {options}]}}";
    }

    private static string Read(string path)
    {
        return File.ReadAllText(Path.Combine([SmtLiveFixtures.RepositoryRoot(), .. path.Split('/')])).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    #endregion
}
