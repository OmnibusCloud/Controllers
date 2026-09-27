using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Utils;

namespace OutWit.Controller.Sweep.Tests.Utils;

[TestFixture]
public class SweepVariantLabelTests
{
    #region Label Tests

    [Test]
    public void LabelPairsParametersWithTheVariantValuesTest()
    {
        var options = new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "XMAX", Token = "{{oc1}}" }, new SweepParameterData { Name = "T", Token = "{{oc2}}" }],
            Variants = [new SweepVariantData { VariantIndex = 0, Values = ["300", "250"] }, new SweepVariantData { VariantIndex = 1, Values = ["350", "250"] }]
        };

        Assert.That(SweepVariantLabel.Of(options, 0), Is.EqualTo("XMAX=300, T=250"));
        Assert.That(SweepVariantLabel.Of(options, 1), Is.EqualTo("XMAX=350, T=250"));
    }

    [Test]
    public void LabelFallsBackToTheTokenAndToAPositionTest()
    {
        var options = new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = string.Empty, Token = "{{oc1}}" }],
            Variants = [new SweepVariantData { VariantIndex = 3, Values = ["300", "extra"] }]
        };

        Assert.That(SweepVariantLabel.Of(options, 3), Is.EqualTo("{{oc1}}=300, p2=extra"));
    }

    [Test]
    public void LabelIsEmptyWithoutAnIdentityTest()
    {
        var deckSet = new SweepOptionsData
        {
            Variants = [new SweepVariantData { VariantIndex = 0 }],
            CalculiX = new SweepCalculiXStudyData { Decks = [new SweepCalculiXDeckData { VariantIndex = 0, DeckBlobId = Guid.NewGuid() }] }
        };

        Assert.That(SweepVariantLabel.Of((SweepOptionsData?)null, 0), Is.Empty);
        Assert.That(SweepVariantLabel.Of(deckSet, 0), Is.Empty, "a deck-set variant has no values");
        Assert.That(SweepVariantLabel.Of(deckSet, 7), Is.Empty, "an unknown variant has no label");
    }

    [Test]
    public void ACaseSetVariantIsLabelledByItsCaseTest()
    {
        var plan = new SweepPlanData
        {
            Options = new SweepOptionsData
            {
                Variants = [new SweepVariantData { VariantIndex = 0 }, new SweepVariantData { VariantIndex = 1 }],
                OpenFOAM = new FoamCaseData()
            },
            OpenFOAMSet = new SweepOpenFOAMSetData
            {
                Cases = [new SweepOpenFOAMCaseData { VariantIndex = 0, Name = "cavity-coarse" }, new SweepOpenFOAMCaseData { VariantIndex = 1 }]
            }
        };

        Assert.That(SweepVariantLabel.Of(plan, 0), Is.EqualTo("cavity-coarse"));
        Assert.That(SweepVariantLabel.Of(plan, 1), Is.Empty, "a case without a name falls back to the variant number");
        Assert.That(SweepVariantLabel.Of(plan, 7), Is.Empty);
    }

    [Test]
    public void APlanWithoutACaseSetIsLabelledByItsValuesTest()
    {
        var plan = new SweepPlanData
        {
            Options = new SweepOptionsData
            {
                Parameters = [new SweepParameterData { Name = "U", Token = "{{oc1}}" }],
                Variants = [new SweepVariantData { VariantIndex = 0, Values = ["10"] }]
            }
        };

        Assert.That(SweepVariantLabel.Of(plan, 0), Is.EqualTo("U=10"));
        Assert.That(SweepVariantLabel.Of((SweepPlanData?)null, 0), Is.Empty);
    }

    #endregion
}
