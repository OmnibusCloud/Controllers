using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public class RenderValidateBlendDataTests
{
    #region Is Tests

    [Test]
    public void IsEqualTest()
    {
        var data = new RenderValidateBlendData
        {
            IsValid = false,
            Issues = ["Particle simulation 'Cube' is not yet portable to remote rendering in the current v1 flow."],
            Warnings = ["Scene uses external font 'Arial'."]
        };

        Assert.That(data, Was.EqualTo(data.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentValidityTest()
    {
        var valid = new RenderValidateBlendData { IsValid = true };
        var invalid = new RenderValidateBlendData { IsValid = false };
        Assert.That(valid, Was.Not.EqualTo(invalid));
    }

    [Test]
    public void IsNotEqualDifferentIssuesTest()
    {
        var d1 = new RenderValidateBlendData { IsValid = false, Issues = ["soft body simulation"] };
        var d2 = new RenderValidateBlendData { IsValid = false, Issues = ["rigid body simulation"] };
        Assert.That(d1, Was.Not.EqualTo(d2));
    }

    [Test]
    public void IsNotEqualDifferentWarningsTest()
    {
        var d1 = new RenderValidateBlendData { Warnings = ["one"] };
        var d2 = new RenderValidateBlendData { Warnings = ["one", "two"] };
        Assert.That(d1, Was.Not.EqualTo(d2));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var data = new RenderValidateBlendData
        {
            IsValid = false,
            Issues = ["issue-1", "issue-2"],
            Warnings = ["warning-1"]
        };

        var clone = (RenderValidateBlendData)data.Clone();

        Assert.That(clone.IsValid, Is.False);
        Assert.That(clone.Issues, Is.EqualTo(new[] { "issue-1", "issue-2" }));
        Assert.That(clone.Warnings, Is.EqualTo(new[] { "warning-1" }));
    }

    [Test]
    public void CloneProducesIndependentListsTest()
    {
        var data = new RenderValidateBlendData { Issues = ["issue-1"], Warnings = ["warning-1"] };
        var clone = (RenderValidateBlendData)data.Clone();

        clone.Issues.Add("issue-2");
        clone.Warnings.Add("warning-2");

        Assert.That(data.Issues, Has.Count.EqualTo(1));
        Assert.That(data.Warnings, Has.Count.EqualTo(1));
    }

    #endregion

    #region MemoryPack Tests

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var data = new RenderValidateBlendData
        {
            IsValid = false,
            Issues = ["Rigid body simulation 'Cube' is not yet portable to remote rendering in the current v1 flow."],
            Warnings = ["Scene uses external volume 'Smoke'."]
        };

        var clone = data.MemoryPackClone();
        Assert.That(clone, Was.EqualTo(data));
    }

    #endregion
}
