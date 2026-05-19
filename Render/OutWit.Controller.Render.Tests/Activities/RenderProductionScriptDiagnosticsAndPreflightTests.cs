using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Blender version + runtime diagnostics + preflight validation tests for
/// the production Render.* scripts. Covers both the engine-level activities
/// (Render.BlenderVersion, Render.RuntimeDiagnostics, Render.Preflight*)
/// and their bundled .wit script counterparts.
/// </summary>
[TestFixture]
internal sealed class RenderProductionScriptDiagnosticsAndPreflightTests : RenderProductionScriptBlenderTestsBase
{
    #region Tests

    [Test]
    public async Task RenderBlenderVersionRealRunTest()
    {
        var script = """
                     Job:BlenderVersionDiag()
                     {
                         String:version = Render.BlenderVersion();
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var version = job.Variables["version"].Value as string;
        Assert.That(version, Is.Not.Null.And.Not.Empty);
        Assert.That(version!.StartsWith("Blender", StringComparison.OrdinalIgnoreCase), Is.True);
    }

    [Test]
    public async Task BundledRenderBlenderVersionScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderBlenderVersion.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var version = job.Variables["result"].Value as string;
        Assert.That(version, Is.Not.Null.And.Not.Empty);
        Assert.That(version!.StartsWith("Blender", StringComparison.OrdinalIgnoreCase), Is.True);
    }

    [Test]
    public async Task RenderRuntimeDiagnosticsRealRunTest()
    {
        var script = """
                     Job:RuntimeDiag()
                     {
                         RenderRuntimeDiagnostics:info = Render.RuntimeDiagnostics();
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderRuntimeDiagnosticsData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.RuntimeTarget, Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostics.BlenderAvailable, Is.True);
        Assert.That(diagnostics.BlenderVersion, Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostics.FfmpegAvailable, Is.True);
        Assert.That(diagnostics.FfprobeAvailable, Is.True);
        Assert.That(diagnostics.SupportsCenterPriorityCrop, Is.True);
        Assert.That(diagnostics.SupportsAlphaBlend, Is.True);
    }

    [Test]
    public async Task BundledRenderRuntimeDiagnosticsScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderRuntimeDiagnostics.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderRuntimeDiagnosticsData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.RuntimeTarget, Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostics.BlenderAvailable, Is.True);
        Assert.That(diagnostics.FfmpegAvailable, Is.True);
        Assert.That(diagnostics.FfprobeAvailable, Is.True);
    }

    [Test]
    public async Task RenderPreflightStillTiledRealRunTest()
    {
        var script = """
                     Job:PreflightDiag(Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderPreflightStillTiled:info = Render.PreflightStillTiled(tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightStillTiledData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.RequestedBlendMode, Is.EqualTo(TileBlendMode.AlphaBlend));
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightVideoRealRunTest()
    {
        var script = """
                     Job:PreflightVideoDiag(RenderOptions:options, VideoOptions:video)
                     {
                         RenderPreflightVideo:info = Render.PreflightVideo(options, video);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateOptions(),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightVideoData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightRealRunTest()
    {
        var script = """
                     Job:PreflightDiag(Int:frame, Int:startFrame, Int:endFrame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions, VideoOptions:video)
                     {
                         RenderPreflight:info = Render.Preflight(frame, startFrame, endFrame, tilesX, tilesY, options, tileOptions, video);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            1,
            3,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.RuntimeDiagnostics, Is.Not.Null);
        Assert.That(diagnostics.Still, Is.Not.Null);
        Assert.That(diagnostics.Frames, Is.Not.Null);
        Assert.That(diagnostics.StillTiled, Is.Not.Null);
        Assert.That(diagnostics.Video, Is.Not.Null);
        Assert.That(diagnostics.CanRenderAll, Is.True);
        Assert.That(diagnostics.Still!.CanRender, Is.True);
        Assert.That(diagnostics.Frames!.CanRender, Is.True);
        Assert.That(diagnostics.StillTiled!.CanRender, Is.True);
        Assert.That(diagnostics.Video!.CanRender, Is.True);
    }

    [Test]
    public async Task RenderPreflightReportsInvalidRequestsRealRunTest()
    {
        var script = """
                     Job:PreflightDiag(Int:frame, Int:startFrame, Int:endFrame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions, VideoOptions:video)
                     {
                         RenderPreflight:info = Render.Preflight(frame, startFrame, endFrame, tilesX, tilesY, options, tileOptions, video);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            10,
            5,
            2,
            2,
            new RenderOptionsData
            {
                Format = RenderFormat.EXR,
                Engine = RenderEngine.Cycles,
                Samples = -1,
                ResolutionX = 64,
                ResolutionY = 64
            },
            CreateTileOptions(32, TileBlendMode.AlphaBlend),
            new VideoOptionsData
            {
                FrameRate = 0,
                ConstantRateFactor = 60
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRenderAll, Is.False);
        Assert.That(diagnostics.Frames!.CanRender, Is.False);
        Assert.That(diagnostics.Frames.Issues.Any(me => me.Contains("endFrame", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Still!.CanRender, Is.False);
        Assert.That(diagnostics.Still.Issues.Any(me => me.Contains("Samples", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.StillTiled!.CanRender, Is.False);
        Assert.That(diagnostics.StillTiled.Issues.Any(me => me.Contains("core tile size", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Video!.CanRender, Is.False);
        Assert.That(diagnostics.Video.Issues.Any(me => me.Contains("FrameRate", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflight.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            1,
            3,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRenderAll, Is.True);
        Assert.That(diagnostics.Still, Is.Not.Null);
        Assert.That(diagnostics.Frames, Is.Not.Null);
        Assert.That(diagnostics.StillTiled, Is.Not.Null);
        Assert.That(diagnostics.Video, Is.Not.Null);
    }

    [Test]
    public async Task BundledRenderPreflightFramesScriptReportsInvalidRangeRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightFrames.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            10,
            5,
            new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                Samples = -1,
                ResolutionX = -64,
                ResolutionY = -64
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("endFrame", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ResolutionX", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("Samples", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightStillScriptReportsInvalidOptionsRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightStill.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                Samples = -1,
                ResolutionX = -64,
                ResolutionY = -64
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ResolutionX", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("Samples", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightStillScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightStill.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task BundledRenderPreflightVideoScriptReportsInvalidOptionsRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightVideo.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            new RenderOptionsData
            {
                Format = RenderFormat.EXR,
                Engine = RenderEngine.Cycles,
                Samples = 4,
                ResolutionX = 64,
                ResolutionY = 64
            },
            new VideoOptionsData
            {
                FrameRate = 0,
                ConstantRateFactor = 60
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightVideoData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("FrameRate", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ConstantRateFactor", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("PNG and JPEG", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task RenderPreflightFramesRealRunTest()
    {
        var script = """
                     Job:PreflightFramesDiag(Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         RenderPreflightFrames:info = Render.PreflightFrames(startFrame, endFrame, options);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            3,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightFramesReportsInvalidRangeRealRunTest()
    {
        var script = """
                     Job:PreflightFramesDiag(Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         RenderPreflightFrames:info = Render.PreflightFrames(startFrame, endFrame, options);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            10,
            5,
            new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                Samples = -1,
                ResolutionX = -64,
                ResolutionY = -64
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("endFrame", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ResolutionX", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("Samples", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightFramesScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightFrames.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            3,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightVideoReportsInvalidOptionsRealRunTest()
    {
        var script = """
                     Job:PreflightVideoDiag(RenderOptions:options, VideoOptions:video)
                     {
                         RenderPreflightVideo:info = Render.PreflightVideo(options, video);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            new RenderOptionsData
            {
                Format = RenderFormat.EXR,
                Engine = RenderEngine.Cycles,
                Samples = 4,
                ResolutionX = 64,
                ResolutionY = 64
            },
            new VideoOptionsData
            {
                FrameRate = 0,
                ConstantRateFactor = 60
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightVideoData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("FrameRate", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ConstantRateFactor", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("PNG and JPEG", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightVideoScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightVideo.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateOptions(),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightVideoData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightStillTiledReportsInvalidOverlapRealRunTest()
    {
        var script = """
                     Job:PreflightDiag(Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderPreflightStillTiled:info = Render.PreflightStillTiled(tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(32, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightStillTiledData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("core tile size", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightStillTiledScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightStillTiled.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightStillTiledData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.RequestedBlendMode, Is.EqualTo(TileBlendMode.AlphaBlend));
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task BundledRenderPreflightStillTiledScriptReportsInvalidOverlapRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightStillTiled.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(32, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightStillTiledData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("core tile size", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    #endregion
}
