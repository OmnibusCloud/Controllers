using System.Diagnostics;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Runner;

/// <summary>
/// Runs a child process under a deadline and a working-set ceiling: the working set is
/// sampled every few milliseconds and the child is killed past either limit.
/// </summary>
public static class PdfChildProcess
{
    #region Constants

    private const int SAMPLE_MS = 10;

    #endregion

    #region Functions

    /// <summary>Runs the child to its end or to a limit.</summary>
    /// <param name="start">What to start (output redirection is set here).</param>
    /// <param name="timeout">The deadline.</param>
    /// <param name="memoryCapBytes">The working-set ceiling.</param>
    /// <returns>What happened.</returns>
    public static PdfChildOutcome Run(ProcessStartInfo start, TimeSpan timeout, long memoryCapBytes)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;

        var clock = Stopwatch.StartNew();
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();

        long peak = 0;
        string? killed = null;
        while (!child.WaitForExit(SAMPLE_MS))
        {
            try
            {
                child.Refresh();
                peak = Math.Max(peak, child.WorkingSet64);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            if (peak > memoryCapBytes)
                killed = "memory";
            else if (clock.Elapsed > timeout)
                killed = "timeout";

            if (killed == null)
                continue;

            try
            {
                child.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            child.WaitForExit();
            break;
        }

        clock.Stop();
        return new PdfChildOutcome(child.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult(), killed, peak, clock.Elapsed);
    }

    #endregion
}
