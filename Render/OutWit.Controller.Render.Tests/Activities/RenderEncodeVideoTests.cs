using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class RenderEncodeVideoTests
{
    #region Constants

    private const string TINY_PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAVSURBVBhXY/jPwABC/xkYGhj+gwAARk8JeKKlzvcAAAAASUVORK5CYII=";

    #endregion

    #region Fields

    private RenderTestBlobService m_blobService = null!;
    private IWitEngine m_engine = null!;
    private string m_storageDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_video_test_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_storageDir);

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_storageDir))
            Directory.Delete(m_storageDir, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void ParseRenderEncodeVideoActivityTest()
    {
        var script = """
                     Job:Encode(BlobCollection:frames, VideoOptions:video)
                     {
                         Blob:result = Render.EncodeVideo(frames, video);
                     }
                     """;

        var job = m_engine.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["result"], Is.Not.Null);
    }

    [Test]
    public async Task EncodeVideoProducesMp4BlobTest()
    {
        var script = """
                     Job:Encode(BlobCollection:frames, VideoOptions:video)
                     {
                         Blob:result = Render.EncodeVideo(frames, video);
                     }
                     """;

        var job = m_engine.Compile(script);

        var frameBlobIds = new List<Guid?>();
        for (var index = 0; index < 3; index++)
        {
            var framePath = Path.Combine(m_storageDir, $"input_{index + 1:D4}.png");
            File.WriteAllBytes(framePath, Convert.FromBase64String(TINY_PNG_BASE64));
            frameBlobIds.Add(m_blobService.RegisterExistingFile(framePath));
        }

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            frameBlobIds,
            new VideoOptionsData { FrameRate = 24, ConstantRateFactor = 23 });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed),
            $"Job failed: {status.Message}");

        var outputBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(outputBlobId, Is.Not.Null);

        var outputPath = m_blobService.GetStoredPath(outputBlobId!.Value);
        Assert.That(File.Exists(outputPath), Is.True);
        Assert.That(outputPath, Does.EndWith(".mp4"));
        Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));
    }

    #endregion
}
