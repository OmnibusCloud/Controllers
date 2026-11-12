using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The Ruby runtime: a program runs inline with the standard library and the four bundled
/// gems at hand, takes arguments and stdin like any task, is reproducible to the fuel unit,
/// sees a file system that is its pinned library and nothing else, stays inside the same
/// limits as the other runtimes, and does not resolve when a file of its library was
/// changed, added or removed.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyRubyRuntimeTests : VerifySandboxTestBase
{
    #region Constants

    private const string RUBY_DIR = "ruby-3.4.1";
    private const string RUBY_MODULE = "ruby.wasm";
    private const string RUBY_TREE = "root";
    private static readonly string DEMO_TASKSET = Path.Combine("OutWit.Controller.AI.Verify.Live", "tasksets", "ruby-demo.jsonl");

    /// <summary>A start must stay well under the default budget: a hello is a few hundred million units.</summary>
    private const long HELLO_FUEL_CEILING = 1_000_000_000;

    private const string STDLIB_PROGRAM =
        "require 'json'\n" +
        "require 'set'\n" +
        "require 'date'\n" +
        "require 'digest'\n" +
        "puts JSON.generate({a: 1, b: [1, 2]})\n" +
        "puts Set[3, 1, 2].sort.inspect\n" +
        "puts Date.new(2024, 2, 29).next_day.iso8601\n" +
        "puts Digest::SHA256.hexdigest('abc')\n";

    private const string STDLIB_EXPECTED =
        "{\"a\":1,\"b\":[1,2]}\n" +
        "[1, 2, 3]\n" +
        "2024-03-01\n" +
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\n";

    private const string GEMS_PROGRAM =
        "require 'prime'\n" +
        "require 'matrix'\n" +
        "require 'csv'\n" +
        "require 'base64'\n" +
        "puts Prime.first(5).inspect\n" +
        "puts 600851475143.prime_division.inspect\n" +
        "puts Matrix[[1, 2], [3, 4]].determinant\n" +
        "puts CSV.parse(\"a,b\\n1,2\\n\").inspect\n" +
        "puts Base64.strict_encode64('hello')\n";

    private const string GEMS_EXPECTED =
        "[2, 3, 5, 7, 11]\n" +
        "[[71, 1], [839, 1], [1471, 1], [6857, 1]]\n" +
        "-2\n" +
        "[[\"a\", \"b\"], [\"1\", \"2\"]]\n" +
        "aGVsbG8=\n";

    #endregion

    #region Program Tests

    [Test]
    public void HelloRunsSilentlyTest()
    {
        var result = Run(Ruby, RubyTask("puts 'hello'"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("hello\n"));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(result.FuelConsumed, Is.LessThan(HELLO_FUEL_CEILING));
        });
    }

    [Test]
    public void StandardLibraryLoadsTest()
    {
        var result = Run(Ruby, RubyTask(STDLIB_PROGRAM));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo(STDLIB_EXPECTED));
        });
    }

    [Test]
    public void BundledGemsLoadWithoutRubyGemsTest()
    {
        var result = Run(Ruby, RubyTask(GEMS_PROGRAM + "puts defined?(Gem).inspect\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo(GEMS_EXPECTED + "nil\n"));
        });
    }

    [Test]
    public void ArgumentsReachTheProgramEvenWithALeadingDashTest()
    {
        var result = Run(Ruby, RubyTask("puts ARGV.inspect", args: ["-x", "--y=1", "z", "-e", "exit 9"]));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("[\"-x\", \"--y=1\", \"z\", \"-e\", \"exit 9\"]\n"));
        });
    }

    [Test]
    public void StdinReachesTheProgramTest()
    {
        var result = Run(Ruby, RubyTask("puts STDIN.read.upcase", stdin: "abc"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("ABC\n"));
        });
    }

    [Test]
    public void SourceAndOutputAreUtf8Test()
    {
        var result = Run(Ruby, RubyTask("s = 'héllo — мир'\nputs s.length\nputs s\nputs Encoding.default_external"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("11\nhéllo — мир\nUTF-8\n"));
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

        var result = Run(Ruby, RubyTask("puts STDIN.read.split.map(&:to_i).sum", suite: suite));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Fail), "the third case expects a wrong sum");
    }

    [Test]
    public void AnUncaughtErrorIsARuntimeErrorTest()
    {
        var result = Run(Ruby, RubyTask("raise ArgumentError, 'boom'"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Stderr, Does.Contain("boom").And.Contain("ArgumentError"));
        });
    }

    [Test]
    public void TheExitCodeIsTheProgramsTest()
    {
        var result = Run(Ruby, RubyTask("puts 'before'\nexit 3"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.Stdout, Is.EqualTo("before\n"));
        });
    }

    [Test]
    public void TheDemoTasksetGivesTheVerdictsItNamesTest()
    {
        // The taskset the fleet proof runs (verify-live run ruby-demo.jsonl): nine programs
        // that pass, one with no suite, a wrong answer, a crash and a loop that never ends.
        var path = Path.GetFullPath(Path.Combine(RuntimesRoot, "..", "..", "..", DEMO_TASKSET));
        var parsed = VerifyTasksetParser.Parse(File.ReadAllText(path));
        Assert.That(parsed.Errors, Is.Empty);

        var verdicts = parsed.Tasks.Select(task => Run(Ruby, task).Verdict).ToList();

        var expected = Enumerable.Repeat(VerifyVerdict.Pass, 10)
            .Concat([VerifyVerdict.Fail, VerifyVerdict.RuntimeError, VerifyVerdict.Timeout]);
        Assert.That(verdicts, Is.EqualTo(expected));
    }

    #endregion

    #region Determinism Tests

    [Test]
    public void ARunIsReproducibleToTheFuelUnitTest()
    {
        // Randomness and the string hash seed come from the task's seed, the clock is pinned:
        // the same task gives the same bytes and costs the same on every run.
        const string program = "require 'prime'\nputs rand(1_000_000)\nputs 'abc'.hash\nputs Time.now.to_i\nputs Prime.first(200).sum";

        var first = Run(Ruby, RubyTask(program));
        var second = Run(Ruby, RubyTask(program));

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
    public void TheGuestSeesItsPinnedLibraryAndNothingElseTest()
    {
        var result = Run(Ruby, RubyTask("puts Dir.children('/').sort.inspect\nputs ENV.to_h.inspect"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("[\"usr\"]\n{}\n"));
        });
    }

    [Test]
    public void NoFileCanBeWrittenTest()
    {
        const string program =
            "begin\n" +
            "  File.write('/usr/x.txt', 'a')\n" +
            "  puts 'WROTE'\n" +
            "rescue SystemCallError => e\n" +
            "  puts 'refused'\n" +
            "end\n";

        var result = Run(Ruby, RubyTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Stdout, Is.EqualTo("refused\n"), result.Stderr);
            Assert.That(File.Exists(Path.Combine(Ruby.RootPath!, "usr", "x.txt")), Is.False);
        });
    }

    [Test]
    public void NoSocketsAndNoSubprocessesTest()
    {
        const string program =
            "begin\n" +
            "  require 'socket'\n" +
            "  puts 'SOCKET'\n" +
            "rescue LoadError\n" +
            "  puts 'no socket'\n" +
            "end\n" +
            "begin\n" +
            "  system('echo ESCAPED')\n" +
            "  puts `echo ESCAPED`\n" +
            "rescue StandardError, NotImplementedError => e\n" +
            "  puts 'no process'\n" +
            "end\n";

        var result = Run(Ruby, RubyTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Stdout, Does.StartWith("no socket\n"), result.Stderr);
            Assert.That(result.Stdout, Does.Not.Contain("ESCAPED"));
        });
    }

    [Test]
    public void AnInfiniteLoopIsBoundedByFuelTest()
    {
        var result = Run(Ruby, RubyTask("loop { }"),
            new VerifyLimitsData { FuelBudget = 2_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    [Test]
    public void AMemoryBombIsBoundedTest()
    {
        var result = Run(Ruby, RubyTask("a = 'a' * 1024\nloop { a << a }"),
            new VerifyLimitsData { MemoryBytes = 128L * 1024 * 1024, FuelBudget = 500_000_000_000, WallTimeMs = 30_000 });

        TestContext.Out.WriteLine($"{result.Verdict} exit={result.ExitCode} stderr={result.Stderr}");
        Assert.That(result.Verdict, Is.AnyOf(VerifyVerdict.MemoryExceeded, VerifyVerdict.Timeout));
    }

    [Test]
    public void DeepRecursionIsContainedTest()
    {
        var result = Run(Ruby, RubyTask("def f(n) = f(n + 1)\nf(0)"),
            new VerifyLimitsData { FuelBudget = 500_000_000_000, WallTimeMs = 30_000 });

        TestContext.Out.WriteLine($"{result.Verdict} exit={result.ExitCode} stderr={result.Stderr}");
        Assert.That(result.Verdict, Is.AnyOf(VerifyVerdict.RuntimeError, VerifyVerdict.Timeout, VerifyVerdict.MemoryExceeded));
    }

    #endregion

    #region Pin Tests

    [Test]
    public void AChangedLibraryFileFailsThePinTest()
    {
        AssertUnavailable(tree =>
        {
            var file = Path.Combine(tree, "usr", "local", "lib", "ruby", "3.4.0", "set.rb");
            var bytes = File.ReadAllBytes(file);
            bytes[bytes.Length / 2] ^= 0x01;
            File.WriteAllBytes(file, bytes);
        });
    }

    [Test]
    public void AnAddedLibraryFileFailsThePinTest()
    {
        AssertUnavailable(tree => File.WriteAllText(Path.Combine(tree, "usr", "local", "lib", "ruby", "3.4.0", "extra.rb"), "puts 1"));
    }

    [Test]
    public void ARemovedLibraryFileFailsThePinTest()
    {
        AssertUnavailable(tree => File.Delete(Path.Combine(tree, "usr", "local", "lib", "ruby", "3.4.0", "set.rb")));
    }

    [Test]
    public void AMissingLibraryIsUnavailableTest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"witai_ruby_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, RUBY_DIR));
            File.Copy(Path.Combine(RuntimesRoot, RUBY_DIR, RUBY_MODULE), Path.Combine(root, RUBY_DIR, RUBY_MODULE));

            var resolved = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.RUBY_3_4_1, out var reason);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.Null);
                Assert.That(reason, Does.Contain("not found"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    #endregion

    #region Tools

    private void AssertUnavailable(Action<string> tamper)
    {
        var root = Path.Combine(Path.GetTempPath(), $"witai_ruby_{Guid.NewGuid():N}");
        try
        {
            var runtimeDir = Path.Combine(root, RUBY_DIR);
            var tree = Path.Combine(runtimeDir, RUBY_TREE);
            Directory.CreateDirectory(runtimeDir);
            File.Copy(Path.Combine(RuntimesRoot, RUBY_DIR, RUBY_MODULE), Path.Combine(runtimeDir, RUBY_MODULE));
            CopyTree(Path.Combine(RuntimesRoot, RUBY_DIR, RUBY_TREE), tree);

            tamper(tree);

            var resolved = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.RUBY_3_4_1, out var reason);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.Null);
                Assert.That(reason, Does.Contain("library").And.Contain("SHA-256"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CopyTree(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    #endregion
}
