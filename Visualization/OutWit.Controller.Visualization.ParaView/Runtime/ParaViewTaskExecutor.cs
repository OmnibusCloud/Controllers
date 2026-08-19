using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Output;
using OutWit.Controller.Visualization.ParaView.Processes;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// Runs one ParaView render task on a node end to end: resolve the runtime, build the isolated
/// workspace, materialize the state and the task's attachment subset, write the controller-owned
/// runner and (when required) the bundled reader, invoke pvpython under the allowlisted environment,
/// interpret exit code + status document, validate the output, publish it, and clean up. Every
/// failure path leaves no partial output behind.
/// </summary>
public sealed class ParaViewTaskExecutor
{
    #region Constants

    /// <summary>pvpython options placed before the runner script.</summary>
    public static readonly IReadOnlyList<string> PVPYTHON_OPTIONS = ["--force-offscreen-rendering", "--disable-registry"];

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
    /// Executes one task.
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
        using var workspace = ParaViewTaskWorkspace.Create(m_tempStorage, jobId, task.TaskIndex);

        var materialized = await workspace.MaterializeAsync(m_blobService, task, cancellationToken);
        m_logger.LogInformation("ParaView.RenderFrame: task {TaskIndex} materialized the state and {Count} attachment(s) ({Bytes} bytes) into {Root}",
            task.TaskIndex, materialized, task.SubsetBytes, workspace.Root);

        var runnerPath = workspace.WriteEmbedded(ParaViewRuntimeInfo.RUNNER_RESOURCE, workspace.RunnerDirectory, ParaViewRuntimeInfo.RUNNER_FILE_NAME);
        var requiredPlugins = task.Runtime.Plugins.Select(me => me.Name).ToList();
        var pluginPath = requiredPlugins.Count > 0
            ? workspace.WriteEmbedded(ParaViewRuntimeInfo.FRD_READER_RESOURCE, workspace.PluginsDirectory, ParaViewRuntimeInfo.FRD_READER_FILE_NAME)
            : null;

        var outputPath = workspace.OutputPathFor(task);
        var runnerTask = BuildRunnerTask(task, workspace, outputPath, pluginPath, requiredPlugins);
        await File.WriteAllTextAsync(workspace.TaskFilePath, runnerTask.ToJson(), cancellationToken);

        var arguments = BuildArguments(runnerPath, workspace.TaskFilePath);
        var environment = ParaViewRunnerEnvironment.Build(
            pvpythonPath, workspace.HomeDirectory, workspace.TempDirectory, ParaViewRunnerEnvironment.ForceSoftwareRenderingByDefault());

        var outcome = await ParaViewProcessRunner.RunAsync(
            pvpythonPath, arguments, workspace.PackageRoot, environment, ParaViewInputLimits.TASK_WALL_CLOCK_LIMIT, m_logger, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var status = ParaViewRunnerStatus.TryRead(workspace.StatusFilePath);
        if (outcome.TimedOut)
            throw new InvalidOperationException($"ParaView.RenderFrame: the runner exceeded its {ParaViewInputLimits.TASK_WALL_CLOCK_LIMIT.TotalMinutes:0}-minute wall-clock limit and was terminated.{Describe(status, outcome)}");

        if (outcome.ExitCode != 0)
            throw new InvalidOperationException($"ParaView.RenderFrame: the runner exited with code {outcome.ExitCode}.{Describe(status, outcome)}");

        if (status == null)
            throw new InvalidOperationException($"ParaView.RenderFrame: the runner exited successfully but wrote no status document.{Describe(null, outcome)}");

        if (!status.Ok)
            throw new InvalidOperationException($"ParaView.RenderFrame: the runner reported a failure at stage '{status.Stage}': {status.Error}{Describe(null, outcome)}");

        // The allowlist, the compatibility policy and the result provenance are tied to the pinned
        // runtime series; the runtime that actually ran is known only here.
        if (!ParaViewRuntimeInfo.IsSameSeries(status.ParaviewVersion, m_allowlist.RuntimeVersion))
            throw new InvalidOperationException($"ParaView.RenderFrame: the runner reported ParaView '{status.ParaviewVersion}' but this controller is pinned to the {m_allowlist.RuntimeVersion} series (allowlist and compatibility policy); check the bundled runtime or the {ParaViewBinaryResolver.ENV_PVPYTHON_PATH} override.");

        var (image, byteSize) = ParaViewOutputValidator.Validate(
            outputPath, workspace.OutputDirectory, task.Options.Format, task.Options.Width, task.Options.Height, task.Options.TransparentBackground);

        var imageBlobId = await m_blobService.UploadFileAsync(outputPath);

        m_logger.LogInformation("ParaView.RenderFrame: task {TaskIndex} rendered {Width}x{Height} {Format} ({Bytes} bytes) in {Seconds:F1} s with ParaView {Version}",
            task.TaskIndex, image.Width, image.Height, image.Format, byteSize, outcome.ElapsedSeconds, status.ParaviewVersion);

        return new ParaViewRenderResultData
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
            RenderSeconds = outcome.ElapsedSeconds,
            Diagnostics = Truncate($"stage={status.Stage}; backend={status.Backend}; proxies={status.ProxyCount}; render={status.RenderSeconds:F2}s", ParaViewInputLimits.MAX_DIAGNOSTICS_CHARS)
        };
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
    /// Builds the runner task document for a task in a workspace.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="workspace">The workspace.</param>
    /// <param name="outputPath">The output path.</param>
    /// <param name="pluginPath">The bundled reader path or null.</param>
    /// <param name="requiredPlugins">Names of the required plugins.</param>
    /// <returns>The runner task.</returns>
    public ParaViewRunnerTask BuildRunnerTask(
        ParaViewRenderTaskData task,
        ParaViewTaskWorkspace workspace,
        string outputPath,
        string? pluginPath,
        IReadOnlyList<string> requiredPlugins)
    {
        return new ParaViewRunnerTask
        {
            TaskId = task.TaskId,
            StatePath = workspace.StatePath,
            PackageRoot = workspace.PackageRoot,
            WorkDir = workspace.Root,
            OutputPath = outputPath,
            StatusPath = workspace.StatusFilePath,
            ViewId = task.ViewId,
            TimestepIndex = task.TimestepIndex,
            TimeValue = task.TimeValue,
            Width = task.Options.Width,
            Height = task.Options.Height,
            Format = ParaViewImageFormats.WireToken(task.Options.Format),
            TransparentBackground = task.Options.TransparentBackground && ParaViewImageFormats.SupportsTransparency(task.Options.Format),
            PluginPath = pluginPath,
            AllowedProxies = [.. m_allowlist.EffectiveKeys(requiredPlugins)],
            BlockedProxyTypes = [.. ParaViewProxyPolicy.BLOCKED_PROXY_TYPES.Order(StringComparer.Ordinal)],
            BlockedPropertyNames = [.. ParaViewProxyPolicy.BLOCKED_PROPERTY_NAMES.Order(StringComparer.Ordinal)],
            FilePropertyNames = [.. ParaViewProxyPolicy.FILE_PROPERTY_NAMES.Order(StringComparer.Ordinal)],
            FileReferenceGroups = [.. ParaViewProxyPolicy.FILE_REFERENCE_GROUPS],
            MaxStateBytes = ParaViewInputLimits.MAX_STATE_BYTES,
            MaxLogicalPathChars = ParaViewInputLimits.MAX_LOGICAL_PATH_CHARS
        };
    }

    #endregion

    #region Tools

    private static string Describe(ParaViewRunnerStatus? status, ParaViewProcessOutcome outcome)
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
