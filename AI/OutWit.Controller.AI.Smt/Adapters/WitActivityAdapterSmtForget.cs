using System.Collections;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// <c>Smt.Forget</c> on the host: empties the collection of result packages it is given, which
/// the call is assigned back to. Nothing is deleted: the blobs stay as the platform keeps them,
/// and the documents were written from the ledger before. It reads nothing and decides nothing.
/// </summary>
internal sealed class WitActivityAdapterSmtForget : WitActivityAdapterFunction<WitActivitySmtForget>
{
    #region Constructors

    /// <summary>Makes the adapter.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtForget(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
    }

    #endregion

    #region Processing

    /// <summary>Empties the collection.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A finished task.</returns>
    /// <exception cref="InvalidOperationException">The call is not assigned back to the variable it is given, the variable is no collection of result packages, or it did not take the empty collection.</exception>
    protected override Task Process(WitActivitySmtForget activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        // The variable it is given is the one it empties: a call assigned to another would leave
        // the results where they were.
        if (!string.Equals(activity.Results?.Reference, activity.ReturnReference, StringComparison.Ordinal))
            throw new InvalidOperationException("Smt.Forget empties the variable it is given: the call is assigned back to that variable.");

        if (!pool.TryGetObject(activity.Results, out var results) || results is not IEnumerable<SmtResultPackageData?>)
            throw new InvalidOperationException("Failed to get parameter 'Results': Smt.Forget empties a collection of result packages.");

        // A variable that does not take the value is not told apart by the pool: it is read back.
        if (!pool.TrySetValue(activity.ReturnReference, Array.Empty<SmtResultPackageData?>())
            || !pool.TryGetObject(activity.ReturnReference, out var emptied) || emptied is not ICollection { Count: 0 })
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: the collection to empty.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    /// <exception cref="ArgumentException">The call has another number of parameters, or its parameter is not a variable reference.</exception>
    protected override WitActivitySmtForget CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            return new WitActivitySmtForget { Results = SmtAdapterTools.Reference(parameters, 0, "Results") };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion
}
