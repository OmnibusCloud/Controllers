using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamTemplatingTests
{
    #region Substitution Tests

    [Test]
    public void LongerTokensAreReplacedBeforeTheirPrefixesTest()
    {
        var text = "inlet {{oc1}} outlet {{oc10}} wall {{oc1}}";
        var substitutions = new List<FoamTokenValueData>
        {
            new() { Token = "{{oc1}}", Value = "A" },
            new() { Token = "{{oc10}}", Value = "B" }
        };

        var result = FoamTemplating.Substitute(text, substitutions);

        Assert.That(result, Is.EqualTo("inlet A outlet B wall A"));
    }

    [Test]
    public void LineEndingsSurviveSubstitutionTest()
    {
        var text = "a {{oc1}};\r\nb {{oc2}};\n";
        var result = FoamTemplating.Substitute(text, [new FoamTokenValueData { Token = "{{oc1}}", Value = "1" }, new FoamTokenValueData { Token = "{{oc2}}", Value = "2" }]);

        Assert.That(result, Is.EqualTo("a 1;\r\nb 2;\n"));
    }

    [Test]
    public void LeftoverTokensAreListedOnceEachInOrderTest()
    {
        var leftovers = FoamTemplating.LeftoverTokens("x {{oc3}} y {{oc1}} z {{oc3}} {{notatoken}}");

        Assert.That(leftovers, Is.EqualTo(new[] { "{{oc3}}", "{{oc1}}" }));
    }

    [Test]
    public void AnEmptyTokenIsIgnoredTest()
    {
        var result = FoamTemplating.Substitute("abc", [new FoamTokenValueData { Token = "", Value = "X" }]);

        Assert.That(result, Is.EqualTo("abc"));
    }

    #endregion
}
