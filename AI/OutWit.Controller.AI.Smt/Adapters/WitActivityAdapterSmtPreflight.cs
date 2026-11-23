using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Planning;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// <c>Smt.Preflight</c> on the host: one pass over the problem set, a row at a time and nothing
/// kept, and the options checked by name (<see cref="SmtPreflightReport"/>). Options out of range
/// are said, not thrown: the set is still read with each such option at its default.
/// </summary>
internal sealed class WitActivityAdapterSmtPreflight : WitActivityAdapterFunction<WitActivitySmtPreflight>
{
    #region Constructors

    /// <summary>Makes the adapter.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="blobService">The host's blob service.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtPreflight(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    /// <summary>Makes the preflight of the set and the options the activity names.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A task that ends when the preflight is set.</returns>
    protected override async Task Process(WitActivitySmtPreflight activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var problemsPath = await BlobService.GetLocalPathAsync(SmtAdapterTools.Blob(pool, activity.Problems, "Problems"));
        var options = SmtAdapterTools.Options(pool, activity.Options);

        var report = await Task.Run(() => SmtPreflightReport.Of(problemsPath, options));

        if (!pool.TrySetValue(activity.ReturnReference, report))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: the problem set and the options.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtPreflight CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            return new WitActivitySmtPreflight
            {
                Problems = SmtAdapterTools.Reference(parameters, 0, "Problems"),
                Options = SmtAdapterTools.Reference(parameters, 1, "Options")
            };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
