using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Validation;

/// <summary>
/// Static pre-launch validation (docs 03, section 8.1) over synthetic packages: the happy path
/// resolves view, timeline and frames; every negative fixture of section 16.2 that the host can see
/// is rejected with an explicit, permanent error.
/// </summary>
[TestFixture]
public sealed class ParaViewPackageValidatorTests
{
    #region Fields

    private string m_root = null!;

    private ParaViewTestBlobService m_blobs = null!;

    private ParaViewPackageValidator m_validator = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"pv_validate_{Guid.NewGuid():N}");
        m_blobs = new ParaViewTestBlobService(Path.Combine(m_root, "blobs"));
        // The embedded (generated) allowlist plus the reader plugin's proxy, so the plugin mechanics are
        // testable before the reader milestone ships the plugin and its own corpus states.
        var embedded = ParaViewProxyAllowlist.Bundled;
        var allowlist = new ParaViewProxyAllowlist(embedded.RuntimeVersion, embedded.Origin, embedded.Proxies,
            new Dictionary<string, IReadOnlyList<string>> { [ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME] = ["sources/OmnibusCloudFrdReader"] });
        m_validator = new ParaViewPackageValidator(allowlist, bundledReaderVersion: "1.2.0");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Happy path

    [Test]
    public void TypicalPackageIsValidAndResolvesThePlanTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs)
            .AddFile("data/field.vtu", "<VTKFile/>");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithTimesteps(0, 1, 2).Build());
        var options = new ParaViewOutputOptionsData { Width = 320, Height = 240, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

        var report = m_validator.Validate(scene, options, m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.Multiple(() =>
        {
            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.ResolvedViewId, Is.EqualTo("RenderView1"));
            Assert.That(report.ResolvedTimestepIndices, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(report.TimestepValues, Is.EqualTo(new[] { 0.0, 1.0, 2.0 }));
            Assert.That(report.AttachmentCount, Is.EqualTo(1));
            Assert.That(report.TotalAttachmentBytes, Is.EqualTo(scene.StateSize + scene.Attachments[0].Size));
            Assert.That(report.ProxyTypes, Does.Contain("sources/XMLUnstructuredGridReader"));
            Assert.That(report.ProxyTypes, Does.Contain("views/RenderView"));
            Assert.That(report.PackageDigest, Has.Length.EqualTo(64));
            Assert.That(report.RuntimeVersion, Is.EqualTo(ParaViewRuntimeInfo.RUNTIME_VERSION));
            Assert.That(report.Width, Is.EqualTo(320));
            Assert.That(report.Format, Is.EqualTo(ParaViewImageFormat.Png));
        });
    }

    [Test]
    public void TurntableIsValidatedAndCountedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs)
            .AddFile("data/field.vtu", "<VTKFile/>");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithTimesteps(0, 1, 2).Build());
        var statePath = m_blobs.GetStoredPath(scene.StateBlobId);

        ParaViewOutputOptionsData With(ParaViewTurntableData? turntable) => new()
        {
            Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All },
            Turntable = turntable
        };

        var plain = m_validator.Validate(scene, With(null), statePath);
        var fixedOrbit = m_validator.Validate(scene, With(new ParaViewTurntableData { Frames = 10 }), statePath);
        var advancing = m_validator.Validate(scene, With(new ParaViewTurntableData { Frames = 10, TimeMode = ParaViewTurntableTimeMode.Advancing }), statePath);
        var noFrames = m_validator.Validate(scene, With(new ParaViewTurntableData { Frames = 0 }), statePath);
        var noSweep = m_validator.Validate(scene, With(new ParaViewTurntableData { Degrees = 0.0 }), statePath);
        var tooWide = m_validator.Validate(scene, With(new ParaViewTurntableData { Degrees = 7200.0 }), statePath);
        var tooMany = m_validator.Validate(scene, With(new ParaViewTurntableData { Frames = 5000 }), statePath);

        Assert.Multiple(() =>
        {
            Assert.That(plain.IsValid, Is.True, string.Join("; ", plain.Errors));
            Assert.That(plain.OutputCount, Is.EqualTo(3));
            Assert.That(fixedOrbit.IsValid, Is.True, string.Join("; ", fixedOrbit.Errors));
            Assert.That(fixedOrbit.OutputCount, Is.EqualTo(30));
            Assert.That(fixedOrbit.ResolvedTimestepIndices, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(advancing.IsValid, Is.True, string.Join("; ", advancing.Errors));
            Assert.That(advancing.OutputCount, Is.EqualTo(10));
            Assert.That(noFrames.IsValid, Is.False);
            Assert.That(noFrames.Errors, Has.Some.Contains("at least 1 orbit frame"));
            Assert.That(noSweep.Errors, Has.Some.Contains("moves nothing"));
            Assert.That(tooWide.Errors, Has.Some.Contains("exceeds 3600"));
            Assert.That(tooMany.IsValid, Is.False);
            Assert.That(tooMany.Errors, Has.Some.Contains("15000 outputs"));
            Assert.That(tooMany.OutputCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void StaticSceneResolvesOneTimestepTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithoutTimeKeeper().Build());
        var options = new ParaViewOutputOptionsData { Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

        var report = m_validator.Validate(scene, options, m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
        Assert.That(report.ResolvedTimestepIndices, Is.EqualTo(new[] { 0 }));
        Assert.That(report.TimestepValues, Is.Empty);
    }

    [Test]
    public void ProducerTimelineIsUsedWhenTheStateCarriesNoneTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithoutTimeKeeper().Build(), timestepValues: [0, 10, 20, 30]);
        var options = new ParaViewOutputOptionsData { Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Range, First = 1, Last = 3, Step = 2 } };

        var report = m_validator.Validate(scene, options, m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
        Assert.That(report.ResolvedTimestepIndices, Is.EqualTo(new[] { 1, 3 }));
        Assert.That(report.TimestepValues, Is.EqualTo(new[] { 0.0, 10.0, 20.0, 30.0 }));
    }

    [Test]
    public void ExplicitViewIsResolvedAndUnknownViewRejectedTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddRenderView("RenderView2");
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(state.Build());

        var ok = m_validator.Validate(scene, new ParaViewOutputOptionsData { ViewId = "RenderView2" }, m_blobs.GetStoredPath(scene.StateBlobId));
        var bad = m_validator.Validate(scene, new ParaViewOutputOptionsData { ViewId = "Nope" }, m_blobs.GetStoredPath(scene.StateBlobId));
        var implicitFirst = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.Multiple(() =>
        {
            Assert.That(ok.IsValid, Is.True, string.Join("; ", ok.Errors));
            Assert.That(ok.ResolvedViewId, Is.EqualTo("RenderView2"));
            Assert.That(bad.IsValid, Is.False);
            Assert.That(bad.Errors, Has.Some.Contains("requested view 'Nope'"));
            Assert.That(implicitFirst.ResolvedViewId, Is.EqualTo("RenderView1"));
            Assert.That(implicitFirst.Warnings, Has.Some.Contains("2 views"));
        });
    }

    [Test]
    public void SeriesGroupWithoutAssociationIsRecordedAsFallbackTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs)
            .AddFile("data/series.pvd", "<VTKFile/>", ParaViewAttachmentRole.SeriesIndex, "series")
            .AddFile("data/series_0.vtu", "a", seriesGroup: "series")
            .AddFile("data/series_1.vtu", "b", seriesGroup: "series");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/series.pvd").WithTimesteps(0, 1).Build());

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData { Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } }, m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
        Assert.That(report.Fallbacks, Has.Count.EqualTo(1));
        Assert.That(report.Fallbacks[0], Does.Contain("series group 'series'"));
    }

    [Test]
    public void BundledReaderRequirementIsAcceptedWhenSatisfiedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/job.frd", "frd");
        var state = new ParaViewStateBuilder();
        var reader = state.AddReader("OmnibusCloudFrdReader", "job.frd", "data/job.frd");
        state.AddRepresentation("UnstructuredGridRepresentation", reader);
        state.AddRenderView();
        var scene = package.BuildScene(state.Build());
        scene.Runtime.Plugins.Add(new ParaViewPluginRequirementData { Name = ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME, Version = "1.0" });

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
        Assert.That(report.RequiredPlugins, Is.EqualTo(new[] { "OmnibusCloudFrdReader@1.0" }));
        Assert.That(report.ProxyTypes, Does.Contain("sources/OmnibusCloudFrdReader"));
    }

    #endregion

    #region Rejections

    [Test]
    public void ProgrammableFilterIsRejectedTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddFilter("ProgrammableFilter", "ProgrammableFilter1", 1000, ("Script", ["import os; os.system('x')"]));
        var report = ValidateState(state);

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("ProgrammableFilter").And.Some.Contains("executes user code"));
    }

    [Test]
    public void ProgrammableSourceAndPythonCalculatorAreRejectedTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddProxy("sources", "ProgrammableSource", ("Script", ["print(1)"]));
        state.AddFilter("PythonCalculator", "PythonCalculator1", 1000, ("Expression", ["inputs[0].PointData['T']*2"]));
        var report = ValidateState(state);

        Assert.That(report.Errors.Count(me => me.Contains("executes user code")), Is.EqualTo(2));
    }

    [Test]
    public void ExecutablePropertyOnAllowlistedProxyIsRejectedTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddFilter("Contour", "Contour2", 1000, ("RequestInformationScript", ["import sys"]));
        var report = ValidateState(state);

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("executable property 'RequestInformationScript'"));
    }

    [Test]
    public void LoadMaterialsWithAPathIsRejectedTest()
    {
        // Audit C-H1: materials/MaterialLibrary is allowlisted and sits outside the file-reference
        // groups, so its LoadMaterials path never met a path gate — a non-empty value must be refused
        // on the host exactly like an executable property; the empty default of every real state
        // stays inert.
        var armed = ParaViewStateBuilder.Typical("data/field.vtu");
        armed.AddProxy("materials", "MaterialLibrary", ("LoadMaterials", ["/etc/passwd"]));
        var report = ValidateState(armed);

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("materials/MaterialLibrary").And.Some.Contains("executable property 'LoadMaterials'"));

        var inert = ParaViewStateBuilder.Typical("data/field.vtu");
        inert.AddProxy("materials", "MaterialLibrary", ("LoadMaterials", [""]));
        Assert.That(ValidateState(inert).IsValid, Is.True, "an empty LoadMaterials is what every saved state carries");
    }

    [Test]
    public void UnknownProxyTypeIsRejectedByTheAllowlistTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddFilter("SomeExoticFilter", "Exotic1", 1000);
        var report = ValidateState(state);

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("filters/SomeExoticFilter").And.Some.Contains("allowlist"));
    }

    [Test]
    public void UnknownPluginRequirementIsRejectedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build());
        scene.Runtime.Plugins.Add(new ParaViewPluginRequirementData { Name = "SomeOtherPlugin", Version = "1.0" });

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("SomeOtherPlugin").And.Some.Contains("not allowlisted"));
    }

    [Test]
    public void ReaderVersionBeyondTheBundledOneIsRejectedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build());
        scene.Runtime.Plugins.Add(new ParaViewPluginRequirementData { Name = ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME, Version = "1.5" });

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("not satisfied by the bundled reader 1.2.0"));
    }

    [Test]
    public void ParaViewVersionMismatchIsRejectedAndPatchToleratedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build(), major: 5, minor: 13);
        var mismatch = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        var patchScene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build());
        patchScene.Runtime.ParaViewPatch = ParaViewRuntimeInfo.RUNTIME_PATCH + 1;
        var patch = m_validator.Validate(patchScene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(patchScene.StateBlobId));

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.IsValid, Is.False);
            Assert.That(mismatch.Errors, Has.Some.Contains("exact major.minor match"));
            Assert.That(patch.IsValid, Is.True, string.Join("; ", patch.Errors));
            Assert.That(patch.Warnings, Has.Some.Contains("patch mismatch tolerated"));
        });
    }

    [TestCase("C:/Users/me/data/field.vtu", "drive letter")]
    [TestCase("/home/me/data/field.vtu", "absolute")]
    [TestCase("\\\\server\\share\\field.vtu", "separators")]
    [TestCase("../field.vtu", "traverses")]
    [TestCase("data/../../field.vtu", "traverses")]
    [TestCase("file:///tmp/field.vtu", "URI")]
    public void ClientPathsInTheStateAreRejectedTest(string path, string reason)
    {
        var report = ValidateState(ParaViewStateBuilder.Typical(path), "data/field.vtu");

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains(reason));
    }

    [Test]
    public void FileReferenceOutsideThePackageIsRejectedTest()
    {
        var report = ValidateState(ParaViewStateBuilder.Typical("data/other.vtu"), "data/field.vtu");

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("'data/other.vtu', which is not an attachment"));
    }

    [Test]
    public void TextureFileReferenceIsCheckedTooTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddProxy("textures", "ImageTexture", ("FileName", ["C:/tex/wood.png"]));
        var report = ValidateState(state);

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("textures/ImageTexture").And.Some.Contains("drive letter"));
    }

    [Test]
    public void SlashInANonFilePropertyIsNotAPathTest()
    {
        // Allowlisted proxies carrying slash-y, drive-y and URI-like values in NON-file properties
        // (a Calculator function is "p/rho"; the validator must not mistake them for paths).
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddFilter("Clip", "Clip1", 1000, ("Scalars", ["POINTS", "p/rho"]), ("Value", ["1/2"]));
        state.AddProxy("sources", "SphereSource", ("Name", ["step 1/2 of C:/nothing file://x"]));
        var report = ValidateState(state);

        Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
    }

    [Test]
    public void FilesDomainMarksAFilePropertyWhateverItsNameTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddProxy("sources", "XMLImageDataReader", ("Source", ["data/other.vti"]));
        var xml = state.Build().Replace("<Element index=\"0\" value=\"data/other.vti\"/>", "<Element index=\"0\" value=\"data/other.vti\"/><Domain name=\"files\" id=\"x.files\"/>");
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(xml);

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("'data/other.vti', which is not an attachment"));
    }

    [Test]
    public void FileAndDirectoryPrefixCollisionIsRejectedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x").AddFile("data", "i am a file");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build());

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("'data' is both a file and a directory"));
    }

    [Test]
    public void UnreferencedReaderInputProducesAWarningOnlyTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs)
            .AddFile("data/field.vtu", "x")
            .AddFile("data/unused.vtu", "y");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build());

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
        Assert.That(report.Warnings, Has.Some.Contains("'data/unused.vtu' is not referenced"));
    }

    [Test]
    public void StateDigestMismatchIsRejectedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build());
        scene.StateSha256 = new string('0', 64);

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("state digest mismatch"));
    }

    [Test]
    public void AttachmentRulesAreEnforcedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build());
        scene.Attachments.Add(new ParaViewAttachmentRefData { BlobId = Guid.Empty, LogicalPath = "../escape.vtu", Sha256 = "zz", Size = -1 });
        scene.Attachments.Add(new ParaViewAttachmentRefData { BlobId = Guid.NewGuid(), LogicalPath = "DATA/FIELD.vtu", Sha256 = new string('a', 64), Size = 1 });

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.Multiple(() =>
        {
            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Errors, Has.Some.Contains("traverses upward"));
            Assert.That(report.Errors, Has.Some.Contains("declared twice"));
        });
    }

    [Test]
    public void ResolutionLimitsAreEnforcedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build());

        var tooWide = m_validator.Validate(scene, new ParaViewOutputOptionsData { Width = ParaViewInputLimits.MAX_DIMENSION + 1, Height = 10 }, m_blobs.GetStoredPath(scene.StateBlobId));
        var tooManyPixels = m_validator.Validate(scene, new ParaViewOutputOptionsData { Width = 16000, Height = 16000 }, m_blobs.GetStoredPath(scene.StateBlobId));
        var zero = m_validator.Validate(scene, new ParaViewOutputOptionsData { Width = 0, Height = 10 }, m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.Multiple(() =>
        {
            Assert.That(tooWide.Errors, Has.Some.Contains("per dimension limit"));
            Assert.That(tooManyPixels.Errors, Has.Some.Contains("megapixel"));
            Assert.That(zero.Errors, Has.Some.Contains("must be positive"));
        });
    }

    [Test]
    public void FrameSelectionOutsideTimelineIsRejectedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithTimesteps(0, 1, 2).Build());

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData { Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Range, First = 1, Last = 5 } }, m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("outside the timeline"));
    }

    [Test]
    public void AttachmentTimestepBeyondTimelineIsRejectedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x", timestepIndices: [7]);
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithTimesteps(0, 1).Build());

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("associated with timestep indices outside the timeline"));
    }

    [Test]
    public void TimelineMismatchBetweenPackageAndStateIsRejectedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithTimesteps(0, 1, 2).Build(), timestepValues: [0, 1]);

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("timeline mismatch"));
    }

    [Test]
    public void CustomProxyDefinitionsAreRejectedTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu")
            .WithExtraStateContent("<CustomProxyDefinitions><CustomProxyDefinition name=\"X\" group=\"filters\"/></CustomProxyDefinitions>");
        var report = ValidateState(state);

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("custom proxy definitions"));
    }

    [Test]
    public void StateWithoutViewsIsRejectedTest()
    {
        var state = new ParaViewStateBuilder();
        state.AddReader("XMLUnstructuredGridReader", "field.vtu", "data/field.vtu");
        var report = ValidateState(state);

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("registers no views"));
    }

    [Test]
    public void MissingStateFileIsRejectedTest()
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").Build());

        var report = m_validator.Validate(scene, new ParaViewOutputOptionsData(), Path.Combine(m_root, "missing.pvsm"));

        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("could not be resolved"));
    }

    #endregion

    #region Tools

    private ParaViewValidationReportData ValidateState(ParaViewStateBuilder state, string attachmentPath = "data/field.vtu")
    {
        var package = new ParaViewPackageBuilder(m_root, m_blobs).AddFile(attachmentPath, "x");
        var scene = package.BuildScene(state.Build());
        return m_validator.Validate(scene, new ParaViewOutputOptionsData(), m_blobs.GetStoredPath(scene.StateBlobId));
    }

    #endregion
}
