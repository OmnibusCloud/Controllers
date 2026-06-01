using System.Globalization;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Builds the Blender Python script for the node render benchmark and parses its
/// timing markers. The script is the methodology fix described in
/// <c>@Docs/Active/plan-render-benchmark-redesign.md</c>:
///
/// <list type="bullet">
/// <item>It generates a moderate, compute-bound scene <b>in code</b> (a floor plus a
/// grid of glossy / glass / diffuse icospheres lit by three area lights) — no binary
/// <c>.blend</c> asset, deterministic, complexity tunable by <paramref name="gridSize"/>.</item>
/// <item>It does everything in <b>one</b> Blender process: configure device, build the
/// scene, run a warmup render (so GPU-context / BVH init is excluded), then render
/// frames in a loop, timing <b>only the render loop</b> with <c>time.perf_counter()</c>.</item>
/// <item>The loop is <b>adaptive</b>: it renders until the elapsed render time reaches the
/// target (or a frame cap), so the benchmark's wall time is bounded on every machine while
/// the per-process Blender startup is amortised and excluded from the measurement.</item>
/// </list>
///
/// The resulting rate (pixels·samples / render-second) is therefore real render throughput,
/// not Blender startup speed — which is what the Grid allocator needs for faithful balancing.
/// Stateless; extracted from <see cref="BlenderRunner"/> to keep that orchestrator small.
/// </summary>
internal static class BlenderBenchmarkScript
{
    #region Constants

    public const string FRAMES_MARKER = "OUTWIT_BENCH_FRAMES=";
    public const string RENDER_SECONDS_MARKER = "OUTWIT_BENCH_RENDER_SECONDS=";

    #endregion

    #region Build

    /// <summary>
    /// Builds the benchmark Python script lines.
    /// </summary>
    /// <param name="engine">Render engine to benchmark.</param>
    /// <param name="samples">Samples per frame (Cycles samples / Eevee TAA samples).</param>
    /// <param name="resolution">Square render resolution in pixels (e.g. 256).</param>
    /// <param name="gridSize">Scene complexity: builds a <c>gridSize×gridSize</c> grid of spheres.</param>
    /// <param name="maxBounces">Cycles light-path max bounces (ignored by Eevee).</param>
    /// <param name="warmupFrames">Renders run before timing starts (excludes init cost).</param>
    /// <param name="targetSeconds">Render-loop time budget; stop once reached.</param>
    /// <param name="maxFrames">Hard cap on rendered frames (protects fast hardware from over-running).</param>
    public static IReadOnlyList<string> BuildScript(
        RenderEngine engine,
        int samples,
        int resolution,
        int gridSize,
        int maxBounces,
        int warmupFrames,
        double targetSeconds,
        int maxFrames)
    {
        var lines = new List<string>
        {
            "import bpy, time, math, os, tempfile",
            "bpy.ops.wm.read_factory_settings(use_empty=True)",
            "scene = bpy.context.scene"
        };

        // Device selection + OUTWIT_RENDER_* markers (reused from the render path).
        lines.AddRange(BlenderRenderArgsBuilder.BuildDeviceConfigurationPython(engine, forceCpuFallback: false));

        // Output resolution.
        lines.Add($"scene.render.resolution_x = {resolution}");
        lines.Add($"scene.render.resolution_y = {resolution}");
        lines.Add("scene.render.resolution_percentage = 100");
        lines.Add("scene.render.image_settings.file_format = 'PNG'");

        // Engine sampling (Cycles samples / Eevee TAA samples), reused from the render path.
        var sampleOptions = new RenderOptionsData
        {
            Engine = engine,
            Samples = samples,
            Denoise = false
        };
        lines.AddRange(BlenderRenderArgsBuilder.BuildEngineConfigurationPython(sampleOptions));

        if (engine == RenderEngine.Cycles && maxBounces > 0)
            lines.Add($"scene.cycles.max_bounces = {maxBounces}");

        // Procedural compute-bound scene (code, not a .blend).
        lines.AddRange(BuildSceneLines(gridSize));

        // Render output to a throwaway temp location (we never read the pixels).
        lines.Add("_tmp = tempfile.mkdtemp(prefix='outwit_bench_')");
        lines.Add("scene.render.filepath = os.path.join(_tmp, 'f_')");

        // Warmup — excludes first-render GPU/BVH init from the timed loop.
        lines.Add($"for _w in range({Math.Max(0, warmupFrames)}):");
        lines.Add("    bpy.ops.render.render(write_still=False)");

        // Adaptive timed render loop — render until the time budget or frame cap is hit.
        lines.Add($"_target = {targetSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
        lines.Add($"_maxf = {Math.Max(1, maxFrames)}");
        lines.Add("_f = 0");
        lines.Add("_t0 = time.perf_counter()");
        lines.Add("while _f < _maxf:");
        lines.Add("    scene.frame_set(_f + 1)");
        lines.Add("    bpy.ops.render.render(write_still=False)");
        lines.Add("    _f += 1");
        lines.Add("    if time.perf_counter() - _t0 >= _target:");
        lines.Add("        break");
        lines.Add("_elapsed = time.perf_counter() - _t0");
        lines.Add($"print('{FRAMES_MARKER}' + str(_f))");
        lines.Add($"print('{RENDER_SECONDS_MARKER}' + ('%.6f' % _elapsed))");

        return lines;
    }

    #endregion

    #region Parse

    /// <summary>
    /// Parses the timed render-loop result from the script's stdout.
    /// Returns null when the markers are absent (treated as a failed benchmark by the caller).
    /// </summary>
    public static (int Frames, double RenderSeconds)? ParseResult(string stdout)
    {
        var framesText = ParseMarker(stdout, FRAMES_MARKER);
        var secondsText = ParseMarker(stdout, RENDER_SECONDS_MARKER);

        if (framesText == null || secondsText == null)
            return null;

        if (!int.TryParse(framesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
            return null;

        if (!double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return null;

        if (frames <= 0 || seconds <= 0)
            return null;

        return (frames, seconds);
    }

    #endregion

    #region Tools

    private static IReadOnlyList<string> BuildSceneLines(int gridSize)
    {
        var grid = Math.Max(1, gridSize);
        return new List<string>
        {
            // Floor.
            "bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))",
            "_fm = bpy.data.materials.new('BenchFloor')",
            "_fm.use_nodes = True",
            "_fm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.4",
            "bpy.context.active_object.data.materials.append(_fm)",
            // Grid of subdivided spheres with mixed glass / metal / diffuse materials.
            $"_grid = {grid}",
            "for _i in range(_grid):",
            "    for _j in range(_grid):",
            "        _x = (_i - _grid / 2.0) * 2.2",
            "        _y = (_j - _grid / 2.0) * 2.2",
            "        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=0.9, location=(_x, _y, 0.9))",
            "        _o = bpy.context.active_object",
            "        _m = bpy.data.materials.new('BenchM_%d_%d' % (_i, _j))",
            "        _m.use_nodes = True",
            "        _b = _m.node_tree.nodes['Principled BSDF']",
            "        _k = (_i + _j) % 3",
            "        if _k == 0:",
            "            _b.inputs['Transmission Weight'].default_value = 1.0",
            "            _b.inputs['Roughness'].default_value = 0.03",
            "            _b.inputs['IOR'].default_value = 1.45",
            "        elif _k == 1:",
            "            _b.inputs['Metallic'].default_value = 1.0",
            "            _b.inputs['Roughness'].default_value = 0.15",
            "        else:",
            "            _b.inputs['Base Color'].default_value = (0.8, 0.2, 0.2, 1.0)",
            "            _b.inputs['Roughness'].default_value = 0.5",
            "        _o.data.materials.append(_m)",
            // Three area lights for plenty of light-path work.
            "for _lx, _ly in [(6, -6), (-6, 6), (0, 8)]:",
            "    bpy.ops.object.light_add(type='AREA', location=(_lx, _ly, 8))",
            "    _l = bpy.context.active_object",
            "    _l.data.energy = 2000",
            "    _l.data.size = 4",
            // Camera framing the whole grid.
            "bpy.ops.object.camera_add(location=(_grid * 1.6, -_grid * 1.6, _grid * 1.4))",
            "_cam = bpy.context.active_object",
            "_cam.rotation_euler = (math.radians(60), 0.0, math.radians(45))",
            "_cam.data.lens = 40",
            "scene.camera = _cam"
        };
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

    #endregion
}
