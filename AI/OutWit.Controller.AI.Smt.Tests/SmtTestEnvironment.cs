using OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The solver finds its module in the test project's <c>@Data</c> (when it is there) as a node
/// finds it beside the controller: the variable that names the module's file is set before
/// anything runs, unless whoever runs the tests has set it. And the sandbox a test made for
/// itself is released when everything has run. Deliberately outside a namespace, so it covers
/// the whole assembly.
/// </summary>
[SetUpFixture]
public sealed class SmtTestEnvironment
{
    #region Constants

    /// <summary>The variable that names the solver module's file; a test holds it to the controller's own name for it.</summary>
    public const string MODULE_VARIABLE = "WITAI_SMT_Z3";

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        if (SmtSolverModule.Path is { } module && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(MODULE_VARIABLE)))
            Environment.SetEnvironmentVariable(MODULE_VARIABLE, module);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        SmtSolverRig.Release();
    }

    #endregion
}
