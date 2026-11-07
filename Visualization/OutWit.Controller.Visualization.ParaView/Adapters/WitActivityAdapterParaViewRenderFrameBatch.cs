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
/// Adapter for <see cref="WitActivityParaViewRenderFrameBatch"/>: node-side execution of one chunk
/// through <see cref="ParaViewTaskExecutor"/> — one materialization, one pvpython process, every
/// output validated and published. The work estimate sums the chunk's output pixels and adds the
/// bytes the node materializes once for the whole chunk; the node benchmark measures the same unit
/// on the same shape (several frames per process), so the grid allocator balances chunks by what a
/// node really achieves on them.
/// </summary>
internal sealed class WitActivityAdapterParaViewRenderFrameBatch : WitActivityAdapterFunction<WitActivityParaViewRenderFrameBatch>
{
    #region Constants

    private const string ACTIVITY_NAME = "ParaView.RenderFrameBatch";

    private const double BYTES_PER_PIXEL_EQUIVALENT = 64.0;

    #endregion

    #region Fields

    private string? m_pvpythonPath;

    #endregion

    #region Constructors

    public WitActivityAdapterParaViewRenderFrameBatch(
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

    protected override WitActivityParaViewRenderFrameBatch CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException($"{ACTIVITY_NAME} expects 1 parameter (ParaViewRenderTaskBatch), got {parameters.Length}");

        return new WitActivityParaViewRenderFrameBatch
        {
            Batch = parameters[0]
        };
    }

    protected override async Task Process(
        WitActivityParaViewRenderFrameBatch activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Batch, out ParaViewRenderTaskBatchData? batch) || batch == null)
            throw new InvalidOperationException("Failed to get ParaViewRenderTaskBatch parameter 'batch'");

        ProcessingManager.ThrowIfCancellationRequested(status.JobId);

        var pvpythonPath = ResolvePvpython();
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var executor = new ParaViewTaskExecutor(BlobService, TempStorage, ParaViewProxyAllowlist.Bundled, Logger);

        var result = await executor.ExecuteBatchAsync(batch, status.JobId, pvpythonPath, cancellationToken);

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for {ACTIVITY_NAME}.");
    }

    protected override double EstimateWork(WitActivityParaViewRenderFrameBatch activity, IWitVariablesCollection pool)
    {
        if (!pool.TryGetValue(activity.Batch, out ParaViewRenderTaskBatchData? batch) || batch == null || batch.Tasks.Count == 0)
            return 1.0;

        // Every output costs its pixels; the chunk's attachment union is materialized once.
        var pixels = (double)Math.Max(1, batch.Options.Width) * Math.Max(1, batch.Options.Height) * batch.Tasks.Count;
        return pixels + Math.Max(0, batch.SubsetBytes) / BYTES_PER_PIXEL_EQUIVALENT;
    }

    /// <summary>
    /// Measures this node's throughput on the batch shape: complete cycles of one pvpython process
    /// rendering <see cref="ParaViewBenchmark.BATCH_CYCLE_FRAMES"/> frames of the procedural scene,
    /// in output pixels per second. A node without a usable runtime reports rate 0 rather than
    /// failing the benchmark pass.
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
            return ParaViewBenchmark.CreateUnavailableResult(ParaViewBenchmark.BATCH_DATASET_ID);
        }

        var result = await ParaViewBenchmark.MeasureAsync(pvpythonPath, TempStorage, options, Logger, cancellationToken, ParaViewBenchmark.BATCH_CYCLE_FRAMES, ACTIVITY_NAME);

        Logger.LogInformation(
            "{ActivityName} benchmark: {Rate:F0} {Unit} on {Window} ({Cycles} cycles of {FramesPerCycle} frames at {Resolution}, {Elapsed}) with ParaView {Version}",
            ACTIVITY_NAME,
            result.Rate,
            ParaViewBenchmark.UNIT,
            result.Custom?[ParaViewBenchmark.CUSTOM_RENDER_WINDOW],
            result.Iterations,
            ParaViewBenchmark.BATCH_CYCLE_FRAMES,
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
