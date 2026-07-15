using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifyRuntimeDiagnostics : WitActivityAdapterFunction<WitActivityVerifyRuntimeDiagnostics>
{
    #region Constructors

    public WitActivityAdapterVerifyRuntimeDiagnostics(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override Task Process(WitActivityVerifyRuntimeDiagnostics activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var diagnostics = VerifyRuntimeDiagnosticsBuilder.Build();

        if (!pool.TrySetValue(activity.ReturnReference, diagnostics))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyRuntimeDiagnostics CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 0)
            throw new ArgumentException($"Expected 0 parameter(s), got {parameters.Length}.");

        return new WitActivityVerifyRuntimeDiagnostics();
    }

    #endregion
}
