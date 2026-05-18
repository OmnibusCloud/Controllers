using OutWit.Engine.Sdk;
using OutWit.Controller.Render.Tests.Utils;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class RenderDiagnosticsTests
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var controllersPath = FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(false, null, controllersPath);
    }

    #endregion

    #region Tests

    [Test]
    public void ParseRenderBlenderVersionActivityTest()
    {
        var script = """
                     Job:Diag()
                     {
                         String:version = Render.BlenderVersion();
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["version"], Is.Not.Null);
    }

    [Test]
    public void ParseBundledRenderBlenderVersionScriptTest()
    {
        var script = """
                     Job:Diag()
                     {
                         String:result = Render.BlenderVersion();
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["result"], Is.Not.Null);
    }

    [Test]
    public void ParseRenderValidateBlendActivityTest()
    {
        var script = """
                     Job:Diag(Blob:scene)
                     {
                         String:result = Render.ValidateBlend(scene);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["result"], Is.Not.Null);
    }

    [Test]
    public void ParseBundledRenderValidateBlendScriptTest()
    {
        var script = """
                     Job:Diag(Blob:scene)
                     {
                         String:result = Render.ValidateBlend(scene);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["result"], Is.Not.Null);
    }

    [Test]
    public void ParseRenderRuntimeDiagnosticsActivityTest()
    {
        var script = """
                     Job:Diag()
                     {
                         RenderRuntimeDiagnostics:info = Render.RuntimeDiagnostics();
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["info"], Is.Not.Null);
    }

    [Test]
    public void ParseRenderPreflightActivityTest()
    {
        var script = """
                     Job:Diag(Int:frame, Int:startFrame, Int:endFrame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions, VideoOptions:video)
                     {
                         RenderPreflight:info = Render.Preflight(frame, startFrame, endFrame, tilesX, tilesY, options, tileOptions, video);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["info"], Is.Not.Null);
    }

    [Test]
    public void ParseBundledRenderPreflightStillScriptTest()
    {
        var script = """
                     Job:Diag(Int:frame, RenderOptions:options)
                     {
                         RenderPreflightFrames:result = Render.PreflightFrames(frame, frame, options);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["result"], Is.Not.Null);
    }

    [Test]
    public void ParseRenderPreflightFramesActivityTest()
    {
        var script = """
                     Job:Diag(Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         RenderPreflightFrames:info = Render.PreflightFrames(startFrame, endFrame, options);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["info"], Is.Not.Null);
    }

    [Test]
    public void ParseRenderPreflightVideoActivityTest()
    {
        var script = """
                     Job:Diag(RenderOptions:options, VideoOptions:video)
                     {
                         RenderPreflightVideo:info = Render.PreflightVideo(options, video);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["info"], Is.Not.Null);
    }

    [Test]
    public void ParseRenderPreflightStillTiledActivityTest()
    {
        var script = """
                     Job:Diag(Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderPreflightStillTiled:info = Render.PreflightStillTiled(tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["info"], Is.Not.Null);
    }

    [TestCase("Render.Frame.Cycles")]
    [TestCase("Render.Frame.Eevee")]
    [TestCase("Render.Frame.GreasePencil")]
    public void ParseEngineSpecificRenderFrameActivityTest(string activityName)
    {
        var script =
            "Job:Diag(RenderTask:task)\n" +
            "{\n" +
            $"    RenderResult:info = {activityName}(task);\n" +
            "}";

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["info"], Is.Not.Null);
    }

    [TestCase("RenderStillCycles.wit", "Render.Frame.Cycles(task)")]
    [TestCase("RenderStillEevee.wit", "Render.Frame.Eevee(task)")]
    [TestCase("RenderStillGreasePencil.wit", "Render.Frame.GreasePencil(task)")]
    [TestCase("RenderFramesCycles.wit", "Render.Frame.Cycles(task)")]
    [TestCase("RenderFramesEevee.wit", "Render.Frame.Eevee(task)")]
    [TestCase("RenderFramesGreasePencil.wit", "Render.Frame.GreasePencil(task)")]
    [TestCase("RenderStillTiledCycles.wit", "Render.Frame.Cycles(task)")]
    [TestCase("RenderStillTiledEevee.wit", "Render.Frame.Eevee(task)")]
    [TestCase("RenderStillTiledGreasePencil.wit", "Render.Frame.GreasePencil(task)")]
    [TestCase("RenderVideoCycles.wit", "Render.Frame.Cycles(task)")]
    [TestCase("RenderVideoEevee.wit", "Render.Frame.Eevee(task)")]
    [TestCase("RenderVideoGreasePencil.wit", "Render.Frame.GreasePencil(task)")]
    public void BundledEngineSpecificRenderScriptUsesExpectedFrameActivityTest(string scriptFileName, string expectedActivityCall)
    {
        var scriptsPath = RenderTestAssetPaths.FindBundledScriptsPath()
                          ?? throw new DirectoryNotFoundException("@Scripts directory not found");
        var scriptPath = Path.Combine(scriptsPath, scriptFileName);
        var script = File.ReadAllText(scriptPath);

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain(expectedActivityCall));
            Assert.That(job, Is.Not.Null);
            Assert.That(job.Activities, Is.Not.Empty);
        });
    }

    [TestCase("Render.Frame.Cycles")]
    [TestCase("Render.Frame.Eevee")]
    [TestCase("Render.Frame.GreasePencil")]
    public void ParseGridForEachWithEngineSpecificRenderFrameActivityTest(string activityName)
    {
        var script =
            "Job:Diag(RenderTaskCollection:tasks)\n" +
            "{\n" +
            $"    RenderResultCollection:results = Grid.ForEach(task in tasks) => {activityName}(task);\n" +
            "}";

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.Multiple(() =>
        {
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Variables["results"], Is.Not.Null);
        });
    }

    #endregion

    #region Tools

    private static string? FindControllersPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "@Controllers", "Debug");
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    #endregion
}
