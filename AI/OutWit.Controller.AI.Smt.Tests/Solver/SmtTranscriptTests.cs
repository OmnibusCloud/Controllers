using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The transcript of a session: the commands sent, each behind its mark, and the answers as
/// they came, of which the last eight kilobytes of UTF-8 are kept, from a whole character on.
/// </summary>
[TestFixture]
public sealed class SmtTranscriptTests
{
    #region Tests

    [Test]
    public void CommandsAreMarkedAndAnswersAreKeptAsTheyCameTest()
    {
        var transcript = new SmtTranscript();

        transcript.Sent(SmtScripts.CheckSat());
        transcript.Answer("sat\n");
        transcript.Note("the solver's stderr");
        transcript.Answer("a line without its end");
        transcript.Answer("");
        transcript.Sent(SmtScripts.GetModel());

        Assert.That(transcript.Tail(), Is.EqualTo("> (check-sat)\nsat\n; the solver's stderr\na line without its end\n> (get-model)\n"));
    }

    [Test]
    public void OnlyTheLastEightKilobytesAreKeptTest()
    {
        var transcript = new SmtTranscript();
        var lines = Enumerable.Range(0, 2_000).Select(index => $"line {index:D6}\n").ToList();

        foreach (var line in lines)
            transcript.Answer(line);

        var whole = string.Concat(lines);
        Assert.Multiple(() =>
        {
            Assert.That(transcript.Tail(), Has.Length.EqualTo(SmtTranscript.LIMIT_BYTES));
            Assert.That(whole, Does.EndWith(transcript.Tail()));
        });
    }

    [Test]
    public void OneAnswerLargerThanTheLimitLeavesItsEndTest()
    {
        var transcript = new SmtTranscript();
        var large = string.Concat(Enumerable.Range(0, 100_000).Select(index => (char)('a' + index % 26)));

        transcript.Sent(SmtScripts.GetModel());
        transcript.Answer(large);

        Assert.That(transcript.Tail(), Is.EqualTo(large[^(SmtTranscript.LIMIT_BYTES - 1)..] + "\n"), "the end of the answer and the line end the transcript gives it");
    }

    [TestCase("\u00e9", 2)]
    [TestCase("\u20ac", 3)]
    [TestCase("\U0001F600", 4)]
    public void TheLimitIsCountedInBytesAndCutsAtAWholeCharacterTest(string character, int bytes)
    {
        var transcript = new SmtTranscript();
        var text = string.Concat(Enumerable.Repeat(character, 5_000));

        // One byte first, so that the limit does not fall between two characters by itself.
        transcript.Answer("a" + text + "\n");
        var tail = transcript.Tail();

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetByteCount(character), Is.EqualTo(bytes));
            Assert.That(Encoding.UTF8.GetByteCount(tail), Is.InRange(SmtTranscript.LIMIT_BYTES - bytes + 1, SmtTranscript.LIMIT_BYTES));
            Assert.That(tail, Does.EndWith(character + "\n"));
            Assert.That(tail.Replace(character, ""), Is.EqualTo("\n"), "nothing but whole characters: no half of one at the start");
        });
    }

    [Test]
    public void AnEmptyTranscriptIsEmptyTest()
    {
        Assert.That(new SmtTranscript().Tail(), Is.Empty);
    }

    #endregion
}
