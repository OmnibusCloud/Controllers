using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Output;
using OutWit.Controller.Visualization.ParaView.Processes;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// Runs one ParaView render batch on a node end to end — a single task is the one-output batch —:
/// resolve the runtime, build the isolated workspace, materialize the state and the batch's attachment
/// union, write the controller-owned runner and (when required) the bundled reader, invoke pvpython
/// ONCE under the allowlisted environment, interpret exit code + status document, validate every
/// output, publish each, and clean up. Every failure path leaves no partial output behind; a batch
/// is all-or-nothing (docs 03, section 13 — a frame set with a hole is never published).
/// </summary>
public sealed class ParaViewTaskExecutor
{
    #region Constants

    /// <summary>pvpython options placed before the runner script.</summary>
    public static readonly IReadOnlyList<string> PVPYTHON_OPTIONS = ["--force-offscreen-rendering", "--disable-registry"];

    private const string RENDER_FRAME = "ParaView.RenderFrame";

    private const string RENDER_FRAME_BATCH = "ParaView.RenderFrameBatch";

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
    /// <param name="allowlist">The proxy allowlist handed to the runner for the post-load check.</param>
    /// <param name="logger">Diagnostics sink.</param>
    public ParaViewTaskExecutor(IWitBlobService blobService, IWitTempStorage tempStorage, ParaViewProxyAllowlist allowlist, ILogger logger)
    {
        m_blobService = blobService;
        m_tempStorage = tempStorage;
        m_allowlist = allowlist;
        m_logger = logger;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Executes one task: the one-output batch through the same pipeline as a chunk.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="jobId">Owning job.</param>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="cancellationToken">Kills the runner process tree when signaled.</param>
    /// <returns>The render result with the published output blob.</returns>
    /// <exception cref="InvalidOperationException">Materialization, the runner, or output validation failed.</exception>
    /// <exception cref="OperationCanceledException">The task was cancelled.</exception>
    public async Task<ParaViewRenderResultData> ExecuteAsync(ParaViewRenderTaskData task, Guid jobId, string pvpythonPath, CancellationToken cancellationToken)
    {
        var batch = await ExecuteBatchAsync(ParaViewTaskSplitter.BatchOf(task), jobId, pvpythonPath, RENDER_FRAME, cancellationToken);
        return batch.Results[0];
    }

    /// <summary>
    /// Executes one batch: every output of the chunk in one pvpython process.
    /// </summary>
    /// <param name="batch">The batch.</param>
    /// <param name="jobId">Owning job.</param>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="cancellationToken">Kills the runner process tree when signaled.</param>
    /// <returns>The per-output results with the published blobs, in render order.</returns>
    /// <exception cref="InvalidOperationException">Materialization, the runner, or an output's validation failed.</exception>
    /// <exception cref="OperationCanceledException">The batch was cancelled.</exception>
    public Task<ParaViewRenderResultBatchData> ExecuteBatchAsync(ParaViewRenderTaskBatchData batch, Guid jobId, string pvpythonPath, CancellationToken cancellationToken)
    {
        return ExecuteBatchAsync(batch, jobId, pvpythonPath, RENDER_FRAME_BATCH, cancellationToken);
    }

    /// <summary>
    /// The pvpython argument array: options, the runner script, then the runner's own arguments.
    /// </summary>
    /// <param name="runnerPath">Runner script path.</param>
    /// <param name="taskFilePath">Task file path.</param>
    /// <returns>The argument array.</returns>
    public static IReadOnlyList<string> BuildArguments(string runnerPath, string taskFilePath)
    {
        var arguments = new List<string>(PVPYTHON_OPTIONS) { runnerPath, ParaViewRunnerTask.TASK_FILE_ARGUMENT, taskFilePath };
        return arguments;
    }

    /// <summary>
    /// Builds the runner task document for a batch in a workspace: the shared state, view, size,
    /// format and policy lists once, then one output record per task with its file, timestep and
    /// camera move.
    /// </summary>
    /// <param name="batch">The batch.</param>
    /// <param name="workspace">The workspace.</param>
    /// <param name="outputPaths">The output path of every task, in task order.</param>
    /// <param name="pluginPath">The bundled reader path or null.</param>
    /// <param name="requiredPlugins">Names of the required plugins.</param>
    /// <returns>The runner task.</returns>
    public ParaViewRunnerTask BuildRunnerTask(
        ParaViewRenderTaskBatchData batch,
        ParaViewTaskWorkspace workspace,
        IReadOnlyList<string> outputPaths,
        string? pluginPath,
        IReadOnlyList<string> requiredPlugins)
    {
        var options = batch.Options;
        var outputs = new List<ParaViewRunnerOutput>(batch.Tasks.Count);
        for (var index = 0; index < batch.Tasks.Count; index++)
        {
            var task = batch.Tasks[index];
            outputs.Add(new ParaViewRunnerOutput
            {
                Index = index,
                TaskId = task.TaskId,
                OutputPath = outputPaths[index],
                TimestepIndex = task.TimestepIndex,
                TimeValue = task.TimeValue,
                CameraAzimuth = options.Turntable == null ? 0.0 : task.AzimuthDegrees,
                CameraAxis = ParaViewCameraAxes.WireToken(options.Turntable?.Axis ?? ParaViewTurntableAxis.ViewUp),
                CameraElevation = options.Turntable == null ? 0.0 : task.ElevationDegrees,
                CameraDolly = options.Turntable == null ? 1.0 : task.DollyFactor
            });
        }

        return new ParaViewRunnerTask
        {
            TaskId = batch.Tasks.Count > 0 ? batch.Tasks[0].TaskId : string.Empty,
            StatePath = workspace.StatePath,
            PackageRoot = workspace.PackageRoot,
            WorkDir = workspace.Root,
            StatusPath = workspace.StatusFilePath,
            ViewId = options.ViewId,
            Width = options.Width,
            Height = options.Height,
            Format = ParaViewImageFormats.WireToken(options.Format),
            TransparentBackground = options.TransparentBackground && ParaViewImageFormats.SupportsTransparency(options.Format),
            PluginPath = pluginPath,
            AllowedProxies = [.. m_allowlist.EffectiveKeys(requiredPlugins)],
            BlockedProxyTypes = [.. ParaViewProxyPolicy.BLOCKED_PROXY_TYPES.Order(StringComparer.Ordinal)],
            BlockedPropertyNames = [.. ParaViewProxyPolicy.BLOCKED_PROPERTY_NAMES.Order(StringComparer.Ordinal)],
            FilePropertyNames = [.. ParaViewProxyPolicy.FILE_PROPERTY_NAMES.Order(StringComparer.Ordinal)],
            FileReferenceGroups = [.. ParaViewProxyPolicy.FILE_REFERENCE_GROUPS],
            MaxStateBytes = ParaViewInputLimits.MAX_STATE_BYTES,
            MaxLogicalPathChars = ParaViewInputLimits.MAX_LOGICAL_PATH_CHARS,
            Outputs = outputs
        };
    }

    #endregion

    #region Tools

    private async Task<ParaViewRenderResultBatchData> ExecuteBatchAsync(
        ParaViewRenderTaskBatchData batch,
        Guid jobId,
        string pvpythonPath,
        string activityName,
        CancellationToken cancellationToken)
    {
        if (batch.Tasks.Count == 0)
            throw new InvalidOperationException($"{activityName}: the batch holds no outputs.");

        var first = batch.Tasks[0];
        using var workspace = ParaViewTaskWorkspace.Create(m_tempStorage, jobId, first.TaskIndex);

        var materialized = await workspace.MaterializeAsync(m_blobService, batch, cancellationToken);
        m_logger.LogInformation("{Activity}: batch {Batch} (tasks {First}..{Last}) materialized the state and {Count} attachment(s) ({Bytes} bytes) into {Root}",
            activityName, batch.BatchIndex, first.TaskIndex, batch.Tasks[^1].TaskIndex, materialized, batch.SubsetBytes, workspace.Root);

        var runnerPath = workspace.WriteEmbedded(ParaViewRuntimeInfo.RUNNER_RESOURCE, workspace.RunnerDirectory, ParaViewRuntimeInfo.RUNNER_FILE_NAME);
        var requiredPlugins = batch.Runtime.Plugins.Select(me => me.Name).ToList();
        var pluginPath = requiredPlugins.Count > 0
            ? workspace.WriteEmbedded(ParaViewRuntimeInfo.FRD_READER_RESOURCE, workspace.PluginsDirectory, ParaViewRuntimeInfo.FRD_READER_FILE_NAME)
            : null;

        var outputPaths = batch.Tasks.Select(workspace.OutputPathFor).ToList();
        if (outputPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputPaths.Count)
            throw new InvalidOperationException($"{activityName}: two outputs of the batch resolve to the same file name.");

        var runnerTask = BuildRunnerTask(batch, workspace, outputPaths, pluginPath, requiredPlugins);
        await File.WriteAllTextAsync(workspace.TaskFilePath, runnerTask.ToJson(), cancellationToken);

        var arguments = BuildArguments(runnerPath, workspace.TaskFilePath);
        var openGlWindow = await ParaViewRenderingBackend.ResolveWindowAsync(pvpythonPath, m_tempStorage, m_logger, cancellationToken);

        ParaViewProcessOutcome outcome;
        ParaViewRunnerStatus status;
        try
        {
            (outcome, status) = await RunRunnerOnceAsync(pvpythonPath, arguments, workspace, openGlWindow, activityName, cancellationToken);
        }
        catch (ParaViewRunnerCrashedException error) when (ParaViewRenderingBackend.IsEglWindow(openGlWindow))
        {
            // Production lesson: a driver's EGL can pass the probe and still segfault real tasks.
            // One CRASHED subprocess (no status document — a segfault, an abort) demotes this node
            // to software for the rest of the process lifetime and the batch retries locally as a
            // whole — the job never sees the crash. A policy refusal, a usage error or the
            // wall-clock limit is the batch's own verdict and is never retried (audit C-M1); the
            // retry starts from a clean slate so nothing of the crashed attempt — not even an
            // output it did finish — can be mistaken for its own (C-M2).
            m_logger.LogWarning("{Activity}: the EGL runner crashed ({Message}); demoting this node to OSMesa and retrying the batch", activityName, error.Message);
            ParaViewRenderingBackend.Demote(pvpythonPath, m_logger);
            workspace.ClearAttemptArtifacts();
            (outcome, status) = await RunRunnerOnceAsync(pvpythonPath, arguments, workspace, ParaViewRunnerEnvironment.OSMESA_WINDOW, activityName, cancellationToken);
        }

        // The allowlist, the compatibility policy and the result provenance are tied to the pinned
        // runtime series; the runtime that actually ran is known only here.
        if (!ParaViewRuntimeInfo.IsSameSeries(status.ParaviewVersion, m_allowlist.RuntimeVersion))
            throw new InvalidOperationException($"{activityName}: the runner reported ParaView '{status.ParaviewVersion}' but this controller is pinned to the {m_allowlist.RuntimeVersion} series (allowlist and compatibility policy); check the bundled runtime or the {ParaViewBinaryResolver.ENV_PVPYTHON_PATH} override.");

        var options = batch.Options;
        var images = ParaViewOutputValidator.ValidateSet(
            outputPaths, workspace.OutputDirectory, options.Format, options.Width, options.Height, options.TransparentBackground);

        var results = new List<ParaViewRenderResultData>(batch.Tasks.Count);
        var outputSeconds = status.Outputs.ToDictionary(me => me.Index, me => me.RenderSeconds);
        for (var index = 0; index < batch.Tasks.Count; index++)
        {
            var task = batch.Tasks[index];
            var (image, byteSize) = images[index];
            var imageBlobId = await m_blobService.UploadFileAsync(outputPaths[index]);
            var renderSeconds = outputSeconds.TryGetValue(index, out var seconds) ? seconds : status.RenderSeconds / batch.Tasks.Count;

            results.Add(new ParaViewRenderResultData
            {
                TaskId = task.TaskId,
                TaskIndex = task.TaskIndex,
                ViewId = task.ViewId,
                TimestepIndex = task.TimestepIndex,
                TimeValue = task.TimeValue,
                ImageBlobId = imageBlobId,
                Width = image.Width,
                Height = image.Height,
                Format = image.Format,
                ByteSize = byteSize,
                RuntimeVersion = status.ParaviewVersion,
                ReaderVersion = status.ReaderVersion,
                // The process wall clock shared by the chunk's outputs: the per-output cost the batch
                // actually paid, startup included — comparable with a single-frame task's figure.
                RenderSeconds = outcome.ElapsedSeconds / batch.Tasks.Count,
                Diagnostics = Truncate($"stage={status.Stage}; backend={status.Backend}; proxies={status.ProxyCount}; render={renderSeconds:F2}s; batch={index + 1}/{batch.Tasks.Count}", ParaViewInputLimits.MAX_DIAGNOSTICS_CHARS)
            });
        }

        m_logger.LogInformation("{Activity}: batch {Batch} rendered {Count} output(s) of {Width}x{Height} {Format} in {Seconds:F1} s ({PerOutput:F2} s per output, render {Render:F2} s) with ParaView {Version}",
            activityName, batch.BatchIndex, results.Count, options.Width, options.Height, options.Format, outcome.ElapsedSeconds, outcome.ElapsedSeconds / results.Count, status.RenderSeconds, status.ParaviewVersion);

        return new ParaViewRenderResultBatchData { Results = results };
    }

    /// <summary>
    /// One runner invocation with the given window class: build the environment, run, and validate
    /// exit code and status document. Throws on every failure path.
    /// </summary>
    private async Task<(ParaViewProcessOutcome Outcome, ParaViewRunnerStatus Status)> RunRunnerOnceAsync(
        string pvpythonPath,
        IReadOnlyList<string> arguments,
        ParaViewTaskWorkspace workspace,
        string? openGlWindow,
        string activityName,
        CancellationToken cancellationToken)
    {
        var environment = ParaViewRunnerEnvironment.Build(
            pvpythonPath, workspace.HomeDirectory, workspace.TempDirectory, openGlWindow);

        var outcome = await ParaViewProcessRunner.RunAsync(
            pvpythonPath, arguments, workspace.PackageRoot, environment, ParaViewInputLimits.TASK_WALL_CLOCK_LIMIT, m_logger, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var status = ParaViewRunnerStatus.TryRead(workspace.StatusFilePath);
        if (outcome.TimedOut)
            throw new InvalidOperationException($"{activityName}: the runner exceeded its {ParaViewInputLimits.TASK_WALL_CLOCK_LIMIT.TotalMinutes:0}-minute wall-clock limit and was terminated.{Describe(status, outcome)}");

        // The runner writes its status in a finally block, so a non-zero exit WITHOUT a status means
        // the interpreter never got there — the process died (a segfault, an abort, an OOM kill).
        if (status == null && outcome.ExitCode != 0)
            throw new ParaViewRunnerCrashedException($"{activityName}: the runner died without writing a status document (exit code {outcome.ExitCode}).{Describe(null, outcome)}");

        if (outcome.ExitCode != 0)
            throw new InvalidOperationException($"{activityName}: the runner exited with code {outcome.ExitCode}.{Describe(status, outcome)}");

        if (status == null)
            throw new InvalidOperationException($"{activityName}: the runner exited successfully but wrote no status document.{Describe(null, outcome)}");

        if (!status.Ok)
            throw new InvalidOperationException($"{activityName}: the runner reported a failure at stage '{status.Stage}': {status.Error}{Describe(null, outcome)}");

        return (outcome, status);
    }

    private static string Describe(ParaViewRunnerStatus? status, ParaViewProcessOutcome outcome)
    {
        var parts = new List<string>();

        if (status != null && !string.IsNullOrEmpty(status.Error))
            parts.Add($"status: [{status.Stage}] {status.Error}");

        var failed = status?.FirstFailedOutput();
        if (failed != null && !string.IsNullOrEmpty(failed.Error))
            parts.Add($"output {failed.Index}: [{failed.Stage}] {failed.Error}");

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
