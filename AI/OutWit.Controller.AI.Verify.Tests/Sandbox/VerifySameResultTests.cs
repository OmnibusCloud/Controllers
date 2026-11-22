using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The same result on every node: what a program can observe, and what decides its verdict, does
/// not follow from the machine it ran on. Each test is one finding of the audit of 2026-11-17
/// (its id is in the test's name) with the input the audit used.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifySameResultTests : VerifySandboxTestBase
{
    #region Constants

    private const string PYTHON_ZIP = "/lib/python314.zip";

    // What os.urandom(12) gave for the default seed before seeds were mixed: it gives it still.
    private const string SEED_ZERO_BYTES = "47abb94d0ec8d0ac56bf7f2d";

    #endregion

    #region Fields

    private string m_otherRoot = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void SetUp()
    {
        m_otherRoot = Path.Combine(Path.GetTempPath(), $"witai_verify_same_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_otherRoot);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (m_otherRoot != null && Directory.Exists(m_otherRoot))
            Directory.Delete(m_otherRoot, recursive: true);
    }

    #endregion

    #region H-5 Tests

    [Test]
    public void H5ACheckOfARuntimeThatFailedIsMadeAgainTest()
    {
        // A node that looked at a runtime while its files were still being unpacked kept the
        // failed check for the life of the process, and answered RuntimeUnavailable until the
        // client was restarted.
        var root = Path.Combine(m_otherRoot, "arriving");
        var module = VerifyRuntimeCatalog.Find(VerifyRuntimeCatalog.LUA_5_4_8)!.ModulePath;
        var target = Path.Combine(root, module);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var missing = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.LUA_5_4_8, out var missingReason);

        File.WriteAllBytes(target, File.ReadAllBytes(Lua.ModulePath)[..4096]);
        var partial = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.LUA_5_4_8, out var partialReason);

        File.Copy(Lua.ModulePath, target, overwrite: true);
        var whole = VerifyRuntimeCatalog.Resolve(root, VerifyRuntimeCatalog.LUA_5_4_8, out var wholeReason);

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Null);
            Assert.That(missingReason, Is.EqualTo("the runtime's module is missing"));
            Assert.That(partial, Is.Null);
            Assert.That(partialReason, Is.EqualTo("the runtime's module failed its SHA-256 pin"));
            Assert.That(whole, Is.Not.Null, wholeReason);
        });
    }

    [Test]
    public void H5ARuntimeMissingOnANodeRefusesThePackageAndNamesNoVerdictTest()
    {
        // A known runtime that a node lacks is the node's state, not the task's property: the
        // package is refused whole and goes to another node.
        var root = Path.Combine(m_otherRoot, "lacking");
        Directory.CreateDirectory(root);
        var batch = new VerifyTaskBatchData { RuntimeId = VerifyRuntimeCatalog.RUBY_3_4_1, Tasks = [RubyTask("puts 1")] };

        var refusal = Assert.Throws<VerifyBatchRefusedException>(() => VerifyBatchExecutor.Execute(Sandbox, root, batch));

        Assert.That(refusal!.Message, Does.Contain("the runtime's module is missing"));
    }

    [Test]
    public void H5ARuntimeNoControllerHasIsFoundBeforeAnyNodeIsAskedTest()
    {
        // An unknown id is a property of the taskset: it is named where the taskset is split.
        var tasks = VerifyTasksetParser.Parse(
            """{"runtime":"cobol-1959","sources":{"m.cob":"DISPLAY 1"}}""" + "\n" +
            """{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}}""" + "\n" +
            """{"runtime":"cobol-1959","sources":{"n.cob":"DISPLAY 2"}}""").Tasks;

        Assert.That(VerifyTasksetChecks.UnknownRuntimes(tasks), Is.EqualTo(new[] { "cobol-1959" }));
    }

    [Test]
    public void H5TheProcessWideSandboxKeepsNoFailureTest()
    {
        // A Lazy kept the exception of the first attempt for the life of the process.
        var fields = typeof(VerifySandboxHost).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        Assert.That(fields.Where(field => field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Lazy<>)), Is.Empty);
    }

    #endregion

    #region H-6 Tests

    [Test]
    public void H6AFilesRecordSaysNothingOfTheNodeTest()
    {
        // The audit's program: the access time of the library was the node's clock to 100 ns, and
        // the file number a mark of the machine that outlived the job. The same library from
        // another folder (another volume position, other times) must read the same.
        const string program =
            "import os\n" +
            "with open('" + PYTHON_ZIP + "', 'rb') as library:\n" +
            "    library.read(4096)\n" +
            "    held = os.fstat(library.fileno())\n" +
            "print('held', held.st_atime_ns, held.st_mtime_ns, held.st_ctime_ns, held.st_dev, held.st_ino, held.st_nlink, held.st_size)\n" +
            "for path in ('" + PYTHON_ZIP + "', '/lib', '/'):\n" +
            "    found = os.stat(path)\n" +
            "    print(path, found.st_atime_ns, found.st_mtime_ns, found.st_ctime_ns, found.st_dev, found.st_ino, found.st_nlink)\n";

        var here = Run(Python, PythonTask(program));
        var elsewhere = Run(Elsewhere(Python), PythonTask(program));

        var lines = here.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(here.Verdict, Is.EqualTo(VerifyVerdict.Pass), here.Stderr);
            Assert.That(lines, Has.Count.EqualTo(4));

            foreach (var line in lines)
            {
                Assert.That(line[1..5], Is.All.EqualTo("0"), $"{line[0]}: the three times and the device");
                Assert.That(line[5], Is.Not.EqualTo("0"), $"{line[0]}: a file number");
                Assert.That(line[6], Is.EqualTo("1"), $"{line[0]}: the links");
            }

            Assert.That(lines[0][5], Is.EqualTo(lines[1][5]), "one file has one number, held open or looked up");
            Assert.That(lines.Skip(1).Select(line => line[5]), Is.Unique, "and another file another");
            Assert.That(elsewhere.Stdout, Is.EqualTo(here.Stdout), "the same library in another folder reads the same, to the byte");
        });
    }

    [Test]
    public void H6TheTimeASourceWasWrittenIsNotInAProgramTest()
    {
        // __TIMESTAMP__ was the moment the node wrote the source file to its disk.
        var task = CTask("#include <stdio.h>\nint main(void) { puts(__TIMESTAMP__); puts(__DATE__); puts(__TIME__); return 0; }\n");

        var first = Run(Clang, task);
        var second = Run(Clang, task);

        Assert.Multiple(() =>
        {
            Assert.That(first.Verdict, Is.EqualTo(VerifyVerdict.Pass), first.Stderr);
            Assert.That(first.Stdout.Split('\n')[0], Is.EqualTo("Thu Jan  1 00:00:00 1970"));
            Assert.That(second.Stdout, Is.EqualTo(first.Stdout));
        });
    }

    #endregion

    #region H-7 Tests

    [Test]
    public void H7ALuaModuleIsFoundUnderItsExactNameOnlyTest()
    {
        // The audit's task: on Windows require('LIB.Util') found lib/util.lua and printed 42; on
        // Linux it would not.
        var library = new VerifySourceFileData { Name = "lib/util.lua", Content = "return { base = 42 }" };

        var exact = Run(Lua, LuaTask("print(require('lib.util').base)", more: library));
        var otherCase = Run(Lua, LuaTask("print(require('LIB.Util').base)", more: library));

        Assert.Multiple(() =>
        {
            Assert.That(exact.Verdict, Is.EqualTo(VerifyVerdict.Pass), exact.Stderr);
            Assert.That(exact.Stdout, Is.EqualTo("42\n"));
            Assert.That(otherCase.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError), "on every node, whatever its file system does with case");
            Assert.That(otherCase.Stderr, Does.Contain("module 'LIB.Util' not found"));
        });
    }

    [Test]
    public void H7AHeaderIsFoundUnderItsExactNameOnlyTest()
    {
        var header = new VerifySourceFileData { Name = "util.h", Content = "#define BASE 42\n" };

        var exact = Run(Clang, CTask("#include <stdio.h>\n#include \"util.h\"\nint main(void) { printf(\"%d\\n\", BASE); return 0; }\n", header));
        var otherCase = Run(Clang, CTask("#include <stdio.h>\n#include \"Util.h\"\nint main(void) { printf(\"%d\\n\", BASE); return 0; }\n", header));

        Assert.Multiple(() =>
        {
            Assert.That(exact.Verdict, Is.EqualTo(VerifyVerdict.Pass), exact.Stderr);
            Assert.That(otherCase.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(otherCase.Stderr, Does.Contain("'Util.h' file not found"));
        });
    }

    [Test]
    public void H7ANameOnlyOneSystemUnderstandsOpensNothingTest()
    {
        // From Python on Windows all of these opened the library, and each told the program
        // which system it ran on.
        const string program =
            "import os\n" +
            "opened = []\n" +
            "for name in ['/LIB/PYTHON314.ZIP', '/lib/python314.zip::$DATA', '/lib/PYTHON~1.ZIP', '/lib\\\\python314.zip', '/lib/python314.zip.', '/lib/python314.zip ', '/lib/./python314.zip/']:\n" +
            "    try:\n" +
            "        os.close(os.open(name, os.O_RDONLY))\n" +
            "        opened.append(name)\n" +
            "    except OSError:\n" +
            "        pass\n" +
            "print(opened)\n" +
            "os.close(os.open('" + PYTHON_ZIP + "', os.O_RDONLY))\n" +
            "print('exact')\n";

        var result = Run(Python, PythonTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("[]\nexact\n"));
        });
    }

    [TestCase("café.txt", TestName = "H7ASourceNameThatIsNotPortableIsRefusedByNameTest(composed accent)")]
    [TestCase("café.txt", TestName = "H7ASourceNameThatIsNotPortableIsRefusedByNameTest(decomposed accent)")]
    [TestCase("lib\\\\util.lua", TestName = "H7ASourceNameThatIsNotPortableIsRefusedByNameTest(backslash)")]
    [TestCase("main.lua:stream", TestName = "H7ASourceNameThatIsNotPortableIsRefusedByNameTest(colon)")]
    [TestCase("my file.lua", TestName = "H7ASourceNameThatIsNotPortableIsRefusedByNameTest(space)")]
    [TestCase("nul.lua", TestName = "H7ASourceNameThatIsNotPortableIsRefusedByNameTest(device)")]
    [TestCase("util.lua.", TestName = "H7ASourceNameThatIsNotPortableIsRefusedByNameTest(trailing dot)")]
    [TestCase("-e.lua", TestName = "H7ASourceNameThatIsNotPortableIsRefusedByNameTest(leading dash)")]
    [TestCase("a/b/c/d/e/f/g/h/i.lua", TestName = "H7ASourceNameThatIsNotPortableIsRefusedByNameTest(nine segments)")]
    public void H7ASourceNameThatIsNotPortableIsRefusedByNameTest(string name)
    {
        // Whatever the runtime: a name must mean the same file on every node.
        foreach (var runtime in new[] { VerifyRuntimeCatalog.LUA_5_4_8, VerifyRuntimeCatalog.CLANG_22_1_0, VerifyRuntimeCatalog.PYTHON_3_14_6 })
        {
            var parsed = VerifyTasksetParser.Parse("{\"runtime\":\"" + runtime + "\",\"entry\":\"main.lua\",\"sources\":{\"main.lua\":\"print(1)\",\"" + name + "\":\"x\"}}");

            Assert.Multiple(() =>
            {
                Assert.That(parsed.Tasks, Is.Empty, runtime);
                Assert.That(parsed.Errors, Has.Count.EqualTo(1), runtime);
                Assert.That(parsed.Errors[0], Does.StartWith("line 1: source name '").And.Contain("is not allowed"), runtime);
            });
        }
    }

    [Test]
    public void H7ASourceNameIsCountedInBytesAndHeldToWhatEverySystemTakesTest()
    {
        // The audit's names: 200 characters, and a segment of 150 CJK characters (454 bytes),
        // were accepted; ext4 and APFS take 255 bytes a segment.
        var longest = new string('a', VerifySourceNames.MAX_NAME_BYTES - 4) + ".lua";
        var tooLong = "a" + longest;
        var wide = new string('文', 150) + ".lua";

        Assert.Multiple(() =>
        {
            Assert.That(VerifySourceNames.Problem(longest), Is.Null);
            Assert.That(VerifySourceNames.Problem(tooLong), Does.Contain("longer than 128 bytes"));
            Assert.That(VerifySourceNames.Problem(new string('a', 200)), Does.Contain("longer than"));
            Assert.That(VerifySourceNames.Problem(wide), Is.Not.Null);
            Assert.That(VerifyTasksetParser.Parse("{\"runtime\":\"lua-5.4.8\",\"sources\":{\"" + wide + "\":\"print(1)\"}}").Errors, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void H7NamesThatDifferInCaseOnlyAreOneNameTest()
    {
        var parsed = VerifyTasksetParser.Parse("""{"runtime":"lua-5.4.8","entry":"main.lua","sources":{"main.lua":"print(1)","Util.lua":"x","util.lua":"y"}}""");

        Assert.That(parsed.Errors.Single(), Does.Contain("given twice"));
    }

    [Test]
    public void L3AFileLeftInARuntimesFolderDoesNotExistForAProgramTest()
    {
        // The audit's folder: a copy of Python's with files nobody pinned. It passed every pin and
        // a task read /leftover.txt.
        var elsewhere = Elsewhere(Python);
        File.WriteAllText(Path.Combine(elsewhere.RootPath!, "leftover.txt"), "left by someone");
        File.WriteAllText(Path.Combine(elsewhere.RootPath!, "lib", "extra.py"), "print('ran')");
        File.WriteAllText(Path.Combine(elsewhere.RootPath!, "lib", "sitecustomize.py"), "print('customized')");

        const string program =
            "try:\n" +
            "    print(open('/leftover.txt').read())\n" +
            "except OSError as error:\n" +
            "    print('absent', error.errno)\n" +
            "try:\n" +
            "    import extra\n" +
            "except ImportError:\n" +
            "    print('no module')\n";

        var resolved = VerifyRuntimeCatalog.Resolve(Path.GetDirectoryName(elsewhere.RootPath!), VerifyRuntimeCatalog.PYTHON_3_14_6, out var reason);
        var result = Run(resolved ?? throw new InvalidOperationException(reason), PythonTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("absent 44\nno module\n"), "ENOENT, as for a file that was never there");
        });
    }

    #endregion

    #region M-6 Tests

    [Test]
    public void M6EverySeedHasItsOwnStreamTest()
    {
        // Seeds 2k and 2k+1 gave the same bytes: a generation run that numbers its seeds got
        // every item twice.
        var streams = Enumerable.Range(0, 8).Select(seed =>
        {
            var task = PythonTask("import os; print(os.urandom(12).hex())");
            task.RandomSeed = seed;
            return Run(Python, task).Stdout.Trim();
        }).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(streams, Is.Unique);
            Assert.That(streams, Is.All.Length.EqualTo(24));
            Assert.That(streams[0], Is.EqualTo(SEED_ZERO_BYTES), "a taskset that sets no seed gets the bytes it always got");
        });
    }

    #endregion

    #region L-1 Tests

    [Test]
    public void L1ACrashIsNotAnExitWithMinusOneTest()
    {
        // The audit's program under a case that expects -1: it passed.
        var suite = new VerifySuiteData { Cases = [new VerifySuiteCaseData { ExpectedExitCode = -1 }] };
        var crash = CTask("int main(void) { __builtin_trap(); }\n");
        crash.Suite = suite;

        var result = Run(Clang, crash);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.CaseResults.Single().Passed, Is.False);
        });
    }

    [Test]
    public void L1AnExitWithTheExpectedCodeStillPassesTest()
    {
        var suite = new VerifySuiteData { Cases = [new VerifySuiteCaseData { ExpectedExitCode = 3, ExpectedStdout = "ok\n" }] };

        var result = Run(Python, PythonTask("import sys\nprint('ok')\nsys.exit(3)", suite: suite));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
    }

    #endregion

    #region L-2 Tests

    [Test]
    public void L2WordsOnStderrDoNotTurnAMetCaseIntoAnotherVerdictTest()
    {
        // The audit's program: prints what the case expects, exits 3 as expected, and writes the
        // words the sandbox looked for on stderr. It was MemoryExceeded.
        var suite = new VerifySuiteData { Cases = [new VerifySuiteCaseData { ExpectedExitCode = 3, ExpectedStdout = "ok\n" }] };
        const string program = "import sys\nprint('ok')\nsys.stderr.write('out of memory budget reached\\nMemoryError\\n')\nsys.exit(3)";

        var result = Run(Python, PythonTask(program, suite: suite));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
            Assert.That(result.CaseResults.Single().Passed, Is.True);
        });
    }

    #endregion

    #region Tools

    private static VerifyTaskData CTask(string code, params VerifySourceFileData[] more)
    {
        return new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.CLANG_22_1_0,
            Sources = [new VerifySourceFileData { Name = "main.c", Content = code }, .. more],
            EntryPoint = "main.c"
        };
    }

    /// <summary>The same runtime from a copy of its files in another folder, as another node has it.</summary>
    private VerifyResolvedRuntime Elsewhere(VerifyResolvedRuntime runtime)
    {
        var root = Path.Combine(m_otherRoot, "elsewhere");
        var folder = runtime.Descriptor.RootDir ?? throw new InvalidOperationException("the runtime has no folder");
        var target = Path.Combine(root, folder);

        if (!Directory.Exists(target))
        {
            var source = Path.Combine(RuntimesRoot, folder);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var copy = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
            }
        }

        return VerifyRuntimeCatalog.Resolve(root, runtime.Descriptor.Id, out var reason) ?? throw new InvalidOperationException(reason);
    }

    #endregion
}
