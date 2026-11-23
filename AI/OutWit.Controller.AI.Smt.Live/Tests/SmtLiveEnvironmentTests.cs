namespace OutWit.Controller.AI.Smt.Live.Tests;

[TestFixture]
public sealed class SmtLiveEnvironmentTests
{
    #region Constants

    private const string KEY = "wit_sk_not_a_real_key";

    #endregion

    #region Tests

    [Test]
    public void ReadTakesTheThreeVariablesTest()
    {
        var variables = new Dictionary<string, string?>
        {
            [SmtLiveEnvironment.SERVER_VARIABLE] = "https://server.invalid",
            [SmtLiveEnvironment.IDENTITY_VARIABLE] = "https://identity.invalid",
            [SmtLiveEnvironment.KEY_VARIABLE] = KEY
        };

        var environment = SmtLiveEnvironment.Read(name => variables.GetValueOrDefault(name), out var error);

        Assert.That(error, Is.Null);
        Assert.That(environment!.Server, Is.EqualTo("https://server.invalid"));
        Assert.That(environment.Identity, Is.EqualTo("https://identity.invalid"));
        Assert.That(environment.Key, Is.EqualTo(KEY));
    }

    [Test]
    public void ReadNamesEveryVariableThatIsNotSetAndNoValueTest()
    {
        var variables = new Dictionary<string, string?> { [SmtLiveEnvironment.KEY_VARIABLE] = KEY, [SmtLiveEnvironment.IDENTITY_VARIABLE] = " " };

        var environment = SmtLiveEnvironment.Read(name => variables.GetValueOrDefault(name), out var error);

        Assert.That(environment, Is.Null);
        Assert.That(error, Does.Contain(SmtLiveEnvironment.SERVER_VARIABLE).And.Contain(SmtLiveEnvironment.IDENTITY_VARIABLE));
        Assert.That(error, Does.Not.Contain(SmtLiveEnvironment.KEY_VARIABLE).And.Not.Contain(KEY));
    }

    [Test]
    public void TheKeyIsReadFromTheVariableTheOtherLiveToolsUseTest()
    {
        Assert.That(SmtLiveEnvironment.KEY_VARIABLE, Is.EqualTo("WITAI_VERIFY_LIVE_KEY"));
    }

    #endregion
}
