using System.Collections;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Tests.Mock;
using OutWit.Controller.AI.Verify.Tests.Sandbox;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Scripts;

/// <summary>
/// Nothing of a node reaches a lab. The bundled scripts run end to end through the engine over
/// hostile tasksets and over runs that fail, and every text a lab can fetch afterwards (the
/// report, the outputs, the provenance, the integrity document, the ledger they are cut from, a
/// preflight's messages, the capabilities, the message of a failed job), and everything a node
/// sent to the server on the way, is read for the machine's name, an account, a local folder,
/// the shape of a local path, and the job's tickets.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyAnonymityTests : VerifyScriptTestBase
{
    #region Constants

    private const string PYTHON = VerifyRuntimeCatalog.PYTHON_3_14_6;

    // Everything a Python program can ask about where it is.
    private const string PYTHON_PROBE =
        "import os, sys\n" +
        "print(sys.executable, sys.argv, sys.path, sys.prefix, sys.platform)\n" +
        "print(os.getcwd(), dict(os.environ), os.uname())\n" +
        "for probe in ('/', '/lib', '/proc/self/exe', '/etc/hostname', '..', '../..'):\n" +
        "    try:\n" +
        "        print(probe, os.stat(probe).st_ino, os.path.realpath(probe))\n" +
        "    except OSError as error:\n" +
        "        print(probe, type(error).__name__)\n" +
        "for name, ask in (('socket', 'gethostname'), ('getpass', 'getuser'), ('platform', 'node')):\n" +
        "    try:\n" +
        "        print(name, getattr(__import__(name), ask)())\n" +
        "    except Exception as error:\n" +
        "        print(name, type(error).__name__)\n";

    private const string C_PROBE =
        "#include <stdio.h>\n#include <stdlib.h>\n" +
        "int main(int argc, char **argv) {\n" +
        "    puts(__FILE__); puts(__TIMESTAMP__); puts(argv[0]);\n" +
        "    const char *home = getenv(\"HOME\"); puts(home ? home : \"(no home)\");\n" +
        "    FILE *file = fopen(\"/etc/passwd\", \"r\"); puts(file ? \"opened\" : \"closed\");\n" +
        "    return 0;\n" +
        "}\n";

    #endregion

    #region Fields

    private string m_runScript = null!;
    private string m_preflightScript = null!;
    private string m_diagnosticsScript = null!;
    private string m_generateAndCheckScript = null!;
    private string m_brokenRoot = null!;
    private VerifyTestNodesManager m_nodes = null!;

    #endregion

    #region Setup

    protected override IWitNodesManager CreateNodesManager(IWitEngineNode node)
    {
        return m_nodes = new VerifyTestNodesManager(node);
    }

    [OneTimeSetUp]
    public void Setup()
    {
        m_runScript = LoadScript("VerifyRun.wit");
        m_preflightScript = LoadScript("VerifyPreflight.wit");
        m_diagnosticsScript = LoadScript("VerifyRuntimeDiagnostics.wit");
        m_generateAndCheckScript = LoadScript("VerifyGenerateAndCheck.wit");

        // A node whose Ruby is still being unpacked: the folder is there, the module is not.
        m_brokenRoot = Path.Combine(Path.GetTempPath(), $"witai_verify_anon_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(m_brokenRoot, Path.GetDirectoryName(VerifyRuntimeCatalog.Find(VerifyRuntimeCatalog.RUBY_3_4_1)!.ModulePath)!));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (m_brokenRoot != null && Directory.Exists(m_brokenRoot))
            Directory.Delete(m_brokenRoot, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void TheScanFindsWhatTheAuditFoundTest()
    {
        // The line of the audit, and its like on the other systems: the scan is not blind.
        Assert.Multiple(() =>
        {
            Assert.That(VerifyLeakScan.Find("""{"stderr":"runtime module not found at 'C:\\Users\\someone\\AppData\\Local\\Temp\\x\\python.wasm'"}"""), Is.Not.Empty);
            Assert.That(VerifyLeakScan.Find("runtime module not found at '/home/someone/.local/share/x/python.wasm'"), Is.Not.Empty);
            Assert.That(VerifyLeakScan.Find("runtime module not found at '/Users/someone/Library/x/python.wasm'"), Is.Not.Empty);
            Assert.That(VerifyLeakScan.Find($"made on {Environment.MachineName}"), Is.Not.Empty);
            Assert.That(VerifyLeakScan.Find(Path.Combine(Path.GetTempPath(), "x")), Is.Not.Empty);
            Assert.That(VerifyLeakScan.Find("ticket 0123456789abcdef0123456789abcdef", ["0123456789abcdef0123456789abcdef"]), Is.Not.Empty);
            Assert.That(VerifyLeakScan.Find("""{"index":0,"verdict":"Pass","stdout":"/lib/python314.zip main.c:1:10: http://x"}"""), Is.Empty, "a guest's own paths are the guest's");
        });
    }

    [Test]
    public async Task H4NoDocumentOfAHostileRunSaysAnythingOfANodeTest()
    {
        var job = Engine.Compile(m_runScript);
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(HostileTaskset(), "taskset.jsonl"), new VerifyOptionsData { SamplingRate = 1.0, BatchSize = 4 });
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);

        var tickets = Items<VerifyTaskPackageData>(job.Variables["packages"].Value, job.Variables["repairs"].Value, job.Variables["disputes"].Value)
            .Select(package => package.Ticket).ToList();
        Assert.That(tickets, Is.Not.Empty.And.All.Length.EqualTo(32));

        // What a lab fetches.
        var documents = new Dictionary<string, string>();
        foreach (var name in new[] { "report", "outputs", "provenance", "integrity", "ledger" })
            documents[name] = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(await BlobService.GetLocalPathAsync((Guid)job.Variables[name].Value!)));

        // What the nodes sent to the server.
        var sent = new List<VerifyResultBatchData>();
        foreach (var package in Items<VerifyResultPackageData>(job.Variables["results"].Value, job.Variables["repaired"].Value, job.Variables["settled"].Value))
            sent.AddRange(VerifyResultPackageReader.Read(await BlobService.GetLocalPathAsync(package.ResultsBlobId), int.MaxValue, int.MaxValue));

        using var report = JsonDocument.Parse(documents["report"]);
        var verdicts = report.RootElement.GetProperty("results").EnumerateArray().Select(result => result.GetProperty("verdict").GetString()).ToList();

        Assert.Multiple(() =>
        {
            // The run was a hostile one, and the programs did print what they could find.
            Assert.That(verdicts, Is.SupersetOf(new[] { "Pass", "RuntimeError", "CompileError", "Timeout", "MemoryExceeded", "OutputExceeded" }));
            Assert.That(documents["outputs"], Does.Contain("PYTHONHOME").And.Contain("wasm32"), "the probe ran and was kept");

            foreach (var (name, text) in documents)
                VerifyLeakScan.AssertClean(text, name, tickets);

            Assert.That(sent, Is.Not.Empty);
            foreach (var batch in sent)
            {
                var diagnostics = batch.Diagnostics!;
                Assert.That(diagnostics.NodeId, Is.EqualTo(Guid.Empty));
                Assert.That(diagnostics.NodeName, Is.Empty);
                Assert.That(diagnostics.NativeLibraryPath, Is.Empty);
                Assert.That(diagnostics.Degree, Is.Zero, "L-6: how many programs a node ran at once stays on the node");
                Assert.That(diagnostics.WallMs, Is.Zero, "L-6: and so does how long the batch took it");

                VerifyLeakScan.AssertClean(string.Join('\n', Texts(batch)), "a result batch as a node sent it");
            }
        });
    }

    [Test]
    public async Task H4AJobNoNodeHasTheRuntimeForFailsInFixedWordsTest()
    {
        // The audit's scenario, on every node at once: a runtime whose files are not there yet.
        // Each node refuses the package; with nobody left the job fails, and the message is the
        // lab's to read.
        var ruby = await UploadAsync("""{"runtime":"ruby-3.4.1","sources":{"main.rb":"puts 1"}}""" + "\n", "taskset.jsonl");
        var healthy = Environment.GetEnvironmentVariable(VerifyRuntimeLocator.OverrideEnvVar);

        IWitProcessingStatus status;
        Environment.SetEnvironmentVariable(VerifyRuntimeLocator.OverrideEnvVar, m_brokenRoot);
        try
        {
            status = await Engine.ScheduleAndWaitAsync(Engine.Compile(m_runScript), ruby, new VerifyOptionsData());
        }
        finally
        {
            Environment.SetEnvironmentVariable(VerifyRuntimeLocator.OverrideEnvVar, healthy);
        }

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed), "a missing runtime is no verdict: nobody could run the package");
            VerifyLeakScan.AssertClean(status.Message ?? "", "the message of the failed job");
        });
    }

    [Test]
    public async Task H5ATasksetWithARuntimeNobodyHasIsRefusedByNameBeforeAnyNodeIsAskedTest()
    {
        var taskset = await UploadAsync(
            """{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}}""" + "\n" +
            """{"runtime":"cobol-1959","sources":{"m.cob":"DISPLAY 1"}}""" + "\n", "taskset.jsonl");

        var sharesBefore = m_nodes.Shares;
        var status = await Engine.ScheduleAndWaitAsync(Engine.Compile(m_runScript), taskset, new VerifyOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
            Assert.That(status.Message, Does.Contain("cobol-1959"), "by its name");
            Assert.That(m_nodes.Shares, Is.EqualTo(sharesBefore), "no node was asked");
            VerifyLeakScan.AssertClean(status.Message ?? "", "the message of the refused taskset");
        });
    }

    [Test]
    public async Task H5ACheckerWithARuntimeNobodyHasIsRefusedByNameTest()
    {
        // The checks would be packages no node can run: the checker is refused where the check
        // tasks are made, as a taskset is where it is split.
        var generators = await UploadAsync("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}}""" + "\n", "generators.jsonl");
        var checker = await UploadAsync("""{"runtime":"cobol-1959","sources":{"c.cob":"DISPLAY 1"},"expected_stdout":"1\n"}""", "checker.json");

        var status = await Engine.ScheduleAndWaitAsync(Engine.Compile(m_generateAndCheckScript), generators, checker, new VerifyOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
            Assert.That(status.Message, Does.Contain("cobol-1959"), "by its name");
            VerifyLeakScan.AssertClean(status.Message ?? "", "the message of the refused checker");
        });
    }

    [Test]
    public async Task H4APreflightSaysNothingOfTheHostTest()
    {
        // Everything a preflight has words for: a broken line, names that are not portable, a
        // runtime nobody has, an entry that is not there, a limit over its ceiling, a wall time.
        var taskset = string.Join('\n',
            """{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"limits":{"fuel":9000000000000,"wall_ms":5}}""",
            """{"runtime":"python-3.14.6","sources":{"m.py":""",
            """{"runtime":"lua-5.4.8","sources":{"..\\..\\x.lua":"print(1)"}}""",
            """{"runtime":"lua-5.4.8","sources":{"caf\u00e9.lua":"print(1)"}}""",
            """{"runtime":"cobol-1959 $(hostname) %USERNAME%","sources":{"m.cob":"DISPLAY 1"}}""",
            """{"runtime":"python-3.14.6","entry":"other.py","sources":{"m.py":"print(1)"}}""",
            """{"runtime":"wasi","sources":{"main.wasm":"not base64 !"}}""",
            """[1, 2, 3]""") + "\n";

        var job = Engine.Compile(m_preflightScript);
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(taskset, "taskset.jsonl"), new VerifyOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        var report = (VerifyPreflightData)job.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(report.WellFormed, Is.False);
            Assert.That(report.Messages, Has.Count.GreaterThan(5));
            VerifyLeakScan.AssertClean(string.Join('\n', report.Messages.Concat(report.RuntimeIds).Concat(report.UnknownRuntimeIds)), "the preflight's words");
        });
    }

    [Test]
    public async Task H4TheCapabilitiesSayNothingOfTheHostTest()
    {
        var job = Engine.Compile(m_diagnosticsScript);
        var status = await Engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        var diagnostics = (VerifyRuntimeDiagnosticsData)job.Variables["result"].Value!;
        var capabilities = await ReadTextAsync((Guid)job.Variables["capabilities"].Value!);

        Assert.Multiple(() =>
        {
            Assert.That(capabilities, Does.Contain(VerifyCapabilitiesWriter.SCHEMA));
            VerifyLeakScan.AssertClean(capabilities, "the capabilities document");
            VerifyLeakScan.AssertClean(string.Join('\n', diagnostics.Runtimes.Select(runtime => $"{runtime.RuntimeId} {runtime.Sha256}").Append(diagnostics.RuntimeTarget).Append(diagnostics.WasmtimeVersion)), "the runtime diagnostics");
        });
    }

    #endregion

    #region Tools

    private static string HostileTaskset()
    {
        var wildPointer = Convert.ToBase64String(HostileModules.ExitWith("random_get", -8, 64));
        var noModule = Convert.ToBase64String(Encoding.ASCII.GetBytes("this is no module at all"));

        return string.Join('\n',
            Line(0, PYTHON, "main.py", PYTHON_PROBE),
            Line(1, PYTHON, "main.py", "raise RuntimeError(open('/proc/self/environ').read())"),
            Line(2, PYTHON, "main.py", "while True: print('x' * 1000)"),
            Line(3, PYTHON, "main.py", "while True: pass", fuel: 300_000_000),
            Line(4, PYTHON, "main.py", "b = bytearray(512 * 1024 * 1024)\nprint(len(b))"),
            Line(5, VerifyRuntimeCatalog.CLANG_22_1_0, "main.c", "#include \"nothere.h\"\nint main(void) { return 0; }\n"),
            Line(6, VerifyRuntimeCatalog.CLANG_22_1_0, "main.c", C_PROBE),
            Line(7, VerifyRuntimeCatalog.WASI, "main.wasm", wildPointer),
            Line(8, VerifyRuntimeCatalog.WASI, "main.wasm", noModule),
            Line(9, VerifyRuntimeCatalog.LUA_5_4_8, "main.lua", "print(arg[0], os.getenv('HOME'), os.getenv('USERPROFILE'), io.open('/etc/passwd'))\nerror('boom')"),
            Line(10, VerifyRuntimeCatalog.RUBY_3_4_1, "main.rb", "puts __FILE__, Dir.pwd, ENV.to_h.inspect, $0\nraise 'boom'"),
            Line(11, VerifyRuntimeCatalog.QUICKJS_0_15_1, "main.js", "console.log(typeof scriptArgs === 'undefined' ? 'none' : scriptArgs);\nthrow new Error('boom');"),
            Line(12, VerifyRuntimeCatalog.TYPESCRIPT_SUCRASE_3_35_1, "main.ts", "const x: number = ;"),
            Line(13, PYTHON, "main.py", "print(input())", stdin: "hello\n", expected: "hello\n")) + "\n";
    }

    private static string Line(int index, string runtime, string entry, string source, long fuel = 0, string? stdin = null, string? expected = null)
    {
        var line = new Dictionary<string, object>
        {
            ["index"] = index,
            ["runtime"] = runtime,
            ["entry"] = entry,
            ["sources"] = new Dictionary<string, string> { [entry] = source }
        };

        if (fuel > 0)
            line["limits"] = new Dictionary<string, long> { ["fuel"] = fuel };

        if (expected != null)
            line["suite"] = new[] { new Dictionary<string, string> { ["stdin"] = stdin ?? "", ["expected_stdout"] = expected } };

        return JsonSerializer.Serialize(line);
    }

    /// <summary>The packages the values of a job's variables hold; a variable that was never set holds none.</summary>
    private static List<T> Items<T>(params object?[] collections)
    {
        return collections
            .OfType<IEnumerable>()
            .SelectMany(collection => collection.OfType<T>())
            .ToList();
    }

    /// <summary>Every text of a result batch.</summary>
    private static IEnumerable<string> Texts(VerifyResultBatchData batch)
    {
        var diagnostics = batch.Diagnostics!;
        yield return diagnostics.RuntimeTarget;
        yield return diagnostics.WasmtimeVersion;
        yield return diagnostics.RuntimeId;
        yield return diagnostics.RuntimeSha256;
        yield return diagnostics.ControllerVersion;

        foreach (var result in batch.Results)
        {
            yield return result.Stdout;
            yield return result.Stderr;
            yield return result.Fault;

            foreach (var item in result.CaseResults)
                yield return item.ActualStdout;
        }
    }

    #endregion
}
