using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public class RenderCollectTests
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var controllersPath = FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(false, null, controllersPath);
    }

    #endregion

    #region Parse Tests

    // Tools at end of file

    #region Tools

    private static string? FindControllersPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "@Controllers", "Debug");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    #endregion

    [Test]
    public void ParseRenderCollectActivityTest()
    {
        var script = """
                     Job:TestJob(RenderResultCollection:rendered, RenderOptions:opts)
                     {
                         BlobCollection:result = Render.Collect(rendered, opts);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
    }

    [Test]
    public void ParseRenderCollectStillActivityTest()
    {
        var script = """
                     Job:TestJob(RenderResultCollection:rendered, RenderOptions:opts)
                     {
                         Blob:result = Render.CollectStill(rendered, opts);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
    }

    #endregion

    #region Processing Tests

    [Test]
    public async Task CollectSortsResultsByIndexTest()
    {
        var script = """
                     Job:CollectTest(RenderResultCollection:rendered, RenderOptions:options)
                     {
                         BlobCollection:result = Render.Collect(rendered, options);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        // Create results out of order
        var blob1 = Guid.NewGuid();
        var blob2 = Guid.NewGuid();
        var blob3 = Guid.NewGuid();

        var results = new List<RenderResultData?>
        {
            new() { Index = 2, ImageBlobId = blob3 },
            new() { Index = 0, ImageBlobId = blob1 },
            new() { Index = 1, ImageBlobId = blob2 }
        };

        var options = new RenderOptionsData();

        var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, results, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var collected = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(collected, Is.Not.Null);
        Assert.That(collected!.Count, Is.EqualTo(3));

        // Should be sorted by Index: blob1, blob2, blob3
        Assert.That(collected[0], Is.EqualTo(blob1));
        Assert.That(collected[1], Is.EqualTo(blob2));
        Assert.That(collected[2], Is.EqualTo(blob3));
    }

    [Test]
    public async Task CollectStillReturnsSingleBlobTest()
    {
        var script = """
                     Job:CollectStillTest(RenderResultCollection:rendered, RenderOptions:options)
                     {
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);
        var blob = Guid.NewGuid();
        var results = new List<RenderResultData?>
        {
            new() { Index = 0, ImageBlobId = blob }
        };

        var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, results, new RenderOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
        Assert.That(job.Variables["result"].Value, Is.EqualTo(blob));
    }

    [Test]
    public async Task CollectStillFailsForMultipleResultsTest()
    {
        var script = """
                     Job:CollectStillTest(RenderResultCollection:rendered, RenderOptions:options)
                     {
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);
        var results = new List<RenderResultData?>
        {
            new() { Index = 0, ImageBlobId = Guid.NewGuid() },
            new() { Index = 1, ImageBlobId = Guid.NewGuid() }
        };

        var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, results, new RenderOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("exactly one render result"));
    }

    #endregion
}
