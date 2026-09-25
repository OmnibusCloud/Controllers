using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

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

    [Test]
    public void AValueThatContainsATokenIsNotSubstitutedAgainTest()
    {
        // One pass over the text: the value put in for {{oc1}} is never
        // scanned again, whatever it contains and in whatever order the
        // values come.
        var text = "a {{oc1}} b {{oc2}}";
        var forward = new List<FoamTokenValueData>
        {
            new() { Token = "{{oc1}}", Value = "{{oc2}}" },
            new() { Token = "{{oc2}}", Value = "5" }
        };
        var backward = new List<FoamTokenValueData>(forward);
        backward.Reverse();

        Assert.Multiple(() =>
        {
            Assert.That(FoamTemplating.Substitute(text, forward), Is.EqualTo("a {{oc2}} b 5"));
            Assert.That(FoamTemplating.Substitute(text, backward), Is.EqualTo("a {{oc2}} b 5"));
        });
    }

    [Test]
    public void ATokenWithoutAValueIsLeftAsItIsTest()
    {
        var result = FoamTemplating.Substitute("{{oc1}} {{oc2}} {{notatoken}}", [new FoamTokenValueData { Token = "{{oc1}}", Value = "1" }]);

        Assert.That(result, Is.EqualTo("1 {{oc2}} {{notatoken}}"));
        Assert.That(FoamTemplating.LeftoverTokens(result), Is.EqualTo(new[] { "{{oc2}}" }));
    }

    [Test]
    public void AVariantsValuesNameTokensOfTheTokenShapeEachOnceTest()
    {
        var findings = FoamTemplating.CheckSubstitutions(
        [
            new FoamTokenValueData { Token = "{{oc1}}", Value = "1" },
            new FoamTokenValueData { Token = "$INLET$", Value = "2" },
            new FoamTokenValueData { Token = "{{oc1}}", Value = "3" },
            new FoamTokenValueData { Token = "{{oc2}}", Value = "4" }
        ]);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "'$INLET$' is not a token of the form {{ocN}}; its value would never be substituted.",
            "Token {{oc1}} has more than one value in this variant."
        }));
        Assert.That(FoamTemplating.CheckSubstitutions([new FoamTokenValueData { Token = "{{oc7}}", Value = "7" }]), Is.Empty);
    }

    #endregion

    #region Coverage Tests

    [Test]
    public void DeclaredTokensThatTheFilesCarryAreCoveredTest()
    {
        var findings = FoamTemplating.CheckCoverage(
            ["{{oc1}}", "{{oc2}}"],
            [("0/U", "internalField uniform ({{oc1}} 0 0);"), ("constant/transportProperties", "nu {{oc2}};")]);

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public void ATokenNoFileCarriesAndAFileTokenNobodyDeclaresAreFindingsTest()
    {
        var findings = FoamTemplating.CheckCoverage(
            ["{{oc1}}", "{{oc3}}"],
            [("0/U", "internalField uniform ({{oc1}} {{oc2}} 0);")]);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "Token {{oc3}} occurs in no templated file of the case.",
            "0/U: token {{oc2}} is not declared by the study."
        }));
    }

    [Test]
    public void AMalformedOrRepeatedTokenIsAFindingTest()
    {
        var findings = FoamTemplating.CheckCoverage(["$inlet", "{{oc1}}", "{{oc1}}"], [("0/U", "{{oc1}}")]);

        Assert.That(findings, Has.Count.EqualTo(2));
        Assert.That(findings[0], Does.Contain("'$inlet' is not a token of the form {{ocN}}"));
        Assert.That(findings[1], Is.EqualTo("Token {{oc1}} is declared twice."));
    }

    [Test]
    public void AStudyWithoutTokensOverAnUntemplatedCaseIsCoveredTest()
    {
        Assert.That(FoamTemplating.CheckCoverage([], []), Is.Empty);
    }

    #endregion
}
