using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Sdk;
using OutWit.Controller.Render.Tests.Utils;

namespace OutWit.Controller.Render.Tests.Benchmark;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class RenderBenchmarkUnavailableIntegrationTests
{
    #region Fields

    private string m_controllersPath = null!;
    private string m_solutionRoot = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                         ?? throw new DirectoryNotFoundException("Solution root not found.");
        m_controllersPath = RenderTestAssetPaths.FindControllersPath()
                            ?? throw new DirectoryNotFoundException("@Controllers\\Debug was not found.");

        WitEngineNodeSdk.Instance.Reload(useIsolatedContext: false, moduleFolder: m_controllersPath);
    }

    #endregion

    #region Tests

    [Test]
    public async Task RenderFrameCyclesBenchmarkReturnsUnavailableResultWhenStillBenchmarkAssetsAreMissingTest()
    {
        var paths = new[]
        {
            RenderTestAssetPaths.GetBenchmarkScenePath(m_solutionRoot),
            RenderTestAssetPaths.GetBenchmarkStillScenePath(m_solutionRoot),
            Path.Combine(m_controllersPath, "render.module", "benchmark_scene.blend"),
            Path.Combine(m_controllersPath, "render.module", "benchmark_scene_still.blend")
        };

        await WithTemporarilyMovedFilesAsync(paths, async () =>
        {
            var result = await WitEngineNodeSdk.Instance.RunBenchmark("Render.Frame.Cycles", (WitBenchmarkOptions)WitBenchmarkOptions.Default);

            Assert.Multiple(() =>
            {
                Assert.That(result.Rate, Is.EqualTo(0));
                Assert.That(result.Iterations, Is.EqualTo(0));
                Assert.That(result.Elapsed, Is.EqualTo(TimeSpan.Zero));
                Assert.That(result.Unit, Is.EqualTo("render-pixels@v1"));
                Assert.That(result.DatasetId, Is.EqualTo("benchmark-still-cycles@v1"));
            });
        });
    }

    [Test]
    public async Task RenderPreflightVideoBenchmarkReturnsUnavailableResultWhenVideoBenchmarkAssetsAreMissingTest()
    {
        var paths = new[]
        {
            RenderTestAssetPaths.GetBenchmarkVideoScenePath(m_solutionRoot),
            Path.Combine(m_controllersPath, "render.module", "benchmark_scene_video.blend")
        };

        await WithTemporarilyMovedFilesAsync(paths, async () =>
        {
            var result = await WitEngineNodeSdk.Instance.RunBenchmark("Render.PreflightVideo", (WitBenchmarkOptions)WitBenchmarkOptions.Default);

            Assert.Multiple(() =>
            {
                Assert.That(result.Rate, Is.EqualTo(0));
                Assert.That(result.Iterations, Is.EqualTo(0));
                Assert.That(result.Elapsed, Is.EqualTo(TimeSpan.Zero));
                Assert.That(result.Unit, Is.EqualTo("video-preflights@v1"));
                Assert.That(result.DatasetId, Is.EqualTo("benchmark-video@v1"));
            });
        });
    }

    #endregion

    #region Tools

    private static async Task WithTemporarilyMovedFilesAsync(IEnumerable<string> sourcePaths, Func<Task> action)
    {
        var moves = new List<(string SourcePath, string BackupPath)>();

        try
        {
            foreach (var sourcePath in sourcePaths)
            {
                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException($"Expected benchmark asset was not found: {sourcePath}", sourcePath);

                var backupPath = sourcePath + ".bak";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);

                File.Move(sourcePath, backupPath);
                moves.Add((sourcePath, backupPath));
            }

            await action();
        }
        finally
        {
            foreach (var move in moves.AsEnumerable().Reverse())
            {
                if (File.Exists(move.SourcePath))
                    File.Delete(move.SourcePath);

                if (File.Exists(move.BackupPath))
                    File.Move(move.BackupPath, move.SourcePath);
            }
        }
    }

    #endregion
}
