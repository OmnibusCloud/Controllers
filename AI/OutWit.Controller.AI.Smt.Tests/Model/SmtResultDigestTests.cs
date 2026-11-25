using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Model;

/// <summary>
/// The digest of a row hashes the row's own characters and never a stand-in for one. A text with
/// half of a surrogate pair alone has no bytes of UTF-8; an encoder that puts the replacement
/// character there would give it the bytes, and so the digest, of a row that holds that character
/// in the same place. Such a row has no digest: <c>Of</c> gives the empty text, <c>Holds</c> is
/// false whatever the row carries, and neither throws. A row of valid text keeps its digest to
/// the bit, the replacement character and whole pairs included.
/// </summary>
[TestFixture]
public sealed class SmtResultDigestTests
{
    #region Constants

    private const char HIGH_HALF = (char)0xD800;
    private const char LOW_HALF = (char)0xDC00;
    private const char REPLACEMENT = (char)0xFFFD;

    /// <summary>
    /// The digest of <see cref="Row"/> with nothing changed, as this package has always computed
    /// it: a change of the encoder that moved one bit of an honest row's digest would show here,
    /// without the solver.
    /// </summary>
    private const string DIGEST = "73c2c7229048d9f7ec2f5f20be8404120e83eb6a8a59f3941efda8df8a27e755";

    #endregion

    #region Tests

    [TestCaseSource(nameof(Texts))]
    public void ARowWithHalfAPairInATextHasNoDigestTest(string name, Action<SmtResultData, string> put)
    {
        foreach (var half in new[] { HIGH_HALF.ToString(), LOW_HALF.ToString(), "a" + HIGH_HALF + "b", LOW_HALF.ToString() + HIGH_HALF })
        {
            var broken = Row();
            put(broken, half);
            var replaced = Row();
            put(replaced, half.Replace(HIGH_HALF, REPLACEMENT).Replace(LOW_HALF, REPLACEMENT));

            // The row carries the digest of the row that holds the replacement character in the same place.
            broken.Digest = SmtResultDigest.Of(replaced);

            Assert.Multiple(() =>
            {
                Assert.That(SmtResultDigest.Of(replaced), Does.Match("^[0-9a-f]{64}$"), $"{name}: the replacement character is a character");
                Assert.That(SmtResultDigest.Of(broken), Is.Not.EqualTo(SmtResultDigest.Of(replaced)), $"{name}: two different rows, one digest");
                Assert.That(SmtResultDigest.Of(broken), Is.Empty, $"{name}: no digest");
                Assert.That(SmtResultDigest.Holds(broken), Is.False, $"{name}: and it holds none, whatever it carries");
            });
        }
    }

    [Test]
    public void ARowWithoutADigestHoldsNoneEvenWhenItCarriesNoneTest()
    {
        var broken = Row();
        broken.Message = HIGH_HALF.ToString();
        broken.Digest = "";

        Assert.That(SmtResultDigest.Holds(broken), Is.False, "the empty text is no digest to hold");
    }

    [Test]
    public void ARowOfValidTextKeepsItsDigestTest()
    {
        var pair = HIGH_HALF.ToString() + LOW_HALF;
        var whole = Row();
        whole.Models = ["(define-fun a () String \"" + pair + REPLACEMENT + "\")"];

        var again = whole.Clone();
        again.Digest = SmtResultDigest.Of(whole);

        Assert.Multiple(() =>
        {
            Assert.That(SmtResultDigest.Of(Row()), Is.EqualTo(DIGEST), "an honest row's digest, to the bit");
            Assert.That(SmtResultDigest.Of(whole), Does.Match("^[0-9a-f]{64}$"), "a whole pair and the replacement character are text");
            Assert.That(SmtResultDigest.Holds(again), Is.True);
            Assert.That(SmtResultDigest.Canonical(whole), Does.Contain(pair), "the canonical form is the row's own characters");
        });
    }

    #endregion

    #region Tools

    /// <summary>Every text of a row that the canonical form holds as the node gave it, each with a way to put something into it.</summary>
    private static IEnumerable<TestCaseData> Texts()
    {
        yield return new TestCaseData("the message", (Action<SmtResultData, string>)((row, text) => row.Message = text));
        yield return new TestCaseData("the solver's word", (Action<SmtResultData, string>)((row, text) => row.ReasonText = text));
        yield return new TestCaseData("a model", (Action<SmtResultData, string>)((row, text) => row.Models[1] = text));
        yield return new TestCaseData("a value", (Action<SmtResultData, string>)((row, text) => row.Values[0] = text));
        yield return new TestCaseData("the transcript", (Action<SmtResultData, string>)((row, text) => row.Transcript = text));
        yield return new TestCaseData("the id", (Action<SmtResultData, string>)((row, text) => row.Id = text));
        yield return new TestCaseData("the code", (Action<SmtResultData, string>)((row, text) => row.Code = text));
    }

    private static SmtResultData Row()
    {
        return new SmtResultData
        {
            Index = 12,
            Id = "cage-1",
            Hash = new string('a', 64),
            Ticket = new string('c', 32),
            Query = SmtQuery.Enumerate,
            Status = SmtResultWords.OK,
            Code = "",
            Message = "",
            Verdict = SmtResultWords.SAT,
            Reason = SmtResultWords.NONE,
            ReasonText = "",
            Proof = SmtResultWords.NONE,
            Count = 2,
            Complete = false,
            LimitReached = true,
            Models = ["(\n  (define-fun a () Int 1)\n)", "(\n  (define-fun a () Int 2)\n)"],
            Values = ["((a 1))", "((a 2))"],
            Checks = 2,
            ResourceCount = 12_345,
            Fuel = 67_890,
            PeakMemoryBytes = 18_874_368,
            Transcript = "> (check-sat)\nsat\n",
            ControllerVersion = "0.2.0",
            Platform = "win-x64",
            WallMs = 12
        };
    }

    #endregion
}
