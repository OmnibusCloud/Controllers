using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Rewrites selected attachment-backed dependency paths inside a working blend copy.
/// </summary>
internal static class BlenderSceneAttachmentRemapHelper
{
    #region Constants

    private const string CACHE_FILE_ATTACHMENT_KIND = "CacheFile";

    private const string FONT_ATTACHMENT_KIND = "Font";

    private const string IMAGE_SEQUENCE_FRAME_ATTACHMENT_KIND = "ImageSequenceFrame";

    private const string LINKED_LIBRARY_ATTACHMENT_KIND = "LinkedLibrary";

    private const string MOVIE_CLIP_ATTACHMENT_KIND = "MovieClip";

    private const string SOUND_ATTACHMENT_KIND = "Sound";

    private const string VOLUME_ATTACHMENT_KIND = "Volume";

    private const string SCENE_ATTACHMENT_BLOB_PACKAGING = "SceneAttachmentBlob";

    private const string VSE_IMAGE_STRIP_FRAME_ATTACHMENT_KIND = "VseImageStripFrame";

    private const string VSE_MOVIE_STRIP_ATTACHMENT_KIND = "VseMovieStrip";

    private const string VSE_SOUND_STRIP_ATTACHMENT_KIND = "VseSoundStrip";

    #endregion

    #region Functions

    public static async Task RemapAttachmentPathsInPlaceAsync(
        BlenderRunner blenderRunner,
        string blendFilePath,
        IReadOnlyList<RenderSceneAttachmentRefData> attachments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blenderRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(blendFilePath);
        ArgumentNullException.ThrowIfNull(attachments);

        var attachmentMappings = CreateAttachmentMappings(blendFilePath, attachments);
        if (attachmentMappings.Count == 0)
            return;

        var mappingPath = Path.Combine(Path.GetTempPath(), $"outwit_attachment_remap_{Guid.NewGuid():N}.json");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"outwit_attachment_remap_{Guid.NewGuid():N}.py");

        try
        {
            await File.WriteAllTextAsync(mappingPath, JsonSerializer.Serialize(attachmentMappings), cancellationToken);
            await File.WriteAllLinesAsync(scriptPath, BuildPythonLines(mappingPath), cancellationToken);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = blenderRunner.BlenderExecutablePath,
                    Arguments = $"-b \"{blendFilePath}\" --python-exit-code 1 --python \"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Blender scene attachment remap failed for '{blendFilePath}' with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
            }
        }
        finally
        {
            TryDelete(mappingPath, blenderRunner.Logger);
            TryDelete(scriptPath, blenderRunner.Logger);
        }
    }

    private static List<SceneAttachmentPathRemapEntry> CreateAttachmentMappings(string blendFilePath, IReadOnlyList<RenderSceneAttachmentRefData> attachments)
    {
        var sceneDirectory = Path.GetDirectoryName(blendFilePath);
        if (string.IsNullOrWhiteSpace(sceneDirectory))
            throw new InvalidOperationException($"Failed to resolve a scene directory for '{blendFilePath}'.");

        return attachments
            .Where(me => IsRemappedAttachmentKind(me.Kind)
                         && string.Equals(me.PackagingStrategy, SCENE_ATTACHMENT_BLOB_PACKAGING, StringComparison.Ordinal))
            .Select(me => new SceneAttachmentPathRemapEntry
            {
                Kind = me.Kind,
                OriginalPath = me.OriginalPath,
                MaterializedPath = Path.GetFullPath(Path.Combine(sceneDirectory, me.RelativePath))
            })
            .ToList();
    }

    private static IReadOnlyList<string> BuildPythonLines(string mappingPath)
    {
        return
        [
            "import bpy, json, os",
            $"with open(r'{NormalizePythonPath(mappingPath)}', 'r', encoding='utf-8') as stream:",
            "    mapping_entries = json.load(stream)",
            "def normalize(path):",
            "    return os.path.normcase(os.path.normpath(path)) if path else ''",
            "font_path_map = {normalize(str(entry.get('OriginalPath') or '')): str(entry.get('MaterializedPath') or '') for entry in mapping_entries if str(entry.get('Kind') or '') == 'Font'}",
            "image_sequence_directory_map = {normalize(os.path.dirname(str(entry.get('OriginalPath') or ''))): os.path.dirname(str(entry.get('MaterializedPath') or '')) for entry in mapping_entries if str(entry.get('Kind') or '') == 'ImageSequenceFrame'}",
            "cache_path_map = {normalize(str(entry.get('OriginalPath') or '')): str(entry.get('MaterializedPath') or '') for entry in mapping_entries if str(entry.get('Kind') or '') == 'CacheFile'}",
            "linked_library_path_map = {normalize(str(entry.get('OriginalPath') or '')): str(entry.get('MaterializedPath') or '') for entry in mapping_entries if str(entry.get('Kind') or '') == 'LinkedLibrary'}",
            "sound_path_map = {normalize(str(entry.get('OriginalPath') or '')): str(entry.get('MaterializedPath') or '') for entry in mapping_entries if str(entry.get('Kind') or '') == 'Sound'}",
            "movie_clip_path_map = {normalize(str(entry.get('OriginalPath') or '')): str(entry.get('MaterializedPath') or '') for entry in mapping_entries if str(entry.get('Kind') or '') == 'MovieClip'}",
            "volume_path_map = {normalize(str(entry.get('OriginalPath') or '')): str(entry.get('MaterializedPath') or '') for entry in mapping_entries if str(entry.get('Kind') or '') == 'Volume'}",
            "vse_movie_path_map = {normalize(str(entry.get('OriginalPath') or '')): str(entry.get('MaterializedPath') or '') for entry in mapping_entries if str(entry.get('Kind') or '') == 'VseMovieStrip'}",
            "vse_sound_path_map = {normalize(str(entry.get('OriginalPath') or '')): str(entry.get('MaterializedPath') or '') for entry in mapping_entries if str(entry.get('Kind') or '') == 'VseSoundStrip'}",
            "vse_image_directory_map = {normalize(os.path.dirname(str(entry.get('OriginalPath') or ''))): os.path.dirname(str(entry.get('MaterializedPath') or '')) for entry in mapping_entries if str(entry.get('Kind') or '') == 'VseImageStripFrame'}",
            "for library in getattr(bpy.data, 'libraries', []):",
            "    library_path = str(getattr(library, 'filepath', '') or '')",
            "    if not library_path:",
            "        continue",
            "    resolved_library_path = bpy.path.abspath(library_path)",
            "    target_path = linked_library_path_map.get(normalize(resolved_library_path))",
            "    if target_path and os.path.exists(target_path):",
            "        library.filepath = target_path",
            "for font in bpy.data.fonts:",
            "    font_path = str(getattr(font, 'filepath', '') or '')",
            "    if not font_path or font_path == '<builtin>':",
            "        continue",
            "    resolved_font_path = bpy.path.abspath(font_path)",
            "    target_path = font_path_map.get(normalize(resolved_font_path))",
            "    if target_path and os.path.exists(target_path):",
            "        font.filepath = target_path",
            "for sound in getattr(bpy.data, 'sounds', []):",
            "    sound_path = str(getattr(sound, 'filepath', '') or '')",
            "    if not sound_path:",
            "        continue",
            "    resolved_sound_path = bpy.path.abspath(sound_path)",
            "    target_path = sound_path_map.get(normalize(resolved_sound_path))",
            "    if target_path and os.path.exists(target_path):",
            "        sound.filepath = target_path",
            "for movie_clip in getattr(bpy.data, 'movieclips', []):",
            "    clip_path = str(getattr(movie_clip, 'filepath', '') or '')",
            "    if not clip_path:",
            "        continue",
            "    resolved_clip_path = bpy.path.abspath(clip_path)",
            "    target_path = movie_clip_path_map.get(normalize(resolved_clip_path))",
            "    if target_path and os.path.exists(target_path):",
            "        movie_clip.filepath = target_path",
            "for volume in getattr(bpy.data, 'volumes', []):",
            "    volume_path = str(getattr(volume, 'filepath', '') or '')",
            "    if not volume_path:",
            "        continue",
            "    resolved_volume_path = bpy.path.abspath(volume_path)",
            "    target_path = volume_path_map.get(normalize(resolved_volume_path))",
            "    if target_path and os.path.exists(target_path):",
            "        volume.filepath = target_path",
            "for cache_file in getattr(bpy.data, 'cache_files', []):",
            "    cache_path = str(getattr(cache_file, 'filepath', '') or '')",
            "    if not cache_path:",
            "        continue",
            "    resolved_cache_path = bpy.path.abspath(cache_path)",
            "    target_path = cache_path_map.get(normalize(resolved_cache_path))",
            "    if target_path and os.path.exists(target_path):",
            "        cache_file.filepath = target_path",
            "for scene in getattr(bpy.data, 'scenes', []):",
            "    editor = getattr(scene, 'sequence_editor', None)",
            "    if editor is None:",
            "        continue",
            "    for strip in getattr(editor, 'strips_all', []):",
            "        strip_type = str(getattr(strip, 'type', '') or '')",
            "        if strip_type == 'IMAGE':",
            "            directory = bpy.path.abspath(str(getattr(strip, 'directory', '') or ''))",
            "            target_directory = vse_image_directory_map.get(normalize(directory))",
            "            if target_directory and os.path.isdir(target_directory):",
            "                strip.directory = target_directory",
            "        elif strip_type == 'MOVIE':",
            "            movie_path = bpy.path.abspath(str(getattr(strip, 'filepath', '') or ''))",
            "            target_path = vse_movie_path_map.get(normalize(movie_path))",
            "            if target_path and os.path.exists(target_path):",
            "                strip.filepath = target_path",
            "        elif strip_type == 'SOUND':",
            "            sound = getattr(strip, 'sound', None)",
            "            sound_path = bpy.path.abspath(str(getattr(sound, 'filepath', '') or getattr(strip, 'filepath', '') or ''))",
            "            target_path = vse_sound_path_map.get(normalize(sound_path))",
            "            if target_path and os.path.exists(target_path):",
            "                if sound is not None and hasattr(sound, 'filepath'):",
            "                    sound.filepath = target_path",
            "                if hasattr(strip, 'filepath'):",
            "                    strip.filepath = target_path",
            "for image in getattr(bpy.data, 'images', []):",
            "    image_path = str(getattr(image, 'filepath', '') or '')",
            "    if not image_path:",
            "        continue",
            "    source = str(getattr(image, 'source', '') or '')",
            "    resolved_image_path = bpy.path.abspath(image_path)",
            "    if source == 'SEQUENCE':",
            "        target_directory = image_sequence_directory_map.get(normalize(os.path.dirname(resolved_image_path)))",
            "        if target_directory:",
            "            target_path = os.path.join(target_directory, os.path.basename(resolved_image_path))",
            "            if os.path.exists(target_path):",
            "                image.filepath = target_path",
            "        continue",
            "    target_directory = vse_image_directory_map.get(normalize(os.path.dirname(resolved_image_path)))",
            "    if target_directory:",
            "        target_path = os.path.join(target_directory, os.path.basename(resolved_image_path))",
            "        if os.path.exists(target_path):",
            "            image.filepath = target_path",
            "bpy.ops.wm.save_mainfile()"
        ];
    }

    internal static bool IsRemappedAttachmentKind(string? kind)
    {
        return string.Equals(kind, FONT_ATTACHMENT_KIND, StringComparison.Ordinal)
               || string.Equals(kind, IMAGE_SEQUENCE_FRAME_ATTACHMENT_KIND, StringComparison.Ordinal)
               || string.Equals(kind, LINKED_LIBRARY_ATTACHMENT_KIND, StringComparison.Ordinal)
               || string.Equals(kind, MOVIE_CLIP_ATTACHMENT_KIND, StringComparison.Ordinal)
               || string.Equals(kind, CACHE_FILE_ATTACHMENT_KIND, StringComparison.Ordinal)
               || string.Equals(kind, SOUND_ATTACHMENT_KIND, StringComparison.Ordinal)
               || string.Equals(kind, VOLUME_ATTACHMENT_KIND, StringComparison.Ordinal)
               || string.Equals(kind, VSE_IMAGE_STRIP_FRAME_ATTACHMENT_KIND, StringComparison.Ordinal)
               || string.Equals(kind, VSE_MOVIE_STRIP_ATTACHMENT_KIND, StringComparison.Ordinal)
               || string.Equals(kind, VSE_SOUND_STRIP_ATTACHMENT_KIND, StringComparison.Ordinal);
    }

    private static string NormalizePythonPath(string path)
    {
        return path.Replace("\\", "/").Replace("'", "\\'");
    }

    private static void TryDelete(string path, ILogger logger)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Best-effort cleanup failed for temporary file {Path}", path);
        }
    }

    #endregion
}
