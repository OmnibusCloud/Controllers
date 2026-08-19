using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Tasks;

/// <summary>
/// Deterministic splitting and per-task attachment subsetting (docs 03, section 11) over a
/// synthetic package: identities, subsets, ordering, time values, limits.
/// </summary>
[TestFixture]
public sealed class ParaViewTaskSplitterTests
{
    #region Fixtures

    private static ParaViewSceneRefData Scene()
    {
        return new ParaViewSceneRefData
        {
            StateBlobId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            StateSha256 = new string('a', 64),
            StateSize = 100,
            Attachments =
            [
                Attachment("data/mesh.vtu", 1000),
                Attachment("data/series.pvd", 10, ParaViewAttachmentRole.SeriesIndex, "series"),
                Attachment("data/series_0.vtu", 200, seriesGroup: "series", timesteps: [0]),
                Attachment("data/series_1.vtu", 300, seriesGroup: "series", timesteps: [1]),
                Attachment("data/series_2.vtu", 400, seriesGroup: "series", timesteps: [2]),
                Attachment("data/coarse_a.vtu", 50, seriesGroup: "coarse", timesteps: [0, 1]),
                Attachment("data/coarse_b.vtu", 60, seriesGroup: "coarse", timesteps: [2]),
                Attachment("data/fallback_x.vtu", 5, seriesGroup: "fallback"),
                Attachment("data/fallback_y.vtu", 6, seriesGroup: "fallback"),
                Attachment("textures/wood.png", 7, ParaViewAttachmentRole.Auxiliary)
            ]
        };
    }

    private static ParaViewAttachmentRefData Attachment(string path, long size, ParaViewAttachmentRole role = ParaViewAttachmentRole.ReaderInput, string seriesGroup = "", int[]? timesteps = null)
    {
        return new ParaViewAttachmentRefData
        {
            BlobId = Guid.NewGuid(),
            LogicalPath = path,
            Sha256 = new string((char)('a' + path.Length % 6), 64),
            Size = size,
            Role = role,
            SeriesGroup = seriesGroup,
            TimestepIndices = timesteps == null ? [] : [.. timesteps]
        };
    }

    private static ParaViewValidationReportData Report(params int[] indices)
    {
        return new ParaViewValidationReportData
        {
            IsValid = true,
            ResolvedViewId = "RenderView1",
            ResolvedTimestepIndices = [.. indices],
            TimestepValues = [0.0, 0.5, 1.0],
            PackageDigest = ParaViewPackageDigest.ComputePackageDigest(Scene())
        };
    }

    private static ParaViewOutputOptionsData Options()
    {
        return new ParaViewOutputOptionsData { Width = 640, Height = 480 };
    }

    #endregion

    #region Tests

    [Test]
    public void SubsetsContainExactlyTheTimestepsFilesPlusStaticsTest()
    {
        var tasks = ParaViewTaskSplitter.Split(Scene(), Report(0, 1, 2), Options());

        Assert.That(tasks, Has.Count.EqualTo(3));

        string[] Paths(int i) => tasks[i].Attachments.Select(me => me.LogicalPath).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(Paths(0), Is.EquivalentTo(new[] { "data/mesh.vtu", "data/series.pvd", "data/series_0.vtu", "data/coarse_a.vtu", "data/fallback_x.vtu", "data/fallback_y.vtu", "textures/wood.png" }));
            Assert.That(Paths(1), Is.EquivalentTo(new[] { "data/mesh.vtu", "data/series.pvd", "data/series_1.vtu", "data/coarse_a.vtu", "data/fallback_x.vtu", "data/fallback_y.vtu", "textures/wood.png" }));
            Assert.That(Paths(2), Is.EquivalentTo(new[] { "data/mesh.vtu", "data/series.pvd", "data/series_2.vtu", "data/coarse_b.vtu", "data/fallback_x.vtu", "data/fallback_y.vtu", "textures/wood.png" }));
            Assert.That(tasks[0].SubsetBytes, Is.EqualTo(100 + 1000 + 10 + 200 + 50 + 5 + 6 + 7));
            Assert.That(tasks[2].SubsetBytes, Is.EqualTo(100 + 1000 + 10 + 400 + 60 + 5 + 6 + 7));
        });
    }

    [Test]
    public void TasksAreOrderedByTimestepWithIndicesAndTimeValuesTest()
    {
        var tasks = ParaViewTaskSplitter.Split(Scene(), Report(2, 0), Options());

        Assert.Multiple(() =>
        {
            Assert.That(tasks.Select(me => me.TaskIndex), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(tasks.Select(me => me.TimestepIndex), Is.EqualTo(new[] { 2, 0 }));
            Assert.That(tasks.Select(me => me.TimeValue), Is.EqualTo(new double?[] { 1.0, 0.0 }));
            Assert.That(tasks.All(me => me.ViewId == "RenderView1"), Is.True);
            Assert.That(tasks.All(me => me.Options.ViewId == "RenderView1"), Is.True);
            Assert.That(tasks.All(me => me.DatasetId == ""), Is.True);
        });
    }

    [Test]
    public void TaskIdentitiesAreDeterministicAndDistinctTest()
    {
        var first = ParaViewTaskSplitter.Split(Scene(), Report(0, 1, 2), Options());
        var second = ParaViewTaskSplitter.Split(Scene(), Report(0, 1, 2), Options());

        Assert.Multiple(() =>
        {
            Assert.That(first.Select(me => me.TaskId), Is.EqualTo(second.Select(me => me.TaskId)));
            Assert.That(first.Select(me => me.TaskId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(first.All(me => me.TaskId.Length == 64), Is.True);
            Assert.That(first.All(me => me.PackageDigest == first[0].PackageDigest), Is.True);
        });
    }

    [Test]
    public void IdentityChangesWithOutputOptionsButNotWithFrameSelectionTest()
    {
        var baseline = ParaViewTaskSplitter.Split(Scene(), Report(1), Options());

        var wider = Options();
        wider.Width = 1280;
        var differentSize = ParaViewTaskSplitter.Split(Scene(), Report(1), wider);

        var otherFrames = Options();
        otherFrames.Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Explicit, Indices = [1, 2] };
        var sameTask = ParaViewTaskSplitter.Split(Scene(), Report(1), otherFrames);

        Assert.Multiple(() =>
        {
            Assert.That(differentSize[0].TaskId, Is.Not.EqualTo(baseline[0].TaskId));
            Assert.That(sameTask[0].TaskId, Is.EqualTo(baseline[0].TaskId));
        });
    }

    [Test]
    public void IdentityChangesWhenThePackageChangesTest()
    {
        var scene = Scene();
        var baseline = ParaViewTaskSplitter.Split(scene, Report(0), Options());

        var changed = (ParaViewSceneRefData)scene.Clone();
        changed.Attachments[0].Sha256 = new string('f', 64);
        var report = Report(0);
        report.PackageDigest = ParaViewPackageDigest.ComputePackageDigest(changed);

        var other = ParaViewTaskSplitter.Split(changed, report, Options());

        Assert.That(other[0].TaskId, Is.Not.EqualTo(baseline[0].TaskId));
    }

    [Test]
    public void PackageDigestIsOrderIndependentTest()
    {
        var scene = Scene();
        var shuffled = (ParaViewSceneRefData)scene.Clone();
        shuffled.Attachments.Reverse();

        Assert.That(ParaViewPackageDigest.ComputePackageDigest(shuffled), Is.EqualTo(ParaViewPackageDigest.ComputePackageDigest(scene)));
    }

    [Test]
    public void InvalidReportIsRefusedTest()
    {
        var report = Report(0);
        report.IsValid = false;
        report.Errors.Add("bad");

        var exception = Assert.Throws<InvalidOperationException>(() => ParaViewTaskSplitter.Split(Scene(), report, Options()));
        Assert.That(exception!.Message, Does.Contain("bad"));
    }

    [Test]
    public void PerTaskSubsetLimitIsEnforcedTest()
    {
        var scene = Scene();
        scene.Attachments[0].Size = ParaViewInputLimits.MAX_TASK_SUBSET_BYTES;

        var exception = Assert.Throws<InvalidOperationException>(() => ParaViewTaskSplitter.Split(scene, Report(0), Options()));
        Assert.That(exception!.Message, Does.Contain("per-task limit"));
    }

    [Test]
    public void ClonesNotSharedBetweenTasksTest()
    {
        var tasks = ParaViewTaskSplitter.Split(Scene(), Report(0, 1), Options());
        tasks[0].Options.Width = 1;
        tasks[0].Attachments[0].LogicalPath = "changed";

        Assert.That(tasks[1].Options.Width, Is.EqualTo(640));
        Assert.That(tasks[1].Attachments[0].LogicalPath, Is.EqualTo("data/mesh.vtu"));
    }

    #endregion
}
