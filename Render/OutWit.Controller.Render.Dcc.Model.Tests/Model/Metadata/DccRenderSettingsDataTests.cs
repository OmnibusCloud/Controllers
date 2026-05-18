using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Metadata;

[TestFixture]
public sealed class DccRenderSettingsDataTests
{
    [Test]
    public void IsTest()
    {
        var settingsA = DccModelTestData.CreateRenderSettings();
        var settingsB = DccModelTestData.CreateRenderSettings();
        var settingsC = DccModelTestData.CreateRenderSettings();
        settingsC.Samples = 128;

        Assert.That(settingsA.Is(settingsB), Is.True);
        Assert.That(settingsA.Is(settingsC), Is.False);
        Assert.That(settingsA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateRenderSettings();
        var clone = (DccRenderSettingsData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.ResolutionX, Is.EqualTo(original.ResolutionX));
            Assert.That(clone.ResolutionY, Is.EqualTo(original.ResolutionY));
            Assert.That(clone.FrameStart, Is.EqualTo(original.FrameStart));
            Assert.That(clone.FrameEnd, Is.EqualTo(original.FrameEnd));
            Assert.That(clone.Fps, Is.EqualTo(original.Fps));
            Assert.That(clone.TargetEngine, Is.EqualTo(original.TargetEngine));
            Assert.That(clone.Samples, Is.EqualTo(original.Samples));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateRenderSettings();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.ResolutionX, Is.EqualTo(original.ResolutionX));
            Assert.That(clone.ResolutionY, Is.EqualTo(original.ResolutionY));
            Assert.That(clone.FrameStart, Is.EqualTo(original.FrameStart));
            Assert.That(clone.FrameEnd, Is.EqualTo(original.FrameEnd));
            Assert.That(clone.Fps, Is.EqualTo(original.Fps));
            Assert.That(clone.TargetEngine, Is.EqualTo(original.TargetEngine));
            Assert.That(clone.Samples, Is.EqualTo(original.Samples));
        });
    }
}
