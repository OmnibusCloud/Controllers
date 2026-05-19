using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Tolerance-based image-golden assertion for render-output tests.
///
/// Workflow:
/// 1. Every test run writes the actual output to
///    <c>@Output/GoldenCandidates/&lt;testKey&gt;_&lt;engine&gt;_&lt;WxH&gt;.png</c>
///    plus a side-by-side <c>..._diff.png</c> visualisation (actual | golden |
///    amplified-diff) so the operator can inspect what changed at a glance.
/// 2. The first time a test runs there is no golden — the assert reports
///    Ignore with the candidate path; the operator eyeballs the candidate and,
///    if it looks right, copies it to <c>@Prerequisites/render-golden/</c> (or
///    re-runs with <c>WIT_RENDER_UPDATE_GOLDENS=1</c> to auto-promote).
/// 3. Subsequent runs compare against the golden using mean-absolute RGB
///    difference with a per-engine tolerance (Cycles is stochastic so the
///    band is wider; Eevee / GreasePencil are tighter).
/// </summary>
internal static class RenderGoldenFileAssert
{
    #region Constants

    private const string UPDATE_ENVIRONMENT_VARIABLE = "WIT_RENDER_UPDATE_GOLDENS";

    private const string GOLDEN_ROOT_SUBPATH = "@Prerequisites/render-golden";
    private const string CANDIDATE_ROOT_SUBPATH = "@Output/GoldenCandidates";

    // Tolerances are mean absolute per-channel difference in the 0..255 range.
    // Cycles uses path tracing with 4 samples in tests — very stochastic.
    // Eevee + GreasePencil are deterministic-ish so the band can be much tighter.
    private const double CYCLES_TOLERANCE = 15.0;
    private const double DEFAULT_TOLERANCE = 5.0;

    // Diff visualisation amplifies the raw per-channel difference so a 2-unit
    // tone shift becomes visible in the side-by-side image.
    private const int DIFF_AMPLIFICATION = 5;

    // Render tests run at 64x64 for speed. That is unreadable to a human, so
    // every candidate gets an upscaled-by-nearest-neighbor _preview / _diff
    // companion for visual inspection. Promote copies the native-res file.
    private const int PREVIEW_UPSCALE = 8;

    #endregion

    #region Functions

    public static void AssertImageMatches(
        string actualPath,
        string solutionRoot,
        string testKey,
        RenderEngine engine,
        int width,
        int height)
    {
        var filename = $"{testKey}_{engine}_{width}x{height}.png";
        var baseName = Path.GetFileNameWithoutExtension(filename);
        var goldenDir = Path.Combine(solutionRoot, GOLDEN_ROOT_SUBPATH.Replace('/', Path.DirectorySeparatorChar));
        var candidateDir = Path.Combine(solutionRoot, CANDIDATE_ROOT_SUBPATH.Replace('/', Path.DirectorySeparatorChar));
        var goldenPath = Path.Combine(goldenDir, filename);
        var candidatePath = Path.Combine(candidateDir, filename);
        var previewPath = Path.Combine(candidateDir, $"{baseName}_preview.png");
        var diffPath = Path.Combine(candidateDir, $"{baseName}_diff.png");

        if (!File.Exists(actualPath))
            Assert.Fail($"Actual render output for '{filename}' was not found at '{actualPath}'.");

        Directory.CreateDirectory(candidateDir);
        File.Copy(actualPath, candidatePath, overwrite: true);
        WriteUpscaledPreview(actualPath, previewPath);

        if (ShouldUpdateGoldens())
        {
            Directory.CreateDirectory(goldenDir);
            File.Copy(actualPath, goldenPath, overwrite: true);
            Assert.Inconclusive(
                $"Golden '{filename}' was (re-)promoted from candidate. " +
                $"Re-run without {UPDATE_ENVIRONMENT_VARIABLE} to verify the new baseline.");
        }

        if (!File.Exists(goldenPath))
        {
            Assert.Ignore(
                $"Golden '{filename}' is missing. " +
                $"Inspect candidate '{candidatePath}' and either copy it to '{goldenPath}' " +
                $"or re-run the test with {UPDATE_ENVIRONMENT_VARIABLE}=1 to auto-promote.");
        }

        using var actual = Image.Load<Rgba32>(actualPath);
        using var golden = Image.Load<Rgba32>(goldenPath);

        if (actual.Width != golden.Width || actual.Height != golden.Height)
        {
            Assert.Fail(
                $"Dimension mismatch for '{filename}': actual {actual.Width}x{actual.Height} vs golden {golden.Width}x{golden.Height}. " +
                $"Inspect candidate '{candidatePath}'.");
        }

        var (meanAbsDiff, maxAbsDiff) = ComputeMeanAndMaxAbsDiff(actual, golden);
        WriteSideBySideDiff(actual, golden, diffPath);

        var tolerance = ResolveTolerance(engine);
        if (meanAbsDiff > tolerance)
        {
            Assert.Fail(
                $"Golden mismatch for '{filename}': mean abs RGB diff {meanAbsDiff:F2} exceeds tolerance {tolerance:F2} " +
                $"(max single-pixel-channel diff {maxAbsDiff}). " +
                $"Inspect candidate='{candidatePath}', diff='{diffPath}', golden='{goldenPath}'.");
        }
    }

    #endregion

    #region Tools

    private static double ResolveTolerance(RenderEngine engine)
    {
        return engine switch
        {
            RenderEngine.Cycles => CYCLES_TOLERANCE,
            _ => DEFAULT_TOLERANCE,
        };
    }

    private static (double meanAbs, int maxAbs) ComputeMeanAndMaxAbsDiff(Image<Rgba32> actual, Image<Rgba32> golden)
    {
        long totalDiff = 0;
        var maxDiff = 0;
        var samples = (long)actual.Width * actual.Height * 3;

        for (var y = 0; y < actual.Height; y++)
        {
            for (var x = 0; x < actual.Width; x++)
            {
                var a = actual[x, y];
                var g = golden[x, y];

                var dr = Math.Abs(a.R - g.R);
                var dgr = Math.Abs(a.G - g.G);
                var db = Math.Abs(a.B - g.B);
                totalDiff += dr + dgr + db;
                if (dr > maxDiff) maxDiff = dr;
                if (dgr > maxDiff) maxDiff = dgr;
                if (db > maxDiff) maxDiff = db;
            }
        }

        return (samples == 0 ? 0d : totalDiff / (double)samples, maxDiff);
    }

    private static void WriteSideBySideDiff(Image<Rgba32> actual, Image<Rgba32> golden, string diffPath)
    {
        var w = actual.Width;
        var h = actual.Height;

        // Amplified per-pixel difference, brighter = larger diff.
        using var diff = new Image<Rgba32>(w, h);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var a = actual[x, y];
                var g = golden[x, y];
                var dr = Math.Min(255, Math.Abs(a.R - g.R) * DIFF_AMPLIFICATION);
                var dgr = Math.Min(255, Math.Abs(a.G - g.G) * DIFF_AMPLIFICATION);
                var db = Math.Min(255, Math.Abs(a.B - g.B) * DIFF_AMPLIFICATION);
                diff[x, y] = new Rgba32((byte)dr, (byte)dgr, (byte)db, 255);
            }
        }

        // Composite: [actual | golden | diff] with an 8px black gutter
        // between panels, upscaled by PREVIEW_UPSCALE so the diff is readable
        // at the typical 64x64 render size.
        const int gutter = 8;
        var panelW = w * PREVIEW_UPSCALE;
        var panelH = h * PREVIEW_UPSCALE;
        var compositeWidth = panelW * 3 + gutter * 2;
        using var actualUp = UpscaleNearestNeighbor(actual);
        using var goldenUp = UpscaleNearestNeighbor(golden);
        using var diffUp = UpscaleNearestNeighbor(diff);
        using var composite = new Image<Rgba32>(compositeWidth, panelH, new Rgba32(0, 0, 0, 255));
        composite.Mutate(ctx =>
        {
            ctx.DrawImage(actualUp, new Point(0, 0), 1f);
            ctx.DrawImage(goldenUp, new Point(panelW + gutter, 0), 1f);
            ctx.DrawImage(diffUp, new Point((panelW + gutter) * 2, 0), 1f);
        });

        Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
        composite.SaveAsPng(diffPath);
    }

    private static void WriteUpscaledPreview(string actualPath, string previewPath)
    {
        using var actual = Image.Load<Rgba32>(actualPath);
        using var upscaled = UpscaleNearestNeighbor(actual);
        Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
        upscaled.SaveAsPng(previewPath);
    }

    private static Image<Rgba32> UpscaleNearestNeighbor(Image<Rgba32> source)
    {
        var clone = source.Clone();
        clone.Mutate(ctx => ctx.Resize(
            source.Width * PREVIEW_UPSCALE,
            source.Height * PREVIEW_UPSCALE,
            KnownResamplers.NearestNeighbor));
        return clone;
    }

    private static bool ShouldUpdateGoldens()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(UPDATE_ENVIRONMENT_VARIABLE),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
