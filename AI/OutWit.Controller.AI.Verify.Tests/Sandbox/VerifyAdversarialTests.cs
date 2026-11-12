using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The adversarial suite (v0): every hostile program lands in a verdict without harming
/// the host — no escape, no hang, no host resource exhaustion. This suite doubles as a
/// publishable trust artifact ("audit the sandbox yourself"), so each case names the
/// class of attack it stands for.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyAdversarialTests : VerifySandboxTestBase
{
    #region Containment

    [Test]
    public void NoNetworkSocketsTest()
    {
        var result = Run(Python, PythonTask("import socket; socket.socket().connect(('1.1.1.1', 80))"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
        Assert.That(result.Stderr, Does.Contain("Error"));
    }

    [Test]
    public void NoHostFilesystemReadTest()
    {
        var probe = OperatingSystem.IsWindows() ? @"C:\Windows\win.ini" : "/etc/passwd";
        var result = Run(Python, PythonTask($"print(open({ToPyLiteral(probe)}).read())"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
        Assert.That(result.Stderr, Does.Contain("Error"));
    }

    [Test]
    public void NoWriteToReadOnlyStdlibPreopenTest()
    {
        var result = Run(Python, PythonTask("open('/lib/evil.txt', 'w').write('x')"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
        Assert.That(result.Stderr, Does.Contain("Error"));
    }

    [Test]
    public void ALibraryFileCannotBeOpenedForWritingOrTruncatedTest()
    {
        // The runtime root is read-only for files as well as for the folder: opening an
        // existing library file to write, append or truncate is refused, and its bytes stay.
        var layer = Path.Combine(Python.RootPath!, "lib", "python314.zip");
        var before = new FileInfo(layer).Length;

        const string program =
            "import os\n" +
            "for mode in ('w', 'r+', 'a', 'wb'):\n" +
            "    try:\n" +
            "        open('/lib/python314.zip', mode).close()\n" +
            "        print('OPENED', mode)\n" +
            "    except OSError:\n" +
            "        print('refused')\n" +
            "for name in ('truncate', 'remove', 'rename'):\n" +
            "    try:\n" +
            "        if name == 'rename':\n" +
            "            os.rename('/lib/python314.zip', '/lib/gone.zip')\n" +
            "        elif name == 'remove':\n" +
            "            os.remove('/lib/python314.zip')\n" +
            "        else:\n" +
            "            os.truncate('/lib/python314.zip', 0)\n" +
            "        print('DONE', name)\n" +
            "    except (OSError, AttributeError):\n" +
            "        print('refused')\n";

        var result = Run(Python, PythonTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo(string.Concat(Enumerable.Repeat("refused\n", 7))));
            Assert.That(new FileInfo(layer).Length, Is.EqualTo(before));
        });
    }

    [Test]
    public void PathTraversalOutOfPreopenIsContainedTest()
    {
        // Even armed with a preopen, ".." must not climb to the real host tree.
        var result = Run(Python, PythonTask("print(open('/../../../../etc/passwd').read())"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
    }

    [Test]
    public void NoSubprocessSpawnTest()
    {
        var result = Run(Python, PythonTask("import subprocess; subprocess.run(['/bin/sh', '-c', 'echo pwned'])"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
    }

    [Test]
    public void NoThreadEscapeTest()
    {
        // WASI p1 has no thread spawn; the attempt must fail as a verdict, not hang the host.
        var result = Run(Python, PythonTask(
            "import threading; t = threading.Thread(target=lambda: None); t.start(); t.join(); print('threaded')"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
    }

    #endregion

    #region Resource Attacks

    [Test]
    public void InfiniteLoopIsBoundedByFuelTest()
    {
        var result = Run(Python, PythonTask("\nwhile True:\n    pass\n"),
            new VerifyLimitsData { FuelBudget = 3_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    [Test]
    public void BusyComputeIsBoundedByWallClockTest()
    {
        // A program that never yields and burns fuel slowly is still stopped by the epoch deadline.
        var result = Run(Python, PythonTask("x = 0\nwhile True:\n    x += 1\n"),
            new VerifyLimitsData { FuelBudget = 500_000_000_000, WallTimeMs = 400 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    [Test]
    public void MemoryBombIsBoundedTest()
    {
        var result = Run(Python, PythonTask("x = b'a'\nwhile True:\n    x += x\n"),
            new VerifyLimitsData { MemoryBytes = 128L * 1024 * 1024, FuelBudget = 500_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.AnyOf(VerifyVerdict.MemoryExceeded, VerifyVerdict.Timeout));
    }

    [Test]
    public void StdoutFloodIsCappedTest()
    {
        var result = Run(Python, PythonTask("\nwhile True:\n    print('flood')\n"),
            new VerifyLimitsData { StdoutLimitBytes = 32 * 1024, FuelBudget = 20_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.StdoutTruncated, Is.True);
        Assert.That(result.Stdout.Length, Is.LessThanOrEqualTo(32 * 1024));
        Assert.That(result.Verdict, Is.AnyOf(VerifyVerdict.OutputExceeded, VerifyVerdict.Timeout));
    }

    [Test]
    public void DeepRecursionIsContainedTest()
    {
        // Python converts stack exhaustion into a RecursionError; either way, no host crash.
        var result = Run(Python, PythonTask("import sys; sys.setrecursionlimit(10**8)\ndef f(n): return f(n+1)\nf(0)"),
            new VerifyLimitsData { FuelBudget = 500_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.AnyOf(VerifyVerdict.RuntimeError, VerifyVerdict.Timeout, VerifyVerdict.MemoryExceeded));
    }

    [Test]
    public void ASleepDoesNotHoldAWorkerTest()
    {
        // A sleep is a call into the host, where neither fuel nor the wall limit reaches.
        // In the sandbox it returns at once and moves the run's own clock: an hour asked for
        // costs nothing, and the clock reads an hour later.
        var started = DateTime.UtcNow;
        var result = Run(Python, PythonTask("import time\nbefore = time.time()\ntime.sleep(3600)\nprint(before, time.time(), time.monotonic())"),
            new VerifyLimitsData { WallTimeMs = 2_000 });
        var elapsed = DateTime.UtcNow - started;

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("0.0 3600.0 3600.0\n"));
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(30)));
        });
    }

    [Test]
    public void AFolderCannotBeListedTest()
    {
        // The one folder a guest has is its runtime's library: nothing to list for a task, and
        // a listing's order would be the host file system's.
        var result = Run(Python, PythonTask(
            "import os\n" +
            "for path in ('/', '/lib'):\n" +
            "    try:\n" +
            "        print('LISTED', sorted(os.listdir(path)))\n" +
            "    except OSError as error:\n" +
            "        print('refused', type(error).__name__)\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("refused PermissionError\nrefused PermissionError\n"));
        });
    }

    [Test]
    public void AFilesTimesCannotBeSetTest()
    {
        var result = Run(Python, PythonTask(
            "import os\n" +
            "for times in ((0, 0), (2**62, 2**62), (-2**62, -2**62)):\n" +
            "    try:\n" +
            "        os.utime('/lib/python314.zip', times)\n" +
            "        print('SET')\n" +
            "    except (OSError, OverflowError, AttributeError, NotImplementedError) as error:\n" +
            "        print('refused')\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("refused\nrefused\nrefused\n"));
        });
    }

    [Test]
    public void JavaScriptInfiniteLoopIsBoundedTest()
    {
        var result = Run(QuickJs, JsTask("while (true) {}",
            limits: new VerifyLimitsData { FuelBudget = 2_000_000_000, WallTimeMs = 30_000 }));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    #endregion

    #region Non-Determinism Probes

    [Test]
    public void WallClockIsPinnedTest()
    {
        // time.time() reads the run's own clock, which moves only when the program sleeps, so
        // two reads in a row are identical — a program cannot observe real time (a
        // non-determinism and side-channel source).
        var result = Run(Python, PythonTask("import time; print(time.time() == time.time())"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(result.Stdout.Trim(), Is.EqualTo("True"));
    }

    [Test]
    public void AWaitLoopEndsBecauseSleepingMovesTheClockTest()
    {
        // The shape of a retry with a deadline: with a clock that never moved this would spin
        // until the fuel ran out. Each sleep moves the run's clock, so the loop ends where it
        // would in real life, and the same way on every node.
        const string program =
            "import time\n" +
            "deadline = time.monotonic() + 5\n" +
            "turns = 0\n" +
            "while time.monotonic() < deadline:\n" +
            "    time.sleep(0.5)\n" +
            "    turns += 1\n" +
            "print(turns, time.monotonic())\n";

        var first = Run(Python, PythonTask(program));
        var second = Run(Python, PythonTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(first.Verdict, Is.EqualTo(VerifyVerdict.Pass), first.Stderr);
            Assert.That(first.Stdout, Is.EqualTo("10 5.0\n"));
            Assert.That(second.Stdout, Is.EqualTo(first.Stdout));
            Assert.That(second.FuelConsumed, Is.EqualTo(first.FuelConsumed));
        });
    }

    [Test]
    public void HostRemainsHealthyAfterHostileBatchTest()
    {
        // After a barrage of hostile programs, a benign task still runs correctly — the host
        // engine and sandbox are not left in a poisoned state.
        var hostile = new[]
        {
            "while True: pass",
            "x=b'a'\nwhile True: x+=x",
            "import socket; socket.socket()",
            "raise SystemExit(9)"
        };
        foreach (var code in hostile)
        {
            Run(Python, PythonTask(code),
                new VerifyLimitsData { FuelBudget = 2_000_000_000, MemoryBytes = 128L * 1024 * 1024, WallTimeMs = 5_000 });
        }

        var healthy = Run(Python, PythonTask("print('still-alive', 2 + 2)"));
        Assert.That(healthy.Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(healthy.Stdout.Trim(), Is.EqualTo("still-alive 4"));
    }

    #endregion

    #region Helpers

    private static string ToPyLiteral(string path)
    {
        return "'" + path.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    }

    #endregion
}
