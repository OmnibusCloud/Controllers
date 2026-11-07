using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Live showcase (manual / [Explicit]) for the DELEGATED-BAKE path — the exact production script the
/// Blender addon resolves to when a scene has an unbaked simulation and the bake strategy is
/// "On render farm" (Still + Cycles -> BakeAndRenderStillCycles). Runs real unbaked-sim scenes from
/// @Data through the bundled BakeAndRenderStillCycles.wit pipeline on the in-process engine:
///   Grid.Delegate(Render.BakeSimulation) -> BuildBlendFromRefs -> SplitBatched -> Grid.ForEach(FrameBatch.Cycles) -> CollectStill
/// i.e. bake the simulation, then render the BAKED scene. A late frame is rendered so the baked
/// deformation/flow is visible (a frozen/unbaked sim would render the rest pose). Renders + a
/// SUMMARY.md land in @Output/BakeShowcase/ for visual review.
///
/// Single-node in-process (no cloud); validates the script + bake correctness the addon depends on.
/// Run with: dotnet test --filter "FullyQualifiedName~BakeAndRenderLiveShowcaseTests"
/// </summary>
[TestFixture]
[Explicit("Live bake showcase: bakes + renders real unbaked-sim @Data scenes to @Output for visual inspection")]
internal sealed class BakeAndRenderLiveShowcaseTests : RenderProductionScriptBlenderTestsBase
{
    // Camera-bearing, reframed sim scenes (the prepped copies proven on the cloud in the full
    // showcase). The raw @Data demos have no camera, so we use these. The frame is chosen late
    // enough that the baked simulation has visibly developed — a frozen/unbaked result would look
    // like the rest pose. Path is relative to @Output/full-showcase/prepped/.
    private static readonly (string Key, string FileName, int Frame, string Note)[] SCENES =
    [
        ("cloth_pressure",  "cloth_pressure.blend",  40, "Point-cache cloth (internal air pressure) — bakes to memory, embedded in the .blend"),
        ("jiggly_pudding",  "jiggly_pudding.blend",  40, "Geometry-Nodes simulation zone — baked PACKED in the .blend"),
    ];

    private const string PREPPED_SUBDIR = "@Output/full-showcase/prepped";

    private const int WIDTH = 512;
    private const int HEIGHT = 512;
    private const int SAMPLES = 16;

    [Test]
    public async Task BakeAndRenderShowcaseToOutput()
    {
        var outDir = Path.Combine(m_solutionRoot!, "@Output", "BakeShowcase");
        Directory.CreateDirectory(outDir);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "BakeAndRenderStillCycles.wit"));

        var summary = new StringBuilder();
        summary.AppendLine("# Bake Showcase — delegated-bake path (BakeAndRenderStillCycles)");
        summary.AppendLine();
        summary.AppendLine($"Engine: Cycles · {WIDTH}x{HEIGHT} · {SAMPLES} samples · denoise on. Late frame so the baked sim is visible.");
        summary.AppendLine();
        summary.AppendLine("| Scene | Frame | Rendered | Notes |");
        summary.AppendLine("|---|---|---|---|");

        foreach (var scene in SCENES)
        {
            var scenePath = Path.Combine(m_solutionRoot!, PREPPED_SUBDIR.Replace('/', Path.DirectorySeparatorChar), scene.FileName);
            if (!File.Exists(scenePath))
            {
                TestContext.Out.WriteLine($"[skip] {scene.Key}: not found at {scenePath}");
                summary.AppendLine($"| `{scene.Key}` | {scene.Frame} | _missing_ | {scene.Note} |");
                continue;
            }

            var renderedCell = "—";
            try
            {
                var job = m_engine.Compile(script);
                var sceneBlobId = m_blobService.RegisterExistingFile(scenePath);
                var status = await m_engine.ScheduleAndWaitAsync(
                    job,
                    CreateSceneRef(sceneBlobId),
                    scene.Frame,
                    CreateShowcaseOptions(),
                    CreateBakeOptions());

                if (status.Result == WitProcessingResult.Completed && (Guid?)job.Variables["result"].Value is { } blobId)
                {
                    var stored = m_blobService.GetStoredPath(blobId);
                    var dest = Path.Combine(outDir, $"{scene.Key}.png");
                    File.Copy(stored, dest, overwrite: true);
                    var kb = new FileInfo(dest).Length / 1024;
                    renderedCell = $"✓ `{scene.Key}.png` ({kb} KB)";
                    TestContext.Out.WriteLine($"[ok] {scene.Key}: baked + rendered frame {scene.Frame} -> {dest} ({kb} KB)");
                }
                else
                {
                    renderedCell = $"✗ {Truncate(status.Message ?? status.Result.ToString())}";
                    TestContext.Out.WriteLine($"[FAIL] {scene.Key}: {status.Result} {status.Message}");
                }
            }
            catch (Exception e)
            {
                renderedCell = $"✗ {Truncate(e.Message)}";
                TestContext.Out.WriteLine($"[EXC] {scene.Key}: {e.Message}");
            }

            summary.AppendLine($"| `{scene.Key}` | {scene.Frame} | {renderedCell} | {scene.Note} |");
        }

        var summaryPath = Path.Combine(outDir, "SUMMARY.md");
        await File.WriteAllTextAsync(summaryPath, summary.ToString());
        TestContext.Out.WriteLine($"\nSummary written to {summaryPath}");

        Assert.Pass($"Bake showcase complete. See {outDir}");
    }

    private static RenderOptionsData CreateShowcaseOptions()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = SAMPLES,
            ResolutionX = WIDTH,
            ResolutionY = HEIGHT,
            Denoise = true
        };
    }

    private static RenderBakeOptionsData CreateBakeOptions()
    {
        // Mirrors the bridge's BridgeRenderLaunchService.CreateBakeOptions(): defaults — the controller
        // bakes every simulation kind it finds and only honours ResolutionMax (0 = keep scene value).
        return new RenderBakeOptionsData();
    }

    private static string Truncate(string value, int max = 120)
    {
        value = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= max ? value : value[..max] + "…";
    }
}
