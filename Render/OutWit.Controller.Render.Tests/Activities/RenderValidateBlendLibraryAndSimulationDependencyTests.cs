using OutWit.Controller.Render.Tests.Utils;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Validate-blend coverage for linked-library dependencies and simulation-cache
/// dependencies (UDIM image sets, VSE strips, fluid/cloth/particle/geometry-cache
/// caches), plus downloaded simulation-fixture regression cases.
/// </summary>
[TestFixture]
public sealed class RenderValidateBlendLibraryAndSimulationDependencyTests : RenderValidateBlendDependencyTestsBase
{
    #region Downloaded Scene Warning Tests

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

    #endregion

    #region Linked Library Tests

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

    #endregion

    #region Fluid Simulation Tests

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

    #endregion

    #region Downloaded Simulation Fixture Tests

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

    #endregion

    #region Particle and Geometry Cache Tests

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

    #endregion
}
