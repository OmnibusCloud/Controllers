using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Activities;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Adapters;

/// <summary>
/// Adapter for <see cref="WitActivityParaViewCompose"/>: node-side composition of a scene from bare
/// data through <see cref="ParaViewComposeExecutor"/>. Dispatched once per job by <c>Grid.Delegate()</c>
/// to the fastest compatible node; the node benchmark (<see cref="ParaViewComposeBenchmark"/>) is a
/// cheap compose-cycle measurement so the ranking exists without a long second benchmark pass.
/// </summary>
internal sealed class WitActivityAdapterParaViewCompose : WitActivityAdapterFunction<WitActivityParaViewCompose>
{
    #region Constants

    private const string ACTIVITY_NAME = ParaViewComposeExecutor.ACTIVITY_NAME;

    private const double BYTES_PER_WORK_UNIT = 1024.0 * 1024.0;

    #endregion

    #region Fields

    private string? m_pvpythonPath;

    #endregion

    #region Constructors

    public WitActivityAdapterParaViewCompose(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        IWitTempStorage tempStorage,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        TempStorage = tempStorage;
    }

    #endregion

    #region Functions

    protected override WitActivityParaViewCompose CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException($"ParaView.Compose expects 2 parameters (ParaViewDataScene, ParaViewOutputOptions), got {parameters.Length}");

        return new WitActivityParaViewCompose
        {
            Data = parameters[0],
            Options = parameters[1]
        };
    }

    protected override async Task Process(
        WitActivityParaViewCompose activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Data, out ParaViewDataSceneData? data) || data == null)
            throw new InvalidOperationException("Failed to get ParaViewDataScene parameter 'data'");

        if (!pool.TryGetValue(activity.Options, out ParaViewOutputOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get ParaViewOutputOptions parameter 'options'");

        ProcessingManager.ThrowIfCancellationRequested(status.JobId);

        var pvpythonPath = ResolvePvpython();
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var executor = new ParaViewComposeExecutor(BlobService, TempStorage, ParaViewProxyAllowlist.Bundled, Logger);

        var scene = await executor.ExecuteAsync(data, options, status.JobId, pvpythonPath, cancellationToken);

        if (!pool.TrySetValue(activity.ReturnReference, scene))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for ParaView.Compose.");
    }

    protected override double EstimateWork(WitActivityParaViewCompose activity, IWitVariablesCollection pool)
    {
        if (!pool.TryGetValue(activity.Data, out ParaViewDataSceneData? data) || data == null)
            return 1.0;

        var bytes = data.Attachments.Sum(me => Math.Max(0, me.Size));
        return 1.0 + bytes / BYTES_PER_WORK_UNIT;
    }

    /// <summary>
    /// Measures this node's compose throughput (compose cycles per second on the embedded benchmark
    /// result). A node without a usable runtime reports rate 0 rather than failing the benchmark pass.
    /// </summary>
    /// <param name="options">Engine benchmark options or null for defaults.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The benchmark result.</returns>
    public override async Task<IWitBenchmarkResult> RunBenchmark(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        string pvpythonPath;
        try
        {
            pvpythonPath = ResolvePvpython();
        }
        catch (InvalidOperationException exception)
        {
            Logger.LogWarning("{ActivityName} benchmark: {Message}", ACTIVITY_NAME, exception.Message);
            return ParaViewComposeBenchmark.CreateUnavailableResult();
        }

        var result = await ParaViewComposeBenchmark.MeasureAsync(pvpythonPath, TempStorage, options, Logger, cancellationToken);

        Logger.LogInformation(
            "{ActivityName} benchmark: {Rate:F3} {Unit} ({Cycles} cycle(s) of {CycleSeconds} s) with ParaView {Version}",
            ACTIVITY_NAME,
            result.Rate,
            ParaViewComposeBenchmark.UNIT,
            result.Iterations,
            result.Custom?[ParaViewComposeBenchmark.CUSTOM_CYCLE_SECONDS],
            result.Custom?[ParaViewComposeBenchmark.CUSTOM_PARAVIEW_VERSION]);

        return result;
    }

    #endregion

    #region Tools

    /// <summary>
    /// Resolves pvpython once per adapter lifetime; re-resolves only when the cached path has disappeared.
    /// </summary>
    private string ResolvePvpython()
    {
        if (m_pvpythonPath != null && File.Exists(m_pvpythonPath))
            return m_pvpythonPath;

        m_pvpythonPath = ParaViewBinaryResolver.Resolve(typeof(WitControllerParaViewModule).Assembly.Location, Logger)
            ?? throw new InvalidOperationException(
                "pvpython not found in the ParaView controller module. Ensure the module carries the bundled ParaView runtime for this platform " +
                $"(or set {ParaViewBinaryResolver.ENV_PVPYTHON_PATH}).");

        return m_pvpythonPath;
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    private IWitTempStorage TempStorage { get; }

    #endregion
}
