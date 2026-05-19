using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Dcc.Tests.Utils;
using OutWit.Controller.Render.Dcc.Variables;

namespace OutWit.Controller.Render.Dcc.Tests.Variables;

[TestFixture]
public sealed class WitVariableDccSceneTests
{
    #region Is Tests

    [Test]
    public void IsEqualTest()
    {
        var scene = new WitVariableDccScene("scene", DccRenderTestData.CreateValidScene());
        Assert.That(scene, Was.EqualTo(scene.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentSceneNameTest()
    {
        var left = new WitVariableDccScene("scene", DccRenderTestData.CreateValidScene());
        var scene = DccRenderTestData.CreateValidScene();
        scene.SceneName = "OtherScene";
        var right = new WitVariableDccScene("scene", scene);

        Assert.That(left, Was.Not.EqualTo(right));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void ClonePreservesSceneValueTest()
    {
        var variable = new WitVariableDccScene("scene", DccRenderTestData.CreateValidScene());
        var clone = variable.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone.Name, Is.EqualTo("scene"));
            Assert.That(clone.GetValue(), Is.Not.Null);
            Assert.That(clone.GetValue()!.SceneName, Is.EqualTo("TestScene"));
            Assert.That(clone.GetValue()!.Nodes[0].MeshId, Is.EqualTo("mesh:cube"));
            Assert.That(clone.GetValue(), Is.Not.SameAs(variable.GetValue()));
        });
    }

    #endregion

    #region MemoryPack Tests

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var variable = new WitVariableDccScene("scene", DccRenderTestData.CreateValidScene());
        var clone = variable.MemoryPackClone();

        Assert.That(clone, Was.EqualTo(variable));
    }

    #endregion
}
