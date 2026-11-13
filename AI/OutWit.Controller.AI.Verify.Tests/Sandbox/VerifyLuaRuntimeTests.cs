using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The Lua runtime: the stock interpreter built without WebAssembly's exception handling (an
/// error unwinds to its protected call by asyncify). A program is its sources as files in a
/// read-only folder that is the guest's whole file system, so arg, ... and require of a
/// neighbour work; errors, coroutines and their mix behave as in Lua; a run is reproducible to
/// the fuel unit; the limits hold; the folder is gone after the task; a changed module does not
/// resolve.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyLuaRuntimeTests : VerifySandboxTestBase
{
    #region Constants

    private const string LUA_DIR = "lua-5.4.8";
    private const string LUA_MODULE = "lua.wasm";
    private static readonly string DEMO_TASKSET = Path.Combine("OutWit.Controller.AI.Verify.Live", "tasksets", "lua-demo.jsonl");

    /// <summary>A start is a fraction of a million units; anything near this is wrong.</summary>
    private const long HELLO_FUEL_CEILING = 50_000_000;

    private const string LIBRARY_PROGRAM =
        "print(string.format('%5.2f|%d|%q', math.pi, 2^53 // 1, 'a\\nb'))\n" +
        "local t = { 5, 3, 9, 1 } table.sort(t) print(table.concat(t, ','))\n" +
        "print(('hello world'):gsub('o', '0'))\n" +
        "print(utf8.len('héllo'), utf8.char(10003))\n" +
        "print(string.unpack('<i4', string.pack('<i4', -7)))\n" +
        "print(math.maxinteger, 7 // 2, 7 % -3, 1 << 62, math.type(1), math.type(1.0))\n";

    private const string LIBRARY_EXPECTED =
        " 3.14|9007199254740992|\"a\\\nb\"\n" +
        "1,3,5,9\n" +
        "hell0 w0rld\t2\n" +
        "5\t✓\n" +
        "-7\t5\n" +
        "9223372036854775807\t3\t-2\t4611686018427387904\tinteger\tfloat\n";

    // Every way an error travels in Lua, with the exception-handling feature off: pcall,
    // xpcall with a handler, an error object, nested protected calls, an error inside a
    // coroutine, a yield across pcall, a to-be-closed variable closed by an error.
    private const string ERRORS_PROGRAM =
        "print(pcall(error, 'plain'))\n" +
        "print(select(2, pcall(error, { code = 7 })).code)\n" +
        "print(select(2, pcall(error, setmetatable({}, { __tostring = function() return 'object' end }))))\n" +
        "print(xpcall(function() error('deep') end, function(m) return 'handled: ' .. m end))\n" +
        "print(pcall(function() return pcall(function() error('inner', 0) end) end))\n" +
        "print(pcall(function() local t = nil; return t.x end))\n" +
        "local co = coroutine.create(function() coroutine.yield(1); error('in coroutine', 0) end)\n" +
        "print(coroutine.resume(co)) print(coroutine.resume(co)) print(coroutine.status(co))\n" +
        "local gen = coroutine.wrap(function() for i = 1, 3 do pcall(coroutine.yield, i) end end)\n" +
        "print(gen(), gen(), gen())\n" +
        "local closed = {}\n" +
        "print(pcall(function() local x <close> = setmetatable({}, { __close = function(_, e) closed[#closed + 1] = tostring(e) end }); error('closing', 0) end))\n" +
        "print(closed[1])\n" +
        "local n = 0 for i = 1, 10000 do if not pcall(error, i) then n = n + 1 end end print(n)\n";

    private const string ERRORS_EXPECTED =
        "false\tplain\n" +
        "7\n" +
        "object\n" +
        "false\thandled: main.lua:4: deep\n" +
        "true\tfalse\tinner\n" +
        "false\tmain.lua:6: attempt to index a nil value (local 't')\n" +
        "true\t1\n" +
        "false\tin coroutine\n" +
        "dead\n" +
        "1\t2\t3\n" +
        "false\tclosing\n" +
        "closing\n" +
        "10000\n";

    #endregion

    #region Program Tests

    [Test]
    public void HelloRunsSilentlyTest()
    {
        var result = Run(Lua, LuaTask("print('hello')"));
        TestContext.Out.WriteLine($"hello: fuel {result.FuelConsumed:N0}, peak memory {result.PeakMemoryBytes:N0} bytes");

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("hello\n"));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(result.FuelConsumed, Is.LessThan(HELLO_FUEL_CEILING));
        });
    }

    [Test]
    public void TheStandardLibraryIsThereTest()
    {
        var result = Run(Lua, LuaTask(LIBRARY_PROGRAM));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo(LIBRARY_EXPECTED));
        });
    }

    [Test]
    public void ErrorsTravelWithoutTheExceptionsFeatureTest()
    {
        var result = Run(Lua, LuaTask(ERRORS_PROGRAM));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo(ERRORS_EXPECTED));
        });
    }

    [Test]
    public void ArgumentsReachTheProgramEvenWithALeadingDashTest()
    {
        var result = Run(Lua, LuaTask("print(#arg, arg[0], arg[1], arg[2], ...)", args: ["-x", "two words"]));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("2\tmain.lua\t-x\ttwo words\t-x\ttwo words\n"));
        });
    }

    [Test]
    public void StdinReachesTheProgramTest()
    {
        var result = Run(Lua, LuaTask("local total = 0 for line in io.lines() do total = total + tonumber(line) end print(total)", stdin: "2\n3\n37\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("42\n"));
        });
    }

    [Test]
    public void ANeighbourAndAFolderModuleAreRequiredTest()
    {
        var task = LuaTask(
            "local helper = require('helper')\nlocal util = require('lib.util')\nprint(helper.twice(util.base))",
            more:
            [
                new VerifySourceFileData { Name = "helper.lua", Content = "return { twice = function(x) return 2 * x end }" },
                new VerifySourceFileData { Name = "lib/util.lua", Content = "return { base = 21 }" }
            ]);

        var result = Run(Lua, task);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("42\n"));
        });
    }

    [Test]
    public void SuiteCasesAreJudgedByStdoutAndExitTest()
    {
        var suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Stdin = "2 3", ExpectedStdout = "5\n" },
                new VerifySuiteCaseData { Stdin = "10 -4", ExpectedStdout = "6\n" },
                new VerifySuiteCaseData { Stdin = "1 1", ExpectedStdout = "3\n" }
            ]
        };

        var result = Run(Lua, LuaTask("local a, b = io.read('n', 'n') print(a + b)", suite: suite));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Fail), "the third case expects a wrong sum");
    }

    [Test]
    public void AnUncaughtErrorIsARuntimeErrorTest()
    {
        var result = Run(Lua, LuaTask("error('boom')"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Stderr, Does.Contain("main.lua:1: boom").And.Contain("stack traceback"));
        });
    }

    [Test]
    public void ASyntaxErrorIsARuntimeErrorTest()
    {
        var result = Run(Lua, LuaTask("print('unclosed"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.Stderr, Does.Contain("main.lua:1: unfinished string"));
        });
    }

    [Test]
    public void TheExitCodeIsTheProgramsTest()
    {
        var result = Run(Lua, LuaTask("print('before')\nos.exit(3)"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.Stdout, Is.EqualTo("before\n"));
        });
    }

    [Test]
    public void ABadSourceNameIsTheTasksVerdictTest()
    {
        var task = LuaTask("print(1)", more: [new VerifySourceFileData { Name = "../escape.lua", Content = "" }]);

        var result = Run(Lua, task);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Does.Contain("../escape.lua").And.Contain("'..'"));
        });
    }

    // A name must mean the same file on every node of the fleet, Windows included.
    [TestCase("/main2.lua", "starts or ends with '/'")]
    [TestCase("lib//x.lua", "'//'")]
    [TestCase("lib\\x.lua", "a character")]
    [TestCase("x:y.lua", "a character")]
    [TestCase("CON.lua", "Windows reserves")]
    [TestCase("lib/aux.lua", "Windows reserves")]
    [TestCase("x.lua.", "ends with a dot")]
    [TestCase("-e.lua", "starts with '-'")]
    [TestCase("./x.lua", "'.' or '..'")]
    [TestCase("MAIN.lua", "twice")]
    [TestCase("main.lua/x.lua", "a file and a folder")]
    public void ANameThatIsNotTheSameFileEverywhereIsRefusedTest(string name, string reason)
    {
        var task = LuaTask("print(1)", more: [new VerifySourceFileData { Name = name, Content = "" }]);

        var result = Run(Lua, task);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Does.Contain(reason));
        });
    }

    [Test]
    public void TheDemoTasksetGivesTheVerdictsItNamesTest()
    {
        // The taskset the fleet proof runs (verify-live run lua-demo.jsonl): ten programs that
        // pass, a wrong answer, a crash and a loop that never ends.
        var path = Path.GetFullPath(Path.Combine(RuntimesRoot, "..", "..", "..", DEMO_TASKSET));
        var parsed = VerifyTasksetParser.Parse(File.ReadAllText(path));
        Assert.That(parsed.Errors, Is.Empty);

        var results = parsed.Tasks.Select(task => Run(Lua, task)).ToList();
        foreach (var result in results.Where(result => result.Verdict != VerifyVerdict.Pass))
            TestContext.Out.WriteLine($"task {result.TaskIndex}: {result.Verdict} {result.Stderr}");

        var expected = Enumerable.Repeat(VerifyVerdict.Pass, 10)
            .Concat([VerifyVerdict.Fail, VerifyVerdict.RuntimeError, VerifyVerdict.Timeout]);
        Assert.That(results.Select(result => result.Verdict), Is.EqualTo(expected));
    }

    #endregion

    #region Determinism Tests

    [Test]
    public void ARunIsReproducibleToTheFuelUnitTest()
    {
        // Lua seeds math.random from the clock and an address at start: the clock is pinned and
        // the addresses of a linear memory are the same on every run, and so is a table's.
        const string program = "print(math.random(1000000))\nprint(os.time(), os.clock())\nprint(tostring({}))\nlocal s = 0 for i = 1, 200000 do s = s + i % 7 end print(s)";

        var first = Run(Lua, LuaTask(program));
        var second = Run(Lua, LuaTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(first.Verdict, Is.EqualTo(VerifyVerdict.Pass), first.Stderr);
            Assert.That(second.Stdout, Is.EqualTo(first.Stdout));
            Assert.That(second.FuelConsumed, Is.EqualTo(first.FuelConsumed));
        });
    }

    #endregion

    #region Containment Tests

    [Test]
    public void TheGuestSeesItsSourcesAndNothingElseTest()
    {
        // The guest's root is the task's sources: the interpreter's module and its licence are
        // not there, and the environment is empty.
        const string program =
            "print(io.open('/main.lua') ~= nil, io.open('helper.lua') ~= nil)\n" +
            "print(io.open('/lua.wasm'), io.open('/LICENSE-lua.txt') == nil)\n" +
            "print(os.getenv('HOME'), os.getenv('PATH'))\n";
        var task = LuaTask(program, more: [new VerifySourceFileData { Name = "helper.lua", Content = "return 1" }]);

        var result = Run(Lua, task);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("true\ttrue\nnil\ttrue\nnil\tnil\n"));
        });
    }

    [Test]
    public void NoFileCanBeWrittenTest()
    {
        const string program =
            "print(io.open('new.txt', 'w'))\n" +
            "print(io.open('main.lua', 'a'))\n" +
            "print(os.remove('main.lua'))\n" +
            "print(os.rename('main.lua', 'moved.lua'))\n" +
            "print(io.open('main.lua'):read('l'))\n";

        var result = Run(Lua, LuaTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Does.Not.Contain("file ("), "nothing opened for writing");
            Assert.That(result.Stdout, Does.EndWith("print(io.open('new.txt', 'w'))\n"), "the source is still there and unchanged");
        });
    }

    [Test]
    public void NoSubprocessesTest()
    {
        const string program =
            "print(os.execute())\n" +
            "print(os.execute('echo ESCAPED'))\n" +
            "print(pcall(io.popen, 'echo ESCAPED'))\n" +
            "print(pcall(os.tmpname))\n";

        var result = Run(Lua, LuaTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Does.StartWith("false\n"));
            Assert.That(result.Stdout, Does.Not.Contain("ESCAPED\n"));
        });
    }

    [Test]
    public void TheSourcesFolderIsGoneAfterTheTaskTest()
    {
        var workingFolder = Path.Combine(Path.GetTempPath(), $"witai_lua_{Guid.NewGuid():N}");
        try
        {
            using (var sandbox = new VerifyWasmSandbox(workingFolder))
            {
                var suite = new VerifySuiteData
                {
                    Cases = [new VerifySuiteCaseData { Stdin = "1", ExpectedStdout = "1\n" }, new VerifySuiteCaseData { Stdin = "2", ExpectedStdout = "2\n" }]
                };
                var passed = sandbox.Execute(Lua, LuaTask("print(io.read('n'))", suite: suite), VerifySandboxDefaults.Resolve(null, null));
                var crashed = sandbox.Execute(Lua, LuaTask("error('x')"), VerifySandboxDefaults.Resolve(null, null));

                Assert.Multiple(() =>
                {
                    Assert.That(passed.Verdict, Is.EqualTo(VerifyVerdict.Pass), passed.Stderr);
                    Assert.That(crashed.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
                    Assert.That(Directory.EnumerateFileSystemEntries(workingFolder), Is.Empty, "no sources and no redirects are left");
                });
            }
        }
        finally
        {
            if (Directory.Exists(workingFolder))
                Directory.Delete(workingFolder, recursive: true);
        }
    }

    [Test]
    public void AnInfiniteLoopIsBoundedByFuelTest()
    {
        var result = Run(Lua, LuaTask("while true do end"), new VerifyLimitsData { FuelBudget = 2_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    [Test]
    public void AMemoryBombIsBoundedTest()
    {
        var result = Run(Lua, LuaTask("local t = {} local s = string.rep('x', 4096) for i = 1, 1e9 do t[i] = s .. i end"),
            new VerifyLimitsData { MemoryBytes = 64L * 1024 * 1024, FuelBudget = 500_000_000_000, WallTimeMs = 30_000 });

        TestContext.Out.WriteLine($"{result.Verdict} exit={result.ExitCode} stderr={result.Stderr}");
        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.MemoryExceeded));
    }

    [Test]
    public void DeepRecursionIsALuaErrorTest()
    {
        var result = Run(Lua, LuaTask("local function f(n) return 1 + f(n + 1) end\nprint(pcall(f, 0))\nf(0)"),
            new VerifyLimitsData { FuelBudget = 500_000_000_000, WallTimeMs = 30_000 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.Stdout, Does.StartWith("false\t").And.Contain("stack overflow"));
        });
    }

    [Test]
    public void TheDeepestUnwindIsALuaErrorNotATrapTest()
    {
        // The deepest C stacks Lua lets a program build (each level a C call into Lua again):
        // the error unwinds all of them at once, inside the unwind buffer, and pcall catches it.
        const string program =
            "local function viaGsub(n) return select(2, string.gsub('a', 'a', function() return viaGsub(n + 1) end)) end\n" +
            "local function viaIndex(n) return setmetatable({}, { __index = function(_, k) return viaIndex(n + 1)[k] end }) end\n" +
            "print(pcall(viaGsub, 0))\n" +
            "print(pcall(function() return viaIndex(0).x end))\n" +
            "print('still running')\n";

        var result = Run(Lua, LuaTask(program), new VerifyLimitsData { FuelBudget = 500_000_000_000, WallTimeMs = 30_000 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Does.Contain("C stack overflow").And.EndWith("still running\n"));
        });
    }

    #endregion

    #region Pin Tests

    [Test]
    public void AChangedModuleFailsThePinTest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"witai_lua_pin_{Guid.NewGuid():N}");
        try
        {
            var runtimeDir = Path.Combine(root, LUA_DIR);
            Directory.CreateDirectory(runtimeDir);
            var bytes = File.ReadAllBytes(Path.Combine(RuntimesRoot, LUA_DIR, LUA_MODULE));
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(Path.Combine(runtimeDir, LUA_MODULE), bytes);

            var resolved = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.LUA_5_4_8, out var reason);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.Null);
                Assert.That(reason, Does.Contain("SHA-256"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    #endregion
}
