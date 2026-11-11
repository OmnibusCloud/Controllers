using OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The test machine's owner lists the loopback origin before anything runs: the engine rig
/// drives the real adapters, which read the machine's policy from the environment once, and
/// their test origin is on 127.0.0.1. Deliberately outside a namespace, so it covers the
/// whole assembly. Tests of the policy itself build their own instances.
/// </summary>
[SetUpFixture]
public sealed class CorpusTestEnvironment
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        Environment.SetEnvironmentVariable(ShardOriginPolicy.EXTRA_ORIGINS_VARIABLE, "127.0.0.1");
    }

    #endregion
}
