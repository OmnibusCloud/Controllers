using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The host's gate of a node's row, a row at a time: a row is taken only when a session of this
/// controller could have written it. Each test starts from a row a session does write
/// (<see cref="SmtSessionCases"/>), changes one thing a liar would change, gives the row a digest
/// of its own again, and holds that the host refuses it: words that do not go together, a number
/// past the envelope the package's options give, a text longer than a session writes or one that
/// is no text. And the edges an honest row may reach are still taken. That every row a session
/// can make is taken is held by <see cref="SmtRowChecksOutcomeTests"/>.
/// </summary>
[TestFixture]
public sealed class SmtRowChecksTests
{
    #region Constants

    private const string MODEL = SmtSessionCases.MODEL;
    private const string VALUES = SmtSessionCases.VALUES;

    private const char HALF_A_PAIR = (char)0xD800;
    private const char THE_OTHER_HALF = (char)0xDC00;

    #endregion

    #region Word Tests

    [TestCaseSource(nameof(WordsThatDoNotGoTogether))]
    public void ARowWhoseWordsNoSessionGivesTogetherIsRefusedTest(string shape, Action<SmtResultData> change)
    {
        AssertRefused(shape, change);
    }

    [TestCaseSource(nameof(NumbersPastTheEnvelope))]
    public void ARowWithANumberNoSessionOfItsPackageReachesIsRefusedTest(string shape, Action<SmtResultData, SmtOptionsData> change)
    {
        var one = SmtSessionCases.Of(shape);
        AssertRefused(shape, row => change(row, one.Options));
    }

    [TestCaseSource(nameof(TextsNoSessionWrites))]
    public void ARowWithATextNoSessionWritesIsRefusedTest(string shape, Action<SmtResultData> change)
    {
        AssertRefused(shape, change);
    }

    [TestCaseSource(nameof(Edges))]
    public void ARowAtTheEdgeOfWhatASessionWritesIsTakenTest(string shape, Action<SmtResultData, SmtOptionsData> change)
    {
        var one = SmtSessionCases.Of(shape);
        var row = SmtRowCheckRig.Row(one);

        change(row, one.Options);
        row.Digest = SmtResultDigest.Of(row);

        Assert.That(SmtRowCheckRig.Holds(row, one.Problem, one.Options), Is.True, Describe(row));
    }

    #endregion

    #region Code Tests

    [Test]
    public void EveryCodeOfTheControllerIsTakenUnderOneStatusAndNoOtherTest()
    {
        var codes = SmtRowCheckRig.SolveCodes();

        Assert.Multiple(() =>
        {
            Assert.That(codes, Has.Count.GreaterThanOrEqualTo(12), "the controller's own words were read off its list");
            foreach (var code in codes)
            {
                var taken = Carriers().Where(shape => TakenWith(shape, code)).ToList();
                Assert.That(taken, Has.Count.EqualTo(1), $"'{code}' is the code of one status: taken under {string.Join(", ", taken)}");
            }
        });
    }

    [Test]
    public void EveryRefusalCodeOfTheModelPackageIsTakenUnderRefusedOnlyTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var code in SmtRefusalCodes.All)
                Assert.That(Carriers().Where(shape => TakenWith(shape, code)), Is.EqualTo(new[] { "refused-hash" }), code);
        });
    }

    [TestCase("no_answer")]
    [TestCase("copies_short")]
    [TestCase("unreadable")]
    [TestCase("memory_insufficient", Description = "a code of a package a node could not work on is no code of a row")]
    [TestCase("Trap")]
    [TestCase("a word of the node's own")]
    public void AWordThatIsNoCodeIsTakenUnderNoStatusTest(string word)
    {
        Assert.That(Carriers().Where(shape => TakenWith(shape, word)), Is.Empty);
    }

    [Test]
    public void AStatusThatSaysItAllCarriesNoCodeTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var shape in new[] { "check-sat", "check-error-in-the-problem", "check-output-in-the-model" })
            {
                Assert.That(TakenWith(shape, ""), Is.True, $"{shape}: the row as the session wrote it");
                foreach (var code in SmtRowCheckRig.SolveCodes().Concat(SmtRefusalCodes.All))
                    Assert.That(TakenWith(shape, code), Is.False, $"{shape} with the code {code}");
            }

            foreach (var shape in Carriers())
                Assert.That(TakenWith(shape, ""), Is.False, $"{shape} without a code");
        });
    }

    #endregion

    #region Cases

    private static IEnumerable<TestCaseData> WordsThatDoNotGoTogether()
    {
        // A verdict is claimed by an answered session, and by one whose output passed its cap after the check.
        yield return Words("a verdict of an input error", "check-error-in-the-problem", row => row.Verdict = SmtResultWords.SAT);
        yield return Words("a verdict and a model of an input error", "check-error-in-the-model", row => { row.Verdict = SmtResultWords.SAT; row.Models = [MODEL]; });
        yield return Words("a verdict of a refusal", "refused-hash", row => row.Verdict = SmtResultWords.UNSAT);
        yield return Words("a verdict of a crash", "check-trap", row => row.Verdict = SmtResultWords.UNKNOWN);
        yield return Words("a verdict of a fault of the node", "check-backstop", row => row.Verdict = SmtResultWords.UNSAT);
        yield return Words("an answered check without a verdict", "check-unsat", row => row.Verdict = SmtResultWords.NONE);

        // The models and the values of a check and of a proof.
        yield return Words("sat without its model", "check-sat", row => row.Models = []);
        yield return Words("sat with two models", "check-sat", row => row.Models = [MODEL, MODEL]);
        yield return Words("sat without the values the check asked for", "check-sat-values", row => row.Values = []);
        yield return Words("values the check did not ask for", "check-sat", row => row.Values = [VALUES]);
        yield return Words("values without a model", "check-error-in-the-values", row => { row.Models = []; row.Values = [VALUES]; });
        yield return Words("unsat with a model", "check-unsat", row => row.Models = [MODEL]);
        yield return Words("unknown with a model", "check-unknown-incomplete", row => row.Models = [MODEL]);
        yield return Words("a counterexample without its model", "prove-counterexample", row => row.Models = []);
        yield return Words("a proof with values", "prove-counterexample", row => row.Values = [VALUES]);
        yield return Words("a count of a check", "check-sat", row => row.Count = 1);

        // The reason of an unknown.
        yield return Words("unknown without a reason", "check-unknown-incomplete", row => (row.Reason, row.ReasonText) = (SmtResultWords.NONE, ""));
        yield return Words("sat with a reason", "check-sat", row => row.Reason = SmtResultWords.RLIMIT);
        yield return Words("unsat with a reason", "prove-proved", row => row.Reason = SmtResultWords.FUEL);
        yield return Words("a reason of a crash", "check-trap", row => row.Reason = SmtResultWords.MEMOUT);
        yield return Words("a reason of an output past its cap", "check-output-after-unknown", row => row.Reason = SmtResultWords.OTHER);
        yield return Words("the solver's word without a reason", "check-sat", row => row.ReasonText = "canceled");
        yield return Words("the solver's word for a stop by the fuel", "check-fuel-in-the-check", row => row.ReasonText = "canceled");
        yield return Words("the solver's word of a crash", "check-trap", row => row.ReasonText = "incomplete");

        // An enumeration: its verdict follows from its count and its end.
        yield return Words("unsat with models", "enumerate-complete", row => row.Verdict = SmtResultWords.UNSAT);
        yield return Words("sat without a model", "enumerate-none", row => row.Verdict = SmtResultWords.SAT);
        yield return Words("unknown that is complete", "enumerate-none", row => row.Verdict = SmtResultWords.UNKNOWN);
        yield return Words("unknown with models", "enumerate-then-unknown", row => row.Verdict = SmtResultWords.UNKNOWN);
        yield return Words("unsat that is not complete", "enumerate-unknown-at-once", row => row.Verdict = SmtResultWords.UNSAT);
        yield return Words("complete and at its limit", "enumerate-complete", row => row.LimitReached = true);
        yield return Words("at its limit below its number of models", "enumerate-complete", row => (row.Complete, row.LimitReached) = (false, true));
        yield return Words("complete at its number of models", "enumerate-limit", row => (row.Complete, row.LimitReached) = (true, false));
        yield return Words("incomplete without a reason", "enumerate-then-unknown", row => (row.Reason, row.ReasonText) = (SmtResultWords.NONE, ""));
        yield return Words("complete with a reason", "enumerate-complete", row => row.Reason = SmtResultWords.RLIMIT);
        yield return Words("at its limit with a reason", "enumerate-limit", row => row.Reason = SmtResultWords.FUEL);
        yield return Words("another count than its models", "enumerate-complete", row => row.Count = 1);
        yield return Words("a model without its values", "enumerate-complete", row => row.Values = [row.Values[0]]);
        yield return Words("more models than it looks for", "enumerate-limit", row => { row.Count = 3; row.Models = [MODEL, MODEL, MODEL]; row.Values = [VALUES, VALUES, VALUES]; });

        // What only an answered enumeration says.
        yield return Words("a complete check", "check-unsat", row => row.Complete = true);
        yield return Words("a proof at its limit", "prove-counterexample", row => row.LimitReached = true);
        yield return Words("a complete enumeration that ended in an error", "enumerate-error-in-the-block", row => row.Complete = true);
        yield return Words("an enumeration at its limit that ended in a fault", "enumerate-backstop-with-a-model", row => row.LimitReached = true);
        yield return Words("a complete enumeration whose output passed its cap", "enumerate-output-with-a-model", row => row.Complete = true);
        yield return Words("an output past its cap with models and no verdict", "enumerate-output-with-a-model", row => row.Verdict = SmtResultWords.NONE);
        yield return Words("an output past its cap with unsat for an enumeration", "enumerate-output-after-unsat", row => row.Verdict = SmtResultWords.UNSAT);

        // An answered query carries no message; a refusal ran nothing.
        yield return Words("an answer with a message", "check-sat", row => row.Message = "taken");
        yield return Words("a refusal with a model", "refused-hash", row => row.Models = [MODEL]);
        yield return Words("a refusal with a check", "refused-hash", row => row.Checks = 1);
        yield return Words("a refusal with fuel", "refused-hash", row => row.Fuel = 1);
        yield return Words("a refusal with memory", "refused-hash", row => row.PeakMemoryBytes = 65_536);
        yield return Words("a refusal with a transcript", "refused-hash", row => row.Transcript = "> (check-sat)\n");
    }

    private static IEnumerable<TestCaseData> NumbersPastTheEnvelope()
    {
        yield return Numbers("more fuel than the hard cap gives", "check-sat", (row, options) => row.Fuel = SmtRowCheckRig.Fuel(options) + 1);
        yield return Numbers("the largest fuel a number holds", "check-fuel-in-the-check", (row, _) => row.Fuel = long.MaxValue);
        yield return Numbers("more memory than the host's cap of it", "check-sat", (row, options) => row.PeakMemoryBytes = SmtRowCheckRig.HostMemoryBytes(options) + 1);
        yield return Numbers("two checks of a check", "check-sat", (row, _) => row.Checks = 2);
        yield return Numbers("two checks of a proof", "prove-proved", (row, _) => row.Checks = 2);
        yield return Numbers("more checks than the models an enumeration looks for", "enumerate-complete", (row, _) => row.Checks = SmtSessionCases.MAX_MODELS + 1);
        yield return Numbers("a model without a check", "check-sat", (row, _) => (row.Checks, row.ResourceCount) = (0, 0));
        yield return Numbers("more models than checks", "enumerate-limit", (row, _) => row.Checks = 1);
        yield return Numbers("a verdict without a check", "check-unsat", (row, _) => (row.Checks, row.ResourceCount) = (0, 0));
        yield return Numbers("a resource count without a check", "check-fuel-before-the-check", (row, _) => row.ResourceCount = 1);
        yield return Numbers("fuel below zero", "check-sat", (row, _) => row.Fuel = -1);
        yield return Numbers("checks below zero", "check-error-in-the-problem", (row, _) => row.Checks = -1);
        yield return Numbers("a resource count below zero", "check-sat", (row, _) => row.ResourceCount = -1);
        yield return Numbers("memory below zero", "check-sat", (row, _) => row.PeakMemoryBytes = -1);
    }

    private static IEnumerable<TestCaseData> TextsNoSessionWrites()
    {
        var half = HALF_A_PAIR.ToString();

        // More than a session's output cap in all, however it is shared out.
        yield return Words("a model past the output cap", "check-sat", row => row.Models = [new string('m', SmtRowCheckRig.OutputLimit + 1)]);
        yield return Words("a model of twenty MiB", "check-sat", row => row.Models = [new string('m', 20 << 20)]);
        yield return Words("a model and values past the output cap together", "check-sat-values",
            row => { row.Models = [new string('m', SmtRowCheckRig.OutputLimit / 2)]; row.Values = [new string('v', SmtRowCheckRig.OutputLimit / 2 + 1)]; });
        yield return Words("models past the output cap together", "enumerate-limit",
            row => { row.Models = [new string('m', SmtRowCheckRig.OutputLimit / 2), new string('m', SmtRowCheckRig.OutputLimit / 2)]; row.Values = [VALUES, VALUES]; });

        // Each text within what a session keeps of it.
        yield return Words("a message longer than a row's", "check-error-in-the-problem", row => row.Message = new string('e', SmtResultWords.MAX_MESSAGE_CHARACTERS + 1));
        yield return Words("the solver's word longer than a session keeps", "check-unknown-other", row => row.ReasonText = new string('r', SmtRowCheckRig.ReasonCharacters + 1));
        yield return Words("a transcript of more bytes than its end", "check-sat", row => row.Transcript = new string((char)0xE9, SmtRowCheckRig.TranscriptLimit / 2 + 1));
        yield return Words("a transcript of more characters than its end", "check-sat", row => row.Transcript = new string('t', SmtRowCheckRig.TranscriptLimit + 1));

        // A text that is no text: half of a surrogate pair alone.
        yield return Words("half a pair in the message", "check-error-in-the-problem", row => row.Message = "unknown constant " + half);
        yield return Words("half a pair in the solver's word", "check-unknown-other", row => row.ReasonText = half);
        yield return Words("half a pair in a model", "check-sat", row => row.Models = [MODEL + half]);
        yield return Words("half a pair in the values", "check-sat-values", row => row.Values = [half + VALUES]);
        yield return Words("half a pair in the transcript", "check-sat", row => row.Transcript += half);
        yield return Words("the second half first", "check-sat", row => row.Models = [THE_OTHER_HALF + half + MODEL]);
        yield return Words("a model that is not there", "check-sat", row => row.Models = [null!]);
    }

    private static IEnumerable<TestCaseData> Edges()
    {
        var pair = HALF_A_PAIR.ToString() + THE_OTHER_HALF;

        yield return Numbers("all the fuel of the hard cap", "check-fuel-in-the-check", (row, options) => row.Fuel = SmtRowCheckRig.Fuel(options));
        yield return Numbers("the memory at the host's cap", "check-memory-exit", (row, options) => row.PeakMemoryBytes = SmtRowCheckRig.HostMemoryBytes(options));
        yield return Numbers("a resource count above thirty-two bits", "check-sat", (row, _) => row.ResourceCount = 5_000_000_000);
        yield return Numbers("a model that fills the output cap", "check-sat", (row, _) => row.Models = [new string('m', SmtRowCheckRig.OutputLimit)]);
        yield return Numbers("a message of a row's length", "check-error-in-the-problem", (row, _) => row.Message = new string('e', SmtResultWords.MAX_MESSAGE_CHARACTERS));
        yield return Numbers("the solver's word at its length", "check-unknown-other", (row, _) => row.ReasonText = new string('r', SmtRowCheckRig.ReasonCharacters));
        yield return Numbers("a transcript that fills its end", "check-sat", (row, _) => row.Transcript = new string((char)0xE9, SmtRowCheckRig.TranscriptLimit / 2));
        yield return Numbers("a whole pair in a model", "check-sat", (row, _) => row.Models = [MODEL + pair]);
        yield return Numbers("a replaced character in a model", "check-sat", (row, _) => row.Models = [MODEL + (char)0xFFFD]);
    }

    private static TestCaseData Words(string name, string shape, Action<SmtResultData> change)
    {
        return new TestCaseData(shape, change).SetArgDisplayNames(name);
    }

    private static TestCaseData Numbers(string name, string shape, Action<SmtResultData, SmtOptionsData> change)
    {
        return new TestCaseData(shape, change).SetArgDisplayNames(name);
    }

    #endregion

    #region Tools

    /// <summary>The rows of the three statuses that carry a code, each as a session or the solver's entry makes it.</summary>
    private static string[] Carriers()
    {
        return ["refused-hash", "check-engine-error", "no-sandbox"];
    }

    /// <summary>Whether the host takes the row of a case with another code in it.</summary>
    private static bool TakenWith(string shape, string code)
    {
        var one = SmtSessionCases.Of(shape);
        var row = SmtRowCheckRig.Row(one);

        row.Code = code;
        row.Digest = SmtResultDigest.Of(row);
        return SmtRowCheckRig.Holds(row, one.Problem, one.Options);
    }

    /// <summary>The row of a case is taken as the session wrote it, and refused once it is changed, its digest made its own again.</summary>
    private static void AssertRefused(string shape, Action<SmtResultData> change)
    {
        var one = SmtSessionCases.Of(shape);
        var row = SmtRowCheckRig.Row(one);
        Assert.That(SmtRowCheckRig.Holds(row, one.Problem, one.Options), Is.True, $"the row as the session wrote it: {Describe(row)}");

        change(row);
        row.Digest = SmtResultDigest.Of(row);

        Assert.That(SmtRowCheckRig.Holds(row, one.Problem, one.Options), Is.False, $"the row a liar made of it: {Describe(row)}");
    }

    private static string Describe(SmtResultData row)
    {
        return $"{row.Status} '{row.Code}' verdict {row.Verdict} reason {row.Reason} proof {row.Proof} count {row.Count} complete {row.Complete} limit {row.LimitReached} "
               + $"models {row.Models?.Count} values {row.Values?.Count} checks {row.Checks} resource {row.ResourceCount} fuel {row.Fuel} peak {row.PeakMemoryBytes}";
    }

    #endregion
}
