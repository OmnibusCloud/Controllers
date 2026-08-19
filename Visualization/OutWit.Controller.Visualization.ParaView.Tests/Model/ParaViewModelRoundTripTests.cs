using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Tests.Model;

/// <summary>
/// MemoryPack round trip + Is/Clone contract for every model DTO with every member populated —
/// a plain class serializes to nothing silently, and a member missing from Is or Clone loses data
/// on the .With() path.
/// </summary>
[TestFixture]
public sealed class ParaViewModelRoundTripTests
{
    #region Fixtures

    public static ParaViewAttachmentRefData Attachment(int index)
    {
        return new ParaViewAttachmentRefData
        {
            BlobId = Guid.NewGuid(),
            LogicalPath = $"data/series/field_{index:D3}.vtu",
            Sha256 = new string((char)('a' + index % 6), 64),
            Size = 1024 * (index + 1),
            Role = ParaViewAttachmentRole.ReaderInput,
            SeriesGroup = "field",
            TimestepIndices = [index, index + 1],
            SeriesOrdinal = index
        };
    }

    public static ParaViewSceneRefData Scene()
    {
        return new ParaViewSceneRefData
        {
            StateBlobId = Guid.NewGuid(),
            StateSha256 = new string('f', 64),
            StateSize = 4096,
            Attachments = [Attachment(0), Attachment(1)],
            Runtime = new ParaViewRuntimeRequirementData
            {
                ParaViewMajor = 6,
                ParaViewMinor = 1,
                ParaViewPatch = 1,
                ProducerPluginVersion = "1.0.0",
                ProducerPlatform = "win-x64",
                Plugins = [new ParaViewPluginRequirementData { Name = "OmnibusCloudFrdReader", Version = "1.0" }]
            },
            TimestepValues = [0.0, 0.5, 1.0],
            PackageManifestJson = "{\"schema\":\"com.omnibuscloud.paraview-package\"}"
        };
    }

    public static ParaViewOutputOptionsData Options()
    {
        return new ParaViewOutputOptionsData
        {
            ViewId = "RenderView1",
            Width = 640,
            Height = 480,
            Format = ParaViewImageFormat.Jpeg,
            TransparentBackground = true,
            Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Explicit, First = 1, Last = 2, Step = 2, Indices = [2, 0] }
        };
    }

    public static ParaViewRenderTaskData RenderTask()
    {
        return new ParaViewRenderTaskData
        {
            TaskId = new string('1', 64),
            TaskIndex = 3,
            StateBlobId = Guid.NewGuid(),
            StateSha256 = new string('e', 64),
            StateSize = 77,
            ViewId = "RenderView1",
            TimestepIndex = 2,
            TimeValue = 0.25,
            Options = Options(),
            Attachments = [Attachment(2)],
            Runtime = Scene().Runtime,
            PackageDigest = new string('9', 64),
            DatasetId = "",
            SubsetBytes = 3149
        };
    }

    public static ParaViewRenderResultData Result()
    {
        return new ParaViewRenderResultData
        {
            TaskId = new string('2', 64),
            TaskIndex = 4,
            ViewId = "RenderView1",
            TimestepIndex = 7,
            TimeValue = 1.75,
            ImageBlobId = Guid.NewGuid(),
            Width = 320,
            Height = 200,
            Format = ParaViewImageFormat.Png,
            ByteSize = 12345,
            RuntimeVersion = "6.1.1",
            ReaderVersion = "1.0.0",
            RenderSeconds = 2.5,
            Diagnostics = "stage=done"
        };
    }

    public static ParaViewValidationReportData Report()
    {
        return new ParaViewValidationReportData
        {
            IsValid = true,
            Errors = [],
            Warnings = ["w1"],
            Fallbacks = ["series group 'x' …"],
            PackageDigest = new string('3', 64),
            ResolvedViewId = "RenderView1",
            ResolvedTimestepIndices = [0, 2, 4],
            TimestepValues = [0, 1, 2, 3, 4],
            AttachmentCount = 5,
            TotalAttachmentBytes = 99999,
            ProxyTypes = ["sources/Contour", "views/RenderView"],
            RequiredPlugins = ["OmnibusCloudFrdReader@1.0"],
            RuntimeVersion = "6.1.1",
            Width = 1920,
            Height = 1080,
            Format = ParaViewImageFormat.Png
        };
    }

    #endregion

    #region Tests

    [Test]
    public void SceneRefRoundTripsTest() => AssertRoundTrip(Scene());

    [Test]
    public void AttachmentRefRoundTripsTest() => AssertRoundTrip(Attachment(5));

    [Test]
    public void OutputOptionsRoundTripTest() => AssertRoundTrip(Options());

    [Test]
    public void RenderTaskRoundTripsTest() => AssertRoundTrip(RenderTask());

    [Test]
    public void RenderResultRoundTripsTest() => AssertRoundTrip(Result());

    [Test]
    public void ValidationReportRoundTripsTest() => AssertRoundTrip(Report());

    [Test]
    public void CloneIsIndependentTest()
    {
        var scene = Scene();
        var clone = (ParaViewSceneRefData)scene.Clone();

        clone.Attachments[0].TimestepIndices.Add(99);
        clone.TimestepValues.Add(9.9);
        clone.Runtime.Plugins[0].Version = "2.0";

        Assert.That(scene.Attachments[0].TimestepIndices, Has.Count.EqualTo(2));
        Assert.That(scene.TimestepValues, Has.Count.EqualTo(3));
        Assert.That(scene.Runtime.Plugins[0].Version, Is.EqualTo("1.0"));
        Assert.That(scene.Is(clone), Is.False);
    }

    [Test]
    public void IsDetectsEveryMemberChangeTest()
    {
        var baseline = RenderTask();

        Assert.Multiple(() =>
        {
            Assert.That(baseline.Is(Mutate(t => t.TaskId = "x")), Is.False);
            Assert.That(baseline.Is(Mutate(t => t.TaskIndex++)), Is.False);
            Assert.That(baseline.Is(Mutate(t => t.TimeValue = null)), Is.False);
            Assert.That(baseline.Is(Mutate(t => t.Options.Width++)), Is.False);
            Assert.That(baseline.Is(Mutate(t => t.Attachments.Clear())), Is.False);
            Assert.That(baseline.Is(Mutate(t => t.SubsetBytes++)), Is.False);
            Assert.That(baseline.Is(Mutate(t => t.Runtime.Plugins.Clear())), Is.False);
            Assert.That(baseline.Is(Mutate(_ => { })), Is.True);
        });

        ParaViewRenderTaskData Mutate(Action<ParaViewRenderTaskData> change)
        {
            var copy = (ParaViewRenderTaskData)baseline.Clone();
            change(copy);
            return copy;
        }
    }

    #endregion

    #region Tools

    private static void AssertRoundTrip<T>(T value) where T : ModelBase
    {
        var bytes = MemoryPackSerializer.Serialize(value);
        Assert.That(bytes, Is.Not.Empty, $"{typeof(T).Name} serialized to nothing");

        var restored = MemoryPackSerializer.Deserialize<T>(bytes);
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Is(value), Is.True, $"{typeof(T).Name} did not round-trip");

        var clone = (T)value.Clone();
        Assert.That(clone.Is(value), Is.True, $"{typeof(T).Name}.Clone() lost a member");
        Assert.That(ReferenceEquals(clone, value), Is.False);
    }

    #endregion
}
