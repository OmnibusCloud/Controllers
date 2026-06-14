using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Scene;

[TestFixture]
public sealed class DccWorldDataTests
{
    #region Tools

    private static DccWorldData CreateWorld()
    {
        return new DccWorldData
        {
            BackgroundColor = new DccColorData { R = 0.2d, G = 0.3d, B = 0.4d, A = 1d },
            Strength = 1.5d,
            EnvironmentImageId = "image:env",
            EnvironmentRotationDegrees = 45d
        };
    }

    #endregion

    #region Tests

    [Test]
    public void IsTest()
    {
        var worldA = CreateWorld();
        var worldB = CreateWorld();

        var differentImage = CreateWorld();
        differentImage.EnvironmentImageId = "image:other";

        var differentRotation = CreateWorld();
        differentRotation.EnvironmentRotationDegrees = 90d;

        Assert.That(worldA.Is(worldB), Is.True);
        Assert.That(worldA.Is(differentImage), Is.False);
        Assert.That(worldA.Is(differentRotation), Is.False);
        Assert.That(worldA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = CreateWorld();
        var clone = (DccWorldData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Is(original), Is.True);
            Assert.That(clone.BackgroundColor, Is.Not.SameAs(original.BackgroundColor));
            Assert.That(clone.Strength, Is.EqualTo(original.Strength));
            Assert.That(clone.EnvironmentImageId, Is.EqualTo(original.EnvironmentImageId));
            Assert.That(clone.EnvironmentRotationDegrees, Is.EqualTo(original.EnvironmentRotationDegrees));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = CreateWorld();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Is(original), Is.True);
            Assert.That(clone.EnvironmentImageId, Is.EqualTo(original.EnvironmentImageId));
            Assert.That(clone.EnvironmentRotationDegrees, Is.EqualTo(original.EnvironmentRotationDegrees));
            Assert.That(clone.Strength, Is.EqualTo(original.Strength));
        });
    }

    [Test]
    public void MemoryPackCloneWithoutEnvironmentImageTest()
    {
        // The constant-colour world (no environment image) must still round-trip — the new
        // trailing fields default cleanly when absent.
        var original = new DccWorldData
        {
            BackgroundColor = new DccColorData { R = 0.5d, G = 0.5d, B = 0.5d, A = 1d },
            Strength = 2d
        };

        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone.Is(original), Is.True);
            Assert.That(clone.EnvironmentImageId, Is.Null);
            Assert.That(clone.EnvironmentRotationDegrees, Is.EqualTo(0d));
        });
    }

    #endregion
}
