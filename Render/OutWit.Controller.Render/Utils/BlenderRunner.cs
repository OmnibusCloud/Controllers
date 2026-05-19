using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Cross-platform utility for running Blender in headless mode.
/// Resolves Blender binary path relative to the controller module directory.
/// </summary>
public sealed class BlenderRunner
{
    #region Fields

    private const string AVAILABLE_BACKENDS_PREFIX = "OUTWIT_RENDER_AVAILABLE=";

    private const string SELECTED_BACKEND_PREFIX = "OUTWIT_RENDER_BACKEND=";

    private const string SELECTION_MESSAGE_PREFIX = "OUTWIT_RENDER_MESSAGE=";

    private readonly string m_blenderPath;
    private readonly ILogger m_logger;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a BlenderRunner that looks for the Blender binary in the specified directory.
    /// </summary>
    /// <param name="blenderDir">Directory containing the Blender installation.</param>
    /// <param name="logger">Logger instance.</param>
    public BlenderRunner(string blenderDir, ILogger logger)
    {
        m_blenderPath = RenderBinaryResolver.ResolveBlenderPath(blenderDir);
        m_logger = logger;

        if (!File.Exists(m_blenderPath))
            logger.LogWarning("Blender executable not found at {BlenderPath}", m_blenderPath);
        else
            RenderBinaryResolver.EnsureExecutable(m_blenderPath, logger);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Renders a single frame from a .blend file.
    /// </summary>
    /// <param name="blendFilePath">Path to the .blend file.</param>
    /// <param name="frame">Frame number to render.</param>
    /// <param name="outputPath">Output file path (without extension — Blender appends frame number).</param>
    /// <param name="options">Render options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the rendered image file.</returns>
    /// <exception cref="InvalidOperationException">Thrown if Blender process fails.</exception>
    public async Task<string> RenderFrameAsync(
        string blendFilePath,
        int frame,
        string outputPath,
        RenderOptionsData options,
        CancellationToken cancellationToken = default,
        RenderTaskData? task = null)
    {
        m_logger.LogInformation("Blender render: frame {Frame} from {BlendFile}", frame, blendFilePath);

        var result = await RunRenderAttemptAsync(
            blendFilePath,
            frame,
            outputPath,
            options,
            task,
            forceCpuFallback: false,
            cancellationToken);

        if (ShouldRetryWithCpuFallback(result))
        {
            m_logger.LogWarning(
                "Blender render failed on auto-selected GPU backend {RenderDevice}; retrying once on CPU. Message: {Message}",
                result.SelectedRenderBackend,
                result.SelectionMessage ?? "No backend selection message reported");

            DeleteRenderedOutputs(outputPath, frame, options.Format);

            result = await RunRenderAttemptAsync(
                blendFilePath,
                frame,
                outputPath,
                options,
                task,
                forceCpuFallback: true,
                cancellationToken);
        }

        EnsureSuccessfulRender(result);

        var renderedPath = FindRenderedFile(outputPath, frame, options.Format);
        if (renderedPath == null)
            throw new InvalidOperationException(
                $"Blender completed but output file not found for frame {frame} at {outputPath}");

        m_logger.LogInformation("Blender render complete: {OutputPath} ({Size} bytes)",
            renderedPath, new FileInfo(renderedPath).Length);

        return renderedPath;
    }

    /// <summary>
    /// Gets the Blender version string.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Version string (e.g., "Blender 4.2.19").</returns>
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _) = await RunProcessAsync("-b --version", cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException("Failed to get Blender version");

        var firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine?.Trim() ?? "Unknown";
    }

    public async Task<(int Width, int Height)> GetSceneResolutionAsync(string blendFilePath, CancellationToken cancellationToken = default)
    {
        const string startMarker = "OUTWIT_SCENE_RESOLUTION_START";
        const string endMarker = "OUTWIT_SCENE_RESOLUTION_END";

        var pythonLines = new List<string>
        {
            "import bpy, json",
            "scene = bpy.context.scene",
            "resolution_x = int(scene.render.resolution_x)",
            "resolution_y = int(scene.render.resolution_y)",
            "resolution_percentage = int(scene.render.resolution_percentage)",
            "payload = {'width': int(round(resolution_x * resolution_percentage / 100.0)), 'height': int(round(resolution_y * resolution_percentage / 100.0))}",
            $"print('{startMarker}')",
            "print(json.dumps(payload))",
            $"print('{endMarker}')"
        };

        var args = $"-b \"{blendFilePath}\" --python-exit-code 1 {BlenderRenderArgsBuilder.BuildPythonExecArgument(pythonLines)}";
        var (exitCode, stdout, stderr) = await RunProcessAsync(args, cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException($"Failed to read Blender scene resolution: {stderr}");

        var startIndex = stdout.IndexOf(startMarker, StringComparison.Ordinal);
        var endIndex = stdout.IndexOf(endMarker, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex)
            throw new InvalidOperationException("Blender scene resolution probe returned no structured diagnostics payload.");

        var json = stdout.Substring(startIndex + startMarker.Length, endIndex - startIndex - startMarker.Length).Trim();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (
            root.GetProperty("width").GetInt32(),
            root.GetProperty("height").GetInt32());
    }

    /// <summary>
    /// Validates that a .blend file can be opened successfully by the local Blender runtime.
    /// </summary>
    /// <param name="blendFilePath">Path to the .blend file to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when Blender can open the file successfully.</returns>
    public async Task<bool> ValidateBlendAsync(string blendFilePath, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateBlendDetailedAsync(blendFilePath, cancellationToken);
        return validation.IsValid;
    }

    /// <summary>
    /// Validates that a .blend file can be opened successfully and does not require unsupported external simulation/cache state.
    /// </summary>
    /// <param name="blendFilePath">Path to the .blend file to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detailed validation diagnostics for the scene.</returns>
    public async Task<RenderValidateBlendData> ValidateBlendDetailedAsync(string blendFilePath, CancellationToken cancellationToken = default)
    {
        var pythonLines = BlenderValidationScript.BuildScript(blendFilePath);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"outwit_validate_blend_{Guid.NewGuid():N}.py");
        await File.WriteAllLinesAsync(scriptPath, pythonLines, cancellationToken);

        (int exitCode, string stdout, string stderr) validationProcessResult;
        try
        {
            var args = $"-b \"{blendFilePath}\" --python-exit-code 1 --python \"{scriptPath}\"";
            validationProcessResult = await RunProcessAsync(args, cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        var (exitCode, stdout, stderr) = validationProcessResult;
        return BlenderValidationScript.ParseResult(blendFilePath, exitCode, stdout, stderr, m_logger);
    }

    /// <summary>
    /// Probes the local Blender runtime for available and selected render backends.
    /// </summary>
    internal async Task<RenderDeviceDiagnostics> GetDeviceDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var args = $"-b --python-exit-code 1 {BlenderRenderArgsBuilder.BuildPythonExecArgument(BlenderRenderArgsBuilder.BuildDeviceConfigurationPython(RenderEngine.Cycles, forceCpuFallback: false))}";
        var (exitCode, stdout, stderr) = await RunProcessAsync(args, cancellationToken);
        var result = ParseInvocationResult(exitCode, stdout, stderr);

        if (result.ExitCode != 0)
        {
            m_logger.LogWarning(
                "Blender device diagnostics probe failed with exit code {ExitCode}: {Stderr}",
                result.ExitCode,
                result.Stderr);
        }

        return CreateDeviceDiagnostics(result);
    }

    /// <summary>
    /// Checks whether the Blender binary exists and is executable.
    /// </summary>
    public bool IsAvailable => File.Exists(m_blenderPath);

    internal string BlenderExecutablePath => m_blenderPath;

    internal ILogger Logger => m_logger;

    #endregion

    #region Tools

    private string BuildFrameArgs(
        string blendFilePath,
        int frame,
        string outputPath,
        RenderOptionsData options,
        RenderTaskData? task,
        bool forceCpuFallback)
    {
        var args = new StringBuilder();
        args.Append($"-b \"{blendFilePath}\"");
        args.Append($" -E {BlenderRenderArgsBuilder.GetBlenderEngineArgument(options.Engine)}");
        args.Append($" -o \"{outputPath}\"");
        args.Append($" -F {BlenderRenderArgsBuilder.FormatToBlenderArg(options.Format)}");

        var pythonLines = new List<string>
        {
            "import bpy",
            "scene = bpy.context.scene"
        };

        pythonLines.AddRange(BlenderRenderArgsBuilder.BuildDeviceConfigurationPython(options.Engine, forceCpuFallback));
        pythonLines.AddRange(BlenderRenderArgsBuilder.BuildImageOutputConfigurationPython(options.Format));
        pythonLines.AddRange(BlenderRenderArgsBuilder.BuildViewLayerRecoveryPython());

        pythonLines.AddRange(BlenderRenderArgsBuilder.BuildEngineConfigurationPython(options));

        if (options.ResolutionX > 0)
            pythonLines.Add($"scene.render.resolution_x = {options.ResolutionX}");

        if (options.ResolutionY > 0)
            pythonLines.Add($"scene.render.resolution_y = {options.ResolutionY}");

        if (options.ResolutionX > 0 || options.ResolutionY > 0)
            pythonLines.Add("scene.render.resolution_percentage = 100");

        if (task != null && !task.IsFullFrame)
        {
            pythonLines.Add("scene.render.use_border = True");
            pythonLines.Add("scene.render.use_crop_to_border = True");
            pythonLines.Add($"scene.render.border_min_x = {task.EffectiveRenderMinX.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            pythonLines.Add($"scene.render.border_max_x = {task.EffectiveRenderMaxX.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            pythonLines.Add($"scene.render.border_min_y = {task.EffectiveRenderMinY.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            pythonLines.Add($"scene.render.border_max_y = {task.EffectiveRenderMaxY.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        else
        {
            pythonLines.Add("scene.render.use_border = False");
            pythonLines.Add("scene.render.use_crop_to_border = False");
        }

        args.Append(' ');
        args.Append(BlenderRenderArgsBuilder.BuildPythonExecArgument(pythonLines));

        args.Append($" -f {frame}");

        return args.ToString();
    }

    private async Task<RenderBlenderInvocationResult> RunRenderAttemptAsync(
        string blendFilePath,
        int frame,
        string outputPath,
        RenderOptionsData options,
        RenderTaskData? task,
        bool forceCpuFallback,
        CancellationToken cancellationToken)
    {
        var args = BuildFrameArgs(blendFilePath, frame, outputPath, options, task, forceCpuFallback);
        m_logger.LogDebug("Blender args: {Args}", args);

        return await RunBlenderAsync(args, cancellationToken);
    }

    private async Task<RenderBlenderInvocationResult> RunBlenderAsync(string args, CancellationToken cancellationToken)
    {
        var (exitCode, stdout, stderr) = await RunProcessAsync(args, cancellationToken);
        var result = ParseInvocationResult(exitCode, stdout, stderr);

        LogInvocationDiagnostics(result);

        return result;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string args, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = m_blenderPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
            throw;
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private RenderBlenderInvocationResult ParseInvocationResult(int exitCode, string stdout, string stderr)
    {
        return new RenderBlenderInvocationResult
        {
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr,
            SelectedRenderBackend = ParseSelectedRenderDevice(stdout),
            SelectionMessage = ParseMarker(stdout, SELECTION_MESSAGE_PREFIX)
        };
    }

    private RenderDeviceDiagnostics CreateDeviceDiagnostics(RenderBlenderInvocationResult result)
    {
        return new RenderDeviceDiagnostics
        {
            AvailableRenderBackends = ParseAvailableRenderBackends(result.Stdout),
            SelectedRenderBackend = result.SelectedRenderBackend,
            SelectionMessage = result.SelectionMessage,
            UsesGpuForRendering = IsGpuBackend(result.SelectedRenderBackend)
        };
    }

    private void LogInvocationDiagnostics(RenderBlenderInvocationResult result)
    {
        if (result.SelectedRenderBackend.HasValue)
            m_logger.LogDebug("Blender selected render backend: {RenderDevice}", result.SelectedRenderBackend.Value);

        if (!string.IsNullOrWhiteSpace(result.SelectionMessage))
            m_logger.LogDebug("Blender render backend selection: {Message}", result.SelectionMessage);

        if (!string.IsNullOrWhiteSpace(result.Stdout))
            m_logger.LogDebug("Blender stdout: {Stdout}", result.Stdout.Trim());
    }

    private void EnsureSuccessfulRender(RenderBlenderInvocationResult result)
    {
        if (result.ExitCode == 0)
            return;

        m_logger.LogError("Blender failed (exit code {ExitCode}):\nstdout: {Stdout}\nstderr: {Stderr}",
            result.ExitCode, result.Stdout, result.Stderr);

        var failureMessage = BuildFailureMessage(result);
        throw new InvalidOperationException(
            $"Blender render failed with exit code {result.ExitCode}: {failureMessage}");
    }

    private static string BuildFailureMessage(RenderBlenderInvocationResult result)
    {
        var stdoutSummary = ExtractFailureSummary(result.Stdout);
        var stderrSummary = ExtractFailureSummary(result.Stderr);

        if (!string.IsNullOrWhiteSpace(stdoutSummary))
        {
            if (string.IsNullOrWhiteSpace(stderrSummary)
                || stderrSummary.Contains("Unable to find the Python binary", StringComparison.OrdinalIgnoreCase))
            {
                return stdoutSummary;
            }

            if (string.Equals(stdoutSummary, stderrSummary, StringComparison.OrdinalIgnoreCase))
                return stdoutSummary;

            return $"{stdoutSummary} | stderr: {stderrSummary}";
        }

        return string.IsNullOrWhiteSpace(stderrSummary)
            ? "Blender reported a non-zero exit code without diagnostics."
            : stderrSummary;
    }

    private static string? ExtractFailureSummary(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(me => me.Trim())
            .Where(me => !string.IsNullOrWhiteSpace(me))
            .ToArray();

        if (lines.Length == 0)
            return null;

        foreach (var line in lines)
        {
            var reportsIndex = line.IndexOf("| ERROR ", StringComparison.OrdinalIgnoreCase);
            if (reportsIndex >= 0)
                return line[(reportsIndex + "| ERROR ".Length)..].Trim();
        }

        var blenderErrorLine = lines.FirstOrDefault(me => me.Contains("error", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(blenderErrorLine))
            return NormalizeFailureLine(blenderErrorLine);

        return NormalizeFailureLine(lines[^1]);
    }

    private static string NormalizeFailureLine(string line)
    {
        var normalized = line.Trim();
        var blenderPrefixIndex = normalized.IndexOf(" : ", StringComparison.Ordinal);
        if (blenderPrefixIndex > 0 && normalized[..blenderPrefixIndex].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[(blenderPrefixIndex + 3)..].Trim();

        return normalized;
    }

    private static bool ShouldRetryWithCpuFallback(RenderBlenderInvocationResult result)
    {
        return result.ExitCode != 0 && IsGpuBackend(result.SelectedRenderBackend);
    }

    private static bool IsGpuBackend(RenderDevice? device)
    {
        return device is RenderDevice.CUDA or RenderDevice.OPTIX or RenderDevice.HIP or RenderDevice.METAL;
    }

    private static void DeleteRenderedOutputs(string outputBasePath, int frame, RenderFormat format)
    {
        var renderedPath = FindRenderedFile(outputBasePath, frame, format);
        if (renderedPath != null && File.Exists(renderedPath))
        {
            try
            {
                File.Delete(renderedPath);
            }
            catch
            {
            }
        }
    }

    private static string[] ParseAvailableRenderBackends(string stdout)
    {
        var value = ParseMarker(stdout, AVAILABLE_BACKENDS_PREFIX);
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static RenderDevice? ParseSelectedRenderDevice(string stdout)
    {
        var value = ParseMarker(stdout, SELECTED_BACKEND_PREFIX);
        if (Enum.TryParse<RenderDevice>(value, ignoreCase: true, out var device))
            return device;

        return null;
    }

    private static string? ParseMarker(string stdout, string prefix)
    {
        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..].Trim();
        }

        return null;
    }

    private static string? FindRenderedFile(string outputBasePath, int frame, RenderFormat format)
    {
        var ext = BlenderRenderArgsBuilder.FormatToExtension(format);
        var dir = Path.GetDirectoryName(outputBasePath) ?? ".";
        var baseName = Path.GetFileName(outputBasePath);

        // Blender appends frame number as 4-digit suffix: output0001.png
        var candidates = new[]
        {
            $"{outputBasePath}{frame:D4}{ext}",
            Path.Combine(dir, $"{baseName}{frame:D4}{ext}"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    #endregion
}
