using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// The node-side compose pipeline against the fake pvpython: materialization of the data scene's
/// attachment (digest stamped when undeclared, verified when declared), the composer contract (task
/// and status documents, cwd, environment), the published state and the package reference built
/// from it, the host-validator guard on the composed state, cleanup — plus every failure path:
/// inadmissible scene, composer failure, missing state, a state the allowlist refuses, a state
/// carrying a node path, a colour array the data lacks, cancellation.
/// </summary>
[TestFixture]
public sealed class ParaViewComposeExecutorTests
{
    #region Constants

    private const string LOGICAL_PATH = "data/model.frd";

    private const string FAKE_FRD = "    1C fake CalculiX result for the fake composer\n";

    #endregion

    #region Fields

    private string m_root = null!;

    private ParaViewTestBlobService m_blobs = null!;

    private IWitTempStorage m_tempStorage = null!;

    private string m_fakePvpython = null!;

    private ParaViewComposeExecutor m_executor = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var solutionRoot = ParaViewTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        m_fakePvpython = ParaViewTestPaths.FindFakePvpythonPath(solutionRoot) ?? string.Empty;
        if (m_fakePvpython.Length == 0)
            Assert.Ignore("fake-pvpython not built");
    }

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"pv_compose_{Guid.NewGuid():N}");
        m_blobs = new ParaViewTestBlobService(Path.Combine(m_root, "blobs"));
        m_tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "temp"));
        m_executor = new ParaViewComposeExecutor(m_blobs, m_tempStorage, ParaViewProxyAllowlist.LoadEmbedded(ParaViewRuntimeInfo.RUNTIME_SERIES), NullLogger.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task SuccessfulCompositionPublishesAValidatedPackageReferenceTest()
    {
        var bytes = Encoding.UTF8.GetBytes(FAKE_FRD);
        var data = await DataSceneAsync(bytes, declareDigest: false);
        data.ColorArrayName = "DISP";
        data.ColormapPreset = "Jet";
        data.Representation = ParaViewSceneRepresentation.SurfaceWithEdges;
        var options = new ParaViewOutputOptionsData { Width = 640, Height = 360 };
        var jobId = Guid.NewGuid();

        var scene = await m_executor.ExecuteAsync(data, options, jobId, m_fakePvpython, CancellationToken.None);

        var statePath = m_blobs.GetStoredPath(scene.StateBlobId);
        var stateText = await File.ReadAllTextAsync(statePath);
        Assert.Multiple(() =>
        {
            Assert.That(scene.StateBlobId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(File.Exists(statePath), Is.True);
            Assert.That(scene.StateSha256, Is.EqualTo(ParaViewPackageDigest.HashFile(statePath)));
            Assert.That(scene.StateSize, Is.EqualTo(new FileInfo(statePath).Length));

            var attachment = scene.Attachments.Single();
            Assert.That(attachment.BlobId, Is.EqualTo(data.Attachments[0].BlobId));
            Assert.That(attachment.LogicalPath, Is.EqualTo(LOGICAL_PATH));
            Assert.That(attachment.Sha256, Is.EqualTo(Sha256(bytes)), "the composer stamps the digest it materialized");
            Assert.That(attachment.Size, Is.EqualTo(bytes.Length));
            Assert.That(attachment.Role, Is.EqualTo(ParaViewAttachmentRole.ReaderInput));

            Assert.That(scene.Runtime.ParaViewMajor, Is.EqualTo(6));
            Assert.That(scene.Runtime.ParaViewMinor, Is.EqualTo(1));
            Assert.That(scene.Runtime.ParaViewPatch, Is.EqualTo(1));
            Assert.That(scene.Runtime.ProducerPluginVersion, Does.StartWith(ParaViewComposeExecutor.PRODUCER_TAG));
            Assert.That(scene.Runtime.Plugins.Single().Name, Is.EqualTo(ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME));
            Assert.That(scene.Runtime.Plugins.Single().Version, Is.EqualTo(ParaViewRuntimeInfo.BundledReaderVersion()));

            Assert.That(scene.TimestepValues, Is.EqualTo(new[] { 1.0, 2.0, 3.0 }));
            Assert.That(scene.PackageManifestJson, Does.Contain(ParaViewComposeExecutor.PRODUCER_TAG));
            Assert.That(scene.PackageManifestJson, Does.Contain("\"DISP\""));

            Assert.That(stateText, Does.Contain(LOGICAL_PATH));
            Assert.That(stateText, Does.Not.Contain(m_tempStorage.RootPath), "the state never carries a node path");
            Assert.That(stateText, Does.Contain("Surface With Edges"));
        });

        // The host validator accepts the reference exactly as Validate will see it.
        var validator = new ParaViewPackageValidator(ParaViewProxyAllowlist.LoadEmbedded(ParaViewRuntimeInfo.RUNTIME_SERIES), ParaViewRuntimeInfo.BundledReaderVersion());
        var report = validator.Validate(scene, options, statePath);
        Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
        Assert.That(report.TimestepValues, Is.EqualTo(new[] { 1.0, 2.0, 3.0 }));

        // The workspace is gone.
        var jobRoot = Path.Combine(m_tempStorage.RootPath, "witcloud_paraview", jobId.ToString("N"));
        Assert.That(Directory.Exists(jobRoot) ? Directory.GetDirectories(jobRoot) : [], Is.Empty);
    }

    [Test]
    public async Task DeclaredDigestIsVerifiedTest()
    {
        var bytes = Encoding.UTF8.GetBytes(FAKE_FRD);
        var data = await DataSceneAsync(bytes, declareDigest: true);

        var scene = await m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, CancellationToken.None);

        Assert.That(scene.Attachments.Single().Sha256, Is.EqualTo(Sha256(bytes)));
    }

    [Test]
    public async Task DeclaredDigestMismatchFailsBeforeComposingTest()
    {
        var data = await DataSceneAsync(Encoding.UTF8.GetBytes(FAKE_FRD), declareDigest: true);
        data.Attachments[0].Sha256 = new string('0', 64);
        var published = PublishedBlobCount();

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("digest mismatch"));
        Assert.That(PublishedBlobCount(), Is.EqualTo(published), "nothing is published on failure");
    }

    [Test]
    public async Task InadmissibleDataSceneFailsBeforeMaterializingTest()
    {
        var data = await DataSceneAsync(Encoding.UTF8.GetBytes(FAKE_FRD), declareDigest: false);
        data.Attachments[0].LogicalPath = "data/model.vtu";
        m_blobs.ClearRequests();

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("not admissible"));
        Assert.That(m_blobs.Requests, Is.Empty, "an inadmissible scene downloads nothing");
    }

    [Test]
    public async Task ComposerFailureFailsWithItsStageAndMessageTest()
    {
        var data = await DataSceneAsync(Encoding.UTF8.GetBytes(FAKE_FRD + "FAKE-COMPOSE-FAIL\n"), declareDigest: false);
        var published = PublishedBlobCount();

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("[inspect]"));
            Assert.That(error.Message, Does.Contain("fake failure requested by the data"));
            Assert.That(PublishedBlobCount(), Is.EqualTo(published));
        });
    }

    [Test]
    public async Task MissingColorArrayFailsNamingTheArraysThatExistTest()
    {
        var data = await DataSceneAsync(Encoding.UTF8.GetBytes(FAKE_FRD), declareDigest: false);
        data.ColorArrayName = "PRESSURE";

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("no points array 'PRESSURE'"));
        Assert.That(error.Message, Does.Contain("NDTEMP"));
    }

    [Test]
    public async Task SuccessWithoutAStateIsRejectedTest()
    {
        var data = await DataSceneAsync(Encoding.UTF8.GetBytes(FAKE_FRD + "FAKE-COMPOSE-NO-STATE\n"), declareDigest: false);

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("saved no state"));
    }

    [Test]
    public async Task StateTheAllowlistRefusesIsRejectedByTheGuardTest()
    {
        var data = await DataSceneAsync(Encoding.UTF8.GetBytes(FAKE_FRD + "FAKE-COMPOSE-BAD-PROXY\n"), declareDigest: false);
        var published = PublishedBlobCount();

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("does not pass validation"));
            Assert.That(error.Message, Does.Contain("ProgrammableSource"));
            Assert.That(PublishedBlobCount(), Is.EqualTo(published), "a refused state is never published");
        });
    }

    [Test]
    public async Task StateCarryingANodePathIsRejectedByTheGuardTest()
    {
        var data = await DataSceneAsync(Encoding.UTF8.GetBytes(FAKE_FRD + "FAKE-COMPOSE-ABSOLUTE\n"), declareDigest: false);

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("does not pass validation"));
    }

    [Test]
    public async Task TimelineOfTheComposedStateIsReportedTest()
    {
        var data = await DataSceneAsync(Encoding.UTF8.GetBytes(FAKE_FRD + "FAKE-TIMESTEPS=7\n"), declareDigest: false);

        var scene = await m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, CancellationToken.None);

        Assert.That(scene.TimestepValues, Has.Count.EqualTo(7));
    }

    [Test]
    public async Task CancellationKillsTheComposerTest()
    {
        var data = await DataSceneAsync(Encoding.UTF8.GetBytes(FAKE_FRD + "FAKE-COMPOSE-HANG\n"), declareDigest: false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(),
            () => m_executor.ExecuteAsync(data, new ParaViewOutputOptionsData(), Guid.NewGuid(), m_fakePvpython, cancellation.Token));
    }

    [Test]
    public void TaskDocumentCarriesThePresentationTokensTest()
    {
        using var workspace = ParaViewTaskWorkspace.Create(m_tempStorage, Guid.NewGuid(), 0);
        var data = new ParaViewDataSceneData
        {
            ColorArrayName = "STRESS",
            ColorAssociation = ParaViewColorAssociation.Cells,
            ColorComponent = 3,
            ColormapPreset = "Turbo",
            Representation = ParaViewSceneRepresentation.Wireframe,
            ShowScalarBar = false,
            CameraDirection = ParaViewCameraDirection.PlusZ,
            FitTo = ParaViewCameraFit.FirstTimestep
        };
        var materialized = new ParaViewMaterializedAttachment("data/frd/model.frd", Path.Combine(workspace.PackageRoot, "data", "frd", "model.frd"), new string('b', 64), 10);

        var task = ParaViewComposeExecutor.BuildTask(data, new ParaViewOutputOptionsData { Width = 800, Height = 600 }, workspace, materialized, Path.Combine(workspace.PluginsDirectory, "reader.py"));
        var json = task.ToJson();
        var parsed = ParaViewComposeTask.FromJson(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"color_association\": \"CELLS\""));
            Assert.That(parsed.Representation, Is.EqualTo("Wireframe"));
            Assert.That(parsed.CameraDirection, Is.EqualTo("+z"), "the '+' travels JSON-escaped; the composer decodes it");
            Assert.That(parsed.FitTo, Is.EqualTo("first"));
            Assert.That(parsed.RegistrationName, Is.EqualTo("model.frd"));
            Assert.That(parsed.DataLogicalPath, Is.EqualTo("data/frd/model.frd"));
            Assert.That(parsed.ViewWidth, Is.EqualTo(800));
            Assert.That(parsed.ViewHeight, Is.EqualTo(600));
            Assert.That(parsed.ColorComponent, Is.EqualTo(3));
            Assert.That(parsed.ShowScalarBar, Is.False);
            Assert.That(parsed.StatePath, Is.EqualTo(workspace.StatePath));
            Assert.That(parsed.StatusPath, Is.EqualTo(workspace.ComposeStatusFilePath));
        });
    }

    [TestCase("6.1.1", 6, 1, 1)]
    [TestCase("6.1.1-fake", 6, 1, 1)]
    [TestCase("6.1", 6, 1, 0)]
    [TestCase("", 0, 0, 0)]
    public void VersionTextParsesTest(string text, int major, int minor, int patch)
    {
        Assert.That(ParaViewComposeExecutor.ParseVersion(text), Is.EqualTo((major, minor, patch)));
    }

    #endregion

    #region Tools

    private async Task<ParaViewDataSceneData> DataSceneAsync(byte[] bytes, bool declareDigest)
    {
        var blobId = await m_blobs.UploadBytesAsync(bytes, "model.frd");
        return new ParaViewDataSceneData
        {
            Attachments =
            [
                new ParaViewAttachmentRefData
                {
                    BlobId = blobId,
                    LogicalPath = LOGICAL_PATH,
                    Sha256 = declareDigest ? Sha256(bytes) : string.Empty,
                    Size = declareDigest ? bytes.Length : 0,
                    Role = ParaViewAttachmentRole.ReaderInput
                }
            ]
        };
    }

    private int PublishedBlobCount()
    {
        var directory = Path.Combine(m_root, "blobs");
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length : 0;
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    #endregion
}
