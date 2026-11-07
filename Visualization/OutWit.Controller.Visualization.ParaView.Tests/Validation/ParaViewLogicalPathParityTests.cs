using System.Diagnostics;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Validation;

/// <summary>
/// The host↔runner parity table (audit wave 2): the logical-path rules live in two hand-written
/// copies — <see cref="ParaViewLogicalPath.Check"/> on the host and <c>check_logical_path</c> in
/// <c>render_task.py</c> on the node. One table of inputs runs through both; a value one side
/// accepts and the other refuses is a drift, which is exactly the class of bug a hand-mirrored
/// rule set breeds. Needs a Python 3 on PATH (the runner's module-level imports are stdlib only).
/// </summary>
[TestFixture]
public sealed class ParaViewLogicalPathParityTests
{
    #region Constants

    private static readonly string[] TABLE =
    [
        "data/wavelet.vti",
        "data/series/field_0001.vtu",
        "a.b-c_d/e f/g.pvsm",
        "deep/er/and/deeper/still/file.vtu",
        "",
        "/absolute/path.vtu",
        "C:/drive/letter.vtu",
        "c:\\drive\\letter.vtu",
        "back\\slash.vtu",
        "http://example.com/x.vtu",
        "file:///tmp/x.vtu",
        "data//double.vtu",
        "data/../escape.vtu",
        "../escape.vtu",
        "data/./dot.vtu",
        "data/trailing./x.vtu",
        "data/trailing /x.vtu",
        "data/bad<name>.vtu",
        "data/bad|name.vtu",
        "data/bad:name.vtu",
        "data/bad?name.vtu",
        "data/bad*name.vtu",
        "data/tab\tname.vtu",
        "data/nul\u0000name.vtu",
        "data/ünïcödé.vtu",
        "data/name.",
        "trailingslash/",
        new string('a', 300) + ".vtu",
        new string('a', 1030) + ".vtu",
    ];

    #endregion

    #region Fields

    private string m_python = null!;

    private string m_root = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_python = FindPython() ?? string.Empty;
        if (m_python.Length == 0)
            Assert.Ignore("no python interpreter on PATH");
    }

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"pv_parity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void HostAndRunnerAgreeOnEveryLogicalPathVerdictTest()
    {
        var runnerPath = Path.Combine(m_root, ParaViewRuntimeInfo.RUNNER_FILE_NAME);
        File.WriteAllText(runnerPath, ParaViewRuntimeInfo.ReadEmbeddedText(ParaViewRuntimeInfo.RUNNER_RESOURCE)!);

        // The table travels as JSON so control characters and unicode survive the round trip.
        var tablePath = Path.Combine(m_root, "table.json");
        File.WriteAllText(tablePath, System.Text.Json.JsonSerializer.Serialize(TABLE));

        var driver = Path.Combine(m_root, "parity.py");
        File.WriteAllText(driver, string.Join("\n",
        [
            "import importlib.util, json, sys",
            "spec = importlib.util.spec_from_file_location('render_task', sys.argv[1])",
            "module = importlib.util.module_from_spec(spec)",
            "spec.loader.exec_module(module)",
            "table = json.load(open(sys.argv[2], encoding='utf-8'))",
            "verdicts = [module.check_logical_path(value, module.DEFAULT_MAX_LOGICAL_PATH_CHARS) for value in table]",
            "json.dump(verdicts, sys.stdout)",
            "",
        ]));

        var info = new ProcessStartInfo(m_python, $"\"{driver}\" \"{runnerPath}\" \"{tablePath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
        Assert.That(process.ExitCode, Is.EqualTo(0), stderr);

        var runnerVerdicts = System.Text.Json.JsonSerializer.Deserialize<string?[]>(stdout)!;
        Assert.That(runnerVerdicts, Has.Length.EqualTo(TABLE.Length));

        var driftLines = new List<string>();
        for (var i = 0; i < TABLE.Length; i++)
        {
            var host = ParaViewLogicalPath.Check(TABLE[i]);
            var runner = runnerVerdicts[i];
            if ((host == null) != (runner == null))
                driftLines.Add($"'{Printable(TABLE[i])}': host={(host ?? "accepted")} | runner={(runner ?? "accepted")}");
        }

        Assert.That(driftLines, Is.Empty, "host and runner disagree:\n" + string.Join("\n", driftLines));
        Assert.That(TABLE.Count(value => ParaViewLogicalPath.Check(value) == null), Is.EqualTo(6), "the table's accepted set is the intended one (five plain names and a 300-character one; the 1030-character one is over the limit)");
    }

    [Test]
    public void RunnerMirrorsTheHostLimitAndTheInvalidCharacterSetTest()
    {
        var script = ParaViewRuntimeInfo.ReadEmbeddedText(ParaViewRuntimeInfo.RUNNER_RESOURCE)!;

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain($"DEFAULT_MAX_LOGICAL_PATH_CHARS = {ParaViewInputLimits.MAX_LOGICAL_PATH_CHARS}"),
                "the runner's default path limit mirrors ParaViewInputLimits");
            foreach (var character in new[] { '<', '>', ':', '"', '|', '?', '*' })
                Assert.That(ParaViewLogicalPath.Check($"data/x{character}y.vtu"), Is.Not.Null, $"host refuses '{character}'");
        });
    }

    #endregion

    #region Tools

    private static string Printable(string value)
    {
        return value.Length > 40 ? value[..40] + "…" : value.Replace("\t", "\\t").Replace("\u0000", "\\0");
    }

    private static string? FindPython()
    {
        foreach (var candidate in new[] { "python3", "python" })
        {
            try
            {
                var info = new ProcessStartInfo(candidate, "--version") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var process = Process.Start(info);
                if (process == null)
                    continue;

                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit(10_000);
                if (process.ExitCode == 0 && output.Contains("Python 3", StringComparison.Ordinal))
                    return candidate;
            }
            catch
            {
                // Not on PATH.
            }
        }

        return null;
    }

    #endregion
}
