using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView;
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
/// Adapter for <see cref="WitActivityParaViewRenderFrame"/>: node-side execution of one task through
/// <see cref="ParaViewTaskExecutor"/>. The work estimate scales with the output pixels and the bytes
/// the node must materialize, so data movement — the dominant cost for scientific datasets — weighs
/// into distribution; the node benchmark (<see cref="ParaViewBenchmark"/>) measures the same unit
/// (pixels per second), so the grid allocator can set node rates against task work.
/// </summary>
internal sealed class WitActivityAdapterParaViewRenderFrame : WitActivityAdapterFunction<WitActivityParaViewRenderFrame>
{
    #region Constants

    private const string ACTIVITY_NAME = "ParaView.RenderFrame";

    private const double BYTES_PER_PIXEL_EQUIVALENT = 64.0;

    #endregion

    #region Fields

    private string? m_pvpythonPath;

    #endregion

    #region Constructors

    public WitActivityAdapterParaViewRenderFrame(
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

    protected override WitActivityParaViewRenderFrame CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException($"ParaView.RenderFrame expects 1 parameter (ParaViewRenderTask), got {parameters.Length}");

        return new WitActivityParaViewRenderFrame
        {
            Task = parameters[0]
        };
    }

    protected override async Task Process(
        WitActivityParaViewRenderFrame activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Task, out ParaViewRenderTaskData? task) || task == null)
            throw new InvalidOperationException("Failed to get ParaViewRenderTask parameter 'task'");

        ProcessingManager.ThrowIfCancellationRequested(status.JobId);

        var pvpythonPath = ResolvePvpython();
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var executor = new ParaViewTaskExecutor(BlobService, TempStorage, ParaViewProxyAllowlist.Bundled, Logger);

        var result = await executor.ExecuteAsync(task, status.JobId, pvpythonPath, cancellationToken);

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for ParaView.RenderFrame.");
    }

    protected override double EstimateWork(WitActivityParaViewRenderFrame activity, IWitVariablesCollection pool)
    {
        if (!pool.TryGetValue(activity.Task, out ParaViewRenderTaskData? task) || task == null)
            return 1.0;

        var pixels = (double)Math.Max(1, task.Options.Width) * Math.Max(1, task.Options.Height);
        return pixels + Math.Max(0, task.SubsetBytes) / BYTES_PER_PIXEL_EQUIVALENT;
    }

    /// <summary>
    /// Measures this node's ParaView throughput (output pixels per second on the procedural benchmark
    /// scene). A node without a usable runtime reports rate 0 rather than failing the benchmark pass,
    /// so the engine can still schedule its other activities.
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
            return ParaViewBenchmark.CreateUnavailableResult();
        }

        var result = await ParaViewBenchmark.MeasureAsync(pvpythonPath, TempStorage, options, Logger, cancellationToken);

        Logger.LogInformation(
            "{ActivityName} benchmark: {Rate:F0} {Unit} on {Window} ({Frames} frames of {Resolution}, render-only {Elapsed}) with ParaView {Version}",
            ACTIVITY_NAME,
            result.Rate,
            ParaViewBenchmark.UNIT,
            result.Custom?[ParaViewBenchmark.CUSTOM_RENDER_WINDOW],
            result.Iterations,
            result.Custom?[ParaViewBenchmark.CUSTOM_RENDER_RESOLUTION],
            result.Elapsed,
            result.Custom?[ParaViewBenchmark.CUSTOM_PARAVIEW_VERSION]);

        return result;
    }

    #endregion

    #region Tools

    /// <summary>
    /// Resolves pvpython once per adapter lifetime (the module and its runtime folder do not move while
    /// the node runs); re-resolves only when the cached path has disappeared.
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
