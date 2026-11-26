using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Planning;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Planning;

/// <summary>
/// Where a package ends: at the planner's number of problems, or before the next problem's text
/// would take the package past its bound of characters, whichever comes first; a problem larger
/// than the bound still goes, alone. The cutter is held to that alone, and the run's cut (the
/// publisher, as Smt.Split and the repair and dispute rounds store packages) is held to the
/// preflight's count of the same problems.
/// </summary>
[TestFixture]
public sealed class SmtPackageCutterTests
{
    #region Constants

    // A problem's text just under the model package's ceiling of 4,194,304 characters: nine of
    // them pass the 32 Mi characters of a package, eight do not.
    private const int LARGE_TEXT = 4_000_000;

    #endregion

    #region Cutter Tests

    [Test]
    public void APackageEndsAtThePlannersNumberOfProblemsTest()
    {
        var cutter = new SmtPackageCutter(3);
        var starts = new List<bool>();
        for (var problem = 0; problem < 7; problem++)
        {
            starts.Add(cutter.StartsNew(10));
            if (starts[^1])
                cutter.Reset();

            cutter.Add(10);
        }

        Assert.That(starts, Is.EqualTo(new[] { false, false, false, true, false, false, true }), "a new package at the fourth and the seventh problem");
    }

    [Test]
    public void APackageEndsBeforeTheNextTextWouldTakeItPastItsBoundTest()
    {
        var cutter = new SmtPackageCutter(SmtPackagePlanner.MIN_PACKAGE_PROBLEMS);
        cutter.Add(SmtPackagePlanner.MAX_PACKAGE_CHARACTERS - 10);

        Assert.Multiple(() =>
        {
            Assert.That(cutter.StartsNew(10), Is.False, "up to the bound, the text still fits");
            Assert.That(cutter.StartsNew(11), Is.True, "one character past it starts a package");
            Assert.That(cutter.Problems, Is.EqualTo(1));
            Assert.That(cutter.Characters, Is.EqualTo(SmtPackagePlanner.MAX_PACKAGE_CHARACTERS - 10));
        });
    }

    [Test]
    public void AProblemLargerThanTheBoundGoesAloneTest()
    {
        var cutter = new SmtPackageCutter(SmtPackagePlanner.MIN_PACKAGE_PROBLEMS);

        Assert.Multiple(() =>
        {
            Assert.That(cutter.StartsNew(SmtPackagePlanner.MAX_PACKAGE_CHARACTERS + 1), Is.False, "an empty package takes any problem");
            cutter.Add(SmtPackagePlanner.MAX_PACKAGE_CHARACTERS + 1);
            Assert.That(cutter.StartsNew(0), Is.True, "and nothing goes beside it");
        });
    }

    [Test]
    public void ResetEmptiesThePackageTest()
    {
        var cutter = new SmtPackageCutter(2);
        cutter.Add(5);
        cutter.Add(5);
        cutter.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(cutter.Problems, Is.Zero);
            Assert.That(cutter.Characters, Is.Zero);
            Assert.That(cutter.StartsNew(5), Is.False);
        });
    }

    [Test]
    public void ANumberOfProblemsBelowOneIsOneTest()
    {
        var cutter = new SmtPackageCutter(0);
        cutter.Add(1);

        Assert.That(cutter.StartsNew(1), Is.True, "a package holds one problem at the least");
    }

    #endregion

    #region Planner Tests

    [TestCase(new long[] { 10, 10, 10 }, 1)]
    [TestCase(new long[] { LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT }, 1)]
    [TestCase(new long[] { LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT, LARGE_TEXT }, 2)]
    [TestCase(new long[] { 40_000_000, 10, 40_000_000 }, 3)]
    public void ThePreflightCountsThePackagesByTextTest(long[] characters, int packages)
    {
        Assert.That(SmtPackagePlanner.Count(characters), Is.EqualTo(packages));
    }

    [Test]
    public void ThePreflightCountsThePackagesByNumberTest()
    {
        var characters = Enumerable.Repeat(10L, 2 * SmtPackagePlanner.MIN_PACKAGE_PROBLEMS + 1).ToList();

        Assert.That(SmtPackagePlanner.Count(characters), Is.EqualTo(3), "sixteen, sixteen and one");
    }

    [Test]
    public void TheRunCutsTheProblemsWhereThePreflightSaysTest()
    {
        // Nine problems of a large text: the run's packages are eight and one, as the preflight counts them.
        var problems = Enumerable.Range(0, 9).Select(index => new SmtProblemData { Index = index, Id = $"p{index}", Hash = new string('a', 64), Text = new string('x', LARGE_TEXT) }).ToList();
        var folder = Path.Combine(Path.GetTempPath(), $"witai_smt_cut_{Guid.NewGuid():N}");
        try
        {
            var blobs = new SmtTestBlobService(Path.Combine(folder, "blobs"));
            var publisher = new SmtPackagePublisher(problems.Count, SmtOptionsRules.Resolve(new SmtOptionsData()), blobs, Path.Combine(folder, "packages"));
            foreach (var problem in problems)
                publisher.AddAsync(problem).GetAwaiter().GetResult();

            var packages = publisher.CompleteAsync().GetAwaiter().GetResult();

            Assert.Multiple(() =>
            {
                Assert.That(packages.Select(package => package.ProblemCount), Is.EqualTo(new[] { 8, 1 }));
                Assert.That(packages, Has.Count.EqualTo(SmtPackagePlanner.Count(problems.Select(SmtPackagePlanner.Characters).ToList())));
                Assert.That(publisher.Problems, Is.EqualTo(9));
            });
        }
        finally
        {
            SmtRigModules.TryDelete(folder);
        }
    }

    #endregion
}
