using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// C and C++ compiled on the node: LLVM for WASI compiles every source and links the program
/// inside the sandbox under its own budget, nothing writable, and the program runs as a raw
/// WASI module under the task's limits. The standard libraries are there, sources in several
/// files and headers work, a compile or link error is a CompileError with the compiler's
/// diagnostics, C++ exceptions are the stated limit, and a build is reproducible to the byte.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyClangRuntimeTests : VerifySandboxTestBase
{
    #region Constants

    private static readonly string DEMO_TASKSET = Path.Combine("OutWit.Controller.AI.Verify.Live", "tasksets", "c-demo.jsonl");

    private const string CXX_PROGRAM =
        "#include <algorithm>\n" +
        "#include <iostream>\n" +
        "#include <map>\n" +
        "#include <numeric>\n" +
        "#include <sstream>\n" +
        "#include <string>\n" +
        "#include <unordered_map>\n" +
        "#include <vector>\n" +
        "struct Point { int x, y; };\n" +
        "template <typename T> T total(const std::vector<T>& v) { return std::accumulate(v.begin(), v.end(), T{}); }\n" +
        "int main() {\n" +
        "  std::vector<int> v{5, 3, 9, 1};\n" +
        "  std::sort(v.begin(), v.end());\n" +
        "  std::map<std::string, int> m{{\"b\", 2}, {\"a\", 1}};\n" +
        "  std::unordered_map<int, Point> u; u[7] = {1, 2};\n" +
        "  std::ostringstream s; for (auto [k, n] : m) s << k << n;\n" +
        "  auto big = std::ranges::max(v);\n" +
        "  std::cout << v[0] << ' ' << total(v) << ' ' << s.str() << ' ' << u[7].y << ' ' << big << '\\n';\n" +
        "  return 0;\n" +
        "}\n";

    #endregion

    #region Program Tests

    [Test]
    public void HelloInCRunsTest()
    {
        var result = Run(Clang, CTask("#include <stdio.h>\nint main(void) { printf(\"hello\\n\"); return 0; }\n"));
        TestContext.Out.WriteLine($"hello: run fuel {result.FuelConsumed:N0}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("hello\n"));
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    [Test]
    public void ACppProgramWithTheStandardLibraryRunsTest()
    {
        var result = Run(Clang, CTask(CXX_PROGRAM, "main.cpp"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("1 18 a1b2 2 9\n"));
        });
    }

    [Test]
    public void SourcesInSeveralFilesWithAHeaderBuildTogetherTest()
    {
        var task = CTask("#include <stdio.h>\n#include \"util.h\"\nint main(void) { printf(\"%d\\n\", twice(21)); return 0; }\n", "main.c",
            new VerifySourceFileData { Name = "util.h", Content = "int twice(int x);\n" },
            new VerifySourceFileData { Name = "lib/util.c", Content = "#include \"../util.h\"\nint twice(int x) { return 2 * x; }\n" });

        var result = Run(Clang, task);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("42\n"));
        });
    }

    [Test]
    public void StdinArgumentsAndTheExitCodeAreTheProgramsTest()
    {
        const string program =
            "#include <stdio.h>\n" +
            "int main(int argc, char** argv) {\n" +
            "  long a, b; if (scanf(\"%ld %ld\", &a, &b) != 2) return 2;\n" +
            "  printf(\"%ld %s %s %d\\n\", a + b, argv[0], argv[1], argc);\n" +
            "  return 3;\n" +
            "}\n";
        var task = CTask(program);
        task.Args = ["-x"];
        task.Stdin = "40 2\n";

        var result = Run(Clang, task);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.Stdout, Is.EqualTo("42 main -x 2\n"));
        });
    }

    [Test]
    public void SuiteCasesAreJudgedByStdoutAndExitTest()
    {
        var task = CTask("#include <stdio.h>\nint main(void) { long a, b; scanf(\"%ld %ld\", &a, &b); printf(\"%ld\\n\", a + b); return 0; }\n");
        task.Suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Stdin = "2 3", ExpectedStdout = "5\n" },
                new VerifySuiteCaseData { Stdin = "10 -4", ExpectedStdout = "6\n" },
                new VerifySuiteCaseData { Stdin = "1 1", ExpectedStdout = "3\n" }
            ]
        };

        var result = Run(Clang, task);

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Fail), "the third case expects a wrong sum");
    }

    [Test]
    public void TheDemoTasksetGivesTheVerdictsItNamesTest()
    {
        // The taskset the fleet proof runs (verify-live run c-demo.jsonl): ten programs that pass
        // (C and C++, one in three files), a wrong answer, a compile error, a crash and a loop
        // that never ends.
        var path = Path.GetFullPath(Path.Combine(RuntimesRoot, "..", "..", "..", DEMO_TASKSET));
        var parsed = VerifyTasksetParser.Parse(File.ReadAllText(path));
        Assert.That(parsed.Errors, Is.Empty);

        var results = parsed.Tasks.Select(task => Run(Clang, task)).ToList();
        foreach (var result in results.Where(result => result.Verdict != VerifyVerdict.Pass))
            TestContext.Out.WriteLine($"task {result.TaskIndex}: {result.Verdict} {result.Stderr}");

        var expected = Enumerable.Repeat(VerifyVerdict.Pass, 10)
            .Concat([VerifyVerdict.Fail, VerifyVerdict.CompileError, VerifyVerdict.RuntimeError, VerifyVerdict.Timeout]);
        Assert.That(results.Select(result => result.Verdict), Is.EqualTo(expected));
    }

    #endregion

    #region Build Tests

    [Test]
    public void ACompileErrorIsACompileErrorWithTheDiagnosticsTest()
    {
        var result = Run(Clang, CTask("int main(void) {\n  return missing;\n}\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Does.Contain("main.c:2").And.Contain("undeclared identifier 'missing'"));
        });
    }

    [Test]
    public void ALinkErrorIsACompileErrorTest()
    {
        var result = Run(Clang, CTask("int helper(void);\nint main(void) { return helper(); }\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Does.Contain("undefined symbol: helper"));
        });
    }

    [Test]
    public void CppExceptionsAreTheStatedLimitTest()
    {
        var result = Run(Clang, CTask("int main() { try { throw 1; } catch (int) { return 0; } }\n", "main.cpp"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Does.Contain("exceptions disabled"));
        });
    }

    [Test]
    public void NothingToCompileIsACompileErrorTest()
    {
        var result = Run(Clang, CTask("int twice(int x);\n", "util.h"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Does.Contain("no C or C++ source"));
        });
    }

    [Test]
    public void TheBuildIsReproducibleTest()
    {
        var first = Run(Clang, CTask(CXX_PROGRAM, "main.cpp"));
        var second = Run(Clang, CTask(CXX_PROGRAM, "main.cpp"));

        Assert.Multiple(() =>
        {
            Assert.That(first.Verdict, Is.EqualTo(VerifyVerdict.Pass), first.Stderr);
            Assert.That(second.Stdout, Is.EqualTo(first.Stdout));
            Assert.That(second.FuelConsumed, Is.EqualTo(first.FuelConsumed));
        });
    }

    #endregion

    #region Memory Tests

    [Test]
    public void ALargeLocalArrayFitsTheStackTest()
    {
        // 4 MB on the stack: the program's stack is 8 MiB, as on Linux.
        const string program =
            "#include <stdio.h>\n" +
            "int main(void) { int a[1000000]; for (int i = 0; i < 1000000; i++) a[i] = i; long long s = 0; for (int i = 0; i < 1000000; i++) s += a[i]; printf(\"%lld\\n\", s); return 0; }\n";

        var result = Run(Clang, CTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("499999500000\n"));
        });
    }

    [Test]
    public void ARecursionTenThousandDeepRunsTest()
    {
        // A depth-first walk down a chain: work after the call keeps it a recursion at -O2. The
        // engine's 512 KiB default ended it near ten thousand levels; the sandbox gives 2 MiB.
        const string program =
            "#include <stdio.h>\n#include <stdlib.h>\n" +
            "static int next[100000]; static volatile long long order;\n" +
            "static void dfs(int v, int d) { if (next[v] >= 0) dfs(next[v], d + 1); order = order * 7 + d; }\n" +
            "int main(int argc, char** argv) { int n = atoi(argv[1]); for (int i = 0; i < n; i++) next[i] = i + 1 < n ? i + 1 : -1; dfs(0, 0); printf(\"%lld\\n\", order); return 0; }\n";
        var task = CTask(program);
        task.Args = ["10000"];

        var result = Run(Clang, task);

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
    }

    [Test]
    public void AStackOverflowIsARuntimeErrorTest()
    {
        // 64 KB a frame, filled through a pointer -O2 cannot see through (an array only partly
        // used would shrink): the program's 8 MiB stack runs out near 128 levels, long before
        // the engine's call stack, on every platform.
        const string program =
            "#include <stdio.h>\n#include <string.h>\n" +
            "static void fill(char* b, int n) { memset(b, n, 65536); }\n" +
            "static void (*volatile touch)(char*, int) = fill;\n" +
            "static int f(int n) { char b[65536]; touch(b, n); return n ? f(n - 1) + b[n % 65536] : 0; }\n" +
            "int main(void) { printf(\"%d\\n\", f(1000)); return 0; }\n";

        var result = Run(Clang, CTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.Stderr, Does.StartWith("wasm trap: out of bounds memory access"));
        });
    }

    [Test]
    public void AWildPointerIsARuntimeErrorTest()
    {
        var result = Run(Clang, CTask("#include <stdio.h>\nint main(void) { puts(\"x\"); volatile int* p = (int*)0x7ffffff0; *p = 1; return 0; }\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.Stdout, Is.EqualTo("x\n"));
            Assert.That(result.Stderr, Does.StartWith("wasm trap: out of bounds memory access"));
        });
    }

    [Test]
    public void AbortIsARuntimeErrorNamedOnStderrTest()
    {
        var result = Run(Clang, CTask("#include <stdlib.h>\nint main(void) { abort(); }\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.Stderr, Does.StartWith("wasm trap: unreachable"));
        });
    }

    [Test]
    public void AnAllocationPastTheCapIsMemoryExceededTest()
    {
        // operator new without exceptions says bad_alloc and aborts.
        var result = Run(Clang, CTask("#include <vector>\nint main() { std::vector<long long> v; for (;;) v.push_back(1); }\n", "main.cpp"),
            new VerifyLimitsData { MemoryBytes = 64L << 20, FuelBudget = 50_000_000_000, WallTimeMs = 60_000 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.MemoryExceeded));
            Assert.That(result.Stderr, Does.Contain("bad_alloc"));
        });
    }

    #endregion

    #region Containment Tests

    [Test]
    public void TheProgramSeesNoFileSystemTest()
    {
        const string program =
            "#include <stdio.h>\n#include <stdlib.h>\n" +
            "int main(void) { printf(\"%s %s\\n\", fopen(\"/main.c\", \"r\") ? \"source\" : \"no source\", getenv(\"HOME\") ? \"home\" : \"no home\"); return 0; }\n";

        var result = Run(Clang, CTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("no source no home\n"));
        });
    }

    [Test]
    public void ARunawayProgramIsATimeoutTest()
    {
        var result = Run(Clang, CTask("int main(void) { volatile int x = 0; for (;;) x++; }\n"),
            new VerifyLimitsData { FuelBudget = 2_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    [Test]
    public void TheBuildLeavesNothingBehindTest()
    {
        var workingFolder = Path.Combine(Path.GetTempPath(), $"witai_clang_{Guid.NewGuid():N}");
        try
        {
            using (var sandbox = new VerifyWasmSandbox(workingFolder))
            {
                var built = sandbox.Execute(Clang, CTask("int main(void) { return 0; }\n"), VerifySandboxDefaults.Resolve(null, null));
                var failed = sandbox.Execute(Clang, CTask("int main(void) { return x; }\n"), VerifySandboxDefaults.Resolve(null, null));

                Assert.Multiple(() =>
                {
                    Assert.That(built.Verdict, Is.EqualTo(VerifyVerdict.Pass), built.Stderr);
                    Assert.That(failed.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
                    Assert.That(Directory.EnumerateFileSystemEntries(workingFolder), Is.Empty, "no sources, objects or programs are left");
                });
            }
        }
        finally
        {
            if (Directory.Exists(workingFolder))
                Directory.Delete(workingFolder, recursive: true);
        }
    }

    #endregion

    #region Tools

    private static VerifyTaskData CTask(string code, string entry = "main.c", params VerifySourceFileData[] more)
    {
        return new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.CLANG_22_1_0,
            Sources = [new VerifySourceFileData { Name = entry, Content = code }, .. more],
            EntryPoint = entry
        };
    }

    #endregion
}
