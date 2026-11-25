using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The services of a host an adapter is given when a test runs it without an engine: none but a
/// temp storage, and that one only from the moment the test sets it. The adapter of
/// <c>Smt.SolvePackage</c> hands the host's temp storage to the process-wide sandbox when it is
/// made, and that sandbox outlives a test's folder: a storage set after the adapter is made is the
/// adapter's for its own work folders, and nobody else's.
/// </summary>
internal sealed class SmtTestServices : IServiceProvider
{
    #region IServiceProvider

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        return serviceType == typeof(IWitTempStorage) ? TempStorage : null;
    }

    #endregion

    #region Properties

    /// <summary>The host's temp storage; null for a host without one.</summary>
    public IWitTempStorage? TempStorage { get; set; }

    #endregion
}
