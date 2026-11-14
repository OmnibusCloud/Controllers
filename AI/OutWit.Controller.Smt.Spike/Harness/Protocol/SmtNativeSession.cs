using System.Diagnostics;
using System.Text;

namespace OutWit.Controller.Smt.Spike.Protocol;

/// <summary>
/// One interactive session with the native Z3 release (the baseline): a process with its stdin
/// and stdout as pipes, below-normal priority, the same driver as the wasm session. The driver
/// is asked for more input when every answer block sent for has arrived.
/// </summary>
internal sealed class SmtNativeSession
{
    #region Fields

    private readonly string m_z3;

    #endregion

    #region Constructors

    /// <summary>Makes a runner for the native solver at a path.</summary>
    /// <param name="z3">The z3 executable.</param>
    public SmtNativeSession(string z3)
    {
        m_z3 = z3;
    }

    #endregion

    #region Functions

    /// <summary>Runs one session to its end.</summary>
    /// <param name="args">The solver's arguments (no argv[0]).</param>
    /// <param name="driver">The protocol driver.</param>
    /// <param name="wallMs">The wall clock after which the process is killed.</param>
    /// <returns>How the session ended.</returns>
    public SmtSessionOutcome Run(IReadOnlyList<string> args, SmtSessionDriver driver, int wallMs)
    {
        var info = new ProcessStartInfo(m_z3)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // No byte-order mark ahead of the first command.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = info };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null && stderr.Length < 64 << 10)
                stderr.AppendLine(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            return new SmtSessionOutcome { End = "start_failed", Fault = exception.Message, WallMs = clock.ElapsedMilliseconds };
        }

        TryLowerPriority(process);
        process.BeginErrorReadLine();

        var stdout = new StringBuilder();
        var timedOut = false;
        var reader = Task.Run(() => Converse(process, driver, stdout));
        if (!reader.Wait(wallMs))
        {
            timedOut = true;
            TryKill(process);
            reader.Wait(5000);
        }

        if (!process.WaitForExit(timedOut ? 5000 : wallMs))
        {
            timedOut = true;
            TryKill(process);
            process.WaitForExit(5000);
        }

        process.WaitForExit();
        long peak = 0;
        try
        {
            peak = process.PeakWorkingSet64;
        }
        catch
        {
            // not every platform reports it after the exit
        }

        return new SmtSessionOutcome
        {
            End = timedOut ? "timeout" : "exited",
            ExitCode = timedOut ? -1 : process.ExitCode,
            Stdout = stdout.ToString(),
            Stderr = stderr.ToString(),
            PeakMemoryBytes = peak,
            WallMs = clock.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// Runs one whole script at once: written to stdin, stdin closed, stdout read to its end. No
    /// interleaving, so it works whatever the solver's buffering of a pipe is.
    /// </summary>
    /// <param name="args">The solver's arguments (no argv[0]).</param>
    /// <param name="script">Every command, the last one (exit).</param>
    /// <param name="nonce">The token of the first answer block's end (to time the check).</param>
    /// <param name="wallMs">The wall clock after which the process is killed.</param>
    /// <returns>How it ended, and the milliseconds until the first nonce line arrived (-1 when none did).</returns>
    public (SmtSessionOutcome Outcome, long FirstBlockMs) RunScript(IReadOnlyList<string> args, string script, string nonce, int wallMs)
    {
        var info = new ProcessStartInfo(m_z3)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = info };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null && stderr.Length < 64 << 10)
                stderr.AppendLine(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            return (new SmtSessionOutcome { End = "start_failed", Fault = exception.Message, WallMs = clock.ElapsedMilliseconds }, -1);
        }

        TryLowerPriority(process);
        process.BeginErrorReadLine();

        var stdout = new StringBuilder();
        long firstBlockMs = -1;
        var reader = Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    if (firstBlockMs < 0 && line == nonce)
                        firstBlockMs = clock.ElapsedMilliseconds;
                    stdout.Append(line).Append('\n');
                }
            }
            catch (IOException)
            {
                // the process went away
            }
        });

        try
        {
            process.StandardInput.Write(script);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // the solver stopped reading (it exited); what it wrote is kept
        }

        var timedOut = !reader.Wait(wallMs);
        if (timedOut)
            TryKill(process);
        process.WaitForExit(10_000);
        reader.Wait(5000);

        return (new SmtSessionOutcome
        {
            End = timedOut ? "timeout" : "exited",
            ExitCode = timedOut || !process.HasExited ? -1 : process.ExitCode,
            Stdout = stdout.ToString(),
            Stderr = stderr.ToString(),
            WallMs = clock.ElapsedMilliseconds
        }, firstBlockMs);
    }

    private static void Converse(Process process, SmtSessionDriver driver, StringBuilder stdout)
    {
        var input = process.StandardInput;
        var output = process.StandardOutput;
        var nonceLines = 0;
        try
        {
            var chunk = driver.Next("", 0);
            while (chunk != null)
            {
                input.Write(chunk);
                input.Flush();

                // Read until every block sent for has its nonce line (or the solver ends).
                var awaited = nonceLines + driver.Pending;
                while (nonceLines < awaited)
                {
                    var line = output.ReadLine();
                    if (line == null)
                        break;
                    stdout.Append(line).Append('\n');
                    if (line == driver.Nonce)
                        nonceLines++;
                }

                if (nonceLines < awaited)
                {
                    driver.Next(stdout.ToString(), 0);
                    break;
                }

                chunk = driver.Next(stdout.ToString(), 0);
            }

            input.Close();
            stdout.Append(output.ReadToEnd().Replace("\r\n", "\n"));
        }
        catch (IOException)
        {
            // the process went away; what it wrote is kept
        }
    }

    private static void TryLowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // best effort
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // already gone
        }
    }

    #endregion
}
