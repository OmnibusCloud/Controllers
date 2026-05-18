using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public class RenderSplitTests
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

    #region Parse Tests

    [Test]
    public void ParseRenderSplitActivityTest()
    {
        var script = """
                     Job:TestJob(Blob:scene, Int:start, Int:end, RenderOptions:opts)
                     {
                         RenderTaskCollection:tasks = Render.Split(scene, start, end, opts);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables.Count, Is.EqualTo(5)); // 4 params + 1 result
    }

    #endregion

    #region Processing Tests

    [Test]
    public async Task SplitGeneratesCorrectFrameTasksTest()
    {
        var script = """
                     Job:SplitTest(Blob:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         RenderTaskCollection:tasks = Render.Split(scene, startFrame, endFrame, options);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);
        var sceneId = Guid.NewGuid();
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 64,
            ResolutionX = 1920,
            ResolutionY = 1080
        };

        var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, sceneId, 1, 5, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var tasks = job.Variables["tasks"].Value as IReadOnlyList<RenderTaskData?>;
        Assert.That(tasks, Is.Not.Null);
        Assert.That(tasks!.Count, Is.EqualTo(5));

        for (int i = 0; i < 5; i++)
        {
            var task = tasks[i];
            Assert.That(task, Is.Not.Null);
            Assert.That(task!.SceneBlobId, Is.EqualTo(sceneId));
            Assert.That(task.Frame, Is.EqualTo(i + 1));
            Assert.That(task.TaskIndex, Is.EqualTo(i));
            Assert.That(task.IsFullFrame, Is.True);
            Assert.That(task.Options.Samples, Is.EqualTo(64));
            Assert.That(task.Options.Format, Is.EqualTo(RenderFormat.PNG));
        }
    }

    [Test]
    public async Task SplitSingleFrameTest()
    {
        var script = """
                     Job:SplitSingle(Blob:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         RenderTaskCollection:tasks = Render.Split(scene, startFrame, endFrame, options);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);
        var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(
            job, Guid.NewGuid(), 42, 42, new RenderOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var tasks = job.Variables["tasks"].Value as IReadOnlyList<RenderTaskData?>;
        Assert.That(tasks, Is.Not.Null);
        Assert.That(tasks!.Count, Is.EqualTo(1));
        Assert.That(tasks[0]!.Frame, Is.EqualTo(42));
    }

    [Test]
    public async Task SplitInvalidRangeFailsTest()
    {
        var script = """
                     Job:SplitInvalid(Blob:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         RenderTaskCollection:tasks = Render.Split(scene, startFrame, endFrame, options);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);
        var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(
            job, Guid.NewGuid(), 10, 5, new RenderOptionsData()); // end < start

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
    }

    #endregion
}
