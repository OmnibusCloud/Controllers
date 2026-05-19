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
/// End-to-end validate-blend tests for the production Render.ValidateBlend
/// activity and the BundledRenderValidateBlend script — attachment-blob
/// remapping (image/font/sequence/library/volume/cache), simulation-cache
/// rejection, and library/UDIM/VSE warning regressions. Owns its blend-file
/// authoring helpers (CreateBlendFileAsync etc.) since they are not used
/// outside this theme.
/// </summary>
[TestFixture]
internal sealed class RenderProductionScriptValidateBlendTests : RenderProductionScriptBlenderTestsBase
{
    #region Tests

    [Test]
    public async Task RenderValidateBlendRealRunTest()
    {
        var script = """
                     Job:ValidateBlendDiag(Blob:scene)
                     {
                         String:result = Render.ValidateBlend(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);
        var status = await m_engine.ScheduleAndWaitAsync(job, sceneBlobId);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings, Is.Empty);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptAcceptsPackedImageBlendCopyRealRunTest()
    {
        var imagePath = Path.Combine(m_blobStoragePath, "external_texture.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(imagePath);

        var sourceBlendPath = Path.Combine(m_blobStoragePath, "scene_with_external_image.blend");
        await CreateBlendFileAsync(
            sourceBlendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(imagePath)}')",
                "image.use_fake_user = True"
            ]);

        var packedBlendPath = Path.Combine(m_blobStoragePath, "scene_with_packed_external_image.blend");
        await CreatePackedBlendCopyAsync(sourceBlendPath, packedBlendPath);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(packedBlendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("image asset", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsCacheFileAttachmentBlobRealRunTest()
    {
        var cachePath = Path.Combine(m_blobStoragePath, "external_cache.abc");
        await File.WriteAllTextAsync(cachePath, "outwit-test");

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_cache_file.blend");
        try
        {
            await CreateBlendFileWithCacheFileAsync(blendPath, cachePath, "ExternalCache");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("CacheFile datablock creation is unavailable", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore("The current Blender runtime does not expose a stable cache_file creation path for bundled script tests.");
            return;
        }

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachmentBlobId = await m_blobService.UploadFileAsync(cachePath);
        File.Delete(cachePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "CacheFile",
                        BlobId = attachmentBlobId,
                        OriginalPath = cachePath,
                        RelativePath = "deps/cache-files/external_cache.abc",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("external cache file", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsFontAttachmentBlobRealRunTest()
    {
        var sourceFontPath = FindTestFontPath();
        if (sourceFontPath == null)
            Assert.Ignore("No test font was found on the current machine.");

        var externalFontPath = Path.Combine(m_blobStoragePath, Path.GetFileName(sourceFontPath));
        File.Copy(sourceFontPath, externalFontPath, overwrite: true);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_font.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"font = bpy.data.fonts.load(r'{NormalizePythonPath(externalFontPath)}')",
                "font.use_fake_user = True"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachmentBlobId = await m_blobService.UploadFileAsync(externalFontPath);
        File.Delete(externalFontPath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "Font",
                        BlobId = attachmentBlobId,
                        OriginalPath = externalFontPath,
                        RelativePath = "deps/fonts/attached-font.ttf",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("external font", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsImageSequenceAttachmentBlobsRealRunTest()
    {
        var externalSequenceDirectory = Path.Combine(m_blobStoragePath, "external-image-sequence");
        Directory.CreateDirectory(externalSequenceDirectory);
        var externalFramePaths = new List<string>();
        for (var index = 1; index <= 2; index++)
        {
            var framePath = Path.Combine(externalSequenceDirectory, $"plate_{index:0000}.png");
            using var image = new Image<Rgba32>(1, 1);
            await image.SaveAsPngAsync(framePath);
            externalFramePaths.Add(framePath);
        }

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_image_sequence.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(externalFramePaths[0])}')",
                "image.source = 'SEQUENCE'",
                "image.use_fake_user = True"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachments = externalFramePaths
            .Select(me => new RenderSceneAttachmentRefData
            {
                Kind = "ImageSequenceFrame",
                BlobId = m_blobService.UploadFileAsync(me).GetAwaiter().GetResult(),
                OriginalPath = me,
                RelativePath = $"deps/image-sequences/Plate/{Path.GetFileName(me)}",
                PackagingStrategy = "SceneAttachmentBlob"
            })
            .ToArray();
        Directory.Delete(externalSequenceDirectory, recursive: true);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId, attachments));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("image sequence", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsLinkedLibraryAttachmentBlobRealRunTest()
    {
        var libraryBlendPath = Path.Combine(m_blobStoragePath, "library.blend");
        await CreateBlendFileAsync(
            libraryBlendPath,
            [
                "mesh = bpy.data.meshes.new('LibraryMesh')",
                "obj = bpy.data.objects.new('LibraryCube', mesh)",
                "bpy.context.scene.collection.objects.link(obj)"
            ]);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_library.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"library_path = r'{NormalizePythonPath(libraryBlendPath)}'",
                "with bpy.data.libraries.load(library_path, link=True) as (data_from, data_to):",
                "    data_to.objects = ['LibraryCube']",
                "for obj in data_to.objects:",
                "    if obj is not None:",
                "        bpy.context.scene.collection.objects.link(obj)"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachmentBlobId = await m_blobService.UploadFileAsync(libraryBlendPath);
        File.Delete(libraryBlendPath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "LinkedLibrary",
                        BlobId = attachmentBlobId,
                        OriginalPath = libraryBlendPath,
                        RelativePath = "deps/linked-libraries/library.blend",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsVolumeAttachmentBlobRealRunTest()
    {
        var externalVolumePath = Path.Combine(m_blobStoragePath, "external_volume.vdb");
        await File.WriteAllTextAsync(externalVolumePath, "outwit-test");

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_volume.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "volume = bpy.data.volumes.new('ExternalVolume')",
                $"volume.filepath = r'{NormalizePythonPath(externalVolumePath)}'",
                "volume.use_fake_user = True"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachmentBlobId = await m_blobService.UploadFileAsync(externalVolumePath);
        File.Delete(externalVolumePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "Volume",
                        BlobId = attachmentBlobId,
                        OriginalPath = externalVolumePath,
                        RelativePath = "deps/volumes/external_volume.vdb",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("external volume", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsTransferredMediaAttachmentsRealRunTest()
    {
        var movieClipDirectory = Path.Combine(m_blobStoragePath, "movie-clip-sequence");
        Directory.CreateDirectory(movieClipDirectory);
        var movieClipPath = Path.Combine(movieClipDirectory, "clip_0001.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(movieClipPath);

        var imageStripDirectory = Path.Combine(m_blobStoragePath, "vse-image-strip");
        Directory.CreateDirectory(imageStripDirectory);
        var imageStripPath = Path.Combine(imageStripDirectory, "frame_0001.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(imageStripPath);

        var soundPath = Path.Combine(m_blobStoragePath, "media.wav");
        CreateTestWaveFile(soundPath);
        var movieStripPath = Path.Combine(m_blobStoragePath, "media.mp4");
        await CreateTestVideoFileAsync(movieStripPath);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_transferred_media.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"clip = bpy.data.movieclips.load(r'{NormalizePythonPath(movieClipPath)}')",
                "clip.use_fake_user = True",
                "scene = bpy.context.scene",
                "editor = scene.sequence_editor_create()",
                $"editor.strips.new_image('Image Strip', r'{NormalizePythonPath(imageStripPath)}', 1, 1)",
                $"editor.strips.new_sound('Sound Strip', r'{NormalizePythonPath(soundPath)}', 2, 1)",
                $"editor.strips.new_movie('Movie Strip', r'{NormalizePythonPath(movieStripPath)}', 3, 1)"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var movieClipBlobId = await m_blobService.UploadFileAsync(movieClipPath);
        var imageStripBlobId = await m_blobService.UploadFileAsync(imageStripPath);
        var soundBlobId = await m_blobService.UploadFileAsync(soundPath);
        var movieStripBlobId = await m_blobService.UploadFileAsync(movieStripPath);

        File.Delete(movieClipPath);
        File.Delete(imageStripPath);
        File.Delete(soundPath);
        File.Delete(movieStripPath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "MovieClip",
                        BlobId = movieClipBlobId,
                        OriginalPath = movieClipPath,
                        RelativePath = "deps/movie-clips/clip_0001.png",
                        PackagingStrategy = "SceneAttachmentBlob"
                    },
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "VseImageStripFrame",
                        BlobId = imageStripBlobId,
                        OriginalPath = imageStripPath,
                        RelativePath = "deps/vse/image-strips/Image_Strip/frame_0001.png",
                        PackagingStrategy = "SceneAttachmentBlob"
                    },
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "Sound",
                        BlobId = soundBlobId,
                        OriginalPath = soundPath,
                        RelativePath = "deps/sounds/media.wav",
                        PackagingStrategy = "SceneAttachmentBlob"
                    },
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "VseSoundStrip",
                        BlobId = soundBlobId,
                        OriginalPath = soundPath,
                        RelativePath = "deps/vse/sound-strips/Sound_Strip/media.wav",
                        PackagingStrategy = "SceneAttachmentBlob"
                    },
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "VseMovieStrip",
                        BlobId = movieStripBlobId,
                        OriginalPath = movieStripPath,
                        RelativePath = "deps/vse/movie-strips/Movie_Strip/media.mp4",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("movie clip", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("external sound", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("VSE image strip", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("VSE sound strip", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("VSE movie strip", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings, Is.Empty);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsFluidCacheIssuesForLavaSceneRealRunTest()
    {
        var lavaScenePath = Path.Combine(m_solutionRoot!, "@Data", "lava_fluid-viscosity-demo.blend");
        if (!File.Exists(lavaScenePath))
            Assert.Ignore($"Lava scene not found at {lavaScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(lavaScenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("Fluid domain", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(validation.Issues.Any(me => me.Contains("cache", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsIssuesForFlipVsApicSimulationFixtureRealRunTest()
    {
        var scenePath = Path.Combine(m_solutionRoot!, "@Data", "fluid-simulation_flip_vs_apic_solver.blend");
        if (!File.Exists(scenePath))
            Assert.Ignore($"Simulation fixture not found at {scenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(scenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        AssertSimulationFixtureBlocked(validation!);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsIssuesForClothInternalAirPressureFixtureRealRunTest()
    {
        var scenePath = Path.Combine(m_solutionRoot!, "@Data", "cloth_internal_air_pressure.blend");
        if (!File.Exists(scenePath))
            Assert.Ignore($"Simulation fixture not found at {scenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(scenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        AssertSimulationFixtureBlocked(validation!);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsIssuesForClothInnerSpringsFixtureRealRunTest()
    {
        var scenePath = Path.Combine(m_solutionRoot!, "@Data", "cloth_inner_springs.blend");
        if (!File.Exists(scenePath))
            Assert.Ignore($"Simulation fixture not found at {scenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(scenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        AssertSimulationFixtureBlocked(validation!);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsFluidCacheDirectoryAndMissingBakedSimulationDataRealRunTest()
    {
        var cacheDirectory = Path.Combine(m_blobStoragePath, "fluid-cache");
        Directory.CreateDirectory(cacheDirectory);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_fluid_cache_directory.blend");
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

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("external cache directory", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(validation.Issues.Any(me => me.Contains("requires baked simulation data", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsFluidMeshCacheRequirementRealRunTest()
    {
        var cacheDirectory = Path.Combine(m_blobStoragePath, "fluid-mesh-cache");
        Directory.CreateDirectory(cacheDirectory);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_fluid_mesh_cache_requirement.blend");
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

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("requires baked mesh cache", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsParticleSimulationIssueRealRunTest()
    {
        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_particle_simulation.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "obj.modifiers.new(name='Particles', type='PARTICLE_SYSTEM')"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("particle simulation", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsGeometryCacheIssueRealRunTest()
    {
        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_geometry_cache.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "obj.modifiers.new(name='GeoCache', type='MESH_SEQUENCE_CACHE')"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("geometry cache", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsWarningsForUdimMonsterSceneRealRunTest()
    {
        var udimScenePath = Path.Combine(m_solutionRoot!, "@Data", "UDIM_monster", "udim-monster.blend");
        if (!File.Exists(udimScenePath))
            Assert.Ignore($"UDIM monster scene not found at {udimScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(udimScenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues.Any(me => me.Contains("UDIM", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("UDIM image set", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsWarningsForVseMediaSceneRealRunTest()
    {
        var vseScenePath = Path.Combine(m_solutionRoot!, "@Data", "vse_media-transform", "vse_media-transform.blend");
        if (!File.Exists(vseScenePath))
            Assert.Ignore($"VSE media scene not found at {vseScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(vseScenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("VSE image strip", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptDoesNotReportFalseLinkedLibraryFindingsForCowboiStorytoolsRealRunTest()
    {
        var cowboiScenePath = Path.Combine(m_solutionRoot!, "@Data", "cowboi_storytools.blend");
        if (!File.Exists(cowboiScenePath))
            Assert.Ignore($"cowboi_storytools scene not found at {cowboiScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(cowboiScenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("Image sequence dependency", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(validation.Issues.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    #endregion

    #region Helpers

    private async Task CreateBlendFileAsync(string blendPath, IEnumerable<string> pythonLines)
    {
        var scriptPath = Path.Combine(m_blobStoragePath, $"create_blend_{Guid.NewGuid():N}.py");

        try
        {
            await File.WriteAllLinesAsync(
                scriptPath,
                [
                    "import bpy",
                    "bpy.ops.wm.read_factory_settings(use_empty=True)",
                    .. pythonLines,
                    $"bpy.ops.wm.save_mainfile(filepath=r'{NormalizePythonPath(blendPath)}')"
                ]);

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
                throw new InvalidOperationException($"Blender scene creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
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

    private async Task CreateBlendFileWithCacheFileAsync(string blendPath, string cachePath, string cacheName)
    {
        await CreateBlendFileAsync(blendPath, BuildCacheFilePythonLines(cachePath, cacheName));
    }

    private async Task CreatePackedBlendCopyAsync(string sourceBlendPath, string packedBlendPath)
    {
        var scriptPath = Path.Combine(m_blobStoragePath, $"pack_blend_{Guid.NewGuid():N}.py");
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

    private static IReadOnlyList<string> BuildCacheFilePythonLines(string cachePath, string cacheName)
    {
        return
        [
            $"cache_path = r'{NormalizePythonPath(cachePath)}'",
            $"cache_name = '{NormalizePythonPath(cacheName)}'",
            "cache_file = None",
            "cache_files = getattr(bpy.data, 'cache_files', None)",
            "load_fn = getattr(cache_files, 'load', None) if cache_files is not None else None",
            "if callable(load_fn):",
            "    try:",
            "        cache_file = load_fn(cache_path)",
            "    except Exception:",
            "        cache_file = None",
            "if cache_file is None:",
            "    new_fn = getattr(cache_files, 'new', None) if cache_files is not None else None",
            "    if callable(new_fn):",
            "        try:",
            "            cache_file = new_fn(cache_name)",
            "            cache_file.filepath = cache_path",
            "        except Exception:",
            "            cache_file = None",
            "if cache_file is None:",
            "    open_op = getattr(getattr(bpy.ops, 'cachefile', None), 'open', None)",
            "    if callable(open_op):",
            "        try:",
            "            open_op(filepath=cache_path)",
            "            cache_file = list(getattr(bpy.data, 'cache_files', []))[-1] if len(getattr(bpy.data, 'cache_files', [])) > 0 else None",
            "        except Exception:",
            "            cache_file = None",
            "if cache_file is None:",
            "    raise RuntimeError('CacheFile datablock creation is unavailable in this Blender runtime.')",
            "cache_file.use_fake_user = True"
        ];
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

    private async Task CreateTestVideoFileAsync(string filePath)
    {
        var ffmpegPath = ResolveFfmpegExecutablePath();
        if (!File.Exists(ffmpegPath))
            Assert.Ignore($"ffmpeg not found at {ffmpegPath}");

        var framePath = Path.Combine(Path.GetDirectoryName(filePath)!, $"video_frame_{Guid.NewGuid():N}.png");

        try
        {
            using (var image = new Image<Rgba32>(2, 2))
                await image.SaveAsPngAsync(framePath);

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -loop 1 -i \"{framePath}\" -t 1 -pix_fmt yuv420p \"{filePath}\"",
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
            if (process.ExitCode != 0 || !File.Exists(filePath))
                Assert.Ignore($"ffmpeg could not create a test video file. Stdout: {stdout} Stderr: {stderr}");
        }
        finally
        {
            if (File.Exists(framePath))
            {
                try { File.Delete(framePath); }
                catch { }
            }
        }
    }

    private static string ResolveFfmpegExecutablePath()
    {
        var resolverType = typeof(BlenderRunner).Assembly.GetType("OutWit.Controller.Render.Utils.RenderBinaryResolver")
                           ?? throw new InvalidOperationException("Failed to resolve RenderBinaryResolver type.");
        var resolveRootMethod = resolverType.GetMethod("ResolveFfmpegRoot", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                ?? throw new InvalidOperationException("Failed to resolve ResolveFfmpegRoot method.");
        var resolvePathMethod = resolverType.GetMethod("ResolveFfmpegPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                ?? throw new InvalidOperationException("Failed to resolve ResolveFfmpegPath method.");

        var ffmpegRoot = resolveRootMethod.Invoke(null, [typeof(BlenderRunner).Assembly.Location]) as string
                         ?? throw new InvalidOperationException("Failed to resolve ffmpeg root.");

        return resolvePathMethod.Invoke(null, [ffmpegRoot]) as string
               ?? throw new InvalidOperationException("Failed to resolve ffmpeg executable path.");
    }

    #endregion
}
