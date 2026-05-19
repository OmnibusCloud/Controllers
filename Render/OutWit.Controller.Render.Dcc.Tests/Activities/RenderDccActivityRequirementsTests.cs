using OutWit.Controller.Render.Dcc.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.Render.Dcc.Tests.Activities;

/// <summary>
/// Verifies attribute-declared scheduling constraints on Render.Dcc activities.
/// Companion to the Render.Tests <c>BlenderRunnerTests</c> activity-requirement
/// checks; the DCC activity types live in a different assembly so they get
/// their own small fixture here.
/// </summary>
[TestFixture]
public sealed class RenderDccActivityRequirementsTests
{
    #region Tests

    [Test]
    public void BuildBlendFromDccSceneDeclaresRequiresLocalAccessTest()
    {
        // DccBlendFileBuilder writes the generated .blend (and the Python
        // build script that produces it) to a per-job temp directory before
        // returning a blob id. So the activity must not be scheduled on
        // nodes whose user has denied filesystem access.
        var attributes = typeof(WitActivityRenderBuildBlendFromDccScene)
            .GetCustomAttributes(typeof(RequiresResourcesAttribute), false);

        Assert.That(attributes, Has.Length.EqualTo(1),
            "Render.BuildBlendFromDccScene must carry [RequiresResources(RequiresLocalAccess = true)].");

        var requirement = (RequiresResourcesAttribute)attributes[0];
        Assert.That(requirement.RequiresLocalAccess, Is.True);
    }

    #endregion
}
