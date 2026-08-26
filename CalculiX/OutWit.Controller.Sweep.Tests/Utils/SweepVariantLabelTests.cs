using OutWit.Controller.CalculiX.Model;
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
            Variants = [new SweepVariantData { VariantIndex = 0, DeckBlobId = Guid.NewGuid() }]
        };

        Assert.That(SweepVariantLabel.Of(null, 0), Is.Empty);
        Assert.That(SweepVariantLabel.Of(deckSet, 0), Is.Empty, "a deck-set variant has no values");
        Assert.That(SweepVariantLabel.Of(deckSet, 7), Is.Empty, "an unknown variant has no label");
    }

    #endregion
}
