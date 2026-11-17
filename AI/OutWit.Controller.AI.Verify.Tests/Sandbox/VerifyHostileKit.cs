using System.Text.Json;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The hostile taskset of the fleet proofs, written by code from the modules of the hostile
/// tests (<see cref="HostileModules"/>): fifteen tasks, each the input of an audit finding or a
/// control beside one, and the verdict each must get on every node. The files beside the live
/// tool's tasksets (<c>tasksets/hostile.jsonl</c>, <c>tasksets/hostile.expected.tsv</c>) are what
/// this writes, to the byte; <see cref="VerifyHostileKitTests"/> holds them to it and runs them.
/// <para>
/// What the first run on a fleet taught, kept in the tasks as they are: a script is handed to
/// its interpreter as a string, so there is no <c>m.py</c> to look at (the library, its folder
/// and the root are there); a sleep returns at once and moves the run's own clock; a bulk
/// memory fill and a call into the host are priced by the bytes they move, so no task of the
/// kit reaches a node's wall-clock backstop.
/// </para>
/// </summary>
internal static class VerifyHostileKit
{
    #region Constants

    /// <summary>The taskset's file name, beside the live tool's tasksets.</summary>
    public const string TASKSET_FILE = "hostile.jsonl";

    /// <summary>The expected verdicts' file name: index, verdict and what the task is, tab-separated.</summary>
    public const string EXPECTED_FILE = "hostile.expected.tsv";

    /// <summary>The task that prints the times, device and links of three files: all the same on every node.</summary>
    public const int FILE_RECORDS_TASK = 8;

    /// <summary>The task that sleeps a hundred seconds.</summary>
    public const int SLEEP_TASK = 10;

    /// <summary>The task that prints three file numbers: its output is the same on every node.</summary>
    public const int FILE_NUMBERS_TASK = 12;

    /// <summary>The task that fills 250 MiB in a loop without end.</summary>
    public const int FILL_TASK = 13;

    private const string PYTHON = "python-3.14.6";
    private const string WASI = "wasi";

    // The script is given to the interpreter as a string: there is no m.py to look at (the first
    // version of this task asked for one and got a RuntimeError on every machine). The library,
    // its folder and the root are there.
    private const string STAT =
        "import os\n" +
        "for path in ('/lib/python314.zip', '/lib', '/'):\n" +
        "    s = os.stat(path)\n" +
        "    print(path, s.st_atime_ns, s.st_mtime_ns, s.st_ctime_ns, s.st_dev, s.st_nlink, s.st_ino != 0)\n";

    private const string FILE_NUMBERS =
        "import os\nfor path in ('/lib/python314.zip', '/lib', '/'):\n    print(path, os.stat(path).st_ino)\n";

    private const string FILL = """
        (module
          (memory (export "memory") 4096)
          (func (export "_start")
            (loop $n
              (memory.fill (i32.const 65536) (i32.const 1) (i32.const 262144000))
              (br $n))))
        """;

    #endregion

    #region Functions

    /// <summary>Writes the kit.</summary>
    /// <returns>The taskset (JSON lines) and its expected verdicts (tab-separated lines), each line ended by a line feed.</returns>
    public static (string Taskset, string Expected) Write()
    {
        var lines = new List<string>();
        var expected = new List<string>();

        void Wasi(string what, string verdict, byte[] module)
        {
            var task = new Dictionary<string, object>
            {
                ["index"] = lines.Count,
                ["runtime"] = WASI,
                ["entry"] = "m.wasm",
                ["sources"] = new Dictionary<string, string> { ["m.wasm"] = Convert.ToBase64String(module) },
                ["suite"] = new[] { new Dictionary<string, string> { ["expected_stdout"] = "" } }
            };
            expected.Add($"{lines.Count}\t{verdict}\t{what} ({module.Length} bytes)");
            lines.Add(JsonSerializer.Serialize(task));
        }

        void Python(string what, string verdict, string source, string stdout)
        {
            var task = new Dictionary<string, object>
            {
                ["index"] = lines.Count,
                ["runtime"] = PYTHON,
                ["entry"] = "m.py",
                ["sources"] = new Dictionary<string, string> { ["m.py"] = source },
                ["suite"] = new[] { new Dictionary<string, string> { ["expected_stdout"] = stdout } }
            };
            expected.Add($"{lines.Count}\t{verdict}\t{what}");
            lines.Add(JsonSerializer.Serialize(task));
        }

        Python("control: prints one line", "Pass", "print(1)", "1\n");
        Python("V:C-1 stdout without end from a script", "OutputExceeded", "while True:\n    print('x' * 1000)\n", "");
        Wasi("V:C-1 48 writes of 64 MiB to stdout", "OutputExceeded", HostileModules.Flood(1, 48));
        Wasi("V:C-2 2000 locals in 2000 nested loops (11.7 s of compile before): priced above the default fuel", "Timeout", HostileModules.NestedLoops(2000, 2000));
        Wasi("V:C-2 4000 loops nested: over the nesting limit", "CompileError", HostileModules.NestedLoops(4000, 4000));
        Wasi("V:H-3 random_get into a wild pointer: exits with the errno it was answered", "RuntimeError", HostileModules.ExitWith("random_get", -16, 64));
        Wasi("V:H-3 clock_time_get into a wild pointer", "RuntimeError", HostileModules.ClockTo(-8));
        Wasi("fuel: a loop without end", "Timeout", HostileModules.Spin());
        Python("V:H-6 a file's times, device and links say nothing of the node", "Pass", STAT,
            "/lib/python314.zip 0 0 0 0 1 True\n/lib 0 0 0 0 1 True\n/ 0 0 0 0 1 True\n");
        Python("fuel: a busy loop in a script", "Timeout", "n = 0\nwhile True:\n    n += 1\n", "");

        // A sleep holds no worker: the run's own clock moves and the call returns at once (the
        // first version of this kit expected the node's backstop here, and was wrong).
        Python("a sleep of 100 s takes none of the node's time", "Pass", "import time\nstart = time.time()\ntime.sleep(100)\nprint('woke', int(time.time() - start))\n", "woke 100\n");
        Python("control: the task after the sleeper", "Pass", "print(2)", "2\n");

        // The file numbers are not known before the run, so the suite cannot hold them: the task
        // fails its suite on every machine, and the two machines agreeing on the output is the proof.
        Python("V:H-6 the file numbers are the same on two machines (fails its suite by design: agreement is the proof)", "Fail", FILE_NUMBERS, "");

        // A fill of 250 MiB is one instruction, and it is priced by the bytes it moves all the
        // same: the fuel ends this one in under a second, on every machine with the same count.
        // (The first version of this task expected the node's backstop to end it, and was wrong:
        // no task of this kit reaches the backstop. It stays covered by the test that shortens it.)
        Wasi("R-7 fills 250 MiB without end: priced by the bytes it moves, the fuel ends it", "Timeout", HostileModules.Bytes(FILL));
        Python("control: the task after the one the clock ended", "Pass", "print(3)", "3\n");

        return (string.Join("\n", lines) + "\n", string.Join("\n", expected) + "\n");
    }

    #endregion
}
