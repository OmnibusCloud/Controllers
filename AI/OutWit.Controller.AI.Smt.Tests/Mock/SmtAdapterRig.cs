using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Adapters;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Variables;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Runs <c>Smt.SolvePackage</c> through the engine's own entry of its adapter, as a node's engine
/// runs it, with a processing manager of the test's own: a job the test can cancel, progress the
/// test can read. The controller's types are met here only, inside method bodies; what a test is
/// handed back is the status and the model package's result.
/// </summary>
internal static class SmtAdapterRig
{
    #region Constants

    private const string PACKAGE_VARIABLE = "package";
    private const string RESULT_VARIABLE = "result";

    #endregion

    #region Functions

    /// <summary>
    /// Starts one package through the adapter.
    /// </summary>
    /// <param name="package">The package's reference.</param>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="manager">The node's processing manager.</param>
    /// <param name="pool">The job's variables: the package and the result are put in it.</param>
    /// <returns>The status, when the adapter is done.</returns>
    public static Task<IWitProcessingStatus> Start(SmtTaskPackageData package, IWitBlobService blobs, IWitProcessingManager manager, WitVariableCollection pool)
    {
        return Start(package, blobs, manager, pool, Guid.NewGuid(), null, null);
    }

    /// <summary>
    /// Starts one package of a given job through the adapter of a host with a temp storage and a
    /// logger of the test's own.
    /// </summary>
    /// <param name="package">The package's reference.</param>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="manager">The node's processing manager.</param>
    /// <param name="pool">The job's variables: the package and the result are put in it.</param>
    /// <param name="jobId">The job the package belongs to: packages of one job share its progress.</param>
    /// <param name="workStorage">The host's temp storage, where the adapter makes its work folder; null for a host without one.</param>
    /// <param name="logger">The host's logger; null for one that keeps nothing.</param>
    /// <returns>The status, when the adapter is done.</returns>
    public static Task<IWitProcessingStatus> Start(SmtTaskPackageData package, IWitBlobService blobs, IWitProcessingManager manager, WitVariableCollection pool,
        Guid jobId, IWitTempStorage? workStorage, ILogger? logger)
    {
        // The storage is shown to the adapter once it is made (SmtTestServices says why).
        var services = new SmtTestServices();
        IWitProcessingAdapter adapter = new WitActivityAdapterSmtSolvePackage(manager, blobs, services, logger ?? NullLogger.Instance);
        services.TempStorage = workStorage;

        pool.Add(new WitVariableSmtTaskPackage(PACKAGE_VARIABLE, package));
        pool.Add(new WitVariableSmtResultPackage(RESULT_VARIABLE));

        var activity = new WitActivitySmtSolvePackage { Package = (WitReference)PACKAGE_VARIABLE };
        activity.SetReturnReference(RESULT_VARIABLE);

        return Task.Run(() => adapter.Process(Guid.NewGuid(), jobId, activity, null, pool, reportProgress: false));
    }

    /// <summary>The result package a run left in its variables, or null when it returned none.</summary>
    /// <param name="pool">The job's variables.</param>
    /// <returns>The result package.</returns>
    public static SmtResultPackageData? Result(WitVariableCollection pool)
    {
        return pool.TryGetValue(RESULT_VARIABLE, out SmtResultPackageData? result) ? result : null;
    }

    /// <summary>The fixed words a failure of the node's own is reported with.</summary>
    public static string NodeFailure => WitActivityAdapterSmtSolvePackage.NODE_FAILURE;

    #endregion
}
