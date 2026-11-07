using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Visualization.ParaView.Tests.Activities;

/// <summary>
/// Every bundled ParaView script compiles against the loaded controllers (ParaView + Variables +
/// Grid + Render for the video script) — the activity names, parameter arities and variable types
/// the scripts use are exactly the ones the module registers.
/// </summary>
[TestFixture]
public sealed class ParaViewScriptsCompileTests
{
    #region Fields

    private string m_scriptsPath = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var controllersPath = ParaViewTestPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");
        m_scriptsPath = ParaViewTestPaths.FindBundledScriptsPath()
                        ?? throw new DirectoryNotFoundException("@Scripts directory not found");

        WitEngineSdk.Instance.Reload(false, null, controllersPath);
    }

    #endregion

    #region Tests

    [TestCase("RenderParaViewFrames.wit", new[] { "scene", "options", "report", "tasks", "rendered", "result" })]
    [TestCase("RenderParaViewStill.wit", new[] { "scene", "options", "report", "tasks", "rendered", "result" })]
    [TestCase("RenderParaViewVideo.wit", new[] { "scene", "options", "video", "report", "tasks", "rendered", "frames", "result" })]
    [TestCase("ValidateParaViewScene.wit", new[] { "scene", "options", "result" })]
    [TestCase("RenderParaViewDataFrames.wit", new[] { "data", "options", "scene", "report", "tasks", "rendered", "result" })]
    [TestCase("RenderParaViewDataStill.wit", new[] { "data", "options", "scene", "report", "tasks", "rendered", "result" })]
    [TestCase("RenderParaViewDataVideo.wit", new[] { "data", "options", "video", "scene", "report", "tasks", "rendered", "frames", "result" })]
    [TestCase("ValidateParaViewData.wit", new[] { "data", "options", "scene", "result" })]
    public void BundledScriptCompilesTest(string fileName, string[] expectedVariables)
    {
        var path = Path.Combine(m_scriptsPath, fileName);
        Assert.That(File.Exists(path), Is.True, $"{fileName} is not staged at {m_scriptsPath}");

        var job = WitEngineSdk.Instance.Compile(File.ReadAllText(path));

        Assert.That(job, Is.Not.Null);
        Assert.That(job.Activities.Count, Is.GreaterThan(0));
        Assert.That(job.Variables.Select(me => me.Name), Is.SupersetOf(expectedVariables));
    }

    [Test]
    public void InlineActivitiesParseWithTheirAritiesTest()
    {
        var job = WitEngineSdk.Instance.Compile("""
            Job:Arity(ParaViewSceneRef:scene, ParaViewOutputOptions:options)
            {
                ParaViewValidationReport:report = ParaView.Validate(scene, options);
                ParaViewRenderTaskCollection:tasks = ParaView.Split(scene, report, options);
                ParaViewRenderResultCollection:rendered = Grid.ForEach(task in tasks)
                    => ParaView.RenderFrame(task);
                BlobCollection:frames = ParaView.Collect(rendered, options);
                Blob:still = ParaView.CollectStill(rendered, options);
            }
            """);

        Assert.That(job.Activities.Count, Is.EqualTo(5));
    }

    #endregion
}
