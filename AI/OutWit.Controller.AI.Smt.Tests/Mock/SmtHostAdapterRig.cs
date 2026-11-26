using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Adapters;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.References;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Runs the host's activities through the engine's own entry of their adapters, as the server's
/// engine runs them, with a processing manager, a temp storage and the job's variables of the
/// test's own: a job the test can cancel, working folders the test can look into. Each activity
/// is given its arguments by the names a script gives them. The controller's types are met here
/// only, inside method bodies; what a test is handed back is the status and the variables.
/// </summary>
internal static class SmtHostAdapterRig
{
    #region Constants

    /// <summary>The name of the variable an activity returns into.</summary>
    public const string RETURNED = "returned";

    private const string PROBLEMS = "problems";
    private const string OPTIONS = "opts";
    private const string PACKAGES = "packages";
    private const string SOLVED = "solved";
    private const string REPAIRS = "repairs";
    private const string REPAIRED = "repaired";
    private const string RATE = "rate";

    #endregion

    #region Functions

    /// <summary>Starts <c>Smt.Settle</c> of a run of one fan-out (no repair round to speak of, no dispute round).</summary>
    /// <param name="problems">The problem set's blob.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="packages">The packages the fan-out gave out.</param>
    /// <param name="solved">What came back.</param>
    /// <param name="host">The host: its blobs, its processing manager, its temp storage.</param>
    /// <param name="pool">The job's variables: the arguments are put in them.</param>
    /// <returns>The status, when the activity is done.</returns>
    public static Task<IWitProcessingStatus> Settle(Guid problems, SmtOptionsData options, IReadOnlyList<SmtTaskPackageData> packages, IReadOnlyList<SmtResultPackageData> solved,
        (IWitBlobService Blobs, IWitProcessingManager Manager, IWitTempStorage Storage) host, SmtTestVariables pool)
    {
        Fan(pool.With(PROBLEMS, problems).With(OPTIONS, options).With(RATE, options.SamplingRate), packages, solved);

        var activity = new WitActivitySmtSettle
        {
            Problems = (WitReference)PROBLEMS,
            Options = (WitReference)OPTIONS,
            Packages = (WitReference)PACKAGES,
            Results = (WitReference)SOLVED,
            Repairs = (WitReference)REPAIRS,
            Repaired = (WitReference)REPAIRED,
            Rate = (WitReference)RATE
        };

        var services = new SmtTestServices { TempStorage = host.Storage };
        return Start(new WitActivityAdapterSmtSettle(host.Manager, host.Blobs, services, NullLogger.Instance), activity, pool);
    }

    /// <summary>Starts <c>Smt.Disputes</c> of one fan-out.</summary>
    /// <param name="packages">The packages the fan-out gave out.</param>
    /// <param name="solved">What came back.</param>
    /// <param name="host">The host.</param>
    /// <param name="pool">The job's variables.</param>
    /// <returns>The status, when the activity is done.</returns>
    public static Task<IWitProcessingStatus> Disputes(IReadOnlyList<SmtTaskPackageData> packages, IReadOnlyList<SmtResultPackageData> solved,
        (IWitBlobService Blobs, IWitProcessingManager Manager, IWitTempStorage Storage) host, SmtTestVariables pool)
    {
        Fan(pool, packages, solved);

        var activity = new WitActivitySmtDisputes
        {
            Packages = (WitReference)PACKAGES,
            Results = (WitReference)SOLVED,
            Repairs = (WitReference)REPAIRS,
            Repaired = (WitReference)REPAIRED
        };

        var services = new SmtTestServices { TempStorage = host.Storage };
        return Start(new WitActivityAdapterSmtDisputes(host.Manager, host.Blobs, services, NullLogger.Instance), activity, pool);
    }

    /// <summary>Starts <c>Smt.Repairs</c> of one fan-out.</summary>
    /// <param name="packages">The packages the fan-out gave out.</param>
    /// <param name="solved">What came back.</param>
    /// <param name="host">The host.</param>
    /// <param name="pool">The job's variables.</param>
    /// <returns>The status, when the activity is done.</returns>
    public static Task<IWitProcessingStatus> Repairs(IReadOnlyList<SmtTaskPackageData> packages, IReadOnlyList<SmtResultPackageData> solved,
        (IWitBlobService Blobs, IWitProcessingManager Manager, IWitTempStorage Storage) host, SmtTestVariables pool)
    {
        Fan(pool.With(RATE, 0.0), packages, solved);

        var activity = new WitActivitySmtRepairs { Packages = (WitReference)PACKAGES, Results = (WitReference)SOLVED, Rate = (WitReference)RATE };

        var services = new SmtTestServices { TempStorage = host.Storage };
        return Start(new WitActivityAdapterSmtRepairs(host.Manager, host.Blobs, services, NullLogger.Instance), activity, pool);
    }

    /// <summary>Starts <c>Smt.Split</c> of a set.</summary>
    /// <param name="problems">The problem set's blob.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="host">The host.</param>
    /// <param name="pool">The job's variables.</param>
    /// <returns>The status, when the activity is done.</returns>
    public static Task<IWitProcessingStatus> Split(Guid problems, SmtOptionsData options, (IWitBlobService Blobs, IWitProcessingManager Manager, IWitTempStorage Storage) host, SmtTestVariables pool)
    {
        pool.With(PROBLEMS, problems).With(OPTIONS, options);

        var activity = new WitActivitySmtSplit { Problems = (WitReference)PROBLEMS, Options = (WitReference)OPTIONS };

        var services = new SmtTestServices { TempStorage = host.Storage };
        return Start(new WitActivityAdapterSmtSplit(host.Manager, host.Blobs, services, NullLogger.Instance), activity, pool);
    }

    /// <summary>Starts <c>Smt.Preflight</c> of a set.</summary>
    /// <param name="problems">The problem set's blob.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="host">The host.</param>
    /// <param name="pool">The job's variables.</param>
    /// <returns>The status, when the activity is done.</returns>
    public static Task<IWitProcessingStatus> Preflight(Guid problems, SmtOptionsData options, (IWitBlobService Blobs, IWitProcessingManager Manager, IWitTempStorage Storage) host,
        SmtTestVariables pool)
    {
        pool.With(PROBLEMS, problems).With(OPTIONS, options);

        var activity = new WitActivitySmtPreflight { Problems = (WitReference)PROBLEMS, Options = (WitReference)OPTIONS };

        return Start(new WitActivityAdapterSmtPreflight(host.Manager, host.Blobs, NullLogger.Instance), activity, pool);
    }

    #endregion

    #region Tools

    /// <summary>Puts a fan-out into the variables, with a repair round that gave nothing out.</summary>
    private static void Fan(SmtTestVariables pool, IReadOnlyList<SmtTaskPackageData> packages, IReadOnlyList<SmtResultPackageData> solved)
    {
        pool.With(PACKAGES, packages.ToList())
            .With(SOLVED, solved.ToList())
            .With(REPAIRS, new List<SmtTaskPackageData>())
            .With(REPAIRED, new List<SmtResultPackageData>());
    }

    private static Task<IWitProcessingStatus> Start(IWitProcessingAdapter adapter, IWitActivity activity, SmtTestVariables pool)
    {
        if (activity is WitActivityFunction function)
            function.SetReturnReference(RETURNED);

        return Task.Run(() => adapter.Process(Guid.NewGuid(), Guid.NewGuid(), activity, null, pool, reportProgress: false));
    }

    #endregion
}
