using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Cross-platform utility for running Blender in headless mode.
/// Resolves Blender binary path relative to the controller module directory.
/// </summary>
public sealed class BlenderRunner
{
    #region Fields

    private const string AVAILABLE_BACKENDS_PREFIX = "OUTWIT_RENDER_AVAILABLE=";

    private const string SELECTED_BACKEND_PREFIX = "OUTWIT_RENDER_BACKEND=";

    private const string SELECTION_MESSAGE_PREFIX = "OUTWIT_RENDER_MESSAGE=";

    private static readonly string[] GPU_BACKEND_CANDIDATES = ["OPTIX", "CUDA", "HIP", "METAL"];

    private readonly string m_blenderPath;
    private readonly ILogger m_logger;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a BlenderRunner that looks for the Blender binary in the specified directory.
    /// </summary>
    /// <param name="blenderDir">Directory containing the Blender installation.</param>
    /// <param name="logger">Logger instance.</param>
    public BlenderRunner(string blenderDir, ILogger logger)
    {
        m_blenderPath = RenderBinaryResolver.ResolveBlenderPath(blenderDir);
        m_logger = logger;

        if (!File.Exists(m_blenderPath))
            logger.LogWarning("Blender executable not found at {BlenderPath}", m_blenderPath);
        else
            RenderBinaryResolver.EnsureExecutable(m_blenderPath, logger);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Renders a single frame from a .blend file.
    /// </summary>
    /// <param name="blendFilePath">Path to the .blend file.</param>
    /// <param name="frame">Frame number to render.</param>
    /// <param name="outputPath">Output file path (without extension — Blender appends frame number).</param>
    /// <param name="options">Render options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the rendered image file.</returns>
    /// <exception cref="InvalidOperationException">Thrown if Blender process fails.</exception>
    public async Task<string> RenderFrameAsync(
        string blendFilePath,
        int frame,
        string outputPath,
        RenderOptionsData options,
        CancellationToken cancellationToken = default,
        RenderTaskData? task = null)
    {
        m_logger.LogInformation("Blender render: frame {Frame} from {BlendFile}", frame, blendFilePath);

        var result = await RunRenderAttemptAsync(
            blendFilePath,
            frame,
            outputPath,
            options,
            task,
            forceCpuFallback: false,
            cancellationToken);

        if (ShouldRetryWithCpuFallback(result))
        {
            m_logger.LogWarning(
                "Blender render failed on auto-selected GPU backend {RenderDevice}; retrying once on CPU. Message: {Message}",
                result.SelectedRenderBackend,
                result.SelectionMessage ?? "No backend selection message reported");

            DeleteRenderedOutputs(outputPath, frame, options.Format);

            result = await RunRenderAttemptAsync(
                blendFilePath,
                frame,
                outputPath,
                options,
                task,
                forceCpuFallback: true,
                cancellationToken);
        }

        EnsureSuccessfulRender(result);

        var renderedPath = FindRenderedFile(outputPath, frame, options.Format);
        if (renderedPath == null)
            throw new InvalidOperationException(
                $"Blender completed but output file not found for frame {frame} at {outputPath}");

        m_logger.LogInformation("Blender render complete: {OutputPath} ({Size} bytes)",
            renderedPath, new FileInfo(renderedPath).Length);

        return renderedPath;
    }

    /// <summary>
    /// Gets the Blender version string.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Version string (e.g., "Blender 4.2.19").</returns>
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _) = await RunProcessAsync("-b --version", cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException("Failed to get Blender version");

        var firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine?.Trim() ?? "Unknown";
    }

    public async Task<(int Width, int Height)> GetSceneResolutionAsync(string blendFilePath, CancellationToken cancellationToken = default)
    {
        const string startMarker = "OUTWIT_SCENE_RESOLUTION_START";
        const string endMarker = "OUTWIT_SCENE_RESOLUTION_END";

        var pythonLines = new List<string>
        {
            "import bpy, json",
            "scene = bpy.context.scene",
            "resolution_x = int(scene.render.resolution_x)",
            "resolution_y = int(scene.render.resolution_y)",
            "resolution_percentage = int(scene.render.resolution_percentage)",
            "payload = {'width': int(round(resolution_x * resolution_percentage / 100.0)), 'height': int(round(resolution_y * resolution_percentage / 100.0))}",
            $"print('{startMarker}')",
            "print(json.dumps(payload))",
            $"print('{endMarker}')"
        };

        var args = $"-b \"{blendFilePath}\" --python-exit-code 1 {BuildPythonExecArgument(pythonLines)}";
        var (exitCode, stdout, stderr) = await RunProcessAsync(args, cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException($"Failed to read Blender scene resolution: {stderr}");

        var startIndex = stdout.IndexOf(startMarker, StringComparison.Ordinal);
        var endIndex = stdout.IndexOf(endMarker, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex)
            throw new InvalidOperationException("Blender scene resolution probe returned no structured diagnostics payload.");

        var json = stdout.Substring(startIndex + startMarker.Length, endIndex - startIndex - startMarker.Length).Trim();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (
            root.GetProperty("width").GetInt32(),
            root.GetProperty("height").GetInt32());
    }

    /// <summary>
    /// Validates that a .blend file can be opened successfully by the local Blender runtime.
    /// </summary>
    /// <param name="blendFilePath">Path to the .blend file to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when Blender can open the file successfully.</returns>
    public async Task<bool> ValidateBlendAsync(string blendFilePath, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateBlendDetailedAsync(blendFilePath, cancellationToken);
        return validation.IsValid;
    }

    /// <summary>
    /// Validates that a .blend file can be opened successfully and does not require unsupported external simulation/cache state.
    /// </summary>
    /// <param name="blendFilePath">Path to the .blend file to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detailed validation diagnostics for the scene.</returns>
    public async Task<RenderValidateBlendData> ValidateBlendDetailedAsync(string blendFilePath, CancellationToken cancellationToken = default)
    {
        const string startMarker = "OUTWIT_VALIDATE_BLEND_START";
        const string endMarker = "OUTWIT_VALIDATE_BLEND_END";
        var supportedAttachedCachePaths = LoadSupportedAttachedPaths(blendFilePath, "CacheFile");
        var supportedAttachedFontPaths = LoadSupportedAttachedPaths(blendFilePath, "Font");
        var supportedAttachedImageSequenceFramePaths = LoadSupportedAttachedPaths(blendFilePath, "ImageSequenceFrame");
        var supportedAttachedLinkedLibraryPaths = LoadSupportedAttachedPaths(blendFilePath, "LinkedLibrary");
        var supportedAttachedMovieClipPaths = LoadSupportedAttachedPaths(blendFilePath, "MovieClip");
        var supportedAttachedSoundPaths = LoadSupportedAttachedPaths(blendFilePath, "Sound");
        var supportedAttachedVolumePaths = LoadSupportedAttachedPaths(blendFilePath, "Volume");
        var supportedAttachedVseImageFramePaths = LoadSupportedAttachedPaths(blendFilePath, "VseImageStripFrame");
        var supportedAttachedVseImageDirectories = supportedAttachedVseImageFramePaths
            .Select(Path.GetDirectoryName)
            .Where(me => !string.IsNullOrWhiteSpace(me))
            .Select(me => me!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var supportedAttachedVseMoviePaths = LoadSupportedAttachedPaths(blendFilePath, "VseMovieStrip");
        var supportedAttachedVseSoundPaths = LoadSupportedAttachedPaths(blendFilePath, "VseSoundStrip");
        var supportedAttachedImageSequenceDirectories = supportedAttachedImageSequenceFramePaths
            .Select(Path.GetDirectoryName)
            .Where(me => !string.IsNullOrWhiteSpace(me))
            .Select(me => me!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pythonLines = new List<string>
        {
            "import bpy, glob, json, os, re",
            "issues = []",
            "warnings = []",
            "def add_unique(collection, message):",
            "    if message not in collection:",
            "        collection.append(message)",
            "def normalize_path(path):",
            "    return os.path.normcase(os.path.normpath(path)) if path else ''",
            "def get_sequence_frame_paths(path):",
            "    directory = os.path.dirname(path)",
            "    file_name = os.path.basename(path)",
            "    if not directory or not os.path.isdir(directory) or not file_name:",
            "        return []",
            "    match = re.match(r'^(.*?)(\\d+)(\\.[^.]+)$', file_name)",
            "    if match is None:",
            "        return []",
            "    prefix, digits, extension = match.groups()",
            "    pattern = re.compile(r'^' + re.escape(prefix) + r'(\\d{' + str(len(digits)) + r'})' + re.escape(extension) + r'$', re.IGNORECASE)",
            "    matches = []",
            "    for candidate_name in os.listdir(directory):",
            "        candidate_match = pattern.match(candidate_name)",
            "        if candidate_match is None:",
            "            continue",
            "        candidate_path = os.path.join(directory, candidate_name)",
            "        if not os.path.isfile(candidate_path):",
            "            continue",
            "        matches.append((int(candidate_match.group(1)), candidate_path))",
            "    return [path for _, path in sorted(matches, key=lambda item: item[0])]",
            $"supported_attached_cache_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedCachePaths)})",
            $"supported_attached_font_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedFontPaths)})",
            $"supported_attached_image_sequence_frame_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedImageSequenceFramePaths)})",
            $"supported_attached_image_sequence_directories = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedImageSequenceDirectories)})",
            $"supported_attached_linked_library_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedLinkedLibraryPaths)})",
            $"supported_attached_movie_clip_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedMovieClipPaths)})",
            $"supported_attached_sound_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedSoundPaths)})",
            $"supported_attached_volume_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedVolumePaths)})",
            $"supported_attached_vse_image_frame_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedVseImageFramePaths)})",
            $"supported_attached_vse_image_directories = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedVseImageDirectories)})",
            $"supported_attached_vse_movie_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedVseMoviePaths)})",
            $"supported_attached_vse_sound_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedVseSoundPaths)})",
            "for library in bpy.data.libraries:",
            "    library_path = str(getattr(library, 'filepath', '') or '')",
            "    if not library_path:",
            "        continue",
            "    if getattr(library, 'packed_file', None) is not None:",
            "        continue",
            "    resolved_library_path = bpy.path.abspath(library_path)",
            "    if not os.path.exists(resolved_library_path):",
            "        add_unique(issues, f\"Linked library '{library.name}' is missing at '{resolved_library_path}'.\")",
            "    elif normalize_path(resolved_library_path) in supported_attached_linked_library_paths:",
            "        continue",
            "    else:",
            "        add_unique(warnings, f\"Scene uses linked library '{library.name}' from '{resolved_library_path}'. Ensure that this dependency remains portable for remote rendering.\")",
            "for image in bpy.data.images:",
            "    source = str(getattr(image, 'source', '') or '')",
            "    if source in {'GENERATED', 'VIEWER', 'RENDER_RESULT'}:",
            "        continue",
            "    if getattr(image, 'packed_file', None) is not None:",
            "        continue",
            "    image_path = str(getattr(image, 'filepath', '') or '')",
            "    if not image_path:",
            "        continue",
            "    resolved_image_path = bpy.path.abspath(image_path)",
            "    is_udim_image = source == 'TILED' or '<UDIM>' in resolved_image_path",
            "    if is_udim_image:",
            "        udim_pattern = resolved_image_path.replace('<UDIM>', '[1-9][0-9][0-9][0-9]')",
            "        udim_matches = sorted(glob.glob(udim_pattern))",
            "        if not udim_matches:",
            "            add_unique(issues, f\"UDIM image dependency '{image.name}' is missing at '{resolved_image_path}'.\")",
            "        else:",
            "            add_unique(warnings, f\"Scene uses external UDIM image set '{image.name}' from '{resolved_image_path}'. Ensure the full tile set is transferred for remote rendering.\")",
            "        continue",
            "    if not os.path.exists(resolved_image_path):",
            "        if source == 'SEQUENCE':",
            "            add_unique(issues, f\"Image sequence dependency '{image.name}' is missing at '{resolved_image_path}'. Ensure the full sequence is available for remote rendering.\")",
            "        else:",
            "            add_unique(issues, f\"Image dependency '{image.name}' is missing at '{resolved_image_path}'.\")",
            "    elif normalize_path(resolved_image_path) in supported_attached_vse_image_frame_paths:",
            "        continue",
            "    elif source == 'SEQUENCE':",
            "        sequence_paths = get_sequence_frame_paths(resolved_image_path)",
            "        if normalize_path(resolved_image_path) in supported_attached_image_sequence_frame_paths:",
            "            continue",
            "        if sequence_paths and all(normalize_path(path) in supported_attached_image_sequence_frame_paths for path in sequence_paths):",
            "            continue",
            "        if normalize_path(os.path.dirname(resolved_image_path)) in supported_attached_image_sequence_directories:",
            "            continue",
            "        add_unique(warnings, f\"Scene uses external image sequence '{image.name}' from '{resolved_image_path}'. Ensure the full sequence is transferred for remote rendering.\")",
            "    else:",
            "        add_unique(warnings, f\"Scene uses external image asset '{image.name}' from '{resolved_image_path}'.\")",
            "for scene in bpy.data.scenes:",
            "    editor = getattr(scene, 'sequence_editor', None)",
            "    if editor is None:",
            "        continue",
            "    for strip in getattr(editor, 'strips_all', []):",
            "        strip_type = str(getattr(strip, 'type', '') or '')",
            "        strip_name = str(getattr(strip, 'name', '') or strip_type or 'Unnamed')",
            "        if strip_type == 'IMAGE':",
            "            directory = bpy.path.abspath(str(getattr(strip, 'directory', '') or ''))",
            "            elements = list(getattr(strip, 'elements', []))",
            "            if not directory or not elements:",
            "                continue",
            "            missing_paths = []",
            "            for element in elements:",
            "                filename = str(getattr(element, 'filename', '') or '')",
            "                if not filename:",
            "                    continue",
            "                resolved_strip_path = os.path.join(directory, filename)",
            "                if not os.path.exists(resolved_strip_path):",
            "                    missing_paths.append(resolved_strip_path)",
            "            if missing_paths:",
            "                add_unique(issues, f\"VSE image strip '{strip_name}' in scene '{scene.name}' is missing media under '{directory}'.\")",
            "            elif normalize_path(directory) in supported_attached_vse_image_directories:",
            "                continue",
            "            else:",
            "                add_unique(warnings, f\"Scene '{scene.name}' uses VSE image strip '{strip_name}' from '{directory}'. Ensure these media files are transferred for remote rendering.\")",
            "        elif strip_type == 'MOVIE':",
            "            movie_path = bpy.path.abspath(str(getattr(strip, 'filepath', '') or ''))",
            "            if not movie_path:",
            "                continue",
            "            if not os.path.exists(movie_path):",
            "                add_unique(issues, f\"VSE movie strip '{strip_name}' in scene '{scene.name}' is missing at '{movie_path}'.\")",
            "            elif normalize_path(movie_path) in supported_attached_vse_movie_paths:",
            "                continue",
            "            else:",
            "                add_unique(warnings, f\"Scene '{scene.name}' uses VSE movie strip '{strip_name}' from '{movie_path}'. Ensure this media file is transferred for remote rendering.\")",
            "        elif strip_type == 'SOUND':",
            "            sound = getattr(strip, 'sound', None)",
            "            sound_path = bpy.path.abspath(str(getattr(sound, 'filepath', '') or getattr(strip, 'filepath', '') or ''))",
            "            if not sound_path:",
            "                continue",
            "            if not os.path.exists(sound_path):",
            "                add_unique(issues, f\"VSE sound strip '{strip_name}' in scene '{scene.name}' is missing at '{sound_path}'.\")",
            "            elif normalize_path(sound_path) in supported_attached_vse_sound_paths or normalize_path(sound_path) in supported_attached_sound_paths:",
            "                continue",
            "            else:",
            "                add_unique(warnings, f\"Scene '{scene.name}' uses VSE sound strip '{strip_name}' from '{sound_path}'. Ensure this media file is transferred for remote rendering.\")",
            "for font in bpy.data.fonts:",
            "    font_path = str(getattr(font, 'filepath', '') or '')",
            "    if not font_path or font_path == '<builtin>':",
            "        continue",
            "    resolved_font_path = bpy.path.abspath(font_path)",
            "    if not os.path.exists(resolved_font_path):",
            "        add_unique(issues, f\"Font dependency '{font.name}' is missing at '{resolved_font_path}'.\")",
            "    elif normalize_path(resolved_font_path) in supported_attached_font_paths:",
            "        continue",
            "    else:",
            "        add_unique(warnings, f\"Scene uses external font '{font.name}' from '{resolved_font_path}'.\")",
            "for movie_clip in getattr(bpy.data, 'movieclips', []):",
            "    clip_path = str(getattr(movie_clip, 'filepath', '') or '')",
            "    if not clip_path:",
            "        continue",
            "    resolved_clip_path = bpy.path.abspath(clip_path)",
            "    if not os.path.exists(resolved_clip_path):",
            "        add_unique(issues, f\"Movie clip dependency '{movie_clip.name}' is missing at '{resolved_clip_path}'.\")",
            "    elif normalize_path(resolved_clip_path) in supported_attached_movie_clip_paths:",
            "        continue",
            "    else:",
            "        add_unique(warnings, f\"Scene uses external movie clip '{movie_clip.name}' from '{resolved_clip_path}'.\")",
            "for sound in getattr(bpy.data, 'sounds', []):",
            "    sound_path = str(getattr(sound, 'filepath', '') or '')",
            "    if not sound_path:",
            "        continue",
            "    resolved_sound_path = bpy.path.abspath(sound_path)",
            "    if not os.path.exists(resolved_sound_path):",
            "        add_unique(issues, f\"Sound dependency '{sound.name}' is missing at '{resolved_sound_path}'.\")",
            "    elif normalize_path(resolved_sound_path) in supported_attached_sound_paths:",
            "        continue",
            "    else:",
            "        add_unique(warnings, f\"Scene uses external sound '{sound.name}' from '{resolved_sound_path}'.\")",
            "for cache_file in getattr(bpy.data, 'cache_files', []):",
            "    cache_path = str(getattr(cache_file, 'filepath', '') or '')",
            "    if not cache_path:",
            "        continue",
            "    resolved_cache_path = bpy.path.abspath(cache_path)",
            "    if not os.path.exists(resolved_cache_path):",
            "        add_unique(issues, f\"Cache dependency '{cache_file.name}' is missing at '{resolved_cache_path}'.\")",
            "    elif normalize_path(resolved_cache_path) in supported_attached_cache_paths:",
            "        continue",
            "    else:",
            "        add_unique(warnings, f\"Scene uses external cache file '{cache_file.name}' from '{resolved_cache_path}'. Ensure this cache remains portable for remote rendering.\")",
            "for volume in getattr(bpy.data, 'volumes', []):",
            "    volume_path = str(getattr(volume, 'filepath', '') or '')",
            "    if not volume_path:",
            "        continue",
            "    resolved_volume_path = bpy.path.abspath(volume_path)",
            "    if not os.path.exists(resolved_volume_path):",
            "        add_unique(issues, f\"Volume dependency '{volume.name}' is missing at '{resolved_volume_path}'.\")",
            "    elif normalize_path(resolved_volume_path) in supported_attached_volume_paths:",
            "        continue",
            "    else:",
            "        add_unique(warnings, f\"Scene uses external volume '{volume.name}' from '{resolved_volume_path}'.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'FLUID':",
            "            continue",
            "        domain = getattr(mod, 'domain_settings', None)",
            "        if domain is None:",
            "            continue",
            "        cache_directory = str(getattr(domain, 'cache_directory', '') or '')",
            "        if cache_directory:",
            "            add_unique(issues, f\"Fluid domain '{obj.name}' uses external cache directory '{cache_directory}', which is not portable to remote nodes in the current v1 flow.\")",
            "        if not bool(getattr(domain, 'is_cache_baked_data', False)):",
            "            add_unique(issues, f\"Fluid domain '{obj.name}' requires baked simulation data before remote rendering.\")",
            "        if bool(getattr(domain, 'use_mesh', False)) and not bool(getattr(domain, 'is_cache_baked_mesh', False)):",
            "            add_unique(issues, f\"Fluid domain '{obj.name}' requires baked mesh cache before remote rendering.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'CLOTH':",
            "            continue",
            "        add_unique(issues, f\"Cloth simulation '{obj.name}' is not yet portable to remote rendering in the current v1 flow.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'PARTICLE_SYSTEM':",
            "            continue",
            "        add_unique(issues, f\"Particle simulation '{obj.name}' is not yet portable to remote rendering in the current v1 flow.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'MESH_SEQUENCE_CACHE':",
            "            continue",
            "        add_unique(issues, f\"Geometry cache '{obj.name}' is not yet portable to remote rendering in the current v1 flow.\")",
            "payload = {'isValid': len(issues) == 0, 'issues': issues, 'warnings': warnings}",
            $"print('{startMarker}')",
            "print(json.dumps(payload))",
            $"print('{endMarker}')"
        };

        var scriptPath = Path.Combine(Path.GetTempPath(), $"outwit_validate_blend_{Guid.NewGuid():N}.py");
        await File.WriteAllLinesAsync(scriptPath, pythonLines, cancellationToken);

        (int exitCode, string stdout, string stderr) validationProcessResult;
        try
        {
            var args = $"-b \"{blendFilePath}\" --python-exit-code 1 --python \"{scriptPath}\"";
            validationProcessResult = await RunProcessAsync(args, cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        var (exitCode, stdout, stderr) = validationProcessResult;
        if (exitCode != 0)
        {
            m_logger.LogWarning("Blend validation failed for {BlendFile}: {Stderr}", blendFilePath, stderr);
            return new RenderValidateBlendData
            {
                IsValid = false,
                Issues = [$"Blender could not validate '{blendFilePath}': {stderr}"]
            };
        }

        var startIndex = stdout.IndexOf(startMarker, StringComparison.Ordinal);
        var endIndex = stdout.IndexOf(endMarker, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex)
        {
            m_logger.LogWarning("Blend validation returned no structured diagnostics for {BlendFile}. Stdout: {Stdout}", blendFilePath, stdout);
            return new RenderValidateBlendData
            {
                IsValid = false,
                Issues = ["Blend validation returned no structured diagnostics payload."]
            };
        }

        var json = stdout.Substring(startIndex + startMarker.Length, endIndex - startIndex - startMarker.Length).Trim();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var issues = root.TryGetProperty("issues", out var issuesElement)
            ? issuesElement.EnumerateArray().Select(me => me.GetString() ?? string.Empty).Where(me => !string.IsNullOrWhiteSpace(me)).ToList()
            : [];
        var warnings = root.TryGetProperty("warnings", out var warningsElement)
            ? warningsElement.EnumerateArray().Select(me => me.GetString() ?? string.Empty).Where(me => !string.IsNullOrWhiteSpace(me)).ToList()
            : [];

        return new RenderValidateBlendData
        {
            IsValid = root.GetProperty("isValid").GetBoolean(),
            Issues = issues,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Probes the local Blender runtime for available and selected render backends.
    /// </summary>
    internal async Task<RenderDeviceDiagnostics> GetDeviceDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var args = $"-b --python-exit-code 1 {BuildPythonExecArgument(BuildDeviceConfigurationPython(RenderEngine.Cycles, forceCpuFallback: false))}";
        var (exitCode, stdout, stderr) = await RunProcessAsync(args, cancellationToken);
        var result = ParseInvocationResult(exitCode, stdout, stderr);

        if (result.ExitCode != 0)
        {
            m_logger.LogWarning(
                "Blender device diagnostics probe failed with exit code {ExitCode}: {Stderr}",
                result.ExitCode,
                result.Stderr);
        }

        return CreateDeviceDiagnostics(result);
    }

    /// <summary>
    /// Checks whether the Blender binary exists and is executable.
    /// </summary>
    public bool IsAvailable => File.Exists(m_blenderPath);

    internal string BlenderExecutablePath => m_blenderPath;

    internal ILogger Logger => m_logger;

    #endregion

    #region Tools

    private string BuildFrameArgs(
        string blendFilePath,
        int frame,
        string outputPath,
        RenderOptionsData options,
        RenderTaskData? task,
        bool forceCpuFallback)
    {
        var args = new StringBuilder();
        args.Append($"-b \"{blendFilePath}\"");
        args.Append($" -E {GetBlenderEngineArgument(options.Engine)}");
        args.Append($" -o \"{outputPath}\"");
        args.Append($" -F {FormatToBlenderArg(options.Format)}");

        var pythonLines = new List<string>
        {
            "import bpy",
            "scene = bpy.context.scene"
        };

        pythonLines.AddRange(BuildDeviceConfigurationPython(options.Engine, forceCpuFallback));
        pythonLines.AddRange(BuildImageOutputConfigurationPython(options.Format));
        pythonLines.AddRange(BuildViewLayerRecoveryPython());

        pythonLines.AddRange(BuildEngineConfigurationPython(options));

        if (options.ResolutionX > 0)
            pythonLines.Add($"scene.render.resolution_x = {options.ResolutionX}");

        if (options.ResolutionY > 0)
            pythonLines.Add($"scene.render.resolution_y = {options.ResolutionY}");

        if (options.ResolutionX > 0 || options.ResolutionY > 0)
            pythonLines.Add("scene.render.resolution_percentage = 100");

        if (task != null && !task.IsFullFrame)
        {
            pythonLines.Add("scene.render.use_border = True");
            pythonLines.Add("scene.render.use_crop_to_border = True");
            pythonLines.Add($"scene.render.border_min_x = {task.EffectiveRenderMinX.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            pythonLines.Add($"scene.render.border_max_x = {task.EffectiveRenderMaxX.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            pythonLines.Add($"scene.render.border_min_y = {task.EffectiveRenderMinY.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            pythonLines.Add($"scene.render.border_max_y = {task.EffectiveRenderMaxY.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        else
        {
            pythonLines.Add("scene.render.use_border = False");
            pythonLines.Add("scene.render.use_crop_to_border = False");
        }

        args.Append(' ');
        args.Append(BuildPythonExecArgument(pythonLines));

        args.Append($" -f {frame}");

        return args.ToString();
    }

    private async Task<RenderBlenderInvocationResult> RunRenderAttemptAsync(
        string blendFilePath,
        int frame,
        string outputPath,
        RenderOptionsData options,
        RenderTaskData? task,
        bool forceCpuFallback,
        CancellationToken cancellationToken)
    {
        var args = BuildFrameArgs(blendFilePath, frame, outputPath, options, task, forceCpuFallback);
        m_logger.LogDebug("Blender args: {Args}", args);

        return await RunBlenderAsync(args, cancellationToken);
    }

    private async Task<RenderBlenderInvocationResult> RunBlenderAsync(string args, CancellationToken cancellationToken)
    {
        var (exitCode, stdout, stderr) = await RunProcessAsync(args, cancellationToken);
        var result = ParseInvocationResult(exitCode, stdout, stderr);

        LogInvocationDiagnostics(result);

        return result;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string args, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = m_blenderPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
            throw;
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static IReadOnlyList<string> BuildDeviceConfigurationPython(RenderEngine engine, bool forceCpuFallback)
    {
        if (engine != RenderEngine.Cycles)
        {
            return
            [
                $"scene.render.engine = '{GetBlenderEngineArgument(engine)}'",
                "print('OUTWIT_RENDER_AVAILABLE=')",
                $"print('OUTWIT_RENDER_BACKEND={GetBlenderEngineArgument(engine)}')",
                $"print('OUTWIT_RENDER_MESSAGE=Using {GetRenderEngineDisplayName(engine)} render path')"
            ];
        }

        if (forceCpuFallback)
        {
            return
            [
                "scene.render.engine = 'CYCLES'",
                "scene.cycles.device = 'CPU'",
                "print('OUTWIT_RENDER_AVAILABLE=')",
                "print('OUTWIT_RENDER_BACKEND=CPU')",
                "print('OUTWIT_RENDER_MESSAGE=Forced CPU fallback after GPU render failure')"
            ];
        }

        var candidateLiteral = "[" + string.Join(", ", GPU_BACKEND_CANDIDATES.Select(ToPythonStringLiteral)) + "]";

        return
        [
            "scene.render.engine = 'CYCLES'",
            "scene.cycles.device = 'CPU'",
            "prefs = bpy.context.preferences.addons['cycles'].preferences if 'cycles' in bpy.context.preferences.addons else None",
            "selected_backend = 'CPU'",
            "def _outwit_refresh_devices():",
            "    if prefs is None:",
            "        return []",
            "    refresh = getattr(prefs, 'refresh_devices', None)",
            "    if callable(refresh):",
            "        refresh()",
            "    else:",
            "        get_devices = getattr(prefs, 'get_devices', None)",
            "        if callable(get_devices):",
            "            get_devices()",
            "    return list(getattr(prefs, 'devices', []))",
            "def _outwit_try_backend(backend):",
            "    if prefs is None:",
            "        return False",
            "    try:",
            "        prefs.compute_device_type = backend",
            "        devices = _outwit_refresh_devices()",
            "        gpu_devices = [device for device in devices if getattr(device, 'type', 'CPU') != 'CPU']",
            "        if len(gpu_devices) == 0:",
            "            return False",
            "        for device in devices:",
            "            device.use = getattr(device, 'type', 'CPU') != 'CPU'",
            "        scene.cycles.device = 'GPU'",
            "        return True",
            "    except Exception:",
            "        return False",
            "available_backends = []",
            $"for backend in {candidateLiteral}:",
            "    if prefs is None:",
            "        break",
            "    try:",
            "        prefs.compute_device_type = backend",
            "        devices = _outwit_refresh_devices()",
            "        gpu_devices = [device for device in devices if getattr(device, 'type', 'CPU') != 'CPU']",
            "        if len(gpu_devices) > 0:",
            "            available_backends.append(backend)",
            "    except Exception:",
            "        pass",
            $"for backend in {candidateLiteral}:",
            "    if _outwit_try_backend(backend):",
            "        selected_backend = backend",
            "        break",
            "selection_message = ('Auto-selected ' + selected_backend) if selected_backend != 'CPU' else ('No GPU backend available; falling back to CPU' if len(available_backends) == 0 else 'GPU backend probe succeeded but Blender still fell back to CPU')",
            "print('OUTWIT_RENDER_AVAILABLE=' + ','.join(available_backends))",
            "print('OUTWIT_RENDER_BACKEND=' + str(selected_backend))",
            "print('OUTWIT_RENDER_MESSAGE=' + selection_message)"
        ];
    }

    private static IReadOnlyList<string> BuildEngineConfigurationPython(RenderOptionsData options)
    {
        var pythonLines = new List<string>();

        if (options.Samples > 0)
        {
            switch (options.Engine)
            {
                case RenderEngine.Cycles:
                    pythonLines.Add($"scene.cycles.samples = {options.Samples}");
                    break;
                case RenderEngine.Eevee:
                case RenderEngine.GreasePencil:
                    pythonLines.Add("eevee = getattr(scene, 'eevee', None)");
                    pythonLines.Add("if eevee is not None:");
                    pythonLines.Add($"    setattr(eevee, 'taa_render_samples', {options.Samples}) if hasattr(eevee, 'taa_render_samples') else None");
                    pythonLines.Add($"    setattr(eevee, 'taa_samples', {options.Samples}) if hasattr(eevee, 'taa_samples') else None");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.Engine), options.Engine, null);
            }
        }

        if (options.Denoise)
        {
            switch (options.Engine)
            {
                case RenderEngine.Cycles:
                    pythonLines.Add("scene.cycles.use_denoising = True");
                    break;
                case RenderEngine.Eevee:
                case RenderEngine.GreasePencil:
                    pythonLines.Add("eevee = getattr(scene, 'eevee', None)");
                    pythonLines.Add("if eevee is not None and hasattr(eevee, 'use_taa_reprojection'):");
                    pythonLines.Add("    eevee.use_taa_reprojection = True");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.Engine), options.Engine, null);
            }
        }

        return pythonLines;
    }

    private static string GetBlenderEngineArgument(RenderEngine engine)
    {
        return engine switch
        {
            RenderEngine.Cycles => "CYCLES",
            RenderEngine.Eevee => "BLENDER_EEVEE_NEXT",
            RenderEngine.GreasePencil => "BLENDER_EEVEE_NEXT",
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };
    }

    private static string GetRenderEngineDisplayName(RenderEngine engine)
    {
        return engine switch
        {
            RenderEngine.Cycles => "Cycles",
            RenderEngine.Eevee => "Eevee/Eevee Next",
            RenderEngine.GreasePencil => "Grease Pencil",
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };
    }

    private static string BuildPythonExecArgument(IReadOnlyList<string> pythonLines)
    {
        var pythonScript = string.Join("\n", pythonLines);
        return $"--python-expr \"exec({ToPythonStringLiteral(pythonScript)})\"";
    }

    private static IReadOnlyList<string> LoadSupportedAttachedPaths(string blendFilePath, string kind)
    {
        var manifestPath = blendFilePath + ".attachments.json";
        if (!File.Exists(manifestPath))
            return [];

        var sceneDirectory = Path.GetDirectoryName(blendFilePath);
        if (string.IsNullOrWhiteSpace(sceneDirectory))
            return [];

        try
        {
            var json = File.ReadAllText(manifestPath);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var currentKind = element.TryGetProperty("Kind", out var kindElement) ? kindElement.GetString() : null;
                var packagingStrategy = element.TryGetProperty("PackagingStrategy", out var packagingElement) ? packagingElement.GetString() : null;
                var relativePath = element.TryGetProperty("RelativePath", out var relativePathElement) ? relativePathElement.GetString() : null;

                if (!string.Equals(currentKind, kind, StringComparison.Ordinal)
                    || !string.Equals(packagingStrategy, "SceneAttachmentBlob", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                result.Add(Path.GetFullPath(Path.Combine(sceneDirectory, relativePath)));
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static string BuildPythonListLiteral(IReadOnlyList<string> values)
    {
        return "[" + string.Join(", ", values.Select(ToPythonStringLiteral)) + "]";
    }

    private RenderBlenderInvocationResult ParseInvocationResult(int exitCode, string stdout, string stderr)
    {
        return new RenderBlenderInvocationResult
        {
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr,
            SelectedRenderBackend = ParseSelectedRenderDevice(stdout),
            SelectionMessage = ParseMarker(stdout, SELECTION_MESSAGE_PREFIX)
        };
    }

    private RenderDeviceDiagnostics CreateDeviceDiagnostics(RenderBlenderInvocationResult result)
    {
        return new RenderDeviceDiagnostics
        {
            AvailableRenderBackends = ParseAvailableRenderBackends(result.Stdout),
            SelectedRenderBackend = result.SelectedRenderBackend,
            SelectionMessage = result.SelectionMessage,
            UsesGpuForRendering = IsGpuBackend(result.SelectedRenderBackend)
        };
    }

    private void LogInvocationDiagnostics(RenderBlenderInvocationResult result)
    {
        if (result.SelectedRenderBackend.HasValue)
            m_logger.LogDebug("Blender selected render backend: {RenderDevice}", result.SelectedRenderBackend.Value);

        if (!string.IsNullOrWhiteSpace(result.SelectionMessage))
            m_logger.LogDebug("Blender render backend selection: {Message}", result.SelectionMessage);

        if (!string.IsNullOrWhiteSpace(result.Stdout))
            m_logger.LogDebug("Blender stdout: {Stdout}", result.Stdout.Trim());
    }

    private void EnsureSuccessfulRender(RenderBlenderInvocationResult result)
    {
        if (result.ExitCode == 0)
            return;

        m_logger.LogError("Blender failed (exit code {ExitCode}):\nstdout: {Stdout}\nstderr: {Stderr}",
            result.ExitCode, result.Stdout, result.Stderr);

        var failureMessage = BuildFailureMessage(result);
        throw new InvalidOperationException(
            $"Blender render failed with exit code {result.ExitCode}: {failureMessage}");
    }

    private static string BuildFailureMessage(RenderBlenderInvocationResult result)
    {
        var stdoutSummary = ExtractFailureSummary(result.Stdout);
        var stderrSummary = ExtractFailureSummary(result.Stderr);

        if (!string.IsNullOrWhiteSpace(stdoutSummary))
        {
            if (string.IsNullOrWhiteSpace(stderrSummary)
                || stderrSummary.Contains("Unable to find the Python binary", StringComparison.OrdinalIgnoreCase))
            {
                return stdoutSummary;
            }

            if (string.Equals(stdoutSummary, stderrSummary, StringComparison.OrdinalIgnoreCase))
                return stdoutSummary;

            return $"{stdoutSummary} | stderr: {stderrSummary}";
        }

        return string.IsNullOrWhiteSpace(stderrSummary)
            ? "Blender reported a non-zero exit code without diagnostics."
            : stderrSummary;
    }

    private static string? ExtractFailureSummary(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(me => me.Trim())
            .Where(me => !string.IsNullOrWhiteSpace(me))
            .ToArray();

        if (lines.Length == 0)
            return null;

        foreach (var line in lines)
        {
            var reportsIndex = line.IndexOf("| ERROR ", StringComparison.OrdinalIgnoreCase);
            if (reportsIndex >= 0)
                return line[(reportsIndex + "| ERROR ".Length)..].Trim();
        }

        var blenderErrorLine = lines.FirstOrDefault(me => me.Contains("error", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(blenderErrorLine))
            return NormalizeFailureLine(blenderErrorLine);

        return NormalizeFailureLine(lines[^1]);
    }

    private static string NormalizeFailureLine(string line)
    {
        var normalized = line.Trim();
        var blenderPrefixIndex = normalized.IndexOf(" : ", StringComparison.Ordinal);
        if (blenderPrefixIndex > 0 && normalized[..blenderPrefixIndex].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[(blenderPrefixIndex + 3)..].Trim();

        return normalized;
    }

    private static bool ShouldRetryWithCpuFallback(RenderBlenderInvocationResult result)
    {
        return result.ExitCode != 0 && IsGpuBackend(result.SelectedRenderBackend);
    }

    private static bool IsGpuBackend(RenderDevice? device)
    {
        return device is RenderDevice.CUDA or RenderDevice.OPTIX or RenderDevice.HIP or RenderDevice.METAL;
    }

    private static void DeleteRenderedOutputs(string outputBasePath, int frame, RenderFormat format)
    {
        var renderedPath = FindRenderedFile(outputBasePath, frame, format);
        if (renderedPath != null && File.Exists(renderedPath))
        {
            try
            {
                File.Delete(renderedPath);
            }
            catch
            {
            }
        }
    }

    private static string[] ParseAvailableRenderBackends(string stdout)
    {
        var value = ParseMarker(stdout, AVAILABLE_BACKENDS_PREFIX);
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static RenderDevice? ParseSelectedRenderDevice(string stdout)
    {
        var value = ParseMarker(stdout, SELECTED_BACKEND_PREFIX);
        if (Enum.TryParse<RenderDevice>(value, ignoreCase: true, out var device))
            return device;

        return null;
    }

    private static string? ParseMarker(string stdout, string prefix)
    {
        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..].Trim();
        }

        return null;
    }

    private static string? FindRenderedFile(string outputBasePath, int frame, RenderFormat format)
    {
        var ext = FormatToExtension(format);
        var dir = Path.GetDirectoryName(outputBasePath) ?? ".";
        var baseName = Path.GetFileName(outputBasePath);

        // Blender appends frame number as 4-digit suffix: output0001.png
        var candidates = new[]
        {
            $"{outputBasePath}{frame:D4}{ext}",
            Path.Combine(dir, $"{baseName}{frame:D4}{ext}"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IReadOnlyList<string> BuildImageOutputConfigurationPython(RenderFormat format)
    {
        return format switch
        {
            RenderFormat.PNG => ["scene.render.image_settings.color_mode = 'RGB'"],
            RenderFormat.JPEG => ["scene.render.image_settings.color_mode = 'RGB'"],
            _ => []
        };
    }

    private static IReadOnlyList<string> BuildViewLayerRecoveryPython()
    {
        return
        [
            "view_layers = list(scene.view_layers)",
            "if len(view_layers) > 0 and not any(bool(getattr(layer, 'use', True)) for layer in view_layers):",
            "    for layer in view_layers:",
            "        layer.use = True",
            "    print('OUTWIT_RENDER_RECOVERY=All view layers were disabled; temporarily enabled them for this render')"
        ];
    }

    private static string ToPythonStringLiteral(string value)
    {
        return $"'{value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", string.Empty).Replace("\n", "\\n")}'";
    }

    private static string FormatToBlenderArg(RenderFormat format)
    {
        return format switch
        {
            RenderFormat.PNG => "PNG",
            RenderFormat.EXR => "OPEN_EXR",
            RenderFormat.JPEG => "JPEG",
            _ => "PNG"
        };
    }

    private static string FormatToExtension(RenderFormat format)
    {
        return format switch
        {
            RenderFormat.PNG => ".png",
            RenderFormat.EXR => ".exr",
            RenderFormat.JPEG => ".jpg",
            _ => ".png"
        };
    }

    #endregion
}
