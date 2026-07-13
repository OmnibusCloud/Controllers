using System.Text.Json;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Pure helpers that build the Blender Python validation script and parse its
/// JSON output for <see cref="BlenderRunner.ValidateBlendDetailedAsync"/>. The
/// script enumerates every external dependency declared in the scene's data
/// blocks (libraries, images, fonts, movie clips, sounds, cache files,
/// volumes, VSE strips) and reports missing files as issues, present-but-not-
/// inlined files as warnings, and flags fluid / cloth / particle / mesh-cache
/// modifiers that the current v1 distributed-render flow cannot transport
/// portably.
///
/// Stateless — extracted from BlenderRunner to keep that orchestrator under
/// the 600-line readability ceiling. The Blender invocation itself stays on
/// BlenderRunner (it owns the process and blender-path).
/// </summary>
internal static class BlenderValidationScript
{
    public const string START_MARKER = "OUTWIT_VALIDATE_BLEND_START";
    public const string END_MARKER = "OUTWIT_VALIDATE_BLEND_END";

    public static IReadOnlyList<string> BuildScript(string blendFilePath)
    {
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
        var supportedAttachedFluidCachePaths = LoadSupportedAttachedPaths(blendFilePath, "FluidCache");
        var supportedAttachedImageSequenceDirectories = supportedAttachedImageSequenceFramePaths
            .Select(Path.GetDirectoryName)
            .Where(me => !string.IsNullOrWhiteSpace(me))
            .Select(me => me!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new List<string>
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
            "def node_group_has_simulation_zone(node_group, visited):",
            "    if node_group is None or id(node_group) in visited:",
            "        return False",
            "    visited.add(id(node_group))",
            "    for node in getattr(node_group, 'nodes', []):",
            "        if getattr(node, 'bl_idname', '') == 'GeometryNodeSimulationOutput':",
            "            return True",
            "        nested_group = getattr(node, 'node_group', None)",
            "        if nested_group is not None and node_group_has_simulation_zone(nested_group, visited):",
            "            return True",
            "    return False",
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
            $"supported_attached_fluid_cache_paths = set(normalize_path(path) for path in {BuildPythonListLiteral(supportedAttachedFluidCachePaths)})",
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
            "        resolved_cache_dir = bpy.path.abspath(cache_directory) if cache_directory else ''",
            "        cache_dir_prefix = (normalize_path(resolved_cache_dir) + os.sep) if resolved_cache_dir else ''",
            "        baked = bool(getattr(domain, 'has_cache_baked_data', getattr(domain, 'is_cache_baked_data', False)))",
            "        mesh_baked = bool(getattr(domain, 'has_cache_baked_mesh', getattr(domain, 'is_cache_baked_mesh', False)))",
            // A REPLAY-mode bake (Render.BakeSimulation + the addon's local-bake path) writes real per-frame
            // cache DATA files but leaves has_cache_baked_data/mesh False. Trust the files on disk: a domain
            // whose cache holds DATA (.vdb / .bobj.gz) for its LAST configured frame (cache_frame_end) is
            // baked through the range. Keying on the END frame avoids false positives from (a) the single
            // current-frame slice Blender writes for an UNBAKED REPLAY domain on save, (b) stray scrubbed
            // frames, and (c) config '*.uni' files that exist without any bake.
            "        end_frame = int(getattr(domain, 'cache_frame_end', 0) or 0)",
            "        cache_has_data = False",
            "        mesh_has_data = False",
            "        if end_frame > 0 and resolved_cache_dir and os.path.isdir(resolved_cache_dir):",
            "            for cache_root, _cache_dirs, cache_files in os.walk(resolved_cache_dir):",
            "                is_mesh_dir = os.path.basename(cache_root).lower() == 'mesh'",
            "                for cache_file in cache_files:",
            "                    low = cache_file.lower()",
            "                    if not (low.endswith('.vdb') or low.endswith('.bobj.gz')):",
            "                        continue",
            "                    frame_match = re.search(r'_(\\d+)\\.', cache_file)",
            "                    if frame_match and int(frame_match.group(1)) == end_frame:",
            "                        cache_has_data = True",
            "                        if is_mesh_dir:",
            "                            mesh_has_data = True",
            "        baked = baked or cache_has_data",
            "        mesh_baked = mesh_baked or mesh_has_data",
            "        cache_attached = bool(cache_dir_prefix) and any(p.startswith(cache_dir_prefix) for p in supported_attached_fluid_cache_paths)",
            "        mesh_baked_ok = (not bool(getattr(domain, 'use_mesh', False))) or mesh_baked",
            "        if baked and cache_attached and mesh_baked_ok:",
            "            continue",
            "        if cache_directory and not cache_attached:",
            "            add_unique(issues, f\"Fluid domain '{obj.name}' uses external cache directory '{cache_directory}', which is not portable to remote nodes. Bake it and attach the cache, then re-submit.\")",
            "        if not baked:",
            "            add_unique(issues, f\"Fluid domain '{obj.name}' requires baked simulation data before remote rendering.\")",
            "        if bool(getattr(domain, 'use_mesh', False)) and not mesh_baked:",
            "            add_unique(issues, f\"Fluid domain '{obj.name}' requires baked mesh cache before remote rendering.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'CLOTH':",
            "            continue",
            "        add_unique(issues, f\"Cloth simulation '{obj.name}' is not yet portable to remote rendering in the current v1 flow.\")",
            "for obj in bpy.data.objects:",
            "    for psys in getattr(obj, 'particle_systems', []):",
            "        settings = getattr(psys, 'settings', None)",
            "        particle_type = str(getattr(settings, 'type', '') or '') if settings is not None else ''",
            "        uses_hair_dynamics = bool(getattr(psys, 'use_hair_dynamics', False)) or getattr(psys, 'cloth', None) is not None",
            "        if particle_type == 'HAIR' and not uses_hair_dynamics:",
            "            continue",
            "        if particle_type == 'HAIR':",
            "            add_unique(issues, f\"Dynamic hair simulation '{obj.name}' (particle system '{psys.name}') is not yet portable to remote rendering in the current v1 flow. Bake it to Alembic and attach the cache, then re-submit.\")",
            "        else:",
            "            add_unique(issues, f\"Particle simulation '{obj.name}' (particle system '{psys.name}') is not yet portable to remote rendering in the current v1 flow. Bake it to Alembic and attach the cache, then re-submit.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'MESH_SEQUENCE_CACHE':",
            "            continue",
            "        mod_cache_file = getattr(mod, 'cache_file', None)",
            "        mod_cache_path = str(getattr(mod_cache_file, 'filepath', '') or '') if mod_cache_file is not None else ''",
            "        resolved_mod_cache_path = bpy.path.abspath(mod_cache_path) if mod_cache_path else ''",
            "        if resolved_mod_cache_path and normalize_path(resolved_mod_cache_path) in supported_attached_cache_paths:",
            "            continue",
            "        add_unique(issues, f\"Geometry cache '{obj.name}' is not yet portable to remote rendering in the current v1 flow. Attach its Alembic/USD cache and re-submit.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'SOFT_BODY':",
            "            continue",
            "        add_unique(issues, f\"Soft body simulation '{obj.name}' is not yet portable to remote rendering in the current v1 flow. Bake it to a cache and re-submit.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'DYNAMIC_PAINT':",
            "            continue",
            "        add_unique(issues, f\"Dynamic Paint simulation '{obj.name}' is not yet portable to remote rendering in the current v1 flow. Bake it to a cache and re-submit.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'MESH_CACHE':",
            "            continue",
            "        add_unique(issues, f\"Mesh cache modifier '{obj.name}' is not yet portable to remote rendering in the current v1 flow. Export to Alembic and attach the cache, then re-submit.\")",
            "for obj in bpy.data.objects:",
            "    if getattr(obj, 'rigid_body', None) is not None:",
            "        add_unique(issues, f\"Rigid body simulation '{obj.name}' is not yet portable to remote rendering in the current v1 flow. Bake it to keyframes or Alembic and re-submit.\")",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if mod.type != 'NODES':",
            "            continue",
            "        if node_group_has_simulation_zone(getattr(mod, 'node_group', None), set()):",
            "            add_unique(issues, f\"Geometry nodes simulation zone on '{obj.name}' is not yet portable to remote rendering in the current v1 flow. Bake it to a cache or Alembic and re-submit.\")",
            "payload = {'isValid': len(issues) == 0, 'issues': issues, 'warnings': warnings}",
            $"print('{START_MARKER}')",
            "print(json.dumps(payload))",
            $"print('{END_MARKER}')"
        };
    }

    public static RenderValidateBlendData ParseResult(
        string blendFilePath,
        int exitCode,
        string stdout,
        string stderr,
        ILogger logger)
    {
        if (exitCode != 0)
        {
            logger.LogWarning("Blend validation failed for {BlendFile}: {Stderr}", blendFilePath, stderr);
            return new RenderValidateBlendData
            {
                IsValid = false,
                Issues = [$"Blender could not validate '{blendFilePath}': {stderr}"]
            };
        }

        var startIndex = stdout.IndexOf(START_MARKER, StringComparison.Ordinal);
        var endIndex = stdout.IndexOf(END_MARKER, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex)
        {
            logger.LogWarning("Blend validation returned no structured diagnostics for {BlendFile}. Stdout: {Stdout}", blendFilePath, stdout);
            return new RenderValidateBlendData
            {
                IsValid = false,
                Issues = ["Blend validation returned no structured diagnostics payload."]
            };
        }

        var json = stdout.Substring(startIndex + START_MARKER.Length, endIndex - startIndex - START_MARKER.Length).Trim();
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
        return "[" + string.Join(", ", values.Select(BlenderRenderArgsBuilder.ToPythonStringLiteral)) + "]";
    }
}
