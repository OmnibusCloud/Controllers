using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Builds the Blender Python script that bakes a scene's sequential simulation (v1: Mantaflow fluid —
/// gas/smoke and liquid domains) into a per-frame, frame-addressable cache, then reports every produced
/// cache file so the controller can transport each frame's slice to the node that renders it. Gas renders
/// from the OpenVDB density grid; liquid renders the surface mesh (.bobj.gz) — both are collected. Runs on
/// a single delegated node
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
            "result = {'BakedDomains': 0, 'BakedPointCaches': 0, 'Cache': [], 'Errors': []}",
            "blend_path = bpy.data.filepath",
            "blend_dir = os.path.dirname(blend_path)",
            "def rel_to_blend(full):",
            "    return os.path.relpath(full, blend_dir).replace('\\\\', '/')",
            "def frame_of(name):",
            "    m = re.search(r'_(\\d+)\\.', name)",
            "    return int(m.group(1)) if m else None",
            // Discover fluid domains.
            "domains = []",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if getattr(mod, 'type', '') == 'FLUID' and getattr(mod, 'fluid_type', '') == 'DOMAIN':",
            "            ds = getattr(mod, 'domain_settings', None)",
            "            if ds is not None:",
            "                domains.append((obj, ds))",
            // No fluid domain is fine — the scene may have only point-cache sims (cloth, particles, …),
            // baked below. 'nothing baked at all' is detected by the adapter from the baked counts.
            // Configure + bake each domain.
            "for obj, ds in domains:",
            "    try:",
            "        cache_dir = ds.cache_directory or ''",
            // Force a clean in-blend-dir cache: empty, absolute (non-'//'), or escaping ('..', e.g. a
            // '//../scenes/cache' left by a save-to-new-location) would resolve outside the node working dir
            // and be rejected by the materialize path-traversal guard.
            "        if (not cache_dir) or (not cache_dir.startswith('//')) or ('..' in cache_dir):",
            "            ds.cache_directory = '//cache_' + re.sub(r'[^A-Za-z0-9_]', '_', obj.name)",
            // Critical settings first: the density grid must be OpenVDB and cache_type ALL so a headless
            // bake writes EVERY frame (default REPLAY/modal writes only one). These must not be skipped.
            "        ds.cache_data_format = 'OPENVDB'",
            "        ds.cache_type = 'ALL'",
            "        existing_start = int(getattr(ds, 'cache_frame_start', 1) or 1)",
            "        existing_end = int(getattr(ds, 'cache_frame_end', END_FRAME) or END_FRAME)",
            // Start at the sim's natural start (a liquid mesh only displays when the cache is contiguous from
            // its start), but bake only up to the requested END — never the scene's full configured range.
            "        ds.cache_frame_start = max(1, min(existing_start, START_FRAME))",
            "        ds.cache_frame_end = max(ds.cache_frame_start, END_FRAME)",
            "        if RESOLUTION_MAX > 0:",
            "            ds.resolution_max = RESOLUTION_MAX",
            // Best-effort: store the noise grid (if used) as OpenVDB too. cache_mesh_format is intentionally
            // NOT touched — it only accepts BOBJECT/OBJECT (mesh geometry), not a grid format.
            "        try:",
            "            if hasattr(ds, 'cache_noise_format'):",
            "                ds.cache_noise_format = 'OPENVDB'",
            "        except Exception:",
            "            pass",
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
            // Bake non-fluid point-cache sims (cloth, soft body, dynamic particles, dynamic paint, rigid
            // body) to MEMORY, which is embedded in the .blend on save -> the baked scene is self-contained
            // for these (no per-frame attachments needed; each node renders its frame from the embedded
            // cache). Count objects whose cache ends up baked so a sim-only (no fluid) scene still succeeds.
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        pc0 = getattr(mod, 'point_cache', None)",
            "        if pc0 is not None:",
            "            try:",
            "                pc0.use_disk_cache = False",
            "            except Exception:",
            "                pass",
            // Free any existing point-cache bake FIRST. A scene may arrive already-baked (Blender demos
            // ship baked) or with a stale/invalidated cache; calling bake_all on a flagged-but-stale cache
            // leaves it FROZEN (the sim never re-steps). Freeing forces a clean full re-bake. No-op when
            // truly unbaked.
            "try:",
            "    bpy.ops.ptcache.free_bake_all()",
            "except Exception as ptc_free_err:",
            "    result['Errors'].append('ptcache.free_bake_all: ' + str(ptc_free_err))",
            "try:",
            "    bpy.ops.ptcache.bake_all(bake=True)",
            "except Exception as ptc_err:",
            "    result['Errors'].append('ptcache.bake_all: ' + str(ptc_err))",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        pc1 = getattr(mod, 'point_cache', None)",
            "        if pc1 is not None and getattr(pc1, 'is_baked', False):",
            "            result['BakedPointCaches'] += 1",
            "    for ps in getattr(obj, 'particle_systems', []):",
            "        if getattr(getattr(ps, 'point_cache', None), 'is_baked', False):",
            "            result['BakedPointCaches'] += 1",
            // Bake Geometry Nodes simulation zones (a NODES modifier whose 'bakes' collection is non-empty)
            // to PACKED, which embeds the bake in the .blend on save -> self-contained, like the point-cache
            // memory path. ANIMATION mode bakes the whole range. simulation_nodes_cache_bake needs the object
            // active + selected.
            "gn_objs = []",
            "for obj in bpy.data.objects:",
            "    for mod in getattr(obj, 'modifiers', []):",
            "        if getattr(mod, 'type', '') == 'NODES' and getattr(mod, 'bakes', None) and len(mod.bakes) > 0:",
            "            try:",
            "                mod.bake_target = 'PACKED'",
            "                for b in mod.bakes:",
            "                    b.bake_mode = 'ANIMATION'",
            "                if obj not in gn_objs:",
            "                    gn_objs.append(obj)",
            "            except Exception as gncfg:",
            "                result['Errors'].append('GN config ' + obj.name + ': ' + str(gncfg))",
            "for obj in gn_objs:",
            "    try:",
            "        with bpy.context.temp_override(active_object=obj, selected_objects=[obj], object=obj):",
            "            bpy.ops.object.simulation_nodes_cache_bake(selected=True)",
            "        result['BakedPointCaches'] += 1",
            "    except Exception as gnbake:",
            "        result['Errors'].append('GN bake ' + obj.name + ': ' + str(gnbake))",
            // After baking, flow/effector FLUID modifiers are no longer needed to RENDER (their effect is
            // captured in the domain cache) and their live source->domain relations can crash a node that
            // renders an isolated frame from the sliced cache. Strip every non-DOMAIN fluid modifier, keeping
            // the objects' geometry (e.g. effector rocks stay visible) and the domain modifier (reads cache).
            "for obj in list(bpy.data.objects):",
            "    for mod in list(getattr(obj, 'modifiers', [])):",
            "        if getattr(mod, 'type', '') == 'FLUID' and getattr(mod, 'fluid_type', '') != 'DOMAIN':",
            "            try:",
            "                obj.modifiers.remove(mod)",
            "            except Exception as strip_err:",
            "                result['Errors'].append('Strip ' + obj.name + ': ' + str(strip_err))",
            // Persist baked state + relative cache directory + the stripped modifiers.
            "try:",
            "    bpy.ops.wm.save_mainfile()",
            "except Exception as save_err2:",
            "    result['Errors'].append('Post-bake save: ' + str(save_err2))",
            // Enumerate ALL produced cache files. GAS/smoke renders from the OpenVDB density grid, which is
            // self-contained per frame -> tag each file with its frame so Split* ships only that frame's slice
            // to the node rendering it. A LIQUID domain renders the surface MESH (.bobj.gz), which Blender only
            // displays when the cache is CONTIGUOUS from the sim start (a lone frame falls back to the domain
            // box) -> emit liquid cache files as Frame=null (global) so EVERY node receives the whole baked
            // cache. Config files (no frame number) are global either way.
            "seen = set()",
            "for obj, ds in domains:",
            "    is_liquid = (getattr(ds, 'domain_type', '') == 'LIQUID')",
            "    cache_dir = ds.cache_directory",
            "    if cache_dir.startswith('//'):",
            "        cache_dir = os.path.join(blend_dir, cache_dir[2:])",
            "    if not os.path.isdir(cache_dir):",
            "        continue",
            "    for root, _dirs, files in os.walk(cache_dir):",
            "        for fn in files:",
            "            full = os.path.join(root, fn)",
            "            rel = rel_to_blend(full)",
            "            if rel in seen:",
            "                continue",
            "            seen.add(rel)",
            "            frame = None if is_liquid else frame_of(fn)",
            "            result['Cache'].append({'RelativePath': rel, 'OriginalPath': '//' + rel, 'Frame': frame})",
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

    public int BakedPointCaches { get; set; }

    public List<RenderBakeCacheEntry> Cache { get; set; } = [];

    public List<string> Errors { get; set; } = [];

    /// <summary>True when the bake produced any baked simulation (a fluid domain or a point-cache sim).</summary>
    public bool BakedAnything => BakedDomains > 0 || BakedPointCaches > 0;
}

/// <summary>One baked cache file produced by the bake script.</summary>
internal sealed class RenderBakeCacheEntry
{
    public string RelativePath { get; set; } = string.Empty;

    public string OriginalPath { get; set; } = string.Empty;

    public int? Frame { get; set; }
}
