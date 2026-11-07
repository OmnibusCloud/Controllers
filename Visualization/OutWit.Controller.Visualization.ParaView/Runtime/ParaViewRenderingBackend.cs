using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Processes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// Decides which OpenGL window class the runner environment requests on this node. Windows and macOS
/// use pvpython's platform default (hardware GL where a driver exists). Headless Linux historically
/// pinned OSMesa (software, works everywhere); the bundled runtime also carries
/// <c>vtkEGLRenderWindow</c>, so a node with a driver EGL stack (e.g. NVIDIA libEGL/GLVND) can render
/// on its GPU. The decision is made by an actual probe render, once per node process: EGL is accepted
/// only when the reported OpenGL renderer is real hardware — Mesa's EGL can silently land on llvmpipe,
/// a software rasterizer behind a GPU-looking window class. Any probe failure falls back to the
/// certified OSMesa path, so no node is ever excluded.
/// </summary>
public static class ParaViewRenderingBackend
{
    #region Constants

    /// <summary>Operations override: set to a VTK window class name to skip probing (e.g. pin <see cref="ParaViewRunnerEnvironment.OSMESA_WINDOW"/>).</summary>
    public const string ENV_OPENGL_WINDOW = "OUTWIT_PVPYTHON_OPENGL_WINDOW";

    /// <summary>The EGL window class the GPU attempt requests.</summary>
    public const string EGL_WINDOW = "vtkEGLRenderWindow";

    /// <summary>Embedded resource name of the probe script.</summary>
    public const string PROBE_RESOURCE = "runner/gpu_probe.py";

    /// <summary>File name of the probe script inside the probe workspace.</summary>
    public const string PROBE_FILE_NAME = "gpu_probe.py";

    /// <summary>Status document file name of one probe run.</summary>
    public const string PROBE_STATUS_FILE_NAME = "gpu_probe_status.json";

    /// <summary>Wall-clock limit of one probe render (startup + trivial scene).</summary>
    public static readonly TimeSpan PROBE_WALL_CLOCK_LIMIT = TimeSpan.FromMinutes(2);

    /// <summary>Consecutive probe processes that must all succeed before EGL is accepted.</summary>
    public const int PROBE_ATTEMPTS = 2;

    /// <summary>Renderer substrings that mean software rasterization even behind an EGL window.</summary>
    private static readonly string[] SOFTWARE_RENDERER_MARKERS = ["llvmpipe", "softpipe", "swrast", "software rasterizer", "mesa offscreen"];

    private static readonly ConcurrentDictionary<string, string?> CACHE = new(StringComparer.OrdinalIgnoreCase);

    // Runtimes whose EGL crashed at work time in this process: software for the rest of the
    // process lifetime, whatever the pinned window says (audit C-M8).
    private static readonly ConcurrentDictionary<string, bool> DEMOTED = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JSON_OPTIONS = new() { PropertyNameCaseInsensitive = true };

    #endregion

    #region Functions

    /// <summary>
    /// Resolves the window class the runner environment should request on this node, probing at most
    /// once per pvpython path for the process lifetime.
    /// </summary>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="tempStorage">Node temp storage the probe workspace is created under.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A window class name for <c>VTK_DEFAULT_OPENGL_WINDOW</c>, or null for the platform default.</returns>
    public static async Task<string?> ResolveWindowAsync(
        string pvpythonPath,
        IWitTempStorage tempStorage,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        // A crash at work time outranks a pinned window: a pinned flaky node would otherwise run every
        // task twice forever, the demotion never sticking (audit C-M8).
        if (DEMOTED.ContainsKey(pvpythonPath))
            return ParaViewRunnerEnvironment.OSMESA_WINDOW;

        var forced = Environment.GetEnvironmentVariable(ENV_OPENGL_WINDOW);
        if (!string.IsNullOrWhiteSpace(forced))
            return forced.Trim();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return null;

        if (CACHE.TryGetValue(pvpythonPath, out var cached))
            return cached;

        var resolved = await ProbeForHardwareEglAsync(pvpythonPath, tempStorage, logger, cancellationToken)
            ? EGL_WINDOW
            : ParaViewRunnerEnvironment.OSMESA_WINDOW;

        CACHE[pvpythonPath] = resolved;
        logger?.LogInformation("ParaView rendering backend on this node: {Window}", resolved);
        return resolved;
    }

    /// <summary>
    /// Runs one probe render with the requested window class and reports what actually rendered.
    /// </summary>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="tempStorage">Node temp storage the probe workspace is created under.</param>
    /// <param name="requestedWindow">Window class to request, or null for the platform default.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The probe outcome, or null when the probe process failed or reported no usable status.</returns>
    public static async Task<ParaViewGpuProbeStatus?> ProbeAsync(
        string pvpythonPath,
        IWitTempStorage tempStorage,
        string? requestedWindow,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        using var workspace = ParaViewTaskWorkspace.Create(tempStorage, Guid.NewGuid(), 0);

        var probePath = workspace.WriteEmbedded(PROBE_RESOURCE, workspace.RunnerDirectory, PROBE_FILE_NAME);
        var statusPath = Path.Combine(workspace.Root, PROBE_STATUS_FILE_NAME);
        var arguments = new List<string>(ParaViewTaskExecutor.PVPYTHON_OPTIONS) { probePath, "--status-file", statusPath };
        var environment = ParaViewRunnerEnvironment.Build(pvpythonPath, workspace.HomeDirectory, workspace.TempDirectory, requestedWindow);

        var outcome = await ParaViewProcessRunner.RunAsync(
            pvpythonPath, arguments, workspace.PackageRoot, environment, PROBE_WALL_CLOCK_LIMIT, logger, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (outcome.TimedOut || outcome.ExitCode != 0)
        {
            logger?.LogInformation("ParaView GPU probe with '{Window}' did not complete (exit {ExitCode}, timedOut {TimedOut}): {Stderr}",
                requestedWindow ?? "platform default", outcome.ExitCode, outcome.TimedOut, outcome.StderrTail);
            return null;
        }

        try
        {
            if (!File.Exists(statusPath))
                return null;

            var status = JsonSerializer.Deserialize<ParaViewGpuProbeStatus>(File.ReadAllText(statusPath), JSON_OPTIONS);
            return status is { Ok: true } ? status : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the reported OpenGL renderer is real hardware. Unknown or empty renderers count as
    /// software: the fallback path is certified, an unverified "GPU" is not.
    /// </summary>
    /// <param name="renderer">The OpenGL renderer string.</param>
    /// <returns>True for a hardware renderer.</returns>
    public static bool IsHardwareRenderer(string? renderer)
    {
        if (string.IsNullOrWhiteSpace(renderer))
            return false;

        return !SOFTWARE_RENDERER_MARKERS.Any(marker => renderer.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the window class is the EGL GPU attempt.
    /// </summary>
    /// <param name="openGlWindow">A resolved window class or null.</param>
    /// <returns>True for <see cref="EGL_WINDOW"/>.</returns>
    public static bool IsEglWindow(string? openGlWindow)
    {
        return string.Equals(openGlWindow, EGL_WINDOW, StringComparison.Ordinal);
    }

    /// <summary>
    /// Permanently (for this node process) demotes the pvpython to OSMesa. The probe certifies one
    /// trivial render — it cannot certify the thousandth: production showed a driver whose EGL passed
    /// the probe and then segfaulted real tasks. Callers demote on the FIRST EGL runner failure and
    /// retry the work on software, so a flaky EGL stack costs one crashed subprocess, not a job.
    /// </summary>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="logger">Optional logger.</param>
    public static void Demote(string pvpythonPath, ILogger? logger)
    {
        CACHE[pvpythonPath] = ParaViewRunnerEnvironment.OSMESA_WINDOW;
        DEMOTED[pvpythonPath] = true;
        logger?.LogWarning("ParaView rendering backend demoted to {Window} on this node (EGL crashed at work time; a pinned {Variable} is overridden for the rest of this process)",
            ParaViewRunnerEnvironment.OSMESA_WINDOW, ENV_OPENGL_WINDOW);
    }

    /// <summary>
    /// Forgets probe results and demotions (tests; a controller reload also starts a fresh process, which clears them naturally).
    /// </summary>
    public static void ResetCache()
    {
        CACHE.Clear();
        DEMOTED.Clear();
    }

    #endregion

    #region Tools

    private static async Task<bool> ProbeForHardwareEglAsync(
        string pvpythonPath,
        IWitTempStorage tempStorage,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Two separate probe processes must both succeed: rapid context creation is exactly
            // where flaky EGL stacks fall over, and every production task is a fresh process.
            for (var attempt = 0; attempt < PROBE_ATTEMPTS; attempt++)
            {
                var status = await ProbeAsync(pvpythonPath, tempStorage, EGL_WINDOW, logger, cancellationToken);
                var accepted = status != null
                               && string.Equals(status.RenderWindow, EGL_WINDOW, StringComparison.Ordinal)
                               && IsHardwareRenderer(status.Renderer);

                logger?.LogInformation("ParaView GPU probe {Attempt}/{Attempts}: window '{Window}', renderer '{Renderer}' — {Verdict}",
                    attempt + 1, PROBE_ATTEMPTS, status?.RenderWindow ?? "none", status?.Renderer ?? "none",
                    accepted ? "hardware EGL" : "falling back to OSMesa");

                if (!accepted)
                    return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogWarning("ParaView GPU probe failed ({Message}); falling back to OSMesa", exception.Message);
            return false;
        }
    }

    #endregion
}
