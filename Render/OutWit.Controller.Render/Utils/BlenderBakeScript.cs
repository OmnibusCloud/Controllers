using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Builds the Blender Python script that bakes a scene's sequential simulation (v1: Mantaflow fluid/gas
/// domains) into a per-frame, frame-addressable OpenVDB cache, then reports the produced cache files so the
/// controller can transport each frame's slice to the node that renders it. Runs on a single delegated node
/// (Grid.Delegate) against the already-open .blend (Blender is launched with <c>-b &lt;blend&gt;</c>), so the
/// script uses <c>bpy.data.filepath</c> rather than an embedded path.
///
/// Mirrors the proven headless bake recipe: force <c>cache_data_format='OPENVDB'</c> + <c>cache_type='ALL'</c>
/// (default REPLAY bakes a single modal frame headless), save the mainfile so <c>//</c> resolves, bake each
/// domain via <c>fluid.bake_all()</c> under a temp-override, save again so the baked state + relative cache
/// directory persist, then enumerate the *.vdb files and emit a manifest (RelativePath relative to the blend
/// directory + the frame parsed from the filename) between markers on stdout.
///
/// Stateless — the Blender invocation itself stays on <see cref="BlenderRunner"/>.
/// </summary>
internal static class BlenderBakeScript
{
    public const string START_MARKER = "OUTWIT_BAKE_SIMULATION_START";
    public const string END_MARKER = "OUTWIT_BAKE_SIMULATION_END";

    /// <summary>
    /// Builds the bake script. <paramref name="startFrame"/>/<paramref name="endFrame"/> are the render range
    /// the bake must cover; the bake itself starts no later than the sim's configured start so frame N's state
    /// is physically correct. <paramref name="resolutionMax"/> &gt; 0 overrides the domain resolution_max.
    /// </summary>
    public static IReadOnlyList<string> BuildScript(int startFrame, int endFrame, int resolutionMax)
    {
        return new List<string>
        {
            "import bpy, os, re, json",
            $"START_FRAME = {startFrame}",
            $"END_FRAME = {endFrame}",
            $"RESOLUTION_MAX = {resolutionMax}",
            "result = {'BakedDomains': 0, 'Cache': [], 'Errors': []}",
            "blend_path = bpy.data.filepath",
            "blend_dir = os.path.dirname(blend_path)",
            "def rel_to_blend(full):",
            "    return os.path.relpath(full, blend_dir).replace('\\\\', '/')",
            "def frame_of(name):",
            "    m = re.search(r'_(\\d+)\\.vdb$', name)",
            "    return int(m.group(1)) if m else None",
            // Discover fluid domains.
            "domains = []",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if getattr(mod, 'type', '') == 'FLUID' and getattr(mod, 'fluid_type', '') == 'DOMAIN':",
            "            ds = getattr(mod, 'domain_settings', None)",
            "            if ds is not None:",
            "                domains.append((obj, ds))",
            "if not domains:",
            "    result['Errors'].append('No fluid domain found to bake.')",
            // Configure + bake each domain.
            "for obj, ds in domains:",
            "    try:",
            "        cache_dir = ds.cache_directory or ''",
            "        if (not cache_dir) or (not cache_dir.startswith('//')):",
            "            ds.cache_directory = '//cache_' + re.sub(r'[^A-Za-z0-9_]', '_', obj.name)",
            "        ds.cache_data_format = 'OPENVDB'",
            "        if hasattr(ds, 'cache_mesh_format'):",
            "            ds.cache_mesh_format = 'OPENVDB'",
            "        if hasattr(ds, 'cache_noise_format'):",
            "            ds.cache_noise_format = 'OPENVDB'",
            "        ds.cache_type = 'ALL'",
            "        existing_start = int(getattr(ds, 'cache_frame_start', 1) or 1)",
            "        existing_end = int(getattr(ds, 'cache_frame_end', END_FRAME) or END_FRAME)",
            "        ds.cache_frame_start = max(1, min(existing_start, START_FRAME))",
            "        ds.cache_frame_end = max(existing_end, END_FRAME)",
            "        if RESOLUTION_MAX > 0:",
            "            ds.resolution_max = RESOLUTION_MAX",
            "    except Exception as cfg_err:",
            "        result['Errors'].append('Configure ' + obj.name + ': ' + str(cfg_err))",
            // Save so // resolves to the blend dir, then bake.
            "try:",
            "    bpy.ops.wm.save_mainfile()",
            "except Exception as save_err:",
            "    result['Errors'].append('Pre-bake save: ' + str(save_err))",
            "for obj, ds in domains:",
            "    try:",
            "        with bpy.context.temp_override(active_object=obj, selected_objects=[obj], object=obj):",
            "            bpy.ops.fluid.bake_all()",
            "        baked = bool(getattr(ds, 'has_cache_baked_data', getattr(ds, 'is_cache_baked_data', False)))",
            "        if baked:",
            "            result['BakedDomains'] += 1",
            "        else:",
            "            result['Errors'].append('Bake produced no cache for ' + obj.name)",
            "    except Exception as bake_err:",
            "        result['Errors'].append('Bake ' + obj.name + ': ' + str(bake_err))",
            // Persist baked state + relative cache directory.
            "try:",
            "    bpy.ops.wm.save_mainfile()",
            "except Exception as save_err2:",
            "    result['Errors'].append('Post-bake save: ' + str(save_err2))",
            // Enumerate produced VDB cache files.
            "seen = set()",
            "for obj, ds in domains:",
            "    cache_dir = ds.cache_directory",
            "    if cache_dir.startswith('//'):",
            "        cache_dir = os.path.join(blend_dir, cache_dir[2:])",
            "    if not os.path.isdir(cache_dir):",
            "        continue",
            "    for root, _dirs, files in os.walk(cache_dir):",
            "        for fn in files:",
            "            if not fn.lower().endswith('.vdb'):",
            "                continue",
            "            full = os.path.join(root, fn)",
            "            rel = rel_to_blend(full)",
            "            if rel in seen:",
            "                continue",
            "            seen.add(rel)",
            "            result['Cache'].append({'RelativePath': rel, 'OriginalPath': '//' + rel, 'Frame': frame_of(fn)})",
            $"print('{START_MARKER}')",
            "print(json.dumps(result))",
            $"print('{END_MARKER}')",
        };
    }

    /// <summary>Parses the manifest JSON the bake script prints between the markers.</summary>
    public static RenderBakeScriptResult ParseResult(string stdout, ILogger logger)
    {
        try
        {
            var start = stdout.IndexOf(START_MARKER, StringComparison.Ordinal);
            var end = stdout.IndexOf(END_MARKER, StringComparison.Ordinal);
            if (start < 0 || end < 0 || end <= start)
                throw new InvalidOperationException("Bake script produced no manifest markers.");

            var jsonStart = start + START_MARKER.Length;
            var json = stdout[jsonStart..end].Trim();
            var parsed = JsonSerializer.Deserialize<RenderBakeScriptResult>(json, JsonOptions)
                         ?? throw new InvalidOperationException("Bake manifest deserialized to null.");
            return parsed;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to parse bake manifest. stdout tail: {Tail}",
                stdout.Length <= 800 ? stdout : stdout[^800..]);
            throw;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

/// <summary>Parsed result of the Blender bake script.</summary>
internal sealed class RenderBakeScriptResult
{
    public int BakedDomains { get; set; }

    public List<RenderBakeCacheEntry> Cache { get; set; } = [];

    public List<string> Errors { get; set; } = [];
}

/// <summary>One baked cache file produced by the bake script.</summary>
internal sealed class RenderBakeCacheEntry
{
    public string RelativePath { get; set; } = string.Empty;

    public string OriginalPath { get; set; } = string.Empty;

    public int? Frame { get; set; }
}
