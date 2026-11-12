using System.Diagnostics;
using System.Text;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Spike for the runtime candidates of tier B (Lua, Ruby, PHP): does a prebuilt WASI build
/// load and run under the sandbox's wasmtime, what does a start cost in fuel and time, and
/// does it read its program the way the sandbox gives one (inline, standard input, a
/// read-only file). Opt-in: set WITAI_VERIFY_CANDIDATES to the folder the candidates were
/// unpacked into; without it every test is ignored. The numbers it prints go into the
/// results note; nothing here ships.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Spike")]
public sealed class RuntimeCandidatesSpikeTests
{
    #region Constants

    private const string CANDIDATES_ENV = "WITAI_VERIFY_CANDIDATES";
    private const ulong FUEL = 2_000_000_000_000;

    #endregion

    #region Fields

    private string m_root = null!;
    private string m_io = null!;
    private WasmEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var root = Environment.GetEnvironmentVariable(CANDIDATES_ENV);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            Assert.Ignore($"Set {CANDIDATES_ENV} to the folder with the runtime candidates to run the spike.");

        m_root = root;
        m_io = Path.Combine(Path.GetTempPath(), $"witai_candidates_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_io);
        m_engine = new WasmEngine(new Config().WithFuelConsumption(true).WithCraneliftNaNCanonicalization(true).WithExceptions(true));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        foreach (var module in Modules.Values)
            module.Dispose();

        m_engine?.Dispose();
        if (m_io != null && Directory.Exists(m_io))
            Directory.Delete(m_io, recursive: true);
    }

    #endregion

    #region Lua Tests

    [Test]
    public void LuaRunsInlineCodeTest()
    {
        var lua = Path.Combine(m_root, "lua", "x", "package", "dist", "lua.wasm");

        Report("lua -e print", Run(lua, ["lua", "-e", "print(6*7)"]));
        Report("lua stdin", Run(lua, ["lua", "-e", "local a = io.read('n'); local b = io.read('n'); print(a + b)"], stdin: "2\n3\n"));
        Report("lua time/random", Run(lua, ["lua", "-e", "print(os.time(), os.clock(), math.random(1000))"]));
        Report("lua error", Run(lua, ["lua", "-e", "error('boom')"]));
        Report("lua exit 7", Run(lua, ["lua", "-e", "os.exit(7)"]));
    }

    [Test]
    public void OurLuaBuildRunsWithExceptionsOnTest()
    {
        // lua.wasm of the toolchain job (lua.org 5.4.8, wasi-sdk 34, longjmp by the
        // exception-handling proposal), in toolchain/lua.wasm of the candidates folder.
        var lua = Path.Combine(m_root, "toolchain", "lua.wasm");

        var task = Path.Combine(m_io, "lua-task");
        Directory.CreateDirectory(task);
        File.WriteAllText(Path.Combine(task, "main.lua"),
            "local helper = require('helper')\n" +
            "local total = 0\n" +
            "for line in io.lines() do total = total + tonumber(line) end\n" +
            "print(helper.twice(total), #arg, arg[0], arg[1], arg[2], ...)\n");
        File.WriteAllText(Path.Combine(task, "helper.lua"), "return { twice = function(x) return 2 * x end }\n");

        Report("lua -v", Run(lua, ["lua", "-v"]));
        Report("lua -e", Run(lua, ["lua", "-e", "print(6*7, _VERSION, math.maxinteger)"]));
        Report("pcall", Run(lua, ["lua", "-e", "print(pcall(error, 'caught')); print(pcall(function() local t = nil; return t.x end)); print(select(2, pcall(string.rep)))"]));
        Report("uncaught", Run(lua, ["lua", "-e", "error('boom')"]));
        Report("exit 7", Run(lua, ["lua", "-e", "os.exit(7)"]));
        Report("file + args + stdin", Run(lua, ["lua", "main.lua", "-x", "two words"], preopen: task, guest: "/", stdin: "2\n3\n"));
        Report("not there", Run(lua, ["lua", "-e", "print(os.execute()); print(os.execute('ls')); print(pcall(os.tmpname)); print(io.tmpfile()); print(pcall(io.popen, 'ls')); print(os.getenv('HOME')); print(io.open('/etc/passwd'))"]));
        Report("coroutines etc", Run(lua, ["lua", "-e", "local co = coroutine.wrap(function() for i = 1, 3 do coroutine.yield(i) end end); print(co(), co(), co()); print(string.format('%5.2f %d %s', 3.14159, 2^53, ('x'):rep(3))); print(7 // 2, 7 % 3, 2^0.5, 1 << 62); local function depth(n) return n == 0 and 0 or 1 + depth(n - 1) end; print(depth(10000)); print(pcall(function() local function f() return 1 + f() end return f() end))"]));
        Report("time/random", Run(lua, ["lua", "-e", "print(os.time(), os.clock(), math.random(1000))"]));
        Report("loop 3M", Run(lua, ["lua", "-e", "local x = 0; for i = 0, 2999999 do x = x + i end; print(x)"]));
        Report("fib(25)", Run(lua, ["lua", "-e", "local function fib(n) if n < 2 then return n end return fib(n-1) + fib(n-2) end; print(fib(25))"]));
        Report("fib(25) again", Run(lua, ["lua", "-e", "local function fib(n) if n < 2 then return n end return fib(n-1) + fib(n-2) end; print(fib(25))"]));
        Report("error storm", Run(lua, ["lua", "-e", "local n = 0; for i = 1, 100000 do if not pcall(error, i) then n = n + 1 end end; print(n)"]));
        Report("memory bomb", Run(lua, ["lua", "-e", "local t = {}; local s = string.rep('a', 1048576); for i = 1, 100000 do t[i] = s .. i end"]));
    }

    #endregion

    #region Ruby Tests

    [Test]
    public void RubyRunsInlineCodeTest()
    {
        var root = Path.Combine(m_root, "ruby", "x", "ruby-3.4-wasm32-unknown-wasip1-full");
        var ruby = Path.Combine(root, "usr", "local", "bin", "ruby");

        Report("ruby -v", Run(ruby, ["ruby", "-v"], preopen: root));
        Report("ruby -e puts (gems off)", Run(ruby, ["ruby", "--disable=gems", "-e", "puts 6*7"], preopen: root));
        Report("ruby -e puts (gems on)", Run(ruby, ["ruby", "-e", "puts 6*7"], preopen: root));
        Report("ruby stdin", Run(ruby, ["ruby", "--disable=gems", "-e", "a = gets.to_i; b = gets.to_i; puts a + b"], preopen: root, stdin: "2\n3\n"));
        Report("ruby json+set", Run(ruby, ["ruby", "--disable=gems", "-e", "require 'json'; require 'set'; puts JSON.generate({a: Set[1,2].size}); puts RUBY_VERSION"], preopen: root));
        Report("ruby time/random", Run(ruby, ["ruby", "--disable=gems", "-e", "puts Time.now.to_i; puts rand(1000)"], preopen: root));
        Report("ruby raise", Run(ruby, ["ruby", "--disable=gems", "-e", "raise 'boom'"], preopen: root));
        Report("ruby exit 7", Run(ruby, ["ruby", "--disable=gems", "-e", "exit 7"], preopen: root));
    }

    #endregion

    #region Ruby Gems Tests

    [Test]
    public void RubyBundledGemsWithoutRubyGemsTest()
    {
        var root = Path.Combine(m_root, "ruby", "x", "ruby-3.4-wasm32-unknown-wasip1-full");
        var ruby = Path.Combine(root, "usr", "local", "bin", "ruby");
        const string gems = "/usr/local/lib/ruby/gems/3.4.0/gems";

        string[] Argv(string code, params string[] libs)
        {
            var argv = new List<string> { "ruby", "--disable=gems" };
            argv.AddRange(libs.Select(lib => $"-I{gems}/{lib}/lib"));
            argv.Add("-e");
            argv.Add(code);
            return argv.ToArray();
        }

        Report("ruby prime", Run(ruby, Argv("require 'prime'; puts Prime.first(5).inspect; puts 97.prime?", "prime-0.1.3"), preopen: root));
        Report("ruby matrix", Run(ruby, Argv("require 'matrix'; puts (Matrix[[1,2],[3,4]] * Matrix[[1,0],[0,1]]).to_a.inspect", "matrix-0.4.2"), preopen: root));
        Report("ruby csv", Run(ruby, Argv("require 'csv'; puts CSV.parse(\"a,b\n1,2\").inspect", "csv-3.3.2"), preopen: root));
        Report("ruby base64", Run(ruby, Argv("require 'base64'; puts Base64.strict_encode64('hello')", "base64-0.2.0"), preopen: root));
        Report("ruby stdlib mix", Run(ruby, Argv("require 'set'; require 'json'; require 'date'; require 'digest'; require 'securerandom'; require 'stringio'; require 'strscan'; require 'time'; puts Digest::SHA256.hexdigest('a')[0,8]; puts Date.new(2024,1,1).yday; puts [3,1,2].sort.inspect"), preopen: root));
        Report("ruby bigdecimal", Run(ruby, Argv("require 'bigdecimal'; puts BigDecimal('1.1') + 1"), preopen: root));
        Report("ruby big loop", Run(ruby, Argv("s = 0; 3_000_000.times { |i| s += i }; puts s"), preopen: root));
        Report("ruby write file", Run(ruby, Argv("File.write('/x.txt', 'a')"), preopen: root));
        Report("ruby system", Run(ruby, Argv("puts system('ls').inspect"), preopen: root));
        Report("ruby socket", Run(ruby, Argv("require 'socket'; TCPSocket.new('example.com', 80)"), preopen: root));
    }

    #endregion

    #region Ruby Encoding Tests

    [Test]
    public void RubyEncodingAndArgsTest()
    {
        var root = Path.Combine(m_root, "ruby", "x", "ruby-3.4-wasm32-unknown-wasip1-full");
        var ruby = Path.Combine(root, "usr", "local", "bin", "ruby");

        Report("ruby default encodings", Run(ruby, ["ruby", "--disable=gems", "-e", "puts Encoding.default_external, __ENCODING__, $stdin.external_encoding; s = $stdin.read; puts s.encoding, s.valid_encoding?, s.length"], preopen: root, stdin: "héllo 日本"));
        Report("ruby -E utf-8", Run(ruby, ["ruby", "--disable=gems", "-Eutf-8", "-e", "puts Encoding.default_external; s = $stdin.read; puts s.encoding, s.length, s.upcase"], preopen: root, stdin: "héllo 日本"));
        Report("ruby args", Run(ruby, ["ruby", "--disable=gems", "-e", "puts ARGV.inspect; puts $0", "a", "b c"], preopen: root));
        Report("ruby env", Run(ruby, ["ruby", "--disable=gems", "-e", "puts ENV.to_h.inspect; puts Dir.pwd; puts Dir.children('/').inspect"], preopen: root));
        Report("ruby syntax error", Run(ruby, ["ruby", "--disable=gems", "-e", "def x; puts 1"], preopen: root));
        Report("ruby deep recursion", Run(ruby, ["ruby", "--disable=gems", "-e", "def f(n) = f(n + 1); f(0)"], preopen: root));
        Report("ruby big alloc", Run(ruby, ["ruby", "--disable=gems", "-e", "a = []; 200.times { a << ('x' * 10_000_000) }; puts a.size"], preopen: root));
    }

    #endregion

    #region PHP Tests

    [Test]
    public void PhpRunsAScriptTest()
    {
        var php = Path.Combine(m_root, "php", "php-cgi-8.2.6.wasm");

        var task = Path.Combine(m_io, "php-task");
        Directory.CreateDirectory(task);
        File.WriteAllText(Path.Combine(task, "main.php"), "<?php echo 6*7, \"\\n\"; echo PHP_VERSION, \"\\n\";");
        File.WriteAllText(Path.Combine(task, "sum.php"), "<?php $in = fopen('php://stdin', 'r'); $a = (int)fgets($in); $b = (int)fgets($in); echo $a + $b, \"\\n\";");
        File.WriteAllText(Path.Combine(task, "exit.php"), "<?php exit(7);");
        File.WriteAllText(Path.Combine(task, "time.php"), "<?php echo time(), \" \", mt_rand(0, 1000), \" \", random_int(0, 1000), \"\\n\";");

        Report("php-cgi -v", Run(php, ["php-cgi", "-v"]));
        Report("php-cgi -q -f file", Run(php, ["php-cgi", "-q", "-f", "/task/main.php"], preopen: task, guest: "/task"));
        Report("php-cgi file + stdin", Run(php, ["php-cgi", "-q", "-f", "/task/sum.php"], preopen: task, guest: "/task", stdin: "2\n3\n"));
        Report("php-cgi exit 7", Run(php, ["php-cgi", "-q", "-f", "/task/exit.php"], preopen: task, guest: "/task"));
        Report("php-cgi time/random", Run(php, ["php-cgi", "-q", "-f", "/task/time.php"], preopen: task, guest: "/task"));
        Report("php-cgi script on stdin", Run(php, ["php-cgi", "-q"], stdin: "<?php echo 6*7, \"\\n\";"));
    }

    [Test]
    public void PhpCommandLineShapeTest()
    {
        // The CGI build run as a command line: the program is a file, the task's stdin is the
        // "request body" (CONTENT_LENGTH without REQUEST_METHOD keeps the command-line mode),
        // a prepended file gives the STDIN constant, errors go to the log (stderr).
        var php = Path.Combine(m_root, "php", "php-cgi-8.2.6.wasm");

        var task = Path.Combine(m_io, "php-shape");
        Directory.CreateDirectory(task);
        File.WriteAllText(Path.Combine(task, "prelude.php"), "<?php define('STDIN', fopen('php://input', 'r'));");
        File.WriteAllText(Path.Combine(task, "sum.php"), "<?php declare(strict_types=1);\n$a = (int)fgets(STDIN); $b = (int)fgets(STDIN); echo $a + $b, \"\n\"; var_dump(feof(STDIN)); echo stream_get_contents(STDIN), '|', \"\n\";");
        File.WriteAllText(Path.Combine(task, "input.php"), "<?php echo strtoupper(file_get_contents('php://input'));");
        File.WriteAllText(Path.Combine(task, "argv.php"), "<?php var_dump($argv ?? null); var_dump($argc ?? null); var_dump($_GET);");
        File.WriteAllText(Path.Combine(task, "boom.php"), "<?php echo \"before\n\";\nthrow new RuntimeException('boom');");
        File.WriteAllText(Path.Combine(task, "warn.php"), "<?php echo $undefined; echo intdiv(1, 0);");
        File.WriteAllText(Path.Combine(task, "syntax.php"), "<?php echo 1 +;");
        File.WriteAllText(Path.Combine(task, "mem.php"), "<?php $a = 'a'; while (true) { $a .= $a; }");
        File.WriteAllText(Path.Combine(task, "inc.php"), "<?php require __DIR__ . '/lib.php'; require 'lib2.php'; echo twice(21), ' ', thrice(2), \"\n\";");
        File.WriteAllText(Path.Combine(task, "lib.php"), "<?php function twice(int $x): int { return 2 * $x; }");
        File.WriteAllText(Path.Combine(task, "lib2.php"), "<?php function thrice(int $x): int { return 3 * $x; }");
        File.WriteAllText(Path.Combine(task, "world.php"), "<?php echo PHP_VERSION, ' ', PHP_OS, ' ', PHP_INT_SIZE, ' ', php_sapi_name(), \"\n\"; echo json_encode(['a' => 1, 'b' => [1.5, null]]), \"\n\"; echo mb_strlen('héllo — мир'), ' ', strlen('héllo'), \"\n\"; var_dump(getenv()); echo implode(',', get_loaded_extensions()), \"\n\"; echo ini_get('memory_limit'), ' ', ini_get('max_execution_time'), \"\n\"; var_dump(@file_put_contents('/task/x.txt', 'a')); var_dump(function_exists('exec') ? @exec('echo ESCAPED') : 'no exec'); var_dump(@fsockopen('example.com', 80)); echo bcadd('1', '2') ?? '', \"\n\";");
        File.WriteAllText(Path.Combine(task, "loop.php"), "<?php $x = 0; for ($i = 0; $i < 3000000; $i++) { $x += $i; } echo $x, \"\n\";");
        File.WriteAllText(Path.Combine(task, "fib.php"), "<?php function fib($n) { return $n < 2 ? $n : fib($n - 1) + fib($n - 2); } echo fib(25), \"\n\";");
        File.WriteAllText(Path.Combine(task, "stderr.php"), "<?php error_log('to the log'); $e = @fopen('php://stderr', 'w'); var_dump($e); echo \"out\n\";");

        string[] Options(params string[] rest) =>
        [
            "php-cgi", "-q",
            "-d", "auto_prepend_file=/task/prelude.php",
            "-d", "register_argc_argv=1",
            "-d", "html_errors=0",
            "-d", "display_errors=0",
            "-d", "log_errors=1",
            "-d", "memory_limit=-1",
            "-d", "max_execution_time=0",
            .. rest
        ];

        const string input = "2\n3\nrest of it";
        (string, string)[] body = [("CONTENT_LENGTH", Encoding.UTF8.GetByteCount(input).ToString())];

        Report("stdin through the body", Run(php, Options("/task/sum.php"), preopen: task, guest: "/task", stdin: input, env: body));
        Report("php://input", Run(php, Options("/task/input.php"), preopen: task, guest: "/task", stdin: input, env: body));
        Report("no stdin given", Run(php, Options("/task/sum.php"), preopen: task, guest: "/task"));
        Report("argv", Run(php, Options("/task/argv.php", "-x", "--y=1", "z w"), preopen: task, guest: "/task"));
        Report("argv after --", Run(php, Options("--", "/task/argv.php", "-x", "z"), preopen: task, guest: "/task"));
        Report("uncaught exception", Run(php, Options("/task/boom.php"), preopen: task, guest: "/task"));
        Report("warning + error", Run(php, Options("/task/warn.php"), preopen: task, guest: "/task"));
        Report("syntax error", Run(php, Options("/task/syntax.php"), preopen: task, guest: "/task"));
        Report("include", Run(php, Options("/task/inc.php"), preopen: task, guest: "/task"));
        Report("world", Run(php, Options("/task/world.php"), preopen: task, guest: "/task"));
        Report("loop 3M", Run(php, Options("/task/loop.php"), preopen: task, guest: "/task"));
        Report("fib(25)", Run(php, Options("/task/fib.php"), preopen: task, guest: "/task"));
        Report("stderr", Run(php, Options("/task/stderr.php"), preopen: task, guest: "/task"));
        Report("memory bomb", Run(php, Options("/task/mem.php"), preopen: task, guest: "/task"));
        Report("same twice", Run(php, Options("/task/fib.php"), preopen: task, guest: "/task"));
    }

    #endregion

    #region Tools

    private void Report(string name, RunOutcome outcome)
    {
        TestContext.Out.WriteLine(
            $"{name,-28} exit={outcome.Exit,-3} fuel={outcome.Fuel,15:N0} {outcome.Milliseconds,6} ms (compile {outcome.CompileMilliseconds} ms)" +
            $"{(outcome.Fault == null ? "" : " FAULT " + outcome.Fault)}\n    out: {Shorten(outcome.Stdout)}\n    err: {Shorten(outcome.Stderr)}");
    }

    private static string Shorten(string text)
    {
        var flat = text.Replace("\r", "").Replace("\n", "\\n");
        return flat.Length <= 300 ? flat : flat[..300] + "…";
    }

    private RunOutcome Run(string modulePath, string[] argv, string? preopen = null, string guest = "/", string? stdin = null,
        (string Name, string Value)[]? env = null)
    {
        Assert.That(File.Exists(modulePath), Is.True, $"candidate missing: {modulePath}");

        var id = Guid.NewGuid().ToString("N");
        var stdoutPath = Path.Combine(m_io, id + ".out");
        var stderrPath = Path.Combine(m_io, id + ".err");
        var stdinPath = Path.Combine(m_io, id + ".in");
        File.WriteAllText(stdinPath, stdin ?? "", new UTF8Encoding(false));

        var compile = Stopwatch.StartNew();
        var module = Modules.GetOrAdd(modulePath, path => WasmModule.FromFile(m_engine, path));
        compile.Stop();

        using var linker = new Linker(m_engine) { AllowShadowing = true };
        linker.DefineWasi();

        // The sandbox pins the clock and seeds randomness; the spike does the same for both
        // names of the WASI module, to see whether the old one is honoured at all.
        foreach (var wasi in new[] { "wasi_snapshot_preview1", "wasi_unstable" })
        {
            try
            {
                linker.DefineFunction(wasi, "clock_time_get", (Caller caller, int clockId, long precision, int timePtr) =>
                {
                    caller.GetMemory("memory")?.WriteInt64(timePtr, 0);
                    return 0;
                });
            }
            catch (WasmtimeException)
            {
            }
        }

        var store = new Store(m_engine);
        var config = new WasiConfiguration().WithArgs(argv).WithStandardInput(stdinPath).WithStandardOutput(stdoutPath).WithStandardError(stderrPath);
        if (preopen != null)
            config = config.WithPreopenedDirectory(preopen, guest, false);
        foreach (var (name, value) in env ?? [])
            config = config.WithEnvironmentVariable(name, value);
        store.SetWasiConfiguration(config);
        store.Fuel = FUEL;

        var exit = 0;
        string? fault = null;
        var clock = Stopwatch.StartNew();
        try
        {
            var instance = linker.Instantiate(store, module);
            instance.GetAction("_start")!();
        }
        catch (WasmtimeException exception) when (exception.ExitCode is { } code)
        {
            exit = code;
        }
        catch (Exception exception)
        {
            exit = -1;
            fault = exception.GetType().Name + ": " + exception.Message.Split('\n')[0];
        }

        clock.Stop();

        // The store holds the redirect files open until it is disposed.
        var fuel = FUEL - store.Fuel;
        store.Dispose();

        return new RunOutcome(exit, File.ReadAllText(stdoutPath), File.ReadAllText(stderrPath), fuel, clock.ElapsedMilliseconds, compile.ElapsedMilliseconds, fault);
    }

    #endregion

    #region Properties

    private System.Collections.Concurrent.ConcurrentDictionary<string, WasmModule> Modules { get; } = new();

    #endregion

    #region Types

    private sealed record RunOutcome(int Exit, string Stdout, string Stderr, ulong Fuel, long Milliseconds, long CompileMilliseconds, string? Fault);

    #endregion
}
