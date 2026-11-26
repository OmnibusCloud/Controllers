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

    [TestCase("http://server.example", "https://identity.example", SmtLiveEnvironment.SERVER_VARIABLE)]
    [TestCase("https://server.example", "http://identity.example", SmtLiveEnvironment.IDENTITY_VARIABLE)]
    [TestCase("ftp://server.example", "https://identity.example", SmtLiveEnvironment.SERVER_VARIABLE)]
    [TestCase("server.example", "https://identity.example", SmtLiveEnvironment.SERVER_VARIABLE)]
    [TestCase("https://server.example", "http://192.168.1.10:5000", SmtLiveEnvironment.IDENTITY_VARIABLE)]
    public void TheKeyGoesToNoAddressThatIsNotHttpsTest(string server, string identity, string named)
    {
        var variables = new Dictionary<string, string?>
        {
            [SmtLiveEnvironment.SERVER_VARIABLE] = server,
            [SmtLiveEnvironment.IDENTITY_VARIABLE] = identity,
            [SmtLiveEnvironment.KEY_VARIABLE] = KEY
        };

        var environment = SmtLiveEnvironment.Read(name => variables.GetValueOrDefault(name), out var error);

        Assert.That(environment, Is.Null);
        Assert.That(error, Does.Contain(named).And.Contain("https://"));
        Assert.That(error, Does.Not.Contain(KEY));
    }

    [TestCase("http://localhost:5000")]
    [TestCase("http://127.0.0.1:8080")]
    [TestCase("http://[::1]:8080")]
    [TestCase("https://server.example")]
    public void AnHttpsAddressOrThisMachineForALocalTestIsTakenTest(string address)
    {
        var variables = new Dictionary<string, string?>
        {
            [SmtLiveEnvironment.SERVER_VARIABLE] = address,
            [SmtLiveEnvironment.IDENTITY_VARIABLE] = address,
            [SmtLiveEnvironment.KEY_VARIABLE] = KEY
        };

        var environment = SmtLiveEnvironment.Read(name => variables.GetValueOrDefault(name), out var error);

        Assert.That(error, Is.Null);
        Assert.That(environment, Is.Not.Null);
    }

    #endregion
}
