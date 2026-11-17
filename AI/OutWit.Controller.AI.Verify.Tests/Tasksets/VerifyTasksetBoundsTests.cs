using System.Text;
using MemoryPack;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// What the server reads from a lab is bounded where it is read (rule R-6; the part of audit
/// finding V:H-1 that batch A left, and V:L-7): a line, a task's text, the number of its
/// sources, arguments and cases, the tasks of a taskset, and what a preflight answers. A
/// taskset is read a line at a time and never as a whole.
/// </summary>
[TestFixture]
public sealed class VerifyTasksetBoundsTests
{
    #region Constants

    private const string HEAD = """{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}""";
    private const long MEBIBYTE = 1024 * 1024;

    #endregion

    #region A Task

    [Test]
    public void H1ASuiteMayHaveNoMoreCasesThanATaskMayTest()
    {
        // A task runs once a case, each run under the task's limits: a suite without a bound
        // multiplied the ceilings by a number of the lab's choosing.
        var most = VerifyTasksetParser.Parse(HEAD + ",\"suite\":[" + string.Join(",", Enumerable.Repeat("{}", VerifyTasksetLimits.MAX_SUITE_CASES)) + "]}");
        var more = VerifyTasksetParser.Parse(HEAD + ",\"suite\":[" + string.Join(",", Enumerable.Repeat("{}", VerifyTasksetLimits.MAX_SUITE_CASES + 1)) + "]}");

        Assert.Multiple(() =>
        {
            Assert.That(most.Errors, Is.Empty);
            Assert.That(most.Tasks.FirstOrDefault()?.Suite?.Cases, Has.Count.EqualTo(VerifyTasksetLimits.MAX_SUITE_CASES));
            Assert.That(more.Tasks, Is.Empty);
            Assert.That(more.Errors.FirstOrDefault(), Is.EqualTo($"line 1: 'suite' has more than {VerifyTasksetLimits.MAX_SUITE_CASES} cases"));
        });
    }

    [Test]
    public void H1ATaskMayHaveNoMoreSourcesThanATaskMayTest()
    {
        // Every source of a Lua or a C task is a file on the node: a million empty ones fit a line.
        static string Sources(int count)
        {
            return string.Join(",", Enumerable.Range(0, count).Select(index => $"\"f{index}.py\":\"\""));
        }

        var most = VerifyTasksetParser.Parse("{\"runtime\":\"python-3.14.6\",\"entry\":\"f0.py\",\"sources\":{" + Sources(VerifyTasksetLimits.MAX_SOURCES) + "}}");
        var more = VerifyTasksetParser.Parse("{\"runtime\":\"python-3.14.6\",\"entry\":\"f0.py\",\"sources\":{" + Sources(VerifyTasksetLimits.MAX_SOURCES + 1) + "}}");

        Assert.Multiple(() =>
        {
            Assert.That(most.Errors, Is.Empty);
            Assert.That(more.Tasks, Is.Empty);
            Assert.That(more.Errors.FirstOrDefault(), Is.EqualTo($"line 1: 'sources' has more than {VerifyTasksetLimits.MAX_SOURCES} files"));
        });
    }

    [Test]
    public void H1ATaskAndACaseMayHaveNoMoreArgumentsThanTheyMayTest()
    {
        var arguments = string.Join(",", Enumerable.Repeat("\"a\"", VerifyTasksetLimits.MAX_ARGS + 1));

        var task = VerifyTasksetParser.Parse(HEAD + ",\"args\":[" + arguments + "]}");
        var testCase = VerifyTasksetParser.Parse(HEAD + ",\"suite\":[{\"args\":[" + arguments + "]}]}");

        Assert.Multiple(() =>
        {
            Assert.That(task.Errors.FirstOrDefault(), Is.EqualTo($"line 1: 'args' has more than {VerifyTasksetLimits.MAX_ARGS} arguments"));
            Assert.That(testCase.Errors.FirstOrDefault(), Is.EqualTo($"line 1: 'suite[0].args' has more than {VerifyTasksetLimits.MAX_ARGS} arguments"));
        });
    }

    [Test]
    public void H1ATaskOfMoreTextThanATaskMayHaveIsRefusedWithoutBeingMadeTest()
    {
        // 12 MiB of source in one line. The parser used to make the string (24 MiB of memory)
        // and leave the refusal to the planner; now the text is counted where it is read and
        // no string is made of text a task may not have.
        var line = Encoding.UTF8.GetBytes("{\"runtime\":\"python-3.14.6\",\"sources\":{\"m.py\":\"" + new string('#', (int)(12 * MEBIBYTE)) + "\"},\"stdin\":\"after\"}");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var refused = Assert.Throws<FormatException>(() => VerifyTasksetParser.ParseTask(line, 0));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(refused?.Message, Is.EqualTo($"the task's text is longer than {VerifyPackagePlanner.MAX_PACKAGE_BYTES} characters, the most a task may have"));
            Assert.That(allocated, Is.LessThan(MEBIBYTE), "nothing of the 12 MiB was copied");
        });
    }

    [Test]
    public void H1AnEscapedTextOverTheBoundIsRefusedTooTest()
    {
        // The same with every character written as an escape: six bytes of JSON a character.
        var escaped = string.Concat(Enumerable.Repeat("\\u0023", (int)VerifyPackagePlanner.MAX_PACKAGE_BYTES + 1));
        var line = Encoding.UTF8.GetBytes("{\"runtime\":\"python-3.14.6\",\"sources\":{\"m.py\":\"" + escaped + "\"}}");

        var refused = Assert.Throws<FormatException>(() => VerifyTasksetParser.ParseTask(line, 0));

        Assert.That(refused?.Message, Does.StartWith("the task's text is longer than"));
    }

    [Test]
    public void ATaskOfExactlyTheTextATaskMayHaveIsTakenTest()
    {
        // The bound refuses nothing the planner would have taken: sources, name and runtime together.
        const string name = "m.py";
        const string runtime = "python-3.14.6";
        var room = (int)VerifyPackagePlanner.MAX_PACKAGE_BYTES - 2 * name.Length - runtime.Length;
        var parsed = VerifyTasksetParser.Parse("{\"runtime\":\"" + runtime + "\",\"sources\":{\"" + name + "\":\"" + new string('#', room) + "\"}}");

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Errors, Is.Empty);
            Assert.That(parsed.Tasks.Select(VerifyPackagePlanner.EstimateBytes), Is.EqualTo(new[] { VerifyPackagePlanner.MAX_PACKAGE_BYTES }));
            Assert.That(VerifyTasksetPlanner.Plan(parsed.Tasks, null).Errors, Is.Empty);
        });
    }

    #endregion

    #region A Line

    [Test]
    public void H1ALineLongerThanALineMayBeIsPassedOverNotKeptTest()
    {
        var taskset = string.Join("\n", HEAD + "}", "{\"x\":\"" + new string('y', 5000) + "\"}", "", HEAD + "}");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(taskset));
        var lines = new VerifyTasksetLines(stream, maxLineBytes: 256);

        var seen = new List<(int Number, bool TooLong, int Length)>();
        while (lines.MoveNext())
            seen.Add((lines.Number, lines.TooLong, lines.Line.Length));

        Assert.That(seen, Is.EqualTo(new[] { (1, false, HEAD.Length + 1), (2, true, 0), (4, false, HEAD.Length + 1) }),
            "the long line is reported by its number with nothing of it kept, and the lines after it are read");
    }

    [Test]
    public void H1ALineTooLongIsAnErrorOfItsLineTest()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(HEAD + "}\n" + new string(' ', 300) + "x\n"));
        var lines = new VerifyTasksetLines(stream, maxLineBytes: 256);
        var indices = new VerifyTaskIndices();

        Assert.That(lines.MoveNext(), Is.True);
        Assert.That(VerifyTasksetParser.ParseLine(lines, indices).TaskIndex, Is.Zero);
        Assert.That(lines.MoveNext(), Is.True);

        var refused = Assert.Throws<FormatException>(() => VerifyTasksetParser.ParseLine(lines, indices));
        Assert.That(refused?.Message, Is.EqualTo($"the line is longer than {VerifyTasksetLimits.MAX_LINE_BYTES} bytes"));
    }

    [Test]
    public void LinesAreReadAcrossTheReadersBufferTest()
    {
        // Lines longer than one read of the stream, a mark before the first, blank lines between,
        // Windows line ends, and no line end after the last.
        var source = new string('#', 300_000);
        var task = "{\"runtime\":\"python-3.14.6\",\"sources\":{\"m.py\":\"" + source + "\"}}";
        var taskset = "﻿" + task + "\r\n\r\n   \t\r\n" + task + "\r\n" + task;

        var parsed = VerifyTasksetParser.Parse(taskset);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Errors, Is.Empty);
            Assert.That(parsed.Tasks.Select(item => item.TaskIndex), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(parsed.Tasks.Select(item => item.Sources[0].Content.Length), Has.All.EqualTo(source.Length));
        });
    }

    #endregion

    #region A Taskset

    [Test]
    public void H1ATasksetIsScannedALineAtATimeTest()
    {
        // 60,000 tasks of 2 KiB: 120 MiB of taskset, made as it is read. As one string it was
        // 240 MiB in memory before a single task was parsed, and then the tasks beside it.
        const int tasks = 60_000;
        var padding = new string('#', 2000);
        using var taskset = new VerifySyntheticTaskset(tasks, index => $$"""{"index":{{index}},"runtime":"{{(index % 3 == 0 ? "quickjs-0.15.1" : "python-3.14.6")}}","sources":{"m":"{{padding}}"},"suite":[{"expected_stdout":"{{index}}\n"}]}""");

        GC.Collect();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var scan = VerifyTasksetScanner.Scan(taskset, null, null);
        var held = taskset.PeakHeapBytes - before;

        TestContext.Out.WriteLine($"{tasks} tasks, {taskset.Bytes / MEBIBYTE} MiB of taskset: the heap grew by {held / MEBIBYTE} MiB at the most while it was scanned");

        Assert.Multiple(() =>
        {
            Assert.That(scan.Tasks, Is.EqualTo(tasks));
            Assert.That(scan.Accepted, Is.True);
            Assert.That(scan.Batches, Is.EqualTo(20_000 / 32 + 40_000 / 32));
            Assert.That(taskset.Bytes, Is.GreaterThan(100 * MEBIBYTE));
            Assert.That(held, Is.LessThan(48 * MEBIBYTE), "the scan holds a line and a task, not the taskset");
        });
    }

    [Test]
    public void TheScanCountsTheBatchesThePlanMakesTest()
    {
        // The preflight's batch count comes from counting, the packages from cutting; both are
        // the planner's one rule, here held to each other over tasks of mixed runtimes, sizes
        // and output caps.
        var random = new Random(20261117);
        var lines = Enumerable.Range(0, 3000).Select(index =>
        {
            var runtime = new[] { "python-3.14.6", "quickjs-0.15.1", "lua-5.4.8" }[random.Next(3)];
            var source = new string('#', random.Next(1, 400_000));
            var stdout = random.Next(4) == 0 ? ",\"limits\":{\"stdout_bytes\":" + random.Next(1, 16_000_000) + "}" : "";
            return "{\"index\":" + index + ",\"runtime\":\"" + runtime + "\",\"sources\":{\"m.lua\":\"" + source + "\"}" + stdout + "}";
        }).ToList();
        var options = new VerifyOptionsData { BatchSize = 48 };

        using var taskset = new VerifySyntheticTaskset(lines.Count, index => lines[index]);
        var scan = VerifyTasksetScanner.Scan(taskset, options, null);
        var plan = VerifyTasksetPlanner.Plan(VerifyTasksetParser.Parse(string.Join("\n", lines)).Tasks, options);

        Assert.Multiple(() =>
        {
            Assert.That(scan.Batches, Is.EqualTo(plan.Batches.Count));
            Assert.That(scan.Planned, Is.EqualTo(plan.Batches.Sum(batch => batch.Tasks.Count)));
            Assert.That(scan.Notes.Count, Is.EqualTo(plan.Notes.Count));
        });
    }

    [Test]
    public async Task TheSpoolGivesThePackagesThePlanGivesTest()
    {
        // A taskset that names its runtimes in any order is planned from working files exactly
        // as it was planned in memory: the same batches in the same packages.
        var lines = Enumerable.Range(0, 2500).Select(index =>
            "{\"index\":" + (index * 3) + ",\"runtime\":\"" + new[] { "quickjs-0.15.1", "python-3.14.6", "lua-5.4.8", "python-3.14.6" }[index % 4]
            + "\",\"sources\":{\"m.lua\":\"print(" + index + ")\"},\"seed\":" + index + (index % 7 == 0 ? ",\"limits\":{\"fuel\":" + (1_000_000 + index) + "}" : "") + "}").ToList();
        var options = new VerifyOptionsData { BatchSize = 16, MaxDegree = 3 };

        var folder = Path.Combine(Path.GetTempPath(), $"witai_verify_spool_{Guid.NewGuid():N}");
        var blobs = new VerifyTestBlobService(Path.Combine(folder, "blobs"));
        try
        {
            var planned = VerifyTasksetPlanner.Plan(VerifyTasksetParser.Parse(string.Join("\n", lines)).Tasks, options);
            var inMemory = await VerifyTaskPackages.PublishAsync(planned.Batches, blobs, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "test");

            var streamed = await PublishFromSpoolAsync(lines, options, Path.Combine(folder, "spool"), blobs);

            Assert.That(streamed, Has.Count.EqualTo(inMemory.Count));
            Assert.That(inMemory, Has.Count.GreaterThan(100), "the run is cut into many packages");

            for (var index = 0; index < inMemory.Count; index++)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(streamed[index].TaskCount, Is.EqualTo(inMemory[index].TaskCount));
                    Assert.That(streamed[index].BatchCount, Is.EqualTo(inMemory[index].BatchCount));
                    Assert.That(streamed[index].IndexDigest, Is.EqualTo(inMemory[index].IndexDigest));
                    Assert.That(streamed[index].Sha256, Is.EqualTo(inMemory[index].Sha256), "the same bytes: tasks, limits, batches and their order");
                });
            }

            Assert.That(Directory.Exists(Path.Combine(folder, "spool")), Is.False, "the working files are gone when the packages are stored");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void TheIndicesOfATasksetAreKeptAsBitsTest()
    {
        var indices = new VerifyTaskIndices();

        Assert.Multiple(() =>
        {
            Assert.That(indices.Add(0, 1, out _), Is.True);
            Assert.That(indices.Add(int.MaxValue, 2, out _), Is.True);
            Assert.That(indices.Add(65_536, 3, out _), Is.True);
            Assert.That(indices.Add(int.MaxValue, 4, out var earlier), Is.False);
            Assert.That(earlier, Is.EqualTo(2), "the line that used it first");
            Assert.That(indices.Add(65_536, 5, out earlier), Is.False);
            Assert.That(earlier, Is.EqualTo(3));
            Assert.That(indices.Count, Is.EqualTo(3));
        });

        // A taskset numbered from zero costs an eighth of a byte a task.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var dense = new VerifyTaskIndices();
        for (var index = 0; index < 1_000_000; index++)
            dense.Add(index, index + 1, out _);

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(8 * MEBIBYTE), "a million indices and the lines of the first 65,536");
        Assert.That(dense.Add(999_999, 0, out var forgotten), Is.False);
        Assert.That(forgotten, Is.Zero, "a line past those remembered is 'an earlier line'");
    }

    #endregion

    #region The Preflight

    [Test]
    public void L7APreflightOfAHostileTasksetIsBoundedTest()
    {
        // 20,000 lines, each wrong in its own way: malformed lines, limits over the ceilings,
        // runtime ids nobody has (long ones, with control characters), and a batch size to clamp.
        // The answer was one message a line and a field, in a job variable.
        const int lines = 20_000;
        using var taskset = new VerifySyntheticTaskset(lines, index => (index % 4) switch
        {
            0 => "{\"runtime\":\"python-3.14.6\",\"sources\":{\"m.py\":\"print(1)\"},\"bogus" + index + new string('x', 400) + "\":1}",
            1 => "{\"runtime\":\"python-3.14.6\",\"sources\":{\"m.py\":\"print(1)\"},\"limits\":{\"fuel\":9000000000000,\"memory\":99999999999,\"wall_ms\":5}}",
            2 => "{\"runtime\":\"cobol-" + index + "\\u0007\\n" + new string('z', 3000) + "\",\"sources\":{\"m.cob\":\"DISPLAY 1\"}}",
            _ => "not json " + new string('!', 1000)
        });

        var report = VerifyPreflightReport.Of(VerifyTasksetScanner.Scan(taskset, new VerifyOptionsData { BatchSize = 1_000_000 }, null));
        var image = MemoryPackSerializer.Serialize(report);

        TestContext.Out.WriteLine($"{lines} bad lines: {report.Messages.Count} messages, {image.Length} bytes as a job variable");

        Assert.Multiple(() =>
        {
            Assert.That(report.WellFormed, Is.False);
            Assert.That(report.TaskCount, Is.EqualTo(lines / 2), "the lines that parsed: the clamped ones and those of unknown runtimes");
            Assert.That(report.Messages, Has.Count.LessThanOrEqualTo(VerifyPreflightReport.MaxMessages));
            Assert.That(report.Messages.Max(message => message.Length), Is.LessThanOrEqualTo(VerifyTasksetLimits.MAX_MESSAGE_LENGTH));
            Assert.That(report.Messages, Has.Some.EqualTo($"and {lines / 2 - VerifyTasksetLimits.MAX_MESSAGES} more malformed line(s) not shown"));
            Assert.That(report.Messages, Has.Some.EqualTo(VerifyTasksetScan.MORE_UNKNOWN_RUNTIMES));
            Assert.That(report.Messages, Has.Some.EqualTo(VerifyTasksetChecks.WALL_TIME_NOTE));
            Assert.That(report.Messages, Has.Some.EqualTo($"batch size 1000000 clamped to {VerifyBatchExecutor.MAX_BATCH_TASKS}"));
            Assert.That(report.Messages, Has.Some.EndWith($"more {VerifyTasksetScan.NOTES} not shown"));
            Assert.That(report.UnknownRuntimeIds, Has.Count.EqualTo(VerifyTasksetLimits.MAX_LISTED_RUNTIMES));
            Assert.That(report.RuntimeIds, Has.Count.EqualTo(VerifyTasksetLimits.MAX_LISTED_RUNTIMES + 1));
            Assert.That(report.RuntimeIds.Concat(report.UnknownRuntimeIds).Max(id => id.Length), Is.LessThanOrEqualTo(64));
            Assert.That(string.Concat(report.Messages.Concat(report.RuntimeIds)).Any(char.IsControl), Is.False, "nothing of a lab's own name but plain characters");
            Assert.That(image.Length, Is.LessThan(32 * 1024), "the whole answer, as it travels");
        });
    }

    [Test]
    public void L7AnHonestPreflightSaysWhatItSaidBeforeTest()
    {
        // The bound changes nothing for a taskset with a handful of things to say.
        var taskset = string.Join("\n",
            """{"index":0,"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"limits":{"fuel":9000000000000}}""",
            """{"index":1,"runtime":"cobol-1959","sources":{"m.cob":"DISPLAY 1"}}""",
            """{"index":2,"runtime":"python-3.14.6"}""");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(taskset));

        var report = VerifyPreflightReport.Of(VerifyTasksetScanner.Scan(stream, null, null));

        Assert.Multiple(() =>
        {
            Assert.That(report.WellFormed, Is.False);
            Assert.That(report.TaskCount, Is.EqualTo(2));
            Assert.That(report.BatchCount, Is.EqualTo(2));
            Assert.That(report.RuntimeIds, Is.EqualTo(new[] { "python-3.14.6", "cobol-1959" }));
            Assert.That(report.UnknownRuntimeIds, Is.EqualTo(new[] { "cobol-1959" }));
            Assert.That(report.Messages, Is.EqualTo(new[]
            {
                "line 3: missing or empty 'sources'",
                "runtime 'cobol-1959' is not known to this build: the taskset would be refused.",
                $"task 0: fuel 9000000000000 clamped to ceiling {VerifyLimitCeilings.MAX_FUEL_BUDGET}"
            }));
        });
    }

    #endregion

    #region Tools

    private static async Task<List<VerifyTaskPackageData>> PublishFromSpoolAsync(IReadOnlyList<string> lines, VerifyOptionsData options, string folder, VerifyTestBlobService blobs)
    {
        using var spool = new VerifyTaskSpool(folder);
        using (var taskset = new VerifySyntheticTaskset(lines.Count, index => lines[index]))
        {
            var scan = VerifyTasksetScanner.Scan(taskset, options, spool);
            Assert.That(scan.Accepted, Is.True, string.Join("; ", scan.Errors.Shown));
        }

        return await VerifyTaskPackages.PublishAsync(spool, options, blobs, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "test");
    }

    #endregion
}
