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
    public void BuildScriptForcesOpenVdbAllCacheTest()
    {
        var text = string.Join("\n", BlenderBakeScript.BuildScript(1, 24, 0));

        // The hard-won headless recipe: OpenVDB data format + ALL cache type (default REPLAY bakes one frame).
        Assert.That(text, Does.Contain("cache_data_format = 'OPENVDB'"));
        Assert.That(text, Does.Contain("cache_type = 'ALL'"));
        Assert.That(text, Does.Contain("bpy.ops.fluid.bake_all()"));
        Assert.That(text, Does.Contain("fluid_type', '') == 'DOMAIN'"));
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
    }

    [Test]
    public void BuildScriptShipsLiquidCacheGlobalTest()
    {
        var text = string.Join("\n", BlenderBakeScript.BuildScript(1, 24, 0));

        // A liquid surface mesh only displays when its cache is contiguous from the sim start, so liquid
        // cache files are emitted Frame=null (global -> every node) rather than per-frame sliced.
        Assert.That(text, Does.Contain("is_liquid = (getattr(ds, 'domain_type', '') == 'LIQUID')"));
        Assert.That(text, Does.Contain("frame = None if is_liquid else frame_of(fn)"));
    }

    [Test]
    public void BuildScriptBakesGeometryNodesSimTest()
    {
        var text = string.Join("\n", BlenderBakeScript.BuildScript(1, 24, 0));

        // GN simulation zones bake to PACKED (embedded in the blend) via simulation_nodes_cache_bake.
        Assert.That(text, Does.Contain("bpy.ops.object.simulation_nodes_cache_bake(selected=True)"));
        Assert.That(text, Does.Contain("mod.bake_target = 'PACKED'"));
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
