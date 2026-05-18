using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class RenderValidateBlendDependencyBlenderTests
{
    #region Fields

    private BlenderRunner m_blenderRunner = null!;

    private string m_tempDirectory = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(solutionRoot);
        if (blenderDir == null)
            Assert.Ignore("No supported Blender prerequisites for current OS/architecture");

        m_blenderRunner = new BlenderRunner(blenderDir, NullLogger.Instance);
        if (!m_blenderRunner.IsAvailable)
            Assert.Ignore($"Blender not found at {blenderDir}");

        var udimScenePath = Path.Combine(solutionRoot, "@Data", "UDIM_monster", "udim-monster.blend");
        if (!File.Exists(udimScenePath))
            Assert.Ignore($"UDIM monster scene not found at {udimScenePath}");
    }

    [SetUp]
    public void SetUp()
    {
        m_tempDirectory = Path.Combine(Path.GetTempPath(), $"witcloud_validate_blend_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_tempDirectory))
            Directory.Delete(m_tempDirectory, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsWarningForExistingExternalImageDependencyTest()
    {
        var imagePath = Path.Combine(m_tempDirectory, "external_texture.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(imagePath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_external_image.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(imagePath)}')",
                "image.use_fake_user = True"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("image asset", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncDoesNotReportExternalImageDependencyAfterPackingBlendCopyTest()
    {
        var imagePath = Path.Combine(m_tempDirectory, "packed_external_texture.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(imagePath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_packable_external_image.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(imagePath)}')",
                "image.use_fake_user = True"
            ]);

        var packedBlendPath = Path.Combine(m_tempDirectory, "scene_with_packed_external_image.blend");
        await CreatePackedBlendCopyAsync(blendPath, packedBlendPath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(packedBlendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("image asset", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForMissingExternalImageDependencyTest()
    {
        var imagePath = Path.Combine(m_tempDirectory, "missing_texture.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(imagePath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_missing_image.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(imagePath)}')",
                "image.use_fake_user = True"
            ]);

        File.Delete(imagePath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("Image dependency", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsWarningsForDownloadedUdimSceneTest()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new InvalidOperationException("Solution root not found.");
        var udimScenePath = Path.Combine(solutionRoot, "@Data", "UDIM_monster", "udim-monster.blend");

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(udimScenePath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues.Any(me => me.Contains("UDIM", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(validation.Warnings.Any(me => me.Contains("UDIM image set", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsWarningsForDownloadedVseMediaSceneTest()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new InvalidOperationException("Solution root not found.");
        var vseScenePath = Path.Combine(solutionRoot, "@Data", "vse_media-transform", "vse_media-transform.blend");
        if (!File.Exists(vseScenePath))
            Assert.Ignore($"VSE media scene not found at {vseScenePath}");

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(vseScenePath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("VSE image strip", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncDoesNotReportFalseLinkedLibraryFindingsForCowboiStorytoolsTest()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new InvalidOperationException("Solution root not found.");
        var cowboiScenePath = Path.Combine(solutionRoot, "@Data", "cowboi_storytools.blend");
        if (!File.Exists(cowboiScenePath))
            Assert.Ignore($"cowboi_storytools scene not found at {cowboiScenePath}");

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(cowboiScenePath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("Image sequence dependency", StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(validation.Issues.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(validation.Warnings.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsWarningForExistingSoundDependencyTest()
    {
        var soundPath = Path.Combine(m_tempDirectory, "external_sound.wav");
        CreateTestWaveFile(soundPath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_sound.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"sound = bpy.data.sounds.load(r'{NormalizePythonPath(soundPath)}')",
                "sound.use_fake_user = True"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("external sound", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForMissingSoundDependencyTest()
    {
        var soundPath = Path.Combine(m_tempDirectory, "missing_sound.wav");
        CreateTestWaveFile(soundPath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_missing_sound.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"sound = bpy.data.sounds.load(r'{NormalizePythonPath(soundPath)}')",
                "sound.use_fake_user = True"
            ]);

        File.Delete(soundPath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("Sound dependency", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsWarningForExistingMovieClipDependencyTest()
    {
        var clipDirectory = Path.Combine(m_tempDirectory, "clip_sequence");
        Directory.CreateDirectory(clipDirectory);

        for (var index = 1; index <= 2; index++)
        {
            var framePath = Path.Combine(clipDirectory, $"clip_{index:0000}.png");
            using var image = new Image<Rgba32>(1, 1);
            await image.SaveAsPngAsync(framePath);
        }

        var firstFramePath = Path.Combine(clipDirectory, "clip_0001.png");
        var blendPath = Path.Combine(m_tempDirectory, "scene_with_movie_clip.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"clip = bpy.data.movieclips.load(r'{NormalizePythonPath(firstFramePath)}')",
                "clip.use_fake_user = True"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("movie clip", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForMissingMovieClipDependencyTest()
    {
        var clipDirectory = Path.Combine(m_tempDirectory, "missing_clip_sequence");
        Directory.CreateDirectory(clipDirectory);

        for (var index = 1; index <= 2; index++)
        {
            var framePath = Path.Combine(clipDirectory, $"clip_{index:0000}.png");
            using var image = new Image<Rgba32>(1, 1);
            await image.SaveAsPngAsync(framePath);
        }

        var firstFramePath = Path.Combine(clipDirectory, "clip_0001.png");
        var blendPath = Path.Combine(m_tempDirectory, "scene_with_missing_movie_clip.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"clip = bpy.data.movieclips.load(r'{NormalizePythonPath(firstFramePath)}')",
                "clip.use_fake_user = True"
            ]);

        File.Delete(firstFramePath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("Movie clip dependency", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsWarningForExistingVolumeDependencyTest()
    {
        var volumePath = Path.Combine(m_tempDirectory, "external_volume.vdb");
        CreateDummyFile(volumePath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_volume.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "volume = bpy.data.volumes.new('ExternalVolume')",
                $"volume.filepath = r'{NormalizePythonPath(volumePath)}'",
                "volume.use_fake_user = True"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("external volume", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForMissingVolumeDependencyTest()
    {
        var volumePath = Path.Combine(m_tempDirectory, "missing_volume.vdb");
        CreateDummyFile(volumePath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_missing_volume.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "volume = bpy.data.volumes.new('MissingVolume')",
                $"volume.filepath = r'{NormalizePythonPath(volumePath)}'",
                "volume.use_fake_user = True"
            ]);

        File.Delete(volumePath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("Volume dependency", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsWarningForExistingImageSequenceDependencyTest()
    {
        var sequenceDirectory = Path.Combine(m_tempDirectory, "sequence");
        Directory.CreateDirectory(sequenceDirectory);

        for (var index = 1; index <= 2; index++)
        {
            var framePath = Path.Combine(sequenceDirectory, $"sequence_{index:0000}.png");
            using var image = new Image<Rgba32>(1, 1);
            await image.SaveAsPngAsync(framePath);
        }

        var firstFramePath = Path.Combine(sequenceDirectory, "sequence_0001.png");
        var blendPath = Path.Combine(m_tempDirectory, "scene_with_sequence.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(firstFramePath)}')",
                "image.source = 'SEQUENCE'",
                "image.use_fake_user = True"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("image sequence", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForMissingImageSequenceDependencyTest()
    {
        var sequenceDirectory = Path.Combine(m_tempDirectory, "missing_sequence");
        Directory.CreateDirectory(sequenceDirectory);

        for (var index = 1; index <= 2; index++)
        {
            var framePath = Path.Combine(sequenceDirectory, $"sequence_{index:0000}.png");
            using var image = new Image<Rgba32>(1, 1);
            await image.SaveAsPngAsync(framePath);
        }

        var firstFramePath = Path.Combine(sequenceDirectory, "sequence_0001.png");
        var blendPath = Path.Combine(m_tempDirectory, "scene_with_missing_sequence.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(firstFramePath)}')",
                "image.source = 'SEQUENCE'",
                "image.use_fake_user = True"
            ]);

        File.Delete(firstFramePath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("Image sequence dependency", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsWarningForExistingLinkedLibraryDependencyTest()
    {
        var libraryBlendPath = Path.Combine(m_tempDirectory, "library.blend");
        await CreateBlendFileAsync(
            libraryBlendPath,
            [
                "mesh = bpy.data.meshes.new('LibraryMesh')",
                "obj = bpy.data.objects.new('LibraryCube', mesh)",
                "bpy.context.scene.collection.objects.link(obj)"
            ]);

        var linkedBlendPath = Path.Combine(m_tempDirectory, "scene_with_library.blend");
        await CreateBlendFileAsync(
            linkedBlendPath,
            [
                $"library_path = r'{NormalizePythonPath(libraryBlendPath)}'",
                "with bpy.data.libraries.load(library_path, link=True) as (data_from, data_to):",
                "    data_to.objects = ['LibraryCube']",
                "for obj in data_to.objects:",
                "    if obj is not None:",
                "        bpy.context.scene.collection.objects.link(obj)"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(linkedBlendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForMissingLinkedLibraryDependencyTest()
    {
        var libraryBlendPath = Path.Combine(m_tempDirectory, "library_missing.blend");
        await CreateBlendFileAsync(
            libraryBlendPath,
            [
                "mesh = bpy.data.meshes.new('LibraryMesh')",
                "obj = bpy.data.objects.new('LibraryCube', mesh)",
                "bpy.context.scene.collection.objects.link(obj)"
            ]);

        var linkedBlendPath = Path.Combine(m_tempDirectory, "scene_with_missing_library.blend");
        await CreateBlendFileAsync(
            linkedBlendPath,
            [
                $"library_path = r'{NormalizePythonPath(libraryBlendPath)}'",
                "with bpy.data.libraries.load(library_path, link=True) as (data_from, data_to):",
                "    data_to.objects = ['LibraryCube']",
                "for obj in data_to.objects:",
                "    if obj is not None:",
                "        bpy.context.scene.collection.objects.link(obj)"
            ]);

        File.Delete(libraryBlendPath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(linkedBlendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForFluidCacheDirectoryAndMissingBakedSimulationDataTest()
    {
        var cacheDirectory = Path.Combine(m_tempDirectory, "fluid-cache");
        Directory.CreateDirectory(cacheDirectory);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_fluid_cache_directory.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "modifier = obj.modifiers.new(name='Fluid', type='FLUID')",
                "modifier.fluid_type = 'DOMAIN'",
                "domain = modifier.domain_settings",
                $"domain.cache_directory = r'{NormalizePythonPath(cacheDirectory)}'"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("external cache directory", StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(validation.Issues.Any(me => me.Contains("requires baked simulation data", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForFluidMeshCacheRequirementTest()
    {
        var cacheDirectory = Path.Combine(m_tempDirectory, "fluid-mesh-cache");
        Directory.CreateDirectory(cacheDirectory);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_fluid_mesh_cache_requirement.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "modifier = obj.modifiers.new(name='Fluid', type='FLUID')",
                "modifier.fluid_type = 'DOMAIN'",
                "domain = modifier.domain_settings",
                $"domain.cache_directory = r'{NormalizePythonPath(cacheDirectory)}'",
                "domain.use_mesh = True"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("requires baked mesh cache", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForDownloadedFlipVsApicSimulationFixtureTest()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new InvalidOperationException("Solution root not found.");
        var scenePath = Path.Combine(solutionRoot, "@Data", "fluid-simulation_flip_vs_apic_solver.blend");
        if (!File.Exists(scenePath))
            Assert.Ignore($"Simulation fixture not found at {scenePath}");

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(scenePath);

        AssertSimulationFixtureBlocked(validation);
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForDownloadedClothInternalAirPressureFixtureTest()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new InvalidOperationException("Solution root not found.");
        var scenePath = Path.Combine(solutionRoot, "@Data", "cloth_internal_air_pressure.blend");
        if (!File.Exists(scenePath))
            Assert.Ignore($"Simulation fixture not found at {scenePath}");

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(scenePath);

        AssertSimulationFixtureBlocked(validation);
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForDownloadedClothInnerSpringsFixtureTest()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new InvalidOperationException("Solution root not found.");
        var scenePath = Path.Combine(solutionRoot, "@Data", "cloth_inner_springs.blend");
        if (!File.Exists(scenePath))
            Assert.Ignore($"Simulation fixture not found at {scenePath}");

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(scenePath);

        AssertSimulationFixtureBlocked(validation);
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForParticleSimulationTest()
    {
        var blendPath = Path.Combine(m_tempDirectory, "scene_with_particle_simulation.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "obj.modifiers.new(name='Particles', type='PARTICLE_SYSTEM')"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("particle simulation", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForGeometryCacheModifierTest()
    {
        var blendPath = Path.Combine(m_tempDirectory, "scene_with_geometry_cache.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "obj.modifiers.new(name='GeoCache', type='MESH_SEQUENCE_CACHE')"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("geometry cache", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsWarningForExistingFontDependencyTest()
    {
        var sourceFontPath = FindTestFontPath();
        if (sourceFontPath == null)
            Assert.Ignore("No test font was found on the current machine.");

        var fontPath = Path.Combine(m_tempDirectory, Path.GetFileName(sourceFontPath));
        File.Copy(sourceFontPath, fontPath, overwrite: true);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_font.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"font = bpy.data.fonts.load(r'{NormalizePythonPath(fontPath)}')",
                "font.use_fake_user = True"
            ]);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("external font", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncStillReportsExternalFontDependencyAfterPackingBlendCopyTest()
    {
        var sourceFontPath = FindTestFontPath();
        if (sourceFontPath == null)
            Assert.Ignore("No test font was found on the current machine.");

        var fontPath = Path.Combine(m_tempDirectory, Path.GetFileName(sourceFontPath));
        File.Copy(sourceFontPath, fontPath, overwrite: true);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_packable_external_font.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"font = bpy.data.fonts.load(r'{NormalizePythonPath(fontPath)}')",
                "font.use_fake_user = True"
            ]);

        var packedBlendPath = Path.Combine(m_tempDirectory, "scene_with_packed_external_font.blend");
        await CreatePackedBlendCopyAsync(blendPath, packedBlendPath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(packedBlendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("external font", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncReportsIssueForMissingFontDependencyTest()
    {
        var sourceFontPath = FindTestFontPath();
        if (sourceFontPath == null)
            Assert.Ignore("No test font was found on the current machine.");

        var fontPath = Path.Combine(m_tempDirectory, Path.GetFileName(sourceFontPath));
        File.Copy(sourceFontPath, fontPath, overwrite: true);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_missing_font.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"font = bpy.data.fonts.load(r'{NormalizePythonPath(fontPath)}')",
                "font.use_fake_user = True"
            ]);

        File.Delete(fontPath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues.Any(me => me.Contains("Font dependency", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    #endregion

    #region Tools

    private async Task CreateBlendFileAsync(string blendPath, params string[] pythonLines)
    {
        await CreateBlendFileAsync(blendPath, (IEnumerable<string>)pythonLines);
    }

    private async Task CreateBlendFileAsync(string blendPath, IEnumerable<string> pythonLines)
    {
        var scriptPath = Path.Combine(m_tempDirectory, $"create_blend_{Guid.NewGuid():N}.py");
        var lines = new List<string>
        {
            "import bpy",
            "bpy.ops.wm.read_factory_settings(use_empty=True)"
        };

        lines.AddRange(pythonLines);
        lines.Add($"bpy.ops.wm.save_mainfile(filepath=r'{NormalizePythonPath(blendPath)}')");

        try
        {
            await File.WriteAllLinesAsync(scriptPath, lines);

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = GetBlenderExecutablePath(),
                    Arguments = $"-b --factory-startup --python-exit-code 1 --python \"{scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Blender scene creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
            }
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                try { File.Delete(scriptPath); }
                catch { }
            }
        }
    }

    private async Task CreatePackedBlendCopyAsync(string sourceBlendPath, string packedBlendPath)
    {
        var scriptPath = Path.Combine(m_tempDirectory, $"pack_blend_{Guid.NewGuid():N}.py");
        var lines = new[]
        {
            "import bpy",
            $"bpy.ops.wm.open_mainfile(filepath=r'{NormalizePythonPath(sourceBlendPath)}')",
            "bpy.ops.file.pack_all()",
            $"bpy.ops.wm.save_as_mainfile(filepath=r'{NormalizePythonPath(packedBlendPath)}', copy=True)"
        };

        try
        {
            await File.WriteAllLinesAsync(scriptPath, lines);

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = GetBlenderExecutablePath(),
                    Arguments = $"-b --factory-startup --python-exit-code 1 --python \"{scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Blender packed-copy creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
            }
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                try { File.Delete(scriptPath); }
                catch { }
            }
        }
    }

    private string GetBlenderExecutablePath()
    {
        return typeof(BlenderRunner)
                   .GetField("m_blenderPath", BindingFlags.Instance | BindingFlags.NonPublic)?
                   .GetValue(m_blenderRunner) as string
               ?? throw new InvalidOperationException("Failed to resolve Blender executable path from BlenderRunner.");
    }

    private static string? FindTestFontPath()
    {
        var candidateDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            "/System/Library/Fonts",
            "/Library/Fonts"
        };

        foreach (var directory in candidateDirectories.Where(Directory.Exists))
        {
            try
            {
                var fontPath = Directory.EnumerateFiles(directory, "*.ttf", SearchOption.AllDirectories).FirstOrDefault()
                               ?? Directory.EnumerateFiles(directory, "*.otf", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(fontPath))
                    return fontPath;
            }
            catch
            {
                // Ignore inaccessible font folders and continue probing.
            }
        }

        return null;
    }

    private static void CreateTestWaveFile(string filePath)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleRate = 8000;
        const short samplesCount = 16;
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var byteRate = sampleRate * blockAlign;
        var dataLength = samplesCount * blockAlign;

        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        for (var index = 0; index < samplesCount; index++)
            writer.Write((short)0);
    }

    private static void CreateDummyFile(string filePath)
    {
        File.WriteAllText(filePath, "outwit-test");
    }

    private static void AssertSimulationFixtureBlocked(RenderValidateBlendData validation)
    {
        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues, Is.Not.Empty);
            Assert.That(
                validation.Issues.Any(me => me.Contains("baked simulation data", StringComparison.OrdinalIgnoreCase)
                                            || me.Contains("baked mesh cache", StringComparison.OrdinalIgnoreCase)
                                            || me.Contains("cache directory", StringComparison.OrdinalIgnoreCase)
                                            || me.Contains("simulation", StringComparison.OrdinalIgnoreCase)),
                Is.True);
        });
    }

    private static string NormalizePythonPath(string path)
    {
        return path.Replace("\\", "/").Replace("'", "\\'");
    }

    #endregion
}
