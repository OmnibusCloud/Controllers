using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

/// <summary>
/// Adapter for <see cref="WitActivityRenderBakeSimulation"/>. Runs on ONE node (via Grid.Delegate): it
/// materializes the scene next to a working copy of the blend, bakes the simulation to a per-frame OpenVDB
/// cache (<see cref="BlenderRunner.BakeSimulationAsync"/>), uploads the baked blend and each frame's cache
/// file as blobs, and returns a new <see cref="RenderSceneRefData"/> carrying the original dependencies plus
/// the Frame-tagged cache attachments. The downstream <c>Render.BuildBlendFromRefs → Render.SplitBatched</c>
/// path then slices that cache per frame exactly as for a user-prebaked sim.
///
/// The benchmark mirrors the render benchmark (render-throughput) so Grid.Delegate ranks bake nodes by the
/// same model it uses to distribute frames — the fastest render node also bakes.
/// </summary>
internal sealed class WitActivityAdapterRenderBakeSimulation : WitActivityAdapterFunction<WitActivityRenderBakeSimulation>
{
    #region Constants

    private const string FluidCacheKind = "FluidCache";
    private const string SceneAttachmentBlobPackaging = "SceneAttachmentBlob";
    private const string BakeBenchmarkDataset = "benchmark-bake-cycles@v1";

    #endregion

    #region Constructors

    public WitActivityAdapterRenderBakeSimulation(
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

    protected override WitActivityRenderBakeSimulation CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 4)
            throw new ArgumentException($"Render.BakeSimulation expects 4 parameters, got {parameters.Length}");

        return new WitActivityRenderBakeSimulation
        {
            Scene = parameters[0],
            StartFrame = parameters[1],
            EndFrame = parameters[2],
            Options = parameters[3]
        };
    }

    protected override async Task Process(
        WitActivityRenderBakeSimulation activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out RenderSceneRefData? scene) || scene == null)
            throw new InvalidOperationException("Failed to get RenderSceneRef parameter 'scene'");

        if (scene.BlendBlobId == Guid.Empty)
            throw new InvalidOperationException("Render.BakeSimulation requires a non-empty BlendBlobId.");

        if (!pool.TryGetValue(activity.StartFrame, out int startFrame))
            throw new InvalidOperationException("Failed to get Int parameter 'startFrame'");

        if (!pool.TryGetValue(activity.EndFrame, out int endFrame))
            throw new InvalidOperationException("Failed to get Int parameter 'endFrame'");

        if (endFrame < startFrame)
            throw new InvalidOperationException($"endFrame ({endFrame}) must be >= startFrame ({startFrame})");

        pool.TryGetValue(activity.Options, out RenderBakeOptionsData? options);
        var resolutionMax = options?.ResolutionMax ?? 0;

        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);

        // Materialize the scene next to a working copy of the blend so it opens fully for baking.
        var sourceBlendPath = await BlobService.GetLocalPathAsync(scene.BlendBlobId);
        var (workingBlendPath, workingDirectory) = await RenderSceneAttachmentTransfer.PrepareWorkingSceneAsync(
            BlobService, TempStorage.RootPath, sourceBlendPath, scene.AttachedFiles, status.JobId, taskIndex: 0, cancellationToken);

        try
        {
            var runner = GetBlenderRunner();

            Logger.LogInformation("Render.BakeSimulation: baking frames {Start}-{End} (resolutionMax={Res})",
                startFrame, endFrame, resolutionMax);

            // Bakes in place: workingBlendPath becomes the baked scene; cache files sit under its relative dir.
            var bakeResult = await runner.BakeSimulationAsync(workingBlendPath, startFrame, endFrame, resolutionMax, cancellationToken);

            // Re-upload the baked blend as a fresh blob.
            var bakedBlendBlobId = await BlobService.UploadFileAsync(workingBlendPath);

            // Carry the original (frame-independent) dependencies forward unchanged.
            var attachments = scene.AttachedFiles
                .Select(me => (RenderSceneAttachmentRefData)me.Clone())
                .ToList();

            // Upload each rendered frame's cache file as a Frame-tagged attachment.
            var sceneDirectory = Path.GetDirectoryName(workingBlendPath)
                                 ?? throw new InvalidOperationException("Failed to resolve the baked scene directory.");

            int uploaded = 0;
            foreach (var entry in bakeResult.Cache)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Only ship cache files for frames that will actually be rendered.
                if (entry.Frame is { } frame && (frame < startFrame || frame > endFrame))
                    continue;

                var cacheFullPath = RenderSceneAttachmentTransfer.ResolveAttachmentTargetPath(sceneDirectory, entry.RelativePath);
                if (!File.Exists(cacheFullPath))
                {
                    Logger.LogWarning("Render.BakeSimulation: baked cache file missing on disk: {Path}", entry.RelativePath);
                    continue;
                }

                var cacheBlobId = await BlobService.UploadFileAsync(cacheFullPath);
                attachments.Add(new RenderSceneAttachmentRefData
                {
                    Kind = FluidCacheKind,
                    BlobId = cacheBlobId,
                    OriginalPath = entry.OriginalPath,
                    RelativePath = entry.RelativePath,
                    PackagingStrategy = SceneAttachmentBlobPackaging,
                    Frame = entry.Frame
                });
                uploaded++;
            }

            Logger.LogInformation(
                "Render.BakeSimulation: {Domains} fluid domain(s) + {PointCaches} point-cache sim(s) baked, {Uploaded} frame cache file(s) attached (of {Total} produced); point-cache sims travel embedded in the baked blend",
                bakeResult.BakedDomains, bakeResult.BakedPointCaches, uploaded, bakeResult.Cache.Count);

            var bakedScene = new RenderSceneRefData
            {
                BlendBlobId = bakedBlendBlobId,
                AttachedFiles = attachments
            };

            if (!pool.TrySetValue(activity.ReturnReference, bakedScene))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.BakeSimulation.");
        }
        finally
        {
            RenderSceneAttachmentTransfer.TryDeleteWorkingScene(workingDirectory);
        }
    }

    #endregion

    #region Benchmarking

    // A single delegated task; node ORDERING is by node Rate (render throughput), so a flat estimate is fine.
    protected override double EstimateWork(WitActivityRenderBakeSimulation activity, IWitVariablesCollection pool) => 1.0;

    public override async Task<IWitBenchmarkResult> RunBenchmark(
        IWitBenchmarkOptions? options,
        CancellationToken cancellationToken)
    {
        var runner = RenderBenchmarkHelper.TryCreateBlenderRunner(Logger, TempStorage);
        if (runner == null)
            return RenderBenchmarkHelper.CreateUnavailableResult(RenderBenchmarkHelper.FRAME_UNIT, BakeBenchmarkDataset);

        // Same render-throughput benchmark as Render.FrameBatch — so Grid.Delegate ranks bake candidates by
        // the SAME model used to distribute frames (a node that renders Cycles fast also bakes fast enough).
        var result = await RenderBenchmarkHelper.MeasureRenderAsync(
            runner,
            RenderEngine.Cycles,
            options,
            unit: RenderBenchmarkHelper.FRAME_UNIT,
            datasetId: BakeBenchmarkDataset,
            cancellationToken: cancellationToken);

        Logger.LogInformation("Render.BakeSimulation benchmark: {Rate:F0} {Unit}", result.Rate, RenderBenchmarkHelper.FRAME_UNIT);
        return result;
    }

    #endregion

    #region Tools

    private BlenderRunner GetBlenderRunner()
    {
        return m_blenderRunner ??= RenderBenchmarkHelper.TryCreateBlenderRunner(Logger, TempStorage)
            ?? throw new InvalidOperationException(
                "Blender not found in the render controller module. Ensure the module includes the Blender portable installation.");
    }

    #endregion

    #region Fields

    private BlenderRunner? m_blenderRunner;

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    private IWitTempStorage TempStorage { get; }

    #endregion
}
