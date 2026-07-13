using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Unit tests for the bake-script generator/parser. The Blender bake itself is exercised by the live
/// delegated-bake proof (a headless Mantaflow bake cannot run in a fast unit test), but the generated
/// script shape and the manifest parser are pinned here.
/// </summary>
[TestFixture]
public class BlenderBakeScriptTests
{
    #region BuildScript

    [Test]
    public void BuildScriptEmitsMarkersAndFrameRangeTest()
    {
        var lines = BlenderBakeScript.BuildScript(startFrame: 5, endFrame: 20, resolutionMax: 96);
        var text = string.Join("\n", lines);

        Assert.That(text, Does.Contain("START_FRAME = 5"));
        Assert.That(text, Does.Contain("END_FRAME = 20"));
        Assert.That(text, Does.Contain("RESOLUTION_MAX = 96"));
        Assert.That(text, Does.Contain(BlenderBakeScript.START_MARKER));
        Assert.That(text, Does.Contain(BlenderBakeScript.END_MARKER));
    }

    [Test]
    public void BuildScriptUsesReplayFrameSteppingForFluidTest()
    {
        var text = string.Join("\n", BlenderBakeScript.BuildScript(1, 24, 0));

        // Headless-safe recipe: OpenVDB + REPLAY, then STEP the timeline so the solver writes each frame.
        Assert.That(text, Does.Contain("cache_data_format = 'OPENVDB'"));
        Assert.That(text, Does.Contain("cache_type = 'REPLAY'"));
        Assert.That(text, Does.Contain("fluid_type', '') == 'DOMAIN'"));
        // The modal bake_all no-ops in headless Blender (writes nothing) — it must be gone, replaced by
        // an explicit frame_set() loop across the bake range.
        Assert.That(text, Does.Not.Contain("bpy.ops.fluid.bake_all()"));
        Assert.That(text, Does.Contain("bpy.context.scene.frame_set(f)"));
        // Success is measured by real cache files on disk, NOT the has_cache_baked_data flag (which REPLAY
        // leaves False and the modal bake set to a false-positive True). The flag must not gate success.
        Assert.That(text, Does.Not.Contain("has_cache_baked_data"));
        Assert.That(text, Does.Contain("os.walk(_cache_dir)"));
        // Free any stale fluid cache before recomputing; GN keeps its own free-before-bake.
        Assert.That(text, Does.Contain("bpy.ops.fluid.free_all()"));
        Assert.That(text, Does.Contain("bpy.ops.object.simulation_nodes_cache_delete(selected=True)"));
    }

    [Test]
    public void BuildScriptBakesPointCacheSimsTest()
    {
        var text = string.Join("\n", BlenderBakeScript.BuildScript(1, 24, 0));

        // Cloth / soft body / particles / dynamic paint / rigid body bake to the point cache (memory),
        // which is embedded in the .blend on save -> self-contained, no per-frame attachments.
        Assert.That(text, Does.Contain("bpy.ops.ptcache.bake_all(bake=True)"));
        Assert.That(text, Does.Contain("BakedPointCaches"));
        // Must free any stale/pre-existing bake before re-baking, else the cache stays frozen.
        Assert.That(text, Does.Contain("bpy.ops.ptcache.free_bake_all()"));
        // ANTI-PIN: never assign use_disk_cache on the SCENE-level rigidbody_world point cache — the RNA
        // setter fires BKE_ptcache_toggle_disk_cache, which segfaults Blender for the object-less scene
        // cache (the 1.23.18 regression: every delegated fluid bake died mid-script on the lava demo).
        Assert.That(text, Does.Not.Contain("rigidbody_world', None)\n    point_cache.use_disk_cache"));
        Assert.That(text, Does.Not.Contain("rbw0.point_cache.use_disk_cache"));
        // A disk-cached rigid body world is unfixable headless (files don't travel; conversion segfaults):
        // the script must FAIL FAST with the marker the runner escalates to a hard error, before any
        // expensive bake work. READING the flag is safe (only assignment crashes).
        Assert.That(text, Does.Contain("RigidBodyDiskCache:"));
        Assert.That(text, Does.Contain("raise SystemExit(0)"));
        // The frame-step loop must emit a FLUSHED live progress marker per frame — it drives the job's
        // visible progress through the activity-progress sink (an 8-minute bake looks hung without it).
        Assert.That(text, Does.Contain($"print('{BlenderBakeScript.PROGRESS_MARKER} %d %d %d' % (f, bake_start, bake_end), flush=True)"));
    }

    [Test]
    public void BuildScriptShipsLiquidCacheGlobalTest()
    {
        var text = string.Join("\n", BlenderBakeScript.BuildScript(1, 24, 0));

        // A liquid surface mesh only displays when its cache is contiguous from the sim start, so liquid
        // cache files are emitted Frame=null (global -> every node) rather than per-frame sliced.
        Assert.That(text, Does.Contain("is_liquid = (getattr(ds, 'domain_type', '') == 'LIQUID')"));
        // Per-frame slicing only for gas density *.vdb (not liquid, not noise) — everything else global.
        Assert.That(text, Does.Contain("(not is_liquid) and low.endswith('.vdb') and ('noise' not in low)"));
    }

    [Test]
    public void BuildScriptBakesGeometryNodesSimTest()
    {
        var text = string.Join("\n", BlenderBakeScript.BuildScript(1, 24, 0));

        // GN simulation zones bake to PACKED (embedded in the blend) via simulation_nodes_cache_bake.
        Assert.That(text, Does.Contain("bpy.ops.object.simulation_nodes_cache_bake(selected=True)"));
        Assert.That(text, Does.Contain("mod.bake_target = 'PACKED'"));
    }

    // Dev utility (not run in CI): dump the exact generated bake script to a file so it can be executed
    // against a real .blend headless (blender -b scene --python <file>) to validate the full recipe
    // end-to-end. Set OUTWIT_BAKE_SCRIPT_OUT (+ optional OUTWIT_BAKE_START/END/RES) and run explicitly.
    [Test]
    [Explicit("Writes the generated bake script to OUTWIT_BAKE_SCRIPT_OUT for manual headless execution.")]
    public void DumpGeneratedBakeScriptTest()
    {
        var outPath = Environment.GetEnvironmentVariable("OUTWIT_BAKE_SCRIPT_OUT");
        Assert.That(outPath, Is.Not.Null.And.Not.Empty, "set OUTWIT_BAKE_SCRIPT_OUT to a file path");

        int Env(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

        var lines = BlenderBakeScript.BuildScript(
            startFrame: Env("OUTWIT_BAKE_START", 1),
            endFrame: Env("OUTWIT_BAKE_END", 25),
            resolutionMax: Env("OUTWIT_BAKE_RES", 48));

        File.WriteAllLines(outPath!, lines);
        TestContext.Progress.WriteLine($"Bake script written to {outPath} ({lines.Count} lines).");
    }

    #endregion

    #region Progress Line

    [Test]
    public void TryParseProgressLineParsesValidMarkerTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BlenderBakeScript.TryParseProgressLine("OUTWIT_BAKE_PROGRESS 46 46 95", out var f1, out var s1), Is.True);
            Assert.That(f1, Is.EqualTo(1.0 / 50.0).Within(1e-9));
            Assert.That(s1, Is.EqualTo("Baking frame 46/95"));

            Assert.That(BlenderBakeScript.TryParseProgressLine("OUTWIT_BAKE_PROGRESS 95 46 95", out var f2, out _), Is.True);
            Assert.That(f2, Is.EqualTo(1.0).Within(1e-9));

            Assert.That(BlenderBakeScript.TryParseProgressLine("OUTWIT_BAKE_PROGRESS 70 46 95", out var f3, out var s3), Is.True);
            Assert.That(f3, Is.EqualTo(25.0 / 50.0).Within(1e-9));
            Assert.That(s3, Is.EqualTo("Baking frame 70/95"));
        });
    }

    [Test]
    public void TryParseProgressLineRejectsGarbageTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BlenderBakeScript.TryParseProgressLine("", out _, out _), Is.False);
            Assert.That(BlenderBakeScript.TryParseProgressLine("Fra:47 Mem:120M", out _, out _), Is.False);
            Assert.That(BlenderBakeScript.TryParseProgressLine("OUTWIT_BAKE_PROGRESS", out _, out _), Is.False);
            Assert.That(BlenderBakeScript.TryParseProgressLine("OUTWIT_BAKE_PROGRESS a b c", out _, out _), Is.False);
            Assert.That(BlenderBakeScript.TryParseProgressLine("OUTWIT_BAKE_PROGRESS 5 10 1", out _, out _), Is.False);
            Assert.That(BlenderBakeScript.TryParseProgressLine("OUTWIT_BAKE_SIMULATION_START", out _, out _), Is.False);
        });
    }

    #endregion

    #region ParseResult

    [Test]
    public void ParseResultReadsManifestTest()
    {
        var stdout =
            "Blender quit\n" +
            BlenderBakeScript.START_MARKER + "\n" +
            "{\"BakedDomains\":1,\"Cache\":[" +
            "{\"RelativePath\":\"cache_Domain/data/fluid_data_0001.vdb\",\"OriginalPath\":\"//cache_Domain/data/fluid_data_0001.vdb\",\"Frame\":1}," +
            "{\"RelativePath\":\"cache_Domain/data/fluid_data_0002.vdb\",\"OriginalPath\":\"//cache_Domain/data/fluid_data_0002.vdb\",\"Frame\":2}]," +
            "\"Errors\":[]}\n" +
            BlenderBakeScript.END_MARKER + "\n";

        var result = BlenderBakeScript.ParseResult(stdout, NullLogger.Instance);

        Assert.That(result.BakedDomains, Is.EqualTo(1));
        Assert.That(result.Cache, Has.Count.EqualTo(2));
        Assert.That(result.Cache[0].Frame, Is.EqualTo(1));
        Assert.That(result.Cache[1].RelativePath, Is.EqualTo("cache_Domain/data/fluid_data_0002.vdb"));
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void ParseResultThrowsWithoutMarkersTest()
    {
        Assert.That(
            () => BlenderBakeScript.ParseResult("no markers here", NullLogger.Instance),
            Throws.Exception);
    }

    #endregion
}
