using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// What a C build may write to the node's disk (finding N-5 of the audit of 2026-11-18): every
/// product was capped by itself and nothing bounded their sum, so a task of forty one-line
/// sources held 2,480 MiB of objects in the sandbox's folder, and the build's fuel paid for
/// about 20 GiB. The objects and the program now share one budget of bytes, checked at the write.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyClangBuildBudgetTests : VerifySandboxTestBase
{
    #region Constants

    private const long MEBIBYTE = 1024 * 1024;

    #endregion

    #region Tests

    [Test]
    public void N5ABuildWritesNoMoreThanItsBudgetObjectsAndProgramTogetherTest()
    {
        // The audit's sources, fewer of them: each compiles to an object of 62 MiB, under the cap
        // of one product. Two fit the build's budget; the third is cut at the write.
        var task = Task(4, 62, referenced: false);

        VerifyResultData result = null!;
        var peakOnDisk = PeakFolderBytes(Sandbox.IoDirectory, () => result = Sandbox.Execute(Clang, task, VerifySandboxDefaults.Resolve(null, null)));

        TestContext.Out.WriteLine($"{result.Verdict}: {result.Stderr}; the sandbox's folder held {peakOnDisk / MEBIBYTE} MiB at the most");
        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Is.EqualTo($"the build wrote more than {VerifyClangBuild.BUILD_LIMIT_BYTES >> 20} MiB, its objects and its program together"));
            Assert.That(peakOnDisk, Is.LessThanOrEqualTo(VerifyClangBuild.BUILD_LIMIT_BYTES + MEBIBYTE), "the build stops growing at its budget");
        });
    }

    [Test]
    public void N5AProgramCountsAgainstTheBudgetItsObjectsLeftTest()
    {
        // Two objects of 40 MiB leave 48 MiB; the program that holds both arrays would be 80 MiB.
        // It is cut at what is left, and the words are the build's, not the linker's cap of one product.
        var result = Sandbox.Execute(Clang, Task(2, 40, referenced: true), VerifySandboxDefaults.Resolve(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Is.EqualTo($"the build wrote more than {VerifyClangBuild.BUILD_LIMIT_BYTES >> 20} MiB, its objects and its program together"));
        });
    }

    [Test]
    public void N5ABuildWithinItsBudgetIsWhatItWasTest()
    {
        // Three objects of 30 MiB, more together than one product may have, and a program that
        // uses none of them: 90 MiB and a little, within the budget.
        var result = Sandbox.Execute(Clang, Task(3, 30, referenced: false), VerifySandboxDefaults.Resolve(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(VerifyClangBuild.BUILD_LIMIT_BYTES, Is.EqualTo(2L * VerifyClangBuild.PRODUCT_LIMIT_BYTES), "a program of the largest size has room for its objects");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// A C task of a number of sources besides main, each one array of a size with a value in it.
    /// A program that reads the arrays holds them; one that does not is linked without them.
    /// </summary>
    private static VerifyTaskData Task(int sources, int mebibytes, bool referenced)
    {
        var names = Enumerable.Range(0, sources).Select(index => $"a{index}").ToList();
        var main = referenced
            ? $"extern unsigned char {string.Join(", ", names.Select(name => name + "[]"))};\nint main(void) {{ return {string.Join(" + ", names.Select(name => name + "[0]"))} - {sources}; }}\n"
            : "int main(void) { return 0; }\n";

        var files = new List<VerifySourceFileData>
        {
            new() { Name = "main.c", Content = main }
        };

        for (var index = 0; index < sources; index++)
            files.Add(new VerifySourceFileData { Name = $"a{index}.c", Content = $"unsigned char a{index}[{mebibytes} * 1024 * 1024] = {{1}};\n" });

        return new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.CLANG_22_1_0,
            EntryPoint = "main.c",
            Sources = files
        };
    }

    /// <summary>The most bytes a folder held at any moment while an action ran, as seen every few milliseconds.</summary>
    private static long PeakFolderBytes(string folder, Action action)
    {
        // What earlier runs left is deleted in the background: it is not this action's.
        OutWit.Controller.AI.Wasm.WasmHostDiscard.Drain(TimeSpan.FromSeconds(30));

        long peak = 0;
        using var done = new ManualResetEventSlim(false);
        var watcher = new Thread(() =>
        {
            while (!done.Wait(5))
                peak = Math.Max(peak, FolderBytes(folder));
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        watcher.Start();

        try
        {
            action();
        }
        finally
        {
            done.Set();
            watcher.Join();
        }

        return Math.Max(peak, FolderBytes(folder));
    }

    private static long FolderBytes(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Exists ? file.Length : 0)
                : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that went between the listing and the look.
            return 0;
        }
    }

    #endregion
}
