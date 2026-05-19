using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Validate-blend coverage for external media-file dependencies (image, sound,
/// movie clip, volume, image-sequence, font) — the "warn for existing /
/// fail for missing" pattern across all media kinds.
/// </summary>
[TestFixture]
public sealed class RenderValidateBlendMediaDependencyTests : RenderValidateBlendDependencyTestsBase
{
    #region Image Tests

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

    #endregion

    #region Sound Tests

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

    #endregion

    #region Movie Clip Tests

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

    #endregion

    #region Volume Tests

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

    #endregion

    #region Image Sequence Tests

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

    #endregion

    #region Font Tests

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
}
