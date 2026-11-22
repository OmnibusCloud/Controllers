using MemoryPack;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Model;

/// <summary>
/// The data types of a problem set as values: a problem and a run's options travel through
/// MemoryPack whole, compare by every member, and copy deep.
/// </summary>
[TestFixture]
public class SmtModelDataTests
{
    #region Problem Tests

    [Test]
    public void ProblemRoundTripsThroughMemoryPackTest()
    {
        var problem = Problem();

        var back = MemoryPackSerializer.Deserialize<SmtProblemData>(MemoryPackSerializer.Serialize(problem))!;

        Assert.That(back.Is(problem), Is.True);
        Assert.That((back.Index, back.Id, back.Query, back.MaxModels, back.Expected), Is.EqualTo((41, "cage-6x6-000412", SmtQuery.Enumerate, 2, SmtProblemStatus.Sat)));
        Assert.That(back.Text, Is.EqualTo("(declare-const c11 Int)\n(assert (> c11 0))\n"));
        Assert.That(back.Hash, Is.EqualTo(new string('a', 64)));
        Assert.That(back.Values, Is.EqualTo(new[] { "(+ c11 1)" }));
        Assert.That(back.Project, Is.EqualTo(new[] { "c11", "|c 12|" }));
        Assert.That((back.Goal, back.Tags), Is.EqualTo(("(> c11 0)", "kit b")));
    }

    [Test]
    public void ProblemComparesEveryMemberTest()
    {
        var problem = Problem();
        var changes = new Action<SmtProblemData>[]
        {
            other => other.Index = 42,
            other => other.Id = "other",
            other => other.Query = SmtQuery.Check,
            other => other.Text = "(assert true)\n",
            other => other.Hash = new string('b', 64),
            other => other.Values.Add("x"),
            other => other.Values[0] = "(+ c11 2)",
            other => other.Project.RemoveAt(1),
            other => other.Project.Reverse(),
            other => other.MaxModels = 3,
            other => other.Goal = "",
            other => other.Expected = SmtProblemStatus.Unsat,
            other => other.Tags = ""
        };

        Assert.That(problem.Clone().Is(problem), Is.True);
        Assert.That(new SmtProblemData().Is(new SmtProblemData()), Is.True);
        Assert.That(problem.Is(new SmtRefusalData()), Is.False);

        foreach (var change in changes)
        {
            var other = problem.Clone();
            change(other);

            Assert.That(other.Is(problem), Is.False);
            Assert.That(problem.Is(Problem()), Is.True, "a change of the copy does not reach what it was copied from");
        }
    }

    [Test]
    public void ProblemSaysWhichOneItIsWithoutItsTextTest()
    {
        var text = Problem().ToString();

        Assert.That(text, Does.Contain("cage-6x6-000412").And.Contain("41").And.Contain("Enumerate"));
        Assert.That(text, Does.Not.Contain("declare-const"));
    }

    #endregion

    #region Options Tests

    [Test]
    public void OptionsRoundTripThroughMemoryPackTest()
    {
        var options = Options();

        var back = MemoryPackSerializer.Deserialize<SmtOptionsData>(MemoryPackSerializer.Serialize(options))!;

        Assert.That(back.Is(options), Is.True);
        Assert.That((back.Profile, back.Query, back.MaxModels, back.Seed), Is.EqualTo(("profile-a", SmtQuery.Prove, 7, 11)));
        Assert.That((back.ResourceLimit, back.HardCapSeconds, back.MemoryMegabytes, back.SamplingRate), Is.EqualTo((4_294_967_295L, 120, 2_048, 0.25)));
    }

    [Test]
    public void OptionsCompareEveryMemberTest()
    {
        var options = Options();
        var changes = new Action<SmtOptionsData>[]
        {
            other => other.Profile = "profile-b",
            other => other.Query = SmtQuery.Check,
            other => other.MaxModels = 8,
            other => other.Seed = 12,
            other => other.ResourceLimit = 1,
            other => other.HardCapSeconds = 60,
            other => other.MemoryMegabytes = 1_024,
            other => other.SamplingRate = 0.5
        };

        Assert.That(options.Clone().Is(options), Is.True);
        Assert.That(options.Clone(), Is.Not.SameAs(options));
        Assert.That(new SmtOptionsData().Is(new SmtOptionsData()), Is.True);

        foreach (var change in changes)
        {
            var other = options.Clone();
            change(other);

            Assert.That(other.Is(options), Is.False);
        }
    }

    [Test]
    public void SamplingRateOfZeroTravelsAsZeroTest()
    {
        // A new instance holds the default; a rate that was set to 0 stays 0 on the way.
        var off = new SmtOptionsData { SamplingRate = 0 };

        var back = MemoryPackSerializer.Deserialize<SmtOptionsData>(MemoryPackSerializer.Serialize(off))!;

        Assert.That(back.SamplingRate, Is.Zero);
        Assert.That(back.Is(new SmtOptionsData()), Is.False);
    }

    #endregion

    #region Tools

    private static SmtProblemData Problem()
    {
        return new SmtProblemData
        {
            Index = 41,
            Id = "cage-6x6-000412",
            Query = SmtQuery.Enumerate,
            Text = "(declare-const c11 Int)\n(assert (> c11 0))\n",
            Hash = new string('a', 64),
            Values = ["(+ c11 1)"],
            Project = ["c11", "|c 12|"],
            MaxModels = 2,
            Goal = "(> c11 0)",
            Expected = SmtProblemStatus.Sat,
            Tags = "kit b"
        };
    }

    private static SmtOptionsData Options()
    {
        return new SmtOptionsData { Profile = "profile-a", Query = SmtQuery.Prove, MaxModels = 7, Seed = 11, ResourceLimit = 4_294_967_295, HardCapSeconds = 120, MemoryMegabytes = 2_048, SamplingRate = 0.25 };
    }

    #endregion
}
