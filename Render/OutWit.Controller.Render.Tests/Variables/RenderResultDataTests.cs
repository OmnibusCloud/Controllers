using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Variables;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public class RenderResultDataTests
{
    #region Is Tests

    [Test]
    public void IsEqualTest()
    {
        var result = new RenderResultData { Index = 42, ImageBlobId = Guid.Parse("{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}") };
        Assert.That(result, Was.EqualTo(result.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentIndexTest()
    {
        var r1 = new RenderResultData { Index = 1, ImageBlobId = Guid.NewGuid() };
        var r2 = new RenderResultData { Index = 2, ImageBlobId = r1.ImageBlobId };
        Assert.That(r1, Was.Not.EqualTo(r2));
    }

    [Test]
    public void IsNotEqualDifferentBlobIdTest()
    {
        var r1 = new RenderResultData { Index = 1, ImageBlobId = Guid.NewGuid() };
        var r2 = new RenderResultData { Index = 1, ImageBlobId = Guid.NewGuid() };
        Assert.That(r1, Was.Not.EqualTo(r2));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var blobId = Guid.NewGuid();
        var result = new RenderResultData
        {
            Index = 99,
            ImageBlobId = blobId,
            TileMinX = 0.25f,
            TileMaxX = 0.5f,
            TileMinY = 0.5f,
            TileMaxY = 1.0f
        };

        var clone = (RenderResultData)result.Clone();

        Assert.That(clone.Index, Is.EqualTo(99));
        Assert.That(clone.ImageBlobId, Is.EqualTo(blobId));
        Assert.That(clone.TileMinX, Is.EqualTo(0.25f));
        Assert.That(clone.TileMaxX, Is.EqualTo(0.5f));
    }

    #endregion

    #region MemoryPack Tests

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var result = new RenderResultData { Index = 7, ImageBlobId = Guid.NewGuid() };
        var clone = result.MemoryPackClone();
        Assert.That(clone, Was.EqualTo(result));
    }

    #endregion
}
