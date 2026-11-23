using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Diagnostics;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary><c>Smt.RuntimeDiagnostics</c>: the diagnostics of this process's controller (<see cref="SmtRuntimeDiagnosticsBuilder"/>).</summary>
internal sealed class WitActivityAdapterSmtRuntimeDiagnostics : WitActivityAdapterFunction<WitActivitySmtRuntimeDiagnostics>
{
    #region Constructors

    /// <summary>Makes the adapter of a host or a node.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtRuntimeDiagnostics(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
    }

    #endregion

    #region Processing

    /// <summary>Builds the diagnostics and sets them as the return value.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A completed task.</returns>
    protected override Task Process(WitActivitySmtRuntimeDiagnostics activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var diagnostics = SmtRuntimeDiagnosticsBuilder.Build();

        if (!pool.TrySetValue(activity.ReturnReference, diagnostics))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: no parameter.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtRuntimeDiagnostics CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 0)
            throw new ArgumentException($"Expected 0 parameter(s), got {parameters.Length}.");

        return new WitActivitySmtRuntimeDiagnostics();
    }

    #endregion
}
