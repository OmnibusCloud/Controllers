using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

[TestFixture]
public sealed class SmtLiveDiagnosticsTests
{
    #region Fields

    private string m_root = null!;
    private SmtLiveServerFake m_server = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = SmtLiveFixtures.NewFolder();
        m_server = new SmtLiveServerFake();
        m_output = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        TestContext.Out.WriteLine(m_output.ToString());
        SmtLiveFixtures.Delete(m_root);
        m_output.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public async Task DiagnosticsPrintsTheControllerAndSavesTheCapabilitiesTest()
    {
        Answer(File.ReadAllBytes(SmtLiveFixtures.Capabilities()));
        var folder = Path.Combine(m_root, "diagnostics");

        var exit = await new SmtLiveDiagnostics(m_server, m_output).RunAsync(folder, CancellationToken.None);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
            Assert.That(m_server.Submitted.Single().Script, Is.EqualTo("SmtRuntimeDiagnostics"));
            Assert.That(m_server.Submitted.Single().Parameters, Is.Empty);
            Assert.That(File.ReadAllBytes(Path.Combine(folder, SmtLiveDocuments.CAPABILITIES.File)), Is.EqualTo(File.ReadAllBytes(SmtLiveFixtures.Capabilities())));
            Assert.That(output, Does.Contain("controller  : 0.3.0, profile z3-5.4.1, solver 5.4.1"));
            Assert.That(output, Does.Contain("present in the server's process: yes; compiled there: no"));
            Assert.That(output, Does.Contain("degree      : 4 session(s) at once (memory)"));
            Assert.That(output, Does.Contain("valid (smt-capabilities.schema.json)"));
            Assert.That(output, Does.Contain("queries     : check, enumerate, prove"));
            Assert.That(output, Does.Contain("node        : min_ram_mb 8192, max_degree 16"), "the least memory of a node's machine");
            Assert.That(output, Does.Contain("pool        : 3 node(s), 3 measured, 12 session(s) at once, rate 41.5"));
        });
    }

    [Test]
    public async Task CapabilitiesThatDoNotHoldToTheirSchemaFailTest()
    {
        var path = Path.Combine(m_root, "capabilities.json");
        File.Copy(SmtLiveFixtures.Capabilities(), path);
        SmtLiveFixtures.Edit(path, root => root.Remove("platforms"));
        Answer(File.ReadAllBytes(path));

        var exit = await new SmtLiveDiagnostics(m_server, m_output).RunAsync(null, CancellationToken.None);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("DOES NOT HOLD TO smt-capabilities.schema.json").And.Contain("$: lacks 'platforms'"));
    }

    [Test]
    public async Task DiagnosticsWithoutCapabilitiesFailTest()
    {
        Answer(null);

        var exit = await new SmtLiveDiagnostics(m_server, m_output).RunAsync(null, CancellationToken.None);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("capabilities: none came back"));
    }

    #endregion

    #region Tools

    private void Answer(byte[]? capabilities)
    {
        var diagnostics = new SmtRuntimeDiagnosticsData
        {
            ControllerVersion = "0.3.0",
            ProfileId = "z3-5.4.1",
            SolverVersion = "5.4.1",
            ModuleFile = "wasm/z3-5.4.1.wasm",
            ModuleSha256 = new string('1', 64),
            ModuleSize = 30_204_160,
            ModulePresent = true,
            NativesVersion = "1.0.0",
            NativesSha256 = new string('3', 64),
            NativesPinned = true,
            WasmtimeBinding = "1.0.0",
            TrapMode = "signals",
            Platform = "linux-x64",
            LogicalCores = 16,
            AvailableMemoryBytes = 16L * 1024 * 1024 * 1024,
            SessionMemoryBytes = 2752L * 1024 * 1024,
            Degree = 4,
            DegreeReason = "memory"
        };

        var values = new Dictionary<string, object?> { ["result"] = diagnostics };
        if (capabilities != null)
            values[SmtLiveDocuments.CAPABILITIES.Variable] = (Guid?)m_server.Add(capabilities);

        m_server.Jobs = (_, _) => SmtLiveJobFake.Completed(values);
    }

    #endregion
}
