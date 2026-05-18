using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Variables;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public class RenderTaskDataTests
{
    #region Is Tests

    [Test]
    public void IsEqualTest()
    {
        var task = CreateFrameTask(1);
        Assert.That(task, Was.EqualTo(task.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentFrameTest()
    {
        var t1 = CreateFrameTask(1);
        var t2 = CreateFrameTask(2);
        Assert.That(t1, Was.Not.EqualTo(t2));
    }

    [Test]
    public void IsNotEqualDifferentSceneTest()
    {
        var t1 = CreateFrameTask(1);
        var t2 = CreateFrameTask(1);
        t2.SceneBlobId = Guid.NewGuid();
        Assert.That(t1, Was.Not.EqualTo(t2));
    }

    [Test]
    public void IsNotEqualDifferentTileTest()
    {
        var t1 = CreateFrameTask(1);
        var t2 = CreateFrameTask(1);
        t2.TileMaxX = 0.5f;
        Assert.That(t1, Was.Not.EqualTo(t2));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var task = new RenderTaskData
        {
            SceneBlobId = Guid.NewGuid(),
            Frame = 42,
            TileMinX = 0.25f,
            TileMaxX = 0.75f,
            TileMinY = 0.0f,
            TileMaxY = 0.5f,
            TaskIndex = 7,
            Options = new RenderOptionsData
            {
                Format = RenderFormat.EXR,
                Samples = 256
            }
        };

        var clone = (RenderTaskData)task.Clone();

        Assert.That(clone.SceneBlobId, Is.EqualTo(task.SceneBlobId));
        Assert.That(clone.Frame, Is.EqualTo(42));
        Assert.That(clone.TileMinX, Is.EqualTo(0.25f));
        Assert.That(clone.TileMaxX, Is.EqualTo(0.75f));
        Assert.That(clone.TaskIndex, Is.EqualTo(7));
        Assert.That(clone.Options.Samples, Is.EqualTo(256));
    }

    #endregion

    #region MemoryPack Tests

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var task = CreateFrameTask(5);
        var clone = task.MemoryPackClone();
        Assert.That(clone, Was.EqualTo(task));
    }

    [Test]
    public void MemoryPackRoundtripWithTileTest()
    {
        var task = new RenderTaskData
        {
            SceneBlobId = Guid.NewGuid(),
            Frame = 1,
            TileMinX = 0.0f,
            TileMaxX = 0.25f,
            TileMinY = 0.75f,
            TileMaxY = 1.0f,
            TaskIndex = 3,
            Options = new RenderOptionsData { Format = RenderFormat.PNG, Samples = 64 }
        };

        var clone = task.MemoryPackClone();
        Assert.That(clone, Was.EqualTo(task));
        Assert.That(clone.IsFullFrame, Is.False);
    }

    #endregion

    #region IsFullFrame Tests

    [Test]
    public void IsFullFrameTrueForDefaultsTest()
    {
        var task = CreateFrameTask(1);
        Assert.That(task.IsFullFrame, Is.True);
    }

    [Test]
    public void IsFullFrameFalseForTileTest()
    {
        var task = CreateFrameTask(1);
        task.TileMaxX = 0.5f;
        Assert.That(task.IsFullFrame, Is.False);
    }

    #endregion

    #region Tools

    private static RenderTaskData CreateFrameTask(int frame)
    {
        return new RenderTaskData
        {
            SceneBlobId = Guid.Parse("{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}"),
            Frame = frame,
            TaskIndex = frame - 1,
            Options = new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                Samples = 4,
                ResolutionX = 64,
                ResolutionY = 64
            }
        };
    }

    #endregion
}
