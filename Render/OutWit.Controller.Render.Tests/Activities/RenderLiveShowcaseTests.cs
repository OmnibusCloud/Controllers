using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Live showcase (manual / [Explicit]): runs real downloaded scenes from @Data
/// through the production Render controller — validate (Phase A/C/E-lite gate)
/// then render the admitted ones via the bundled RenderStill.wit pipeline
/// (BuildBlendFromRefs -> SplitBatched -> Grid.ForEach(FrameBatch) -> Collect).
/// Renders + a SUMMARY.md are written to @Output/LiveShowcase/ for visual review.
///
/// Not part of the normal suite (heavy, needs the downloaded scenes). Run with:
///   dotnet test --filter "FullyQualifiedName~RenderLiveShowcaseTests"
/// </summary>
[TestFixture]
[Explicit("Live showcase: renders real @Data scenes to @Output for visual inspection")]
internal sealed class RenderLiveShowcaseTests : RenderProductionScriptBlenderTestsBase
{
    // (key, relative @Data path, expectation). expectRender=true → should pass
    // validation and produce an image; false → expected to be blocked (sim).
    private static readonly (string Key, string RelPath, bool ExpectRender, string Note)[] SCENES =
    [
        ("lone-monk",        "lone-monk.blend",                            true,  "Phase C: static HAIR scatter (grass+bush) — blocked in 1.19, admitted in 1.20"),
        ("bmw27",            "BMW27.blend",                                true,  "Control: clean scene"),
        ("classroom",        "classroom/classroom.blend",                  true,  "Control: clean scene"),
        ("cloth-springs",    "cloth_inner_springs.blend",                  false, "Phase A: cloth sim — honest block"),
        ("fluid-flip-apic",  "fluid-simulation_flip_vs_apic_solver.blend", false, "Phase A: fluid sim — honest block"),
        ("lava-fluid",       "lava_fluid-viscosity-demo.blend",            false, "Phase A: fluid sim — honest block"),
        ("gn-2d-smoke",      "2D_smoke_simulation.blend",                  false, "Phase A: GN simulation zone — NEW honest block (silently passed in 1.19)"),
    ];

    private const int WIDTH = 512;
    private const int HEIGHT = 512;
    private const int SAMPLES = 16;

    [Test]
    public async Task RenderLiveShowcaseToOutput()
    {
        var outDir = Path.Combine(m_solutionRoot!, "@Output", "LiveShowcase");
        Directory.CreateDirectory(outDir);

        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(m_solutionRoot!)
                         ?? throw new InvalidOperationException("No supported Blender prerequisites for current OS/architecture.");
        var validator = new BlenderRunner(blenderDir, NullLogger.Instance);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStill.wit"));

        var summary = new StringBuilder();
        summary.AppendLine("# Live Showcase — Render controller 1.20.0 scene-support");
        summary.AppendLine();
        summary.AppendLine($"Engine: Cycles · {WIDTH}x{HEIGHT} · {SAMPLES} samples · denoise on. Frame 1.");
        summary.AppendLine();
        summary.AppendLine("| Scene | Validation | Rendered | Notes |");
        summary.AppendLine("|---|---|---|---|");

        foreach (var scene in SCENES)
        {
            var scenePath = Path.Combine(m_solutionRoot!, "@Data", scene.RelPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(scenePath))
            {
                TestContext.Out.WriteLine($"[skip] {scene.Key}: not found at {scenePath}");
                summary.AppendLine($"| `{scene.Key}` | _missing_ | — | {scene.Note} |");
                continue;
            }

            // 1) Validate through the real controller validator.
            var validation = await validator.ValidateBlendDetailedAsync(scenePath);
            var verdict = validation.IsValid ? "PASS" : "BLOCK";
            var firstIssue = validation.Issues.FirstOrDefault();
            var firstWarning = validation.Warnings.FirstOrDefault();
            var detail = validation.IsValid
                ? (firstWarning is null ? "clean" : $"warn: {Truncate(firstWarning)}")
                : $"block: {Truncate(firstIssue ?? "(no message)")}";
            TestContext.Out.WriteLine($"[{verdict}] {scene.Key}: {detail}");

            // 2) Render the admitted scenes; sims are validate-only.
            var renderedCell = "—";
            if (validation.IsValid)
            {
                try
                {
                    var job = m_engine.Compile(script);
                    var sceneBlobId = m_blobService.RegisterExistingFile(scenePath);
                    var status = await m_engine.ScheduleAndWaitAsync(
                        job,
                        CreateSceneRef(sceneBlobId),
                        1,
                        CreateShowcaseOptions());

                    if (status.Result == WitProcessingResult.Completed && (Guid?)job.Variables["result"].Value is { } blobId)
                    {
                        var stored = m_blobService.GetStoredPath(blobId);
                        var dest = Path.Combine(outDir, $"{scene.Key}.png");
                        File.Copy(stored, dest, overwrite: true);
                        var kb = new FileInfo(dest).Length / 1024;
                        renderedCell = $"✓ `{scene.Key}.png` ({kb} KB)";
                        TestContext.Out.WriteLine($"       rendered -> {dest} ({kb} KB)");
                    }
                    else
                    {
                        renderedCell = $"✗ {Truncate(status.Message ?? status.Result.ToString())}";
                        TestContext.Out.WriteLine($"       render FAILED: {status.Result} {status.Message}");
                    }
                }
                catch (Exception e)
                {
                    renderedCell = $"✗ {Truncate(e.Message)}";
                    TestContext.Out.WriteLine($"       render EXCEPTION: {e.Message}");
                }
            }

            var detailEscaped = detail.Replace("|", "\\|");
            summary.AppendLine($"| `{scene.Key}` | **{verdict}** | {renderedCell} | {scene.Note}<br/>{detailEscaped} |");
        }

        var summaryPath = Path.Combine(outDir, "SUMMARY.md");
        await File.WriteAllTextAsync(summaryPath, summary.ToString());
        TestContext.Out.WriteLine($"\nSummary written to {summaryPath}");

        Assert.Pass($"Live showcase complete. See {outDir}");
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

    private static string Truncate(string value, int max = 120)
    {
        value = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= max ? value : value[..max] + "…";
    }
}
