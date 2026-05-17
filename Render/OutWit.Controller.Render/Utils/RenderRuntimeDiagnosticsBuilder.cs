using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

internal static class RenderRuntimeDiagnosticsBuilder
{
    #region Functions

    public static async Task<RenderRuntimeDiagnosticsData> BuildAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var controllerAssemblyPath = typeof(WitControllerRenderModule).Assembly.Location;
        var blenderDir = RenderBinaryResolver.ResolveBlenderRoot(controllerAssemblyPath);
        var ffmpegDir = RenderBinaryResolver.ResolveFfmpegRoot(controllerAssemblyPath);
        var blenderRunner = new BlenderRunner(blenderDir, logger);
        var ffmpegRunner = new FfmpegRunner(ffmpegDir, logger);
        var deviceDiagnostics = blenderRunner.IsAvailable
            ? await blenderRunner.GetDeviceDiagnosticsAsync(cancellationToken)
            : null;

        var diagnostics = new RenderRuntimeDiagnosticsData
        {
            RuntimeTarget = RenderBinaryResolver.GetCurrentRuntimeTarget(),
            BlenderAvailable = blenderRunner.IsAvailable,
            BlenderVersion = blenderRunner.IsAvailable
                ? await TryGetVersionAsync(logger, () => blenderRunner.GetVersionAsync(cancellationToken))
                : null,
            AvailableRenderBackends = deviceDiagnostics?.AvailableRenderBackends ?? [],
            SelectedRenderBackend = deviceDiagnostics?.SelectedRenderBackend?.ToString(),
            UsesGpuForRendering = deviceDiagnostics?.UsesGpuForRendering ?? false,
            RenderBackendSelectionMessage = deviceDiagnostics?.SelectionMessage,
            FfmpegAvailable = ffmpegRunner.IsAvailable,
            FfmpegVersion = ffmpegRunner.IsAvailable
                ? await TryGetVersionAsync(logger, () => ffmpegRunner.GetVersionAsync(cancellationToken))
                : null,
            FfprobeAvailable = ffmpegRunner.IsProbeAvailable,
            FfprobeVersion = ffmpegRunner.IsProbeAvailable
                ? await TryGetVersionAsync(logger, () => ffmpegRunner.GetProbeVersionAsync(cancellationToken))
                : null,
            SupportsCenterPriorityCrop = ffmpegRunner.IsAvailable,
            SupportsAlphaBlend = ffmpegRunner.IsAvailable && ffmpegRunner.IsProbeAvailable
        };

        logger.LogDebug(
            "Render runtime diagnostics collected: RuntimeTarget={RuntimeTarget}, BlenderAvailable={BlenderAvailable}, SelectedRenderBackend={SelectedRenderBackend}, UsesGpuForRendering={UsesGpuForRendering}, AvailableRenderBackends={AvailableRenderBackends}, Message={Message}, FfmpegAvailable={FfmpegAvailable}, FfprobeAvailable={FfprobeAvailable}",
            diagnostics.RuntimeTarget,
            diagnostics.BlenderAvailable,
            diagnostics.SelectedRenderBackend ?? "none",
            diagnostics.UsesGpuForRendering,
            diagnostics.AvailableRenderBackends.Length == 0 ? "none" : string.Join(",", diagnostics.AvailableRenderBackends),
            diagnostics.RenderBackendSelectionMessage ?? "none",
            diagnostics.FfmpegAvailable,
            diagnostics.FfprobeAvailable);

        return diagnostics;
    }

    private static async Task<string?> TryGetVersionAsync(ILogger logger, Func<Task<string>> getter)
    {
        try
        {
            return await getter();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to collect render runtime diagnostics version information");
            return null;
        }
    }

    #endregion
}
