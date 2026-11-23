using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Live.Pack;

namespace OutWit.Controller.AI.Smt.Live.Tests.Pack;

[TestFixture]
public sealed class SmtLiveProblemIdsTests
{
    #region Constants

    /// <summary>An id of a problem set's row, as the model package's schema states it.</summary>
    private static readonly Regex ID = new("^[A-Za-z0-9._/+:@=-]{1,64}\\z");

    #endregion

    #region Tests

    [TestCase("p000.smt2", "p000")]
    [TestCase("QF_LIA/a/b.smt2", "QF_LIA/a/b")]
    [TestCase("dir\\x.SMT2", "dir/x")]
    [TestCase("v1.2+x@y=z:w.smt2", "v1.2+x@y=z:w")]
    [TestCase("a b(c)!.smt2", "a_b_c__")]
    [TestCase(".smt2", ".smt2")]
    [TestCase("x?.smt2", "x_")]
    public void AnIdIsThePathMadeAValidIdTest(string path, string id)
    {
        Assert.That(SmtLiveProblemIds.Plain(path), Is.EqualTo(id));
    }

    [Test]
    public void ALongPathKeepsItsTailBehindAHashTest()
    {
        var path = "non-incremental/QF_LIA/20180326-Bromberger/more_slacked/CAV_2009_benchmarks/smt/35-vars/v35_problem__004.smt2.slack.smt2";

        var id = SmtLiveProblemIds.Plain(path);

        Assert.That(id, Has.Length.EqualTo(64));
        Assert.That(id, Does.Match("^[0-9a-f]{8}="));
        Assert.That(id, Does.EndWith("v35_problem__004.smt2.slack"));
        Assert.That(id, Does.Match(ID.ToString()));
        Assert.That(SmtLiveProblemIds.Plain(path.Replace("004", "005")), Is.Not.EqualTo(id), "the hash is of the whole path");
    }

    [Test]
    public void IdsThatWouldCollideAreMadeDifferentTheSameWayEveryTimeTest()
    {
        var first = new SmtLiveProblemIds();
        var second = new SmtLiveProblemIds();
        var paths = new[] { "a b.smt2", "a_b.smt2", "a(b.smt2", "a_b.SMT2" };

        var ids = paths.Select(first.Of).ToList();

        Assert.That(ids, Is.Unique);
        Assert.That(ids[0], Is.EqualTo("a_b"), "the first file keeps the plain id");
        Assert.That(ids.Skip(1), Has.All.Match("^[0-9a-f]{8}=a_b$"));
        Assert.That(paths.Select(second.Of), Is.EqualTo(ids), "the same folder gives the same ids");
    }

    [Test]
    public void EveryIdIsAValidIdTest()
    {
        var ids = new SmtLiveProblemIds();
        var paths = new[]
        {
            "x.smt2", "\u0437\u0430\u0434\u0430\u0447\u0430.smt2", "tab\there.smt2", new string('a', 200) + ".smt2",
            new string('/', 3) + "x.smt2", "deep/" + string.Concat(Enumerable.Repeat("folder/", 20)) + "f.smt2", "x.smt2.bak.smt2"
        };

        foreach (var path in paths)
            Assert.That(ids.Of(path), Does.Match(ID.ToString()), path);
    }

    #endregion
}
