using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public class RenderResultBatchDataTests
{
    #region Is Tests

    [Test]
    public void IsEqualTest()
    {
        var batch = CreateBatch(3);
        Assert.That(batch, Was.EqualTo(batch.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentResultCountTest()
    {
        Assert.That(CreateBatch(3), Was.Not.EqualTo(CreateBatch(2)));
    }

    [Test]
    public void IsNotEqualDifferentResultTest()
    {
        var b1 = CreateBatch(2);
        var b2 = CreateBatch(2);
        b2.Results[1].Index = 99;
        Assert.That(b1, Was.Not.EqualTo(b2));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void CloneIsDeepTest()
    {
        var batch = CreateBatch(3);
        var clone = (RenderResultBatchData)batch.Clone();

        clone.Results[0].Index = 999;

        Assert.That(batch.Results[0].Index, Is.EqualTo(0), "Clone must not share result instances");
        Assert.That(clone.Results, Has.Count.EqualTo(3));
    }

    #endregion

    #region MemoryPack Tests

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var batch = CreateBatch(4);
        var clone = batch.MemoryPackClone();
        Assert.That(clone, Was.EqualTo(batch));
    }

    #endregion

    #region Tools

    private static RenderResultBatchData CreateBatch(int count)
    {
        var results = new List<RenderResultData>();
        for (var i = 0; i < count; i++)
        {
            results.Add(new RenderResultData
            {
                Index = i,
                ImageBlobId = Guid.NewGuid()
            });
        }

        return new RenderResultBatchData { Results = results };
    }

    #endregion
}
