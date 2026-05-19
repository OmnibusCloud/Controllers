using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Tests.Mock;
using OutWit.Controller.Render.Dcc.Tests.Utils;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Tests.Activities;

/// <summary>
/// Shared scaffolding for the RenderBuildBlendFromDccScene* test fixtures.
/// Holds the temp-storage + blob-service setup and the small builders used by
/// every fixture (CreateRenderOptions / CreateVideoOptions / CreateTileOptions /
/// LoadSceneFromJsonAsync / FindLatestExportedDccSceneJsonPath /
/// AssertImageContainsMeaningfullyLitPixels). Per-theme helpers stay with
/// their owning fixture.
/// </summary>
internal abstract class RenderBuildBlendFromDccSceneTestsBase
{
    #region Constants

    protected static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    protected const string MINIMAL_PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    #endregion

    #region Fields

    protected RenderTestBlobService m_blobService = null!;
    protected string m_storageDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_buildblend_validate_dcc_test_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_storageDir);

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
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

    #region Tools

    protected static RenderOptionsData CreateRenderOptions(int width = 64, int height = 64)
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = width,
            ResolutionY = height,
            Denoise = false
        };
    }


    protected static VideoOptionsData CreateVideoOptions()
    {
        return new VideoOptionsData
        {
            FrameRate = 24,
            ConstantRateFactor = 23
        };
    }


    protected static TileOptionsData CreateTileOptions()
    {
        return new TileOptionsData
        {
            OverlapPx = 8,
            BlendMode = TileBlendMode.CenterPriorityCrop
        };
    }


    protected static async Task<DccSceneData> LoadSceneFromJsonAsync(string sceneJsonPath)
    {
        var json = await File.ReadAllTextAsync(sceneJsonPath);
        return JsonSerializer.Deserialize<DccSceneData>(json, JSON_OPTIONS)
               ?? throw new InvalidOperationException($"Failed to deserialize DCC scene from '{sceneJsonPath}'.");
    }


    protected static string? FindLatestExportedDccSceneJsonPath(string solutionRoot, string sceneName)
    {
        var candidateRoot = Path.Combine(solutionRoot, "@Output", "Temp", "candidate_validate_smoke");
        if (!Directory.Exists(candidateRoot))
            return null;

        return Directory
            .EnumerateDirectories(candidateRoot, sceneName + "_*", SearchOption.TopDirectoryOnly)
            .Select(me => new DirectoryInfo(me))
            .OrderByDescending(me => me.LastWriteTimeUtc)
            .Select(me => Path.Combine(me.FullName, "output", "dcc-scene.json"))
            .FirstOrDefault(File.Exists);
    }


    protected static void AssertImageContainsMeaningfullyLitPixels(string imagePath, string context)
    {
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(imagePath);

        long meaningfullyLitPixels = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.R >= 8 || pixel.G >= 8 || pixel.B >= 8)
                    meaningfullyLitPixels++;
            }
        }

        Assert.That(meaningfullyLitPixels, Is.GreaterThan(0),
            $"{context}: rendered image contains only near-black pixels and is not visually meaningful.");
    }


    #endregion
}
