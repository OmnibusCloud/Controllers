using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Processes;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// Runs <c>ParaView.Compose</c> on a node end to end (docs 06, part A): materialize the data scene's
/// attachment into an isolated workspace, write the controller-owned composer script and the
/// bundled reader, invoke pvpython under the allowlisted environment, read the status document, hash
/// and publish the saved state, and return the ordinary package reference the rest of the chain
/// consumes. The reference is run through the host validator BEFORE it is returned: a composer that
/// ever produced something the allowlist refuses fails here, on the node, with the validator's words.
/// </summary>
public sealed class ParaViewComposeExecutor
{
    #region Constants

    /// <summary>Activity name for diagnostics.</summary>
    public const string ACTIVITY_NAME = "ParaView.Compose";

    /// <summary>Most timesteps the "all timesteps" fit inspects inside the composer (evenly sampled, the last always included).</summary>
    public const int MAX_FIT_SAMPLES = 25;

    /// <summary>The producer tag stamped into the package reference's runtime requirement.</summary>
    public const string PRODUCER_TAG = "ParaView.Compose";

    /// <summary>Schema of the manifest JSON the composed package carries as provenance.</summary>
    public const int MANIFEST_SCHEMA = 1;

    #endregion

    #region Fields

    private readonly IWitBlobService m_blobService;

    private readonly IWitTempStorage m_tempStorage;

    private readonly ParaViewProxyAllowlist m_allowlist;

    private readonly ILogger m_logger;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an executor over the node's services.
    /// </summary>
    /// <param name="blobService">Blob storage.</param>
    /// <param name="tempStorage">Node temp storage.</param>
    /// <param name="allowlist">The proxy allowlist the composed state is checked against before it is returned.</param>
    /// <param name="logger">Diagnostics sink.</param>
    public ParaViewComposeExecutor(IWitBlobService blobService, IWitTempStorage tempStorage, ParaViewProxyAllowlist allowlist, ILogger logger)
    {
        m_blobService = blobService;
        m_tempStorage = tempStorage;
        m_allowlist = allowlist;
        m_logger = logger;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Composes the scene.
    /// </summary>
    /// <param name="data">The data scene (validated by <see cref="ParaViewDataSceneValidator"/> first).</param>
    /// <param name="options">The output options (view size the camera is framed for).</param>
    /// <param name="jobId">Owning job.</param>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="cancellationToken">Kills the composer process tree when signaled.</param>
    /// <returns>The package reference of the composed state.</returns>
    /// <exception cref="InvalidOperationException">The data scene is invalid, materialization failed, the composer failed, or the composed state does not pass the host validator.</exception>
    /// <exception cref="OperationCanceledException">The composition was cancelled.</exception>
    public async Task<ParaViewSceneRefData> ExecuteAsync(ParaViewDataSceneData data, ParaViewOutputOptionsData options, Guid jobId, string pvpythonPath, CancellationToken cancellationToken)
    {
        var admission = new List<string>();
        ParaViewDataSceneValidator.Validate(data, admission);
        if (admission.Count > 0)
            throw new InvalidOperationException($"{ACTIVITY_NAME}: the data scene is not admissible: {string.Join(" | ", admission)}");

        using var workspace = ParaViewTaskWorkspace.Create(m_tempStorage, jobId, 0);

        var attachment = data.Attachments[0];
        var materialized = await workspace.MaterializeAttachmentAsync(m_blobService, attachment, cancellationToken);
        m_logger.LogInformation("{ActivityName}: materialized '{LogicalPath}' ({Bytes} bytes) into {Root}", ACTIVITY_NAME, materialized.LogicalPath, materialized.Size, workspace.Root);

        var runnerPath = workspace.WriteEmbedded(ParaViewRuntimeInfo.COMPOSE_RUNNER_RESOURCE, workspace.RunnerDirectory, ParaViewRuntimeInfo.COMPOSE_RUNNER_FILE_NAME);
        var pluginPath = workspace.WriteEmbedded(ParaViewRuntimeInfo.FRD_READER_RESOURCE, workspace.PluginsDirectory, ParaViewRuntimeInfo.FRD_READER_FILE_NAME);

        var task = BuildTask(data, options, workspace, materialized, pluginPath);
        await File.WriteAllTextAsync(workspace.ComposeTaskFilePath, task.ToJson(), cancellationToken);

        var arguments = BuildArguments(runnerPath, workspace.ComposeTaskFilePath);
        var openGlWindow = await ParaViewRenderingBackend.ResolveWindowAsync(pvpythonPath, m_tempStorage, m_logger, cancellationToken);

        ParaViewProcessOutcome outcome;
        ParaViewComposeStatus status;
        try
        {
            (outcome, status) = await RunComposeOnceAsync(pvpythonPath, arguments, workspace, openGlWindow, m_logger, cancellationToken);
        }
        catch (InvalidOperationException error) when (ParaViewRenderingBackend.IsEglWindow(openGlWindow))
        {
            // The same self-healing as the render path: a flaky EGL stack demotes the node and the
            // composition retries on the software window.
            m_logger.LogWarning("{ActivityName}: the EGL composer failed ({Message}); demoting this node to OSMesa and retrying", ACTIVITY_NAME, error.Message);
            ParaViewRenderingBackend.Demote(pvpythonPath, m_logger);
            File.Delete(workspace.ComposeStatusFilePath);
            if (File.Exists(workspace.StatePath))
                File.Delete(workspace.StatePath);
            (outcome, status) = await RunComposeOnceAsync(pvpythonPath, arguments, workspace, ParaViewRunnerEnvironment.OSMESA_WINDOW, m_logger, cancellationToken);
        }

        if (!ParaViewRuntimeInfo.IsSameSeries(status.ParaviewVersion, m_allowlist.RuntimeVersion))
            throw new InvalidOperationException($"{ACTIVITY_NAME}: the composer reported ParaView '{status.ParaviewVersion}' but this controller is pinned to the {m_allowlist.RuntimeVersion} series; check the bundled runtime or the {ParaViewBinaryResolver.ENV_PVPYTHON_PATH} override.");

        var stateInfo = new FileInfo(workspace.StatePath);
        if (!stateInfo.Exists || stateInfo.Length == 0)
            throw new InvalidOperationException($"{ACTIVITY_NAME}: the composer reported success but saved no state.");

        if (stateInfo.Length > ParaViewInputLimits.MAX_STATE_BYTES)
            throw new InvalidOperationException($"{ACTIVITY_NAME}: the composed state is {stateInfo.Length} bytes, over the {ParaViewInputLimits.MAX_STATE_BYTES} byte limit.");

        var stateSha256 = ParaViewPackageDigest.HashFile(workspace.StatePath);

        // The composed state must be what the host validator will accept — checked here, with the
        // validator's own words, BEFORE anything is published (the validator reads the state from the
        // local path; the blob id it also insists on is a placeholder until the upload).
        var scene = BuildSceneRef(data, attachment, materialized, status, stateSha256, stateInfo.Length, Guid.NewGuid());
        var validator = new ParaViewPackageValidator(m_allowlist, ParaViewRuntimeInfo.BundledReaderVersion());
        var report = validator.Validate(scene, options, workspace.StatePath);
        if (!report.IsValid)
            throw new InvalidOperationException($"{ACTIVITY_NAME}: the composed state does not pass validation: {string.Join(" | ", report.Errors.Take(8))}");

        var stateBlobId = await m_blobService.UploadFileAsync(workspace.StatePath);
        scene.StateBlobId = stateBlobId;

        m_logger.LogInformation(
            "{ActivityName}: composed '{LogicalPath}' coloured by {Association}/{Array} ({Timesteps} timestep(s), {PointArrays} point / {CellArrays} cell array(s)) into state {StateBlobId} ({Bytes} bytes) in {Seconds:F1} s with ParaView {Version}",
            ACTIVITY_NAME, materialized.LogicalPath, status.ColorAssociation, status.ColorArray.Length == 0 ? "(solid)" : status.ColorArray,
            status.TimestepValues.Count, status.PointArrays.Count, status.CellArrays.Count, stateBlobId, stateInfo.Length, outcome.ElapsedSeconds, status.ParaviewVersion);

        return scene;
    }

    /// <summary>
    /// One composer invocation with the given window class: build the environment, run, and validate
    /// the exit code and status document. Throws on every failure path. Shared with the benchmark.
    /// </summary>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="arguments">Composer arguments.</param>
    /// <param name="workspace">Task workspace.</param>
    /// <param name="openGlWindow">Window class to request or null for the platform default.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Kills the composer process tree when signaled.</param>
    /// <returns>The outcome and the parsed status document.</returns>
    public static async Task<(ParaViewProcessOutcome Outcome, ParaViewComposeStatus Status)> RunComposeOnceAsync(
        string pvpythonPath,
        IReadOnlyList<string> arguments,
        ParaViewTaskWorkspace workspace,
        string? openGlWindow,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var environment = ParaViewRunnerEnvironment.Build(
            pvpythonPath, workspace.HomeDirectory, workspace.TempDirectory, openGlWindow);

        var outcome = await ParaViewProcessRunner.RunAsync(
            pvpythonPath, arguments, workspace.PackageRoot, environment, ParaViewInputLimits.TASK_WALL_CLOCK_LIMIT, logger, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var status = ParaViewComposeStatus.TryRead(workspace.ComposeStatusFilePath);
        if (outcome.TimedOut)
            throw new InvalidOperationException($"{ACTIVITY_NAME}: the composer exceeded its {ParaViewInputLimits.TASK_WALL_CLOCK_LIMIT.TotalMinutes:0}-minute wall-clock limit and was terminated.{Describe(status, outcome)}");

        if (outcome.ExitCode != 0)
            throw new InvalidOperationException($"{ACTIVITY_NAME}: the composer exited with code {outcome.ExitCode}.{Describe(status, outcome)}");

        if (status == null)
            throw new InvalidOperationException($"{ACTIVITY_NAME}: the composer exited successfully but wrote no status document.{Describe(null, outcome)}");

        if (!status.Ok)
            throw new InvalidOperationException($"{ACTIVITY_NAME}: the composer reported a failure at stage '{status.Stage}': {status.Error}{Describe(null, outcome)}");

        return (outcome, status);
    }

    /// <summary>
    /// The pvpython argument array: options, the composer script, then its own arguments.
    /// </summary>
    /// <param name="runnerPath">Composer script path.</param>
    /// <param name="taskFilePath">Task file path.</param>
    /// <returns>The argument array.</returns>
    public static IReadOnlyList<string> BuildArguments(string runnerPath, string taskFilePath)
    {
        return new List<string>(ParaViewTaskExecutor.PVPYTHON_OPTIONS) { runnerPath, ParaViewComposeTask.TASK_FILE_ARGUMENT, taskFilePath };
    }

    /// <summary>
    /// Builds the composer task document for a data scene in a workspace.
    /// </summary>
    /// <param name="data">The data scene.</param>
    /// <param name="options">The output options (view size).</param>
    /// <param name="workspace">The workspace.</param>
    /// <param name="materialized">The materialized attachment.</param>
    /// <param name="pluginPath">The bundled reader path.</param>
    /// <returns>The composer task.</returns>
    public static ParaViewComposeTask BuildTask(
        ParaViewDataSceneData data,
        ParaViewOutputOptionsData options,
        ParaViewTaskWorkspace workspace,
        ParaViewMaterializedAttachment materialized,
        string pluginPath)
    {
        return new ParaViewComposeTask
        {
            PackageRoot = workspace.PackageRoot,
            WorkDir = workspace.Root,
            StatePath = workspace.StatePath,
            StatusPath = workspace.ComposeStatusFilePath,
            DataPath = materialized.Path,
            DataLogicalPath = materialized.LogicalPath,
            RegistrationName = materialized.LogicalPath[(materialized.LogicalPath.LastIndexOf('/') + 1)..],
            PluginPath = pluginPath,
            ColorArrayName = data.ColorArrayName,
            ColorAssociation = ParaViewComposeTokens.WireToken(data.ColorAssociation),
            ColorComponent = data.ColorComponent,
            ColormapPreset = data.ColormapPreset,
            Representation = ParaViewComposeTokens.WireToken(data.Representation),
            ShowScalarBar = data.ShowScalarBar,
            CameraDirection = ParaViewComposeTokens.WireToken(data.CameraDirection),
            FitTo = ParaViewComposeTokens.WireToken(data.FitTo),
            ViewWidth = Math.Max(1, options.Width),
            ViewHeight = Math.Max(1, options.Height),
            MaxFitSamples = MAX_FIT_SAMPLES
        };
    }

    /// <summary>
    /// Builds the package reference of a composed state.
    /// </summary>
    /// <param name="data">The data scene.</param>
    /// <param name="attachment">The declared attachment.</param>
    /// <param name="materialized">The materialized attachment (stamps digest and size).</param>
    /// <param name="status">The composer's status.</param>
    /// <param name="stateSha256">Digest of the saved state.</param>
    /// <param name="stateSize">Size of the saved state.</param>
    /// <param name="stateBlobId">Blob id of the saved state (empty until published).</param>
    /// <returns>The package reference.</returns>
    public static ParaViewSceneRefData BuildSceneRef(
        ParaViewDataSceneData data,
        ParaViewAttachmentRefData attachment,
        ParaViewMaterializedAttachment materialized,
        ParaViewComposeStatus status,
        string stateSha256,
        long stateSize,
        Guid stateBlobId)
    {
        var (major, minor, patch) = ParseVersion(status.ParaviewVersion);

        var stamped = (ParaViewAttachmentRefData)attachment.Clone();
        stamped.Sha256 = materialized.Sha256;
        stamped.Size = materialized.Size;

        var manifest = new
        {
            schema = MANIFEST_SCHEMA,
            producer = PRODUCER_TAG,
            controller = ControllerBuildInfo.VERSION,
            paraview = status.ParaviewVersion,
            reader = status.ReaderVersion,
            data = new { logicalPath = materialized.LogicalPath, sha256 = materialized.Sha256, size = materialized.Size },
            scene = new
            {
                colorArrayName = data.ColorArrayName,
                colorAssociation = ParaViewComposeTokens.WireToken(data.ColorAssociation),
                colorComponent = data.ColorComponent,
                colormapPreset = data.ColormapPreset,
                representation = ParaViewComposeTokens.WireToken(data.Representation),
                showScalarBar = data.ShowScalarBar,
                cameraDirection = ParaViewComposeTokens.WireToken(data.CameraDirection),
                fitTo = ParaViewComposeTokens.WireToken(data.FitTo)
            },
            applied = new
            {
                colorArray = status.ColorArray,
                colorAssociation = status.ColorAssociation,
                colorRange = status.ColorRange,
                bounds = status.Bounds,
                fitSamples = status.FitSamples
            },
            arrays = new { points = status.PointArrays, cells = status.CellArrays },
            timesteps = status.TimestepValues
        };

        return new ParaViewSceneRefData
        {
            StateBlobId = stateBlobId,
            StateSha256 = stateSha256,
            StateSize = stateSize,
            Attachments = [stamped],
            Runtime = new ParaViewRuntimeRequirementData
            {
                ParaViewMajor = major,
                ParaViewMinor = minor,
                ParaViewPatch = patch,
                ProducerPluginVersion = $"{PRODUCER_TAG}/{ControllerBuildInfo.VERSION}",
                ProducerPlatform = RuntimeInformation.RuntimeIdentifier,
                Plugins = [new ParaViewPluginRequirementData { Name = ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME, Version = status.ReaderVersion }]
            },
            TimestepValues = [.. status.TimestepValues],
            PackageManifestJson = JsonSerializer.Serialize(manifest)
        };
    }

    /// <summary>
    /// Parses a runtime version text ("6.1.1", "6.1.1-fake") into its numeric components.
    /// </summary>
    /// <param name="versionText">The version text.</param>
    /// <returns>Major, minor and patch (zero when absent).</returns>
    public static (int Major, int Minor, int Patch) ParseVersion(string? versionText)
    {
        var parts = (versionText ?? string.Empty).Trim().Split('.', '-', '+', ' ');
        return (Component(parts, 0), Component(parts, 1), Component(parts, 2));

        static int Component(string[] parts, int index)
        {
            return index < parts.Length && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }
    }

    #endregion

    #region Tools

    private static string Describe(ParaViewComposeStatus? status, ParaViewProcessOutcome outcome)
    {
        var parts = new List<string>();

        if (status != null && !string.IsNullOrEmpty(status.Error))
            parts.Add($"status: [{status.Stage}] {status.Error}");

        var stderr = outcome.StderrTail.Trim();
        if (stderr.Length > 0)
            parts.Add("stderr tail: " + Truncate(stderr, ParaViewInputLimits.MAX_DIAGNOSTICS_CHARS));

        var stdout = outcome.StdoutTail.Trim();
        if (stdout.Length > 0 && stderr.Length == 0)
            parts.Add("stdout tail: " + Truncate(stdout, ParaViewInputLimits.MAX_DIAGNOSTICS_CHARS));

        return parts.Count == 0 ? string.Empty : " " + string.Join(" | ", parts);
    }

    private static string Truncate(string text, int maxChars)
    {
        return text.Length <= maxChars ? text : text[^maxChars..];
    }

    #endregion
}
