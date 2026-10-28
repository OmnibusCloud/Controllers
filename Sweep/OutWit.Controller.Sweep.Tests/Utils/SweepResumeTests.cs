using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Utils;

namespace OutWit.Controller.Sweep.Tests.Utils;

/// <summary>
/// The schedule of record and the fit check behind a resumed sweep: the
/// schedule variable wins over a fresh plan's, and a cursor that does not
/// fit the plan is named by what is wrong with it rather than run.
/// </summary>
[TestFixture]
public class SweepResumeTests
{
    #region Tools

    private static SweepPlanData Plan(params int[] sizes)
    {
        return new SweepPlanData
        {
            Options = new SweepOptionsData
            {
                Variants = Enumerable.Range(0, sizes.Sum()).Select(index => new SweepVariantData { VariantIndex = index }).ToList(),
                CalculiX = new SweepCalculiXStudyData { BaseDeckBlobId = Guid.NewGuid() }
            },
            ChunkSizes = [.. sizes]
        };
    }

    private static SweepScheduleData Schedule(params int[] sizes)
    {
        return new SweepScheduleData { ChunkSizes = [.. sizes] };
    }

    private static SweepStateData AfterChunks(int chunks, params int[] sizes)
    {
        var harvested = sizes.Take(chunks).Sum();
        return new SweepStateData
        {
            ChunkIndex = chunks,
            NextVariantOrdinal = harvested,
            SucceededCount = harvested,
            ManifestBlobId = harvested > 0 ? Guid.NewGuid() : null
        };
    }

    #endregion

    #region Schedule Tests

    [Test]
    public void TheScheduleVariableWinsOverTheFreshPlanTest()
    {
        // The fleet was six machines wide at the first run and three now: the resumed run
        // keeps the boundaries its manifest was harvested by.
        Assert.That(SweepResume.Sizes(Plan(3, 3, 1), Schedule(6, 1)), Is.EqualTo(new[] { 6, 1 }));
    }

    [Test]
    public void WithoutAScheduleTheRunGoesByThePlansTest()
    {
        var plan = Plan(2, 2);

        Assert.That(SweepResume.Sizes(plan, null), Is.EqualTo(new[] { 2, 2 }));
        Assert.That(SweepResume.Sizes(plan, new SweepScheduleData()), Is.EqualTo(new[] { 2, 2 }), "an empty schedule is no schedule");
        Assert.That(SweepResume.ScheduleOf(plan).ChunkSizes, Is.EqualTo(new[] { 2, 2 }));
        Assert.That(SweepResume.Findings(plan, AfterChunks(1, 2, 2), null), Is.Empty);
    }

    [Test]
    public void TheZeroCursorIsNotStartedTest()
    {
        Assert.That(SweepResume.IsStarted(new SweepStateData()), Is.False);
        Assert.That(SweepResume.IsStarted(AfterChunks(1, 2, 2)), Is.True);
    }

    #endregion

    #region Fit Tests

    [Test]
    public void AStateThatFitsHasNoFindingsTest()
    {
        Assert.That(SweepResume.Findings(Plan(2, 2, 3), AfterChunks(2, 2, 2, 3), Schedule(2, 2, 3)), Is.Empty);
        Assert.That(SweepResume.Findings(Plan(2, 2, 3), AfterChunks(3, 2, 2, 3), Schedule(2, 2, 3)), Is.Empty, "a fully harvested state resumes into nothing left");
        Assert.That(SweepResume.Findings(Plan(3, 3, 1), AfterChunks(1, 6, 1), Schedule(6, 1)), Is.Empty, "the handed schedule, not the fresh plan, is what the cursor must fit");
    }

    [Test]
    public void AScheduleOfAnotherStudyIsNamedTest()
    {
        var findings = SweepResume.Findings(Plan(2, 2), AfterChunks(1, 3, 3), Schedule(3, 3));

        Assert.That(findings, Has.Count.EqualTo(1));
        Assert.That(findings[0], Does.Contain("[3, 3]").And.Contain("covers 6 variant(s), the study has 4"));
    }

    [Test]
    public void ACursorOutsideTheScheduleIsNamedTest()
    {
        var state = AfterChunks(2, 2, 2);
        state.ChunkIndex = 3;

        var findings = SweepResume.Findings(Plan(2, 2), state, Schedule(2, 2));

        Assert.That(findings.Single(), Does.Contain("chunk 3 is outside a schedule of 2 chunk(s)"));
    }

    [Test]
    public void ANextOrdinalOffItsChunkBoundaryIsNamedTest()
    {
        var state = AfterChunks(1, 2, 2);
        state.NextVariantOrdinal = 3;
        state.SucceededCount = 3;

        var findings = SweepResume.Findings(Plan(2, 2), state, Schedule(2, 2));

        Assert.That(findings.Single(), Does.Contain("the next ordinal 3 is not the 2 variant(s) of the 1 chunk(s) before it"));
    }

    [Test]
    public void CountsThatDoNotAddUpAreNamedTest()
    {
        var state = AfterChunks(1, 2, 2);
        state.FailedCount = 1;

        var findings = SweepResume.Findings(Plan(2, 2), state, Schedule(2, 2));

        Assert.That(findings.Single(), Does.Contain("2 succeeded, 1 failed, 0 refused").And.Contain("do not add up to the 2 variant(s) harvested"));
    }

    [Test]
    public void AHarvestedStateWithoutAManifestIsNamedTest()
    {
        var state = AfterChunks(1, 2, 2);
        state.ManifestBlobId = null;

        var findings = SweepResume.Findings(Plan(2, 2), state, Schedule(2, 2));

        Assert.That(findings.Single(), Does.Contain("the manifest blob is missing"));
    }

    #endregion
}
