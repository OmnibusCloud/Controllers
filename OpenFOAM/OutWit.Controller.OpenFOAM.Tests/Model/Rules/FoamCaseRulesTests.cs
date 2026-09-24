using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

[TestFixture]
public class FoamCaseRulesTests
{
    #region Tools

    private static FoamCaseData PitzDaily()
    {
        return new FoamCaseData
        {
            BaseFiles =
            [
                new FoamFileRefData { RelativePath = "system/controlDict", BlobId = Guid.NewGuid() },
                new FoamFileRefData { RelativePath = "system/blockMeshDict", BlobId = Guid.NewGuid() },
                new FoamFileRefData { RelativePath = "0/U", BlobId = Guid.NewGuid(), Templated = true }
            ],
            Recipe = new FoamRecipeData
            {
                Application = "simpleFoam",
                Steps = [new FoamStepData { Utility = "blockMesh" }, new FoamStepData { Utility = "simpleFoam" }]
            },
            Extraction = new FoamExtractionRequestData
            {
                Responses = [new FoamResponseSpecData { Name = "pRange", Kind = FoamResponseKind.FieldMinMax, Fields = ["p"] }]
            }
        };
    }

    #endregion

    #region Case Tests

    [Test]
    public void AGoodCaseHasNoFindingsTest()
    {
        Assert.That(FoamCaseRules.Validate(PitzDaily()), Is.Empty);
    }

    [Test]
    public void AMissingCaseIsOneFindingTest()
    {
        Assert.That(FoamCaseRules.Validate(null), Is.EqualTo(new[] { "The task carries no case." }));
    }

    [Test]
    public void FindingsComeInTheOrderTreeRecipeResponsesTest()
    {
        var data = PitzDaily();
        data.BaseFiles.Add(new FoamFileRefData { RelativePath = "processor0/0/U", BlobId = Guid.NewGuid() });
        data.Recipe = new FoamRecipeData { Application = "simpleFoam", Steps = [new FoamStepData { Utility = "bash" }, new FoamStepData { Utility = "simpleFoam" }] };
        data.Extraction = new FoamExtractionRequestData { Responses = [new FoamResponseSpecData { Name = "controlDict", Kind = FoamResponseKind.FieldMinMax, Fields = ["p"] }] };

        var findings = FoamCaseRules.Validate(data);

        Assert.That(findings, Has.Count.EqualTo(3));
        Assert.That(findings[0], Does.StartWith("processor0/0/U:"));
        Assert.That(findings[1], Does.Contain("'bash' is not on the allow-list"));
        Assert.That(findings[2], Does.Contain("already carries system/controlDict"), "a response never overwrites a file the case ships");
    }

    [Test]
    public void TheKitIsAskedOnlyWhenAPredicateIsGivenTest()
    {
        var asked = new List<string>();

        var withoutKit = FoamCaseRules.Validate(PitzDaily());
        var withKit = FoamCaseRules.Validate(PitzDaily(), name =>
        {
            asked.Add(name);
            return name != "blockMesh";
        });

        Assert.That(withoutKit, Is.Empty);
        Assert.That(withKit, Is.EqualTo(new[] { "Step 1: the kit has no 'blockMesh'." }));
        Assert.That(asked, Is.EquivalentTo(new[] { "simpleFoam", "blockMesh", "simpleFoam" }));
    }

    #endregion
}
