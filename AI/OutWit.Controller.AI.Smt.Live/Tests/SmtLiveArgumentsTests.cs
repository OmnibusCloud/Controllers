using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests;

[TestFixture]
public sealed class SmtLiveArgumentsTests
{
    #region Verb Tests

    [TestCase("diagnostics", "Diagnostics", 0)]
    [TestCase("diagnostics --out d", "Diagnostics", 0)]
    [TestCase("preflight p.jsonl", "Preflight", 1)]
    [TestCase("run p.jsonl --out d", "Run", 1)]
    [TestCase("compare a b", "Compare", 2)]
    [TestCase("pack folder --out p.jsonl", "Pack", 1)]
    [TestCase("pack folder --query prove --out p.jsonl", "Pack", 1)]
    public void ParseReadsEveryVerbTest(string line, string verb, int inputs)
    {
        var arguments = SmtLiveArguments.Parse(Split(line), out var error);

        Assert.That(error, Is.Null);
        Assert.That(arguments, Is.Not.Null);
        Assert.That(arguments!.Verb, Is.EqualTo(Enum.Parse<SmtLiveVerb>(verb)));
        Assert.That(arguments.Inputs, Has.Count.EqualTo(inputs));
    }

    [TestCase("--help")]
    [TestCase("-h")]
    [TestCase("help")]
    [TestCase("run --help")]
    [TestCase("pack -h")]
    public void ParseGivesNoErrorWhenHelpIsAskedForTest(string line)
    {
        var arguments = SmtLiveArguments.Parse(Split(line), out var error);

        Assert.That(arguments, Is.Null);
        Assert.That(error, Is.Null);
    }

    #endregion

    #region Option Tests

    [Test]
    public void ParseReadsEveryOptionOfARunByItsDocumentNameTest()
    {
        var arguments = SmtLiveArguments.Parse(Split("run p.jsonl --query enumerate --rate 0.25 --rlimit 2000000000 --hard-cap 100 --memory 2048 --seed 7 --max-models 9 --out d"), out var error);

        Assert.That(error, Is.Null);
        var options = arguments!.Options;
        Assert.Multiple(() =>
        {
            Assert.That(arguments.Inputs, Is.EqualTo(new[] { "p.jsonl" }));
            Assert.That(arguments.Output, Is.EqualTo("d"));
            Assert.That(arguments.Query, Is.EqualTo(SmtQuery.Enumerate));
            Assert.That(options.Query, Is.EqualTo(SmtQuery.Enumerate));
            Assert.That(options.SamplingRate, Is.EqualTo(0.25));
            Assert.That(options.ResourceLimit, Is.EqualTo(2_000_000_000L));
            Assert.That(options.HardCapSeconds, Is.EqualTo(100));
            Assert.That(options.MemoryMegabytes, Is.EqualTo(2048));
            Assert.That(options.Seed, Is.EqualTo(7));
            Assert.That(options.MaxModels, Is.EqualTo(9));
        });
    }

    [Test]
    public void ParseLeavesAnOptionNotGivenAtItsDefaultTest()
    {
        var arguments = SmtLiveArguments.Parse(Split("preflight p.jsonl"), out _);
        var options = arguments!.Options;

        Assert.Multiple(() =>
        {
            Assert.That(arguments.Output, Is.Null);
            Assert.That(arguments.Query, Is.Null);
            Assert.That(options.Is(new SmtOptionsData()), Is.True, "every number 0, the sampling rate the default of a new instance");
            Assert.That(options.SamplingRate, Is.EqualTo(SmtOptionsRules.DEFAULT_SAMPLING_RATE));
        });
    }

    [Test]
    public void ParseTakesANumberOutOfRangeForTheControllerToRefuseTest()
    {
        var arguments = SmtLiveArguments.Parse(Split("preflight p.jsonl --seed -1 --rate 2"), out var error);

        Assert.That(error, Is.Null);
        Assert.That(arguments!.Options.Seed, Is.EqualTo(-1));
        Assert.That(arguments.Options.SamplingRate, Is.EqualTo(2.0));
        Assert.That(SmtOptionsRules.Check(arguments.Options), Has.Count.EqualTo(2), "the controller's rule says which options are out of range");
    }

    #endregion

    #region Refusal Tests

    [TestCase("", "no verb")]
    [TestCase("frobnicate", "not a verb")]
    [TestCase("run", "takes 1 argument")]
    [TestCase("run a.jsonl b.jsonl", "takes 1 argument")]
    [TestCase("compare a", "takes 2 argument")]
    [TestCase("pack folder", "pack needs --out")]
    [TestCase("run p.jsonl --out", "--out needs a value")]
    [TestCase("run p.jsonl --rate --seed 1", "--rate needs a value")]
    [TestCase("run p.jsonl --rate fast", "--rate must be a number")]
    [TestCase("run p.jsonl --rate NaN", "--rate must be a number")]
    [TestCase("run p.jsonl --seed 1.5", "--seed must be a whole number")]
    [TestCase("run p.jsonl --rlimit 99999999999999999999", "--rlimit must be a whole number")]
    [TestCase("run p.jsonl --max-models 12345678901", "--max-models must be a whole number")]
    [TestCase("run p.jsonl --query solve", "--query must be check, enumerate or prove")]
    [TestCase("run p.jsonl --rate 1 --rate 0", "--rate is given twice")]
    [TestCase("run p.jsonl --frob 1", "'--frob' is not an option of run")]
    [TestCase("preflight p.jsonl --out d", "'--out' is not an option of preflight")]
    [TestCase("compare a b --rate 1", "'--rate' is not an option of compare")]
    [TestCase("pack folder --rate 1 --out p.jsonl", "'--rate' is not an option of pack")]
    [TestCase("diagnostics --query check", "'--query' is not an option of diagnostics")]
    public void ParseRefusesAWrongCommandLineWithASentenceTest(string line, string sentence)
    {
        var arguments = SmtLiveArguments.Parse(Split(line), out var error);

        Assert.That(arguments, Is.Null);
        Assert.That(error, Does.Contain(sentence));
    }

    #endregion

    #region Word Tests

    [TestCase("check", SmtQuery.Check)]
    [TestCase("enumerate", SmtQuery.Enumerate)]
    [TestCase("prove", SmtQuery.Prove)]
    public void AQueryReadsAndWritesAsTheSameWordTest(string word, SmtQuery query)
    {
        Assert.That(SmtLiveArguments.TryQuery(word, out var read), Is.True);
        Assert.That(read, Is.EqualTo(query));
        Assert.That(SmtLiveArguments.WordOf(query), Is.EqualTo(word));
    }

    #endregion

    #region Tools

    private static string[] Split(string line)
    {
        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    #endregion
}
