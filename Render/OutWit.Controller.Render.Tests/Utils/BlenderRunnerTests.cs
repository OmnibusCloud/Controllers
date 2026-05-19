using System.Runtime.InteropServices;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Controller.Render.Variables;

namespace OutWit.Controller.Render.Tests.Utils;

[TestFixture]
public class BlenderRunnerTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witcloud_blender_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_testDir))
            Directory.Delete(m_testDir, recursive: true);
    }

    #endregion

    #region Availability Tests

    [Test]
    public void IsAvailableReturnsFalseForMissingBlenderTest()
    {
        var runner = new BlenderRunner(m_testDir, NullLogger.Instance);
        Assert.That(runner.IsAvailable, Is.False);
    }

    [Test]
    public void IsAvailableReturnsTrueWhenBlenderExistsTest()
    {
        var blenderPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(m_testDir, "blender.exe")
            : Path.Combine(m_testDir, "blender");

        File.WriteAllText(blenderPath, "fake blender");

        var runner = new BlenderRunner(m_testDir, NullLogger.Instance);
        Assert.That(runner.IsAvailable, Is.True);
    }

    #endregion

    #region Requirement Attribute Tests

    [Test]
    public void RenderFrameActivityHasResourceRequirementsTest()
    {
        var activityType = typeof(OutWit.Controller.Render.Activities.WitActivityRenderFrame);
        var attributes = activityType.GetCustomAttributes(typeof(Engine.Data.Attributes.RequiresResourcesAttribute), false);

        Assert.That(attributes, Has.Length.EqualTo(1));

        var requirement = (Engine.Data.Attributes.RequiresResourcesAttribute)attributes[0];
        Assert.That(requirement.MinRamMb, Is.GreaterThanOrEqualTo(4096));
        Assert.That(requirement.MinTempStorageMb, Is.GreaterThanOrEqualTo(10240));
    }

    [Test]
    public void RenderFrameActivityAllowsDesktopOsOnlyTest()
    {
        var activityType = typeof(OutWit.Controller.Render.Activities.WitActivityRenderFrame);
        var osAttributes = activityType.GetCustomAttributes(typeof(Engine.Data.Attributes.RequiresOsAttribute), false);

        Assert.That(osAttributes, Has.Length.EqualTo(1));

        var requirement = (Engine.Data.Attributes.RequiresOsAttribute)osAttributes[0];
        Assert.That(requirement.Platform, Does.Contain("Windows"));
        Assert.That(requirement.Platform, Does.Contain("Linux"));
        Assert.That(requirement.Platform, Does.Contain("OSX"));
        Assert.That(requirement.Platform, Does.Not.Contain("Android"));
    }

    [Test]
    public void RenderFrameActivityCanRunInParallelOnClientTest()
    {
        var activityType = typeof(OutWit.Controller.Render.Activities.WitActivityRenderFrame);
        var parallelAttrs = activityType.GetCustomAttributes(
            typeof(Engine.Data.Attributes.CanRunInParallelOnClientAttribute), false);

        Assert.That(parallelAttrs, Has.Length.EqualTo(1));

        var attr = (Engine.Data.Attributes.CanRunInParallelOnClientAttribute)parallelAttrs[0];
        Assert.That(attr.CanRunInParallel, Is.False,
            "Render.Frame should NOT run in parallel on client — Blender captures all CPU/GPU resources");
    }

    [Test]
    public void RenderFrameActivityHasNoGpuRequirementTest()
    {
        var activityType = typeof(OutWit.Controller.Render.Activities.WitActivityRenderFrame);
        var gpuAttributes = activityType.GetCustomAttributes(typeof(Engine.Data.Attributes.RequiresGpuAttribute), false);

        Assert.That(gpuAttributes, Is.Empty, "Render.Frame should not require GPU — CPU rendering is supported");
    }

    [TestCase(RenderEngine.Cycles, "CYCLES")]
    [TestCase(RenderEngine.Eevee, "BLENDER_EEVEE_NEXT")]
    [TestCase(RenderEngine.GreasePencil, "BLENDER_EEVEE_NEXT")]
    public void BlenderRunnerMapsRenderEngineToExpectedBlenderArgumentTest(RenderEngine engine, string expectedArgument)
    {
        // The argument-mapping helper was extracted from BlenderRunner into the
        // pure static BlenderRenderArgsBuilder class. Call it directly — no
        // reflection — since the helper is now internal-public surface.
        var actualArgument = BlenderRenderArgsBuilder.GetBlenderEngineArgument(engine);

        Assert.That(actualArgument, Is.EqualTo(expectedArgument));
    }

    [Test]
    public void BlenderRunnerBuildFailureMessagePrefersStdoutSceneErrorOverPythonWarningTest()
    {
        var method = typeof(BlenderRunner).GetMethod(
            "BuildFailureMessage",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "Blender failure-message helper was not found.");

        var resultType = typeof(BlenderRunner).Assembly.GetType("OutWit.Controller.Render.Utils.RenderBlenderInvocationResult");
        Assert.That(resultType, Is.Not.Null, "Blender invocation result type was not found.");

        var result = Activator.CreateInstance(resultType!);
        Assert.That(result, Is.Not.Null, "Blender invocation result instance could not be created.");
        resultType!.GetProperty("ExitCode")!.SetValue(result, 1);
        resultType.GetProperty("Stdout")!.SetValue(result, "00:00.438  reports          | ERROR Cannot render, no camera");
        resultType.GetProperty("Stderr")!.SetValue(result, "Unable to find the Python binary, the multiprocessing module may not be functional!");

        var failureMessage = method!.Invoke(null, [result]) as string;

        Assert.That(failureMessage, Is.EqualTo("Cannot render, no camera"));
    }

    [Test]
    public void BlenderRunnerBuildFailureMessageIncludesDistinctStdoutAndStderrDetailsTest()
    {
        var method = typeof(BlenderRunner).GetMethod(
            "BuildFailureMessage",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "Blender failure-message helper was not found.");

        var resultType = typeof(BlenderRunner).Assembly.GetType("OutWit.Controller.Render.Utils.RenderBlenderInvocationResult");
        Assert.That(resultType, Is.Not.Null, "Blender invocation result type was not found.");

        var result = Activator.CreateInstance(resultType!);
        Assert.That(result, Is.Not.Null, "Blender invocation result instance could not be created.");
        resultType!.GetProperty("ExitCode")!.SetValue(result, 1);
        resultType.GetProperty("Stdout")!.SetValue(result, "00:00.438  reports          | ERROR Cannot render, no camera");
        resultType.GetProperty("Stderr")!.SetValue(result, "Traceback: something else failed");

        var failureMessage = method!.Invoke(null, [result]) as string;

        Assert.That(failureMessage, Is.EqualTo("Cannot render, no camera | stderr: Traceback: something else failed"));
    }

    #endregion
}
