using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public class RenderTaskBatchDataTests
{
    #region Is Tests

    [Test]
    public void IsEqualTest()
    {
        var batch = CreateBatch(sceneId: Guid.NewGuid(), frames: [1, 2, 3]);
        Assert.That(batch, Was.EqualTo(batch.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentTaskCountTest()
    {
        var sceneId = Guid.NewGuid();
        var b1 = CreateBatch(sceneId, [1, 2, 3]);
        var b2 = CreateBatch(sceneId, [1, 2]);
        Assert.That(b1, Was.Not.EqualTo(b2));
    }

    [Test]
    public void IsNotEqualDifferentSceneTest()
    {
        var b1 = CreateBatch(Guid.NewGuid(), [1, 2]);
        var b2 = CreateBatch(Guid.NewGuid(), [1, 2]);
        Assert.That(b1, Was.Not.EqualTo(b2));
    }

    [Test]
    public void IsNotEqualDifferentFrameTest()
    {
        var sceneId = Guid.NewGuid();
        var b1 = CreateBatch(sceneId, [1, 2, 3]);
        var b2 = CreateBatch(sceneId, [1, 2, 4]);
        Assert.That(b1, Was.Not.EqualTo(b2));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void CloneIsDeepTest()
    {
        var batch = CreateBatch(Guid.NewGuid(), [1, 2, 3]);
        var clone = (RenderTaskBatchData)batch.Clone();

        clone.Tasks[0].Frame = 999;

        Assert.That(batch.Tasks[0].Frame, Is.EqualTo(1), "Clone must not share task instances");
        Assert.That(clone.Tasks, Has.Count.EqualTo(3));
        Assert.That(clone.SceneBlobId, Is.EqualTo(batch.SceneBlobId));
    }

    #endregion

    #region MemoryPack Tests

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var batch = CreateBatch(Guid.NewGuid(), [10, 11, 12, 13]);
        var clone = batch.MemoryPackClone();
        Assert.That(clone, Was.EqualTo(batch));
    }

    #endregion

    #region Attachments Tests

    [Test]
    public void IsNotEqualDifferentAttachmentsTest()
    {
        var sceneId = Guid.NewGuid();
        var b1 = CreateBatch(sceneId, [1, 2]);
        var b2 = CreateBatch(sceneId, [1, 2]);
        b2.Attachments.Add(CreateAttachment());
        Assert.That(b1, Was.Not.EqualTo(b2));
    }

    [Test]
    public void CloneDeepCopiesAttachmentsTest()
    {
        var batch = CreateBatch(Guid.NewGuid(), [1, 2]);
        batch.Attachments.Add(CreateAttachment());

        var clone = (RenderTaskBatchData)batch.Clone();
        clone.Attachments[0].RelativePath = "deps/changed.bin";

        Assert.That(batch.Attachments[0].RelativePath, Is.EqualTo("deps/lib/library.blend"), "Clone must not share attachment instances");
        Assert.That(clone.Attachments, Has.Count.EqualTo(1));
    }

    [Test]
    public void MemoryPackRoundtripWithAttachmentsTest()
    {
        var batch = CreateBatch(Guid.NewGuid(), [10, 11]);
        batch.Attachments.Add(CreateAttachment());

        var clone = batch.MemoryPackClone();
        Assert.That(clone, Was.EqualTo(batch));
        Assert.That(clone.Attachments, Has.Count.EqualTo(1));
    }

    [Test]
    public void AttachmentFrameSurvivesCloneAndMemoryPackTest()
    {
        var batch = CreateBatch(Guid.NewGuid(), [1, 2]);
        var att = CreateAttachment();
        att.Frame = 7;
        batch.Attachments.Add(att);

        var clone = (RenderTaskBatchData)batch.Clone();
        Assert.That(clone.Attachments[0].Frame, Is.EqualTo(7));

        var mp = batch.MemoryPackClone();
        Assert.That(mp.Attachments[0].Frame, Is.EqualTo(7));
        Assert.That(mp, Was.EqualTo(batch));

        var other = (RenderTaskBatchData)batch.Clone();
        other.Attachments[0].Frame = 8;
        Assert.That(batch, Was.Not.EqualTo(other), "Frame must participate in equality");
    }

    #endregion

    #region Tools

    private static RenderTaskBatchData CreateBatch(Guid sceneId, int[] frames)
    {
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Eevee,
            Samples = 32,
            ResolutionX = 1920,
            ResolutionY = 1080
        };

        var tasks = new List<RenderTaskData>();
        for (var i = 0; i < frames.Length; i++)
        {
            tasks.Add(new RenderTaskData
            {
                SceneBlobId = sceneId,
                Frame = frames[i],
                TileMinX = 0f,
                TileMaxX = 1f,
                TileMinY = 0f,
                TileMaxY = 1f,
                TaskIndex = i,
                Options = (RenderOptionsData)options.Clone()
            });
        }

        return new RenderTaskBatchData
        {
            SceneBlobId = sceneId,
            Options = options,
            Tasks = tasks
        };
    }

    private static RenderSceneAttachmentRefData CreateAttachment()
    {
        return new RenderSceneAttachmentRefData
        {
            Kind = "LinkedLibrary",
            BlobId = Guid.Parse("{11111111-2222-3333-4444-555555555555}"),
            OriginalPath = "//deps/lib/library.blend",
            RelativePath = "deps/lib/library.blend",
            PackagingStrategy = "SceneAttachmentBlob"
        };
    }

    #endregion
}
