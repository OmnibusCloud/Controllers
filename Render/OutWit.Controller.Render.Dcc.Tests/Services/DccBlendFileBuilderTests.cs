using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Dcc.Services;
using OutWit.Controller.Render.Dcc.Tests.Mock;
using OutWit.Controller.Render.Dcc.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Tests.Services;

[TestFixture]
public sealed class DccBlendFileBuilderTests
{
    #region Fields

    private string m_storageDir = null!;
    private RenderTestBlobService m_blobService = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_dcc_builder_test_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_storageDir);
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
    public async Task BuildAsyncBuildsBlendArtifactTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc build test.");

        var scene = DccRenderTestData.CreateValidScene();
        var texturePath = Path.Combine(m_storageDir, "albedo.png");
        File.WriteAllBytes(texturePath, Convert.FromBase64String(MINIMAL_PNG_BASE64));
        var textureBlobId = m_blobService.RegisterExistingFile(texturePath);
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(textureBlobId));
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var artifact = await DccBlendFileBuilder.BuildAsync(buildInput, m_blobService, NullLogger.Instance, CancellationToken.None);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(artifact.LocalBlendPath), Is.True);
                Assert.That(Path.GetExtension(artifact.LocalBlendPath), Is.EqualTo(".blend"));
                Assert.That(new FileInfo(artifact.LocalBlendPath).Length, Is.GreaterThan(0));
            });
        }
        finally
        {
            DccBlendFileBuilder.Cleanup(artifact, NullLogger.Instance);
        }
    }

    [Test]
    public void BuildAsyncRejectsRelativeTexturePathWithoutMaterializedAttachmentTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc missing-texture regression test.");

        var scene = DccRenderTestData.CreateValidScene();
        var buildInput = DccSceneBuildInputFactory.Create(scene);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await DccBlendFileBuilder.BuildAsync(buildInput, m_blobService, NullLogger.Instance, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("Cannot read 'textures/albedo.png'").IgnoreCase);
    }

    #endregion

    #region Constants

    private const string MINIMAL_PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    #endregion
}
