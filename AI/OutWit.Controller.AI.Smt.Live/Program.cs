using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Pack;
using OutWit.Controller.AI.Smt.Live.Server;

namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// The live client of the Smt controller. <c>diagnostics</c>, <c>preflight</c> and <c>run</c>
/// drive the controller's bundled scripts on a real server through the public SDK, as a lab's
/// application does; <c>compare</c>, <c>pack</c>, <c>local</c> and <c>comparative</c> work on
/// this machine only and need no server.
/// </summary>
public static class Program
{
    #region Functions

    /// <summary>
    /// Runs the tool.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code (<see cref="SmtLiveExitCodes"/>).</returns>
    public static async Task<int> Main(string[] args)
    {
        // The measured verbs count from the tool's start: the moment the files were ready.
        var clock = SmtLiveClock.FromProcessStart();

        var arguments = SmtLiveArguments.Parse(args, out var error);
        if (arguments == null)
        {
            if (error == null)
            {
                SmtLiveUsage.Write(Console.Out);
                return SmtLiveExitCodes.OK;
            }

            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            SmtLiveUsage.Write(Console.Error);
            return SmtLiveExitCodes.USAGE;
        }

        // Ctrl+C stops a local run between problems (the running sessions end at their next read)
        // and the local waiting for a job, which goes on on the server.
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, press) =>
        {
            press.Cancel = true;
            cancel.Cancel();
        };

        try
        {
            return await RunAsync(arguments, clock, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Console.Error.WriteLine("stopped");
            return SmtLiveExitCodes.FAILED;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return SmtLiveExitCodes.FAILED;
        }
    }

    private static async Task<int> RunAsync(SmtLiveArguments arguments, SmtLiveClock clock, CancellationToken ct)
    {
        switch (arguments.Verb)
        {
            case SmtLiveVerb.Compare:
                return new SmtLiveCompare(Console.Out).Run(arguments.Inputs[0], arguments.Inputs[1]);
            case SmtLiveVerb.Pack when arguments.Output != null:
                return new SmtLivePack(Console.Out).Run(arguments.Inputs[0], arguments.Query, arguments.Output);
            case SmtLiveVerb.Comparative:
                return new SmtLiveComparative(Console.Out).Run(arguments.Inputs[0], arguments.Inputs[1], arguments.Fixed);
        }

        if (arguments.Verb != SmtLiveVerb.Diagnostics && !File.Exists(arguments.Inputs[0]))
        {
            Console.Error.WriteLine($"the problem set {arguments.Inputs[0]} is not there");
            return SmtLiveExitCodes.USAGE;
        }

        if (arguments.Verb == SmtLiveVerb.Local)
            return await new SmtLiveLocal(Console.Out, clock).RunAsync(arguments.Inputs[0], arguments.Options, arguments.Degree, arguments.Output, ct);

        var environment = SmtLiveEnvironment.Read(Environment.GetEnvironmentVariable, out var error);
        if (environment == null)
        {
            Console.Error.WriteLine(error);
            return SmtLiveExitCodes.USAGE;
        }

        await using var server = await SmtLiveServerCloud.ConnectAsync(environment, Console.Out, ct);
        return arguments.Verb switch
        {
            SmtLiveVerb.Diagnostics => await new SmtLiveDiagnostics(server, Console.Out).RunAsync(arguments.Output, ct),
            SmtLiveVerb.Preflight => await new SmtLivePreflight(server, Console.Out).RunAsync(arguments.Inputs[0], arguments.Options, ct),
            _ => await new SmtLiveRun(server, Console.Out, clock).RunAsync(arguments.Inputs[0], arguments.Options, arguments.Output, ct)
        };
    }

    #endregion
}
