using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.Sweep.Utils;

namespace OutWit.Controller.Sweep.Tests.Utils;

[TestFixture]
public class SweepDeckTemplatingTests
{
    #region Constants

    private const string TEMPLATE =
        "*MATERIAL, NAME=Steel\n*ELASTIC\n{{oc1}}, {{oc2}}\n*CLOAD\nLOAD_FACE, 2, -1500.0\n";

    #endregion

    #region Fields

    private static readonly List<SweepParameterData> PARAMETERS =
    [
        new SweepParameterData { Name = "E-modulus", Token = "{{oc1}}" },
        new SweepParameterData { Name = "Poisson", Token = "{{oc2}}" }
    ];

    #endregion

    #region Instantiation Tests

    [Test]
    public void InstantiateSubstitutesEveryTokenTest()
    {
        var deck = SweepDeckTemplating.Instantiate(TEMPLATE, PARAMETERS, ["210000.0", "0.3"]);

        Assert.That(deck, Is.EqualTo("*MATERIAL, NAME=Steel\n*ELASTIC\n210000.0, 0.3\n*CLOAD\nLOAD_FACE, 2, -1500.0\n"));
    }

    [Test]
    public void InstantiateRejectsValueCountMismatchTest()
    {
        Assert.That(
            () => SweepDeckTemplating.Instantiate(TEMPLATE, PARAMETERS, ["210000.0"]),
            Throws.InvalidOperationException.With.Message.Contains("1 value(s) for 2 parameter(s)"));
    }

    [Test]
    public void InstantiateRejectsLeftoverPlaceholderTest()
    {
        // A template carrying a token the parameter list does not know about
        // must fail loudly — a deck with a literal "{{" would reach ccx and
        // die there with a far worse message.
        var template = TEMPLATE + "*DENSITY\n{{oc3}}\n";

        Assert.That(
            () => SweepDeckTemplating.Instantiate(template, PARAMETERS, ["210000.0", "0.3"]),
            Throws.InvalidOperationException.With.Message.Contains("{{oc3}}"));
    }

    [Test]
    public void InstantiateNeverResubstitutesAValueCarryingAnotherTokenTest()
    {
        // Substitution is ONE pass over the original text. A value that
        // itself carries another parameter's token used to be silently
        // re-substituted by the sequential-Replace implementation (the
        // deck got the OTHER parameter's value); now the surviving braces
        // are refused loudly instead — never quietly mangled.
        Assert.That(
            () => SweepDeckTemplating.Instantiate(TEMPLATE, PARAMETERS, ["{{oc2}}", "0.3"]),
            Throws.InvalidOperationException.With.Message.Contains("parameter list does not cover"));
    }

    [Test]
    public void InstantiateToleratesBracesInsideDeckCommentsTest()
    {
        // "**" lines are ccx comments — a literal "{{" in a remark is the
        // deck author's own business and must not fail every variant.
        var template = "** a remark with {{braces}} in it\n  ** indented {{too}}\n" + TEMPLATE;

        var deck = SweepDeckTemplating.Instantiate(template, PARAMETERS, ["210000.0", "0.3"]);

        Assert.That(deck, Does.StartWith("** a remark with {{braces}} in it"));
        Assert.That(deck, Does.Contain("210000.0, 0.3"));
    }

    [Test]
    public void InstantiateFirstParameterClaimingATokenWinsTest()
    {
        // Two parameters sharing one token: the first claims it — the exact
        // semantics of the old sequential Replace, pinned across the rewrite.
        var parameters = new List<SweepParameterData>
        {
            new() { Name = "First", Token = "{{oc1}}" },
            new() { Name = "Second", Token = "{{oc1}}" },
            new() { Name = "Poisson", Token = "{{oc2}}" }
        };

        var deck = SweepDeckTemplating.Instantiate(TEMPLATE, parameters, ["111.0", "222.0", "0.3"]);

        Assert.That(deck, Does.Contain("111.0, 0.3"));
        Assert.That(deck, Does.Not.Contain("222.0"));
    }

    #endregion

    #region Validation Tests

    [Test]
    public void ValidateAcceptsTemplateCoveringAllParametersTest()
    {
        Assert.That(() => SweepDeckTemplating.ValidateTemplate(TEMPLATE, PARAMETERS), Throws.Nothing);
    }

    [Test]
    public void ValidateRejectsTokenAbsentFromDeckTest()
    {
        var parameters = new List<SweepParameterData>(PARAMETERS)
        {
            new() { Name = "Thickness", Token = "{{oc9}}" }
        };

        Assert.That(
            () => SweepDeckTemplating.ValidateTemplate(TEMPLATE, parameters),
            Throws.InvalidOperationException.With.Message.Contains("Thickness"));
    }

    [Test]
    public void ValidateRejectsEmptyTokenTest()
    {
        var parameters = new List<SweepParameterData> { new() { Name = "Broken", Token = " " } };

        Assert.That(
            () => SweepDeckTemplating.ValidateTemplate(TEMPLATE, parameters),
            Throws.InvalidOperationException.With.Message.Contains("Broken"));
    }

    #endregion
}
