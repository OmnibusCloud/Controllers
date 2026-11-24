using OutWit.Controller.AI.Smt.Live.Tests.Mock;

/// <summary>
/// The controller's locator finds the solver module the controller's tests run, as a node finds
/// it beside the controller: the variable that names the module's file is set before anything
/// runs, unless whoever runs the tests has set it. Deliberately outside a namespace, so it covers
/// the whole assembly.
/// </summary>
[SetUpFixture]
public sealed class SmtLiveTestEnvironment
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        if (SmtLiveSolver.Find() is { } module && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SmtLiveSolver.MODULE_VARIABLE)))
            Environment.SetEnvironmentVariable(SmtLiveSolver.MODULE_VARIABLE, module);
    }

    #endregion
}
