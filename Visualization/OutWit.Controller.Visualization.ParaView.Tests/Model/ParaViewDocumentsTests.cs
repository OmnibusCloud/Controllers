using System.Reflection;
using System.Text.Json;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;

namespace OutWit.Controller.Visualization.ParaView.Tests.Model;

/// <summary>
/// The committed job document vocabulary (Documents/) must name every published type and the
/// generated C++/Python bindings must carry every struct — the ParaView plugin vendors them.
/// The generator's CI check mode guards staleness; this guards presence and the published set.
/// </summary>
[TestFixture]
public sealed class ParaViewDocumentsTests
{
    private static readonly string[] PublishedTypeIds =
    [
        "paraview.sceneRef@1",
        "paraview.attachmentRef@1",
        "paraview.runtimeRequirement@1",
        "paraview.pluginRequirement@1",
        "paraview.outputOptions@1",
        "paraview.frameSelection@1",
        "paraview.turntable@1",
        "paraview.renderResult@1",
        "paraview.validationReport@1"
    ];

    private string m_documentsDir = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var root = ParaViewTestPaths.FindSolutionRoot();
        if (root == null)
            Assert.Ignore("Solution root not found");

        m_documentsDir = Path.Combine(root, "Visualization", "OutWit.Controller.Visualization.ParaView.Model", "Documents");
        if (!Directory.Exists(m_documentsDir))
            Assert.Ignore("Documents/ not generated yet");
    }

    [Test]
    public void SchemaNamesEveryPublishedTypeTest()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(m_documentsDir, "paraview.schema.json")));
        var types = document.RootElement.GetProperty("x-oc-vocabulary").GetProperty("types")
            .EnumerateArray().Select(me => me.GetString()).ToList();

        Assert.That(types, Is.EquivalentTo(PublishedTypeIds));

        var defs = document.RootElement.GetProperty("$defs");
        foreach (var id in PublishedTypeIds)
            Assert.That(defs.TryGetProperty(id, out _), Is.True, $"$defs lacks {id}");
    }

    [Test]
    public void PublishedAttributesMatchTheFrozenSetTest()
    {
        var annotated = typeof(ParaViewSceneRefData).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributes(inherit: false)
                .Where(me => me.GetType().Name == "JobDocumentContractAttribute")
                .Select(me => me.GetType().GetProperty("TypeId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)?.GetValue(me)?.ToString()))
            .ToList();

        Assert.That(annotated, Is.EquivalentTo(PublishedTypeIds));
    }

    [Test]
    public void CppAndPythonBindingsCarryEveryStructTest()
    {
        var hpp = File.ReadAllText(Path.Combine(m_documentsDir, "paraview_documents.hpp"));
        var py = File.ReadAllText(Path.Combine(m_documentsDir, "paraview_documents.py"));

        Assert.Multiple(() =>
        {
            foreach (var name in new[] { "paraview_scene_ref", "paraview_attachment_ref", "paraview_output_options", "paraview_frame_selection", "paraview_render_result", "paraview_validation_report", "paraview_runtime_requirement", "paraview_plugin_requirement" })
                Assert.That(hpp, Does.Contain($"struct {name}"), $"hpp lacks {name}");

            foreach (var name in new[] { "ParaviewSceneRef", "ParaviewAttachmentRef", "ParaviewOutputOptions", "ParaviewFrameSelection", "ParaviewRenderResult", "ParaviewValidationReport", "ParaviewRuntimeRequirement", "ParaviewPluginRequirement" })
                Assert.That(py, Does.Contain($"class {name}"), $"py lacks {name}");
        });
    }
}
