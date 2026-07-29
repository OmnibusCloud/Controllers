using System.Diagnostics;
using System.Reflection;
using OutWit.Controller.CalculiX.Extraction;
using OutWit.Engine.Data.Benchmark;

namespace OutWit.Controller.CalculiX.Runtime;

/// <summary>
/// Node benchmark of the Ccx.Solve activity: one complete run of a fixed
/// reference deck (a 20³-node static cube, embedded in the assembly so it
/// travels with the module unconditionally) through the same solver and the
/// same thread policy the real solves use. Rate is solves per second against
/// a unit string nodes are ranked within; the Custom bag carries the solved
/// maximum displacement as the marker that two nodes' scores measured the
/// same computation, not just the same duration (stable to engineering
/// tolerance across platforms — ccx is not bitwise-reproducible).
/// </summary>
public static class CcxBenchmark
{
    #region Constants

    /// <summary>Ranking scale id; nodes are compared only within one unit string.</summary>
    public const string UNIT = "ccx-static@ref20-v1";

    private const string DECK_RESOURCE = "benchmark.inp";

    private const string JOB_NAME = "benchmark";

    private const string SCRATCH_ROOT = "outwit-ccx-benchmark";

    #endregion

    #region Functions

    /// <summary>
    /// Runs the reference solve once and scores it.
    /// </summary>
    /// <param name="solverPath">Full path of the ccx executable.</param>
    /// <param name="cancellationToken">Kills the solver process tree when signaled.</param>
    /// <returns>The measured score.</returns>
    /// <exception cref="InvalidOperationException">The reference solve did not finish cleanly.</exception>
    public static async Task<WitBenchmarkResult> MeasureAsync(string solverPath, CancellationToken cancellationToken = default)
    {
        var scratchDirectory = Path.Combine(Path.GetTempPath(), SCRATCH_ROOT, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);

        try
        {
            await using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(DECK_RESOURCE)
                ?? throw new InvalidOperationException($"Embedded benchmark deck '{DECK_RESOURCE}' is missing."))
            await using (var deck = File.Create(Path.Combine(scratchDirectory, $"{JOB_NAME}.inp")))
            {
                await resource.CopyToAsync(deck, cancellationToken);
            }

            var stopwatch = Stopwatch.StartNew();
            var outcome = await CcxProcessRunner.RunAsync(solverPath, JOB_NAME, scratchDirectory, threads: 0, cancellationToken);
            stopwatch.Stop();

            if (outcome.ExitCode != 0)
                throw new InvalidOperationException($"Reference solve exited {outcome.ExitCode}: {outcome.LogTail}");

            var custom = new Dictionary<string, string>
            {
                ["nodes"] = "8000",
                ["elements"] = "6859"
            };

            var frdPath = Path.Combine(scratchDirectory, $"{JOB_NAME}.frd");
            if (File.Exists(frdPath))
            {
                var displacement = FrdResultReader.Read(frdPath)
                    .LastOrDefault(block => block.Name == "DISP" && block.Components.Count >= 3);

                if (displacement != null)
                {
                    var checksum = displacement.Values.Values.Max(values =>
                        System.Math.Sqrt(values[0] * values[0] + values[1] * values[1] + values[2] * values[2]));
                    custom["checksum"] = checksum.ToString("E6", System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return new WitBenchmarkResult
            {
                Rate = 1.0 / stopwatch.Elapsed.TotalSeconds,
                Unit = UNIT,
                Elapsed = stopwatch.Elapsed,
                Iterations = 1,
                Custom = custom
            };
        }
        finally
        {
            try
            {
                Directory.Delete(scratchDirectory, recursive: true);
            }
            catch
            {
                // Scratch cleanup is best-effort; the OS temp reaper covers stragglers.
            }
        }
    }

    #endregion
}
