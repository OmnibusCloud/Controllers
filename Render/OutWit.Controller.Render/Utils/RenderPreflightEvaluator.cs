namespace OutWit.Controller.Render.Utils;

using OutWit.Controller.Render.Model;

internal static class RenderPreflightEvaluator
{
    #region Functions

    public static RenderPreflightFramesData EvaluateFrames(
        int startFrame,
        int endFrame,
        RenderOptionsData options,
        RenderRuntimeDiagnosticsData diagnostics)
    {
        var issues = new List<string>();

        if (!diagnostics.BlenderAvailable)
            issues.Add("Packaged Blender runtime is not available.");

        if (endFrame < startFrame)
            issues.Add($"endFrame ({endFrame}) must be >= startFrame ({startFrame}).");

        if (options.ResolutionX < 0)
            issues.Add($"RenderOptions.ResolutionX must be >= 0, got {options.ResolutionX}.");

        if (options.ResolutionY < 0)
            issues.Add($"RenderOptions.ResolutionY must be >= 0, got {options.ResolutionY}.");

        if (options.Samples < 0)
            issues.Add($"RenderOptions.Samples must be >= 0, got {options.Samples}.");

        return new RenderPreflightFramesData
        {
            CanRender = issues.Count == 0,
            RuntimeTarget = diagnostics.RuntimeTarget,
            Issues = issues
        };
    }

    public static RenderPreflightStillTiledData EvaluateStillTiled(
        int tilesX,
        int tilesY,
        RenderOptionsData options,
        TileOptionsData tileOptions,
        RenderRuntimeDiagnosticsData diagnostics)
    {
        var issues = new List<string>();
        var outputWidth = options.ResolutionX > 0 ? options.ResolutionX : 1920;
        var outputHeight = options.ResolutionY > 0 ? options.ResolutionY : 1080;

        if (tilesX <= 0)
            issues.Add($"tilesX must be > 0, got {tilesX}.");

        if (tilesY <= 0)
            issues.Add($"tilesY must be > 0, got {tilesY}.");

        if (!diagnostics.BlenderAvailable)
            issues.Add("Packaged Blender runtime is not available.");

        if (!diagnostics.FfmpegAvailable)
            issues.Add("Packaged ffmpeg runtime is not available.");

        if (tileOptions.BlendMode == TileBlendMode.AlphaBlend && !diagnostics.FfprobeAvailable)
            issues.Add("Packaged ffprobe runtime is required for alpha-blend tiled stitching.");

        if (options.Format == RenderFormat.EXR)
            issues.Add("Tiled still collection currently supports PNG and JPEG only.");

        if (tileOptions.OverlapPx < 0)
            issues.Add($"TileOptions.OverlapPx must be >= 0, got {tileOptions.OverlapPx}.");

        if (tileOptions.OverlapPx >= outputWidth || tileOptions.OverlapPx >= outputHeight)
        {
            issues.Add($"TileOptions.OverlapPx must be smaller than the output resolution. Got {tileOptions.OverlapPx}px for output {outputWidth}x{outputHeight}.");
        }

        if (tilesX > 0 && tilesY > 0)
        {
            var coreTileWidth = Math.Max(1, outputWidth / tilesX);
            var coreTileHeight = Math.Max(1, outputHeight / tilesY);
            if (tileOptions.OverlapPx >= coreTileWidth || tileOptions.OverlapPx >= coreTileHeight)
            {
                issues.Add($"TileOptions.OverlapPx must be smaller than the core tile size. Got {tileOptions.OverlapPx}px for tile size {coreTileWidth}x{coreTileHeight}.");
            }
        }

        if (tileOptions.BlendMode == TileBlendMode.CenterPriorityCrop && !diagnostics.SupportsCenterPriorityCrop)
            issues.Add("Center-priority crop tiled stitching is not supported by the current packaged runtime.");

        if (tileOptions.BlendMode == TileBlendMode.AlphaBlend && !diagnostics.SupportsAlphaBlend)
            issues.Add("Alpha-blend tiled stitching is not supported by the current packaged runtime.");

        return new RenderPreflightStillTiledData
        {
            CanRender = issues.Count == 0,
            RuntimeTarget = diagnostics.RuntimeTarget,
            RequestedBlendMode = tileOptions.BlendMode,
            Issues = issues
        };
    }

    public static RenderPreflightVideoData EvaluateVideo(
        RenderOptionsData options,
        VideoOptionsData video,
        RenderRuntimeDiagnosticsData diagnostics)
    {
        var issues = new List<string>();

        if (!diagnostics.BlenderAvailable)
            issues.Add("Packaged Blender runtime is not available.");

        if (!diagnostics.FfmpegAvailable)
            issues.Add("Packaged ffmpeg runtime is not available.");

        if (video.FrameRate <= 0)
            issues.Add($"VideoOptions.FrameRate must be > 0, got {video.FrameRate}.");

        if (video.ConstantRateFactor is < 0 or > 51)
            issues.Add($"VideoOptions.ConstantRateFactor must be between 0 and 51, got {video.ConstantRateFactor}.");

        if (options.Format == RenderFormat.EXR)
            issues.Add("Video rendering currently supports PNG and JPEG frame formats only.");

        return new RenderPreflightVideoData
        {
            CanRender = issues.Count == 0,
            RuntimeTarget = diagnostics.RuntimeTarget,
            Issues = issues
        };
    }

    #endregion
}
