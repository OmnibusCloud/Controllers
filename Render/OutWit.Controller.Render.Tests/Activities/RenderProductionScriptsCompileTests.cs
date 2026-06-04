using OutWit.Controller.Render.Tests.Utils;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Compiles every bundled production <c>.wit</c> script against the registered activities/variables.
/// Guards the persistent-batch migration: a typo'd activity name (Render.SplitBatched /
/// Render.SplitTilesBatched / Render.FrameBatch[.Engine]) or variable type
/// (RenderTaskBatchCollection / RenderResultBatchCollection) in any script fails compilation here.
/// </summary>
[TestFixture]
public class RenderProductionScriptsCompileTests
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(false, null, controllersPath);
    }

    #endregion

    #region Tests

    [TestCaseSource(nameof(BundledScripts))]
    public void ProductionScriptCompilesTest(string scriptPath)
    {
        var source = File.ReadAllText(scriptPath);
        var job = WitEngineSdk.Instance.Compile(source);

        Assert.That(job, Is.Not.Null, $"Compilation returned null for {Path.GetFileName(scriptPath)}");
        Assert.That(job.Activities.Count, Is.GreaterThan(0), $"No activities compiled for {Path.GetFileName(scriptPath)}");
    }

    [Test]
    public void FrameScriptsUseBatchedSplitTest()
    {
        // Persistent-batch applies to FRAME distribution: no bundled script may still use the bare
        // per-frame splitter Render.Split( — frames go through Render.SplitBatched + Render.FrameBatch.
        // Tiled stills intentionally stay on the per-tile path (Render.SplitTiles + Render.Frame): the
        // CLI animation render that makes FrameBatch macOS-safe cannot vary the border per frame, so
        // Render.Split( (exact, not SplitTiles/SplitBatched) must appear in ZERO bundled scripts.
        var offenders = new List<string>();
        foreach (var path in EnumerateBundledScripts())
        {
            var text = File.ReadAllText(path);
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"Render\.Split\s*\("))
                offenders.Add(Path.GetFileName(path));
        }

        Assert.That(offenders, Is.Empty,
            $"Frame scripts still use the per-frame Render.Split (must be Render.SplitBatched): {string.Join(", ", offenders)}");
    }

    #endregion

    #region Tools

    private static IEnumerable<string> BundledScripts()
    {
        var dir = RenderTestAssetPaths.FindBundledScriptsPath();
        if (dir == null)
            return [];

        return Directory.GetFiles(dir, "*.wit", SearchOption.TopDirectoryOnly);
    }

    private static IEnumerable<string> EnumerateBundledScripts()
    {
        var dir = RenderTestAssetPaths.FindBundledScriptsPath();
        return dir == null ? [] : Directory.GetFiles(dir, "*.wit", SearchOption.TopDirectoryOnly);
    }

    #endregion
}
