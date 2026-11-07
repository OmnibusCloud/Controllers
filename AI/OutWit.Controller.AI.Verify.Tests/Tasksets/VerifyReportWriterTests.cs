using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The verdict report is the run's identity: the same verdicts produce the same bytes
/// whatever the tasks cost (fuel, wall time) and whichever order the results arrived in.
/// </summary>
[TestFixture]
public sealed class VerifyReportWriterTests
{
    [Test]
    public void ReportIgnoresFuelPeakMemoryAndWallTimeTest()
    {
        var cheap = Results(fuel: 100, wallMs: 5);
        var costly = Results(fuel: 100_123, wallMs: 900);
        costly[1].PeakMemoryBytes += 65536; // one page more on another host, as seen on the fleet

        var one = Encoding.UTF8.GetString(VerifyReportWriter.Write(cheap));
        var two = Encoding.UTF8.GetString(VerifyReportWriter.Write(costly));

        Assert.Multiple(() =>
        {
            Assert.That(two, Is.EqualTo(one));
            Assert.That(one, Does.Not.Contain("fuel"));
            Assert.That(one, Does.Not.Contain("memory"));
            Assert.That(one, Does.Not.Contain("wall"));
        });
    }

    [Test]
    public void ReportIsOrderedByTaskIndexWhateverTheArrivalOrderTest()
    {
        var results = Results(fuel: 1, wallMs: 1);
        var reversed = results.AsEnumerable().Reverse().ToList();

        var one = Encoding.UTF8.GetString(VerifyReportWriter.Write(results));
        var two = Encoding.UTF8.GetString(VerifyReportWriter.Write(reversed));

        using var document = JsonDocument.Parse(one);
        var indices = document.RootElement.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("index").GetInt32()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(two, Is.EqualTo(one));
            Assert.That(indices, Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }

    [Test]
    public void ReportCarriesTheIdentityFieldsAndTheHistogramTest()
    {
        var results = Results(fuel: 1, wallMs: 1);

        using var document = JsonDocument.Parse(VerifyReportWriter.Write(results));
        var summary = document.RootElement.GetProperty("summary");
        var failed = document.RootElement.GetProperty("results")[1];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("total").GetInt32(), Is.EqualTo(3));
            Assert.That(summary.GetProperty("pass").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("verdicts").GetProperty("Fail").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("verdicts").GetProperty("Timeout").GetInt32(), Is.Zero);
            Assert.That(failed.GetProperty("verdict").GetString(), Is.EqualTo("Fail"));
            Assert.That(failed.GetProperty("exit").GetInt32(), Is.EqualTo(1));
            Assert.That(failed.TryGetProperty("peak_memory", out _), Is.False, "a metric of the provenance");
            Assert.That(failed.GetProperty("cases").GetArrayLength(), Is.EqualTo(1));
        });
    }

    private static List<VerifyResultData> Results(long fuel, int wallMs)
    {
        return
        [
            new VerifyResultData { TaskIndex = 0, Verdict = VerifyVerdict.Pass, ExitCode = 0, PeakMemoryBytes = 65536, FuelConsumed = fuel, WallMs = wallMs },
            new VerifyResultData
            {
                TaskIndex = 1, Verdict = VerifyVerdict.Fail, ExitCode = 1, PeakMemoryBytes = 2 * 65536, FuelConsumed = fuel * 2, WallMs = wallMs * 2,
                CaseResults = [new VerifyCaseResultData { CaseIndex = 0, Passed = false, Verdict = VerifyVerdict.Fail, ExitCode = 1 }]
            },
            new VerifyResultData { TaskIndex = 2, Verdict = VerifyVerdict.Pass, ExitCode = 0, PeakMemoryBytes = 65536, FuelConsumed = fuel * 3, WallMs = wallMs * 3 }
        ];
    }
}
