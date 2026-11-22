using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The test machine's owner lists the loopback origin before anything runs: the engine rig
/// drives the real adapters, which read the machine's policy from the environment once, and
/// their test origin is on 127.0.0.1. And the PDF stage finds its module in the test project's
/// @Data (when it is there), the processors of the tests and of the engine rig alike, as a node
/// finds it beside the controller. Deliberately outside a namespace, so it covers the whole
/// assembly. Tests of the policy itself build their own instances.
/// </summary>
[SetUpFixture]
public sealed class CorpusTestEnvironment
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        Environment.SetEnvironmentVariable(ShardOriginPolicy.EXTRA_ORIGINS_VARIABLE, "127.0.0.1");

        if (PdfTestModule.Path is { } module && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(PdfModuleLocator.OVERRIDE_VARIABLE)))
            Environment.SetEnvironmentVariable(PdfModuleLocator.OVERRIDE_VARIABLE, module);
    }

    #endregion
}
