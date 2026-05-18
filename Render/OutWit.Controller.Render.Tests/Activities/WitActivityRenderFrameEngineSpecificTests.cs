using MemoryPack;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.References;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class WitActivityRenderFrameEngineSpecificTests
{
    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(false, null, controllersPath);
    }

    #endregion

    #region Tests

    [TestCase(typeof(WitActivityRenderFrame), "Render.Frame")]
    [TestCase(typeof(WitActivityRenderFrameCycles), "Render.Frame.Cycles")]
    [TestCase(typeof(WitActivityRenderFrameEevee), "Render.Frame.Eevee")]
    [TestCase(typeof(WitActivityRenderFrameGreasePencil), "Render.Frame.GreasePencil")]
    public void ConstructorsAndToStringTest(Type activityType, string activityName)
    {
        var activity = CreateActivity(activityType, null);

        Assert.Multiple(() =>
        {
            Assert.That(activity.StagesCount, Is.EqualTo(1));
            Assert.That(activity.ReturnReference, Is.Null);
            Assert.That(GetTask(activity), Was.EqualTo(null));
            Assert.That(activity.ToString(), Does.Contain(activityName));
        });

        var configured = CreateActivity(activityType, (WitReference)"task");
        configured.SetReturnReference("result");

        Assert.Multiple(() =>
        {
            Assert.That(configured.StagesCount, Is.EqualTo(1));
            Assert.That(configured.ReturnReference, Is.EqualTo("result"));
            Assert.That(GetTask(configured), Was.EqualTo((WitReference)"task"));
            Assert.That(configured.ToString(), Does.Contain(activityName));
            Assert.That(configured.ToString(), Does.Contain("task"));
            Assert.That(configured.ToString(), Does.Contain("result"));
        });
    }

    [TestCase(typeof(WitActivityRenderFrame), "Render.Frame")]
    [TestCase(typeof(WitActivityRenderFrameCycles), "Render.Frame.Cycles")]
    [TestCase(typeof(WitActivityRenderFrameEevee), "Render.Frame.Eevee")]
    [TestCase(typeof(WitActivityRenderFrameGreasePencil), "Render.Frame.GreasePencil")]
    public void IsCloneAndMemoryPackCloneTest(Type activityType, string activityName)
    {
        var activity = CreateActivity(activityType, (WitReference)"task");
        activity.SetReturnReference("result");

        var clone = activity.Clone();
        var memoryPackClone = MemoryPackCloneActivity(activity);
        var differentTask = CreateActivity(activityType, (WitReference)"task2");
        differentTask.SetReturnReference("result");

        Assert.Multiple(() =>
        {
            Assert.That(clone, Was.EqualTo(activity), $"Clone equality failed for {activityName}.");
            Assert.That(memoryPackClone, Was.EqualTo(activity), $"MemoryPack clone equality failed for {activityName}.");
            Assert.That(differentTask, Was.Not.EqualTo(activity), $"Task-sensitive equality failed for {activityName}.");
        });
    }

    [TestCase(typeof(WitActivityRenderFrame), "Render.Frame")]
    [TestCase(typeof(WitActivityRenderFrameCycles), "Render.Frame.Cycles")]
    [TestCase(typeof(WitActivityRenderFrameEevee), "Render.Frame.Eevee")]
    [TestCase(typeof(WitActivityRenderFrameGreasePencil), "Render.Frame.GreasePencil")]
    public void HasExpectedAttributesTest(Type activityType, string activityName)
    {
        var activityAttribute = activityType
            .GetCustomAttributes(typeof(ActivityAttribute), false)
            .OfType<ActivityAttribute>()
            .SingleOrDefault();
        var memoryPackableAttribute = activityType
            .GetCustomAttributes(typeof(MemoryPackableAttribute), false);

        Assert.Multiple(() =>
        {
            Assert.That(activityAttribute, Is.Not.Null);
            Assert.That(activityAttribute!.Type, Is.EqualTo(activityName));
            Assert.That(memoryPackableAttribute, Has.Length.EqualTo(1));
        });
    }

    [TestCase(typeof(WitActivityRenderFrame), "Render.Frame")]
    [TestCase(typeof(WitActivityRenderFrameCycles), "Render.Frame.Cycles")]
    [TestCase(typeof(WitActivityRenderFrameEevee), "Render.Frame.Eevee")]
    [TestCase(typeof(WitActivityRenderFrameGreasePencil), "Render.Frame.GreasePencil")]
    public void ParseActivityTest(Type activityType, string activityName)
    {
        var script =
            "Job:Diag(RenderTask:task)\n" +
            "{\n" +
            $"    RenderResult:info = {activityName}(task);\n" +
            "}";

        var job = WitEngineSdk.Instance.Compile(script);
        var expected = CreateActivity(activityType, (WitReference)"task");
        expected.SetReturnReference("info");

        Assert.Multiple(() =>
        {
            Assert.That(job.MemoryPackClone(), Was.EqualTo(job));
            Assert.That(job.Activities.Count, Is.EqualTo(1));
            Assert.That(job.Activities.Single(), Was.EqualTo(expected));
            Assert.That(job.Variables["info"], Is.Not.Null);
        });
    }

    [TestCase(typeof(WitActivityRenderFrame), "Render.Frame")]
    [TestCase(typeof(WitActivityRenderFrameCycles), "Render.Frame.Cycles")]
    [TestCase(typeof(WitActivityRenderFrameEevee), "Render.Frame.Eevee")]
    [TestCase(typeof(WitActivityRenderFrameGreasePencil), "Render.Frame.GreasePencil")]
    public void ParseActivityWrongParametersTest(Type activityType, string activityName)
    {
        var script =
            "Job:Diag(RenderTask:task)\n" +
            "{\n" +
            $"    RenderResult:info = {activityName}();\n" +
            "}";

        var ex = Assert.Throws<ArgumentException>(() => WitEngineSdk.Instance.Compile(script));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain(activityName));
    }

    #endregion

    #region Tools

    private static WitActivityFunction CreateActivity(Type activityType, IWitParameter? task)
    {
        if (activityType == typeof(WitActivityRenderFrame))
            return new WitActivityRenderFrame { Task = task };

        if (activityType == typeof(WitActivityRenderFrameCycles))
            return new WitActivityRenderFrameCycles { Task = task };

        if (activityType == typeof(WitActivityRenderFrameEevee))
            return new WitActivityRenderFrameEevee { Task = task };

        if (activityType == typeof(WitActivityRenderFrameGreasePencil))
            return new WitActivityRenderFrameGreasePencil { Task = task };

        throw new NotSupportedException($"Unsupported activity type: {activityType.FullName}");
    }

    private static IWitParameter? GetTask(IWitActivity activity)
    {
        return activity switch
        {
            WitActivityRenderFrame renderFrame => renderFrame.Task,
            WitActivityRenderFrameCycles renderFrameCycles => renderFrameCycles.Task,
            WitActivityRenderFrameEevee renderFrameEevee => renderFrameEevee.Task,
            WitActivityRenderFrameGreasePencil renderFrameGreasePencil => renderFrameGreasePencil.Task,
            _ => throw new NotSupportedException($"Unsupported activity instance type: {activity.GetType().FullName}")
        };
    }

    private static WitActivityFunction MemoryPackCloneActivity(WitActivityFunction activity)
    {
        return activity switch
        {
            WitActivityRenderFrame renderFrame => renderFrame.MemoryPackClone(),
            WitActivityRenderFrameCycles renderFrameCycles => renderFrameCycles.MemoryPackClone(),
            WitActivityRenderFrameEevee renderFrameEevee => renderFrameEevee.MemoryPackClone(),
            WitActivityRenderFrameGreasePencil renderFrameGreasePencil => renderFrameGreasePencil.MemoryPackClone(),
            _ => throw new NotSupportedException($"Unsupported activity instance type: {activity.GetType().FullName}")
        };
    }

    #endregion
}
