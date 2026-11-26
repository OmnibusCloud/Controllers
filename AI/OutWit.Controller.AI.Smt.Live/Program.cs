using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Pack;
using OutWit.Controller.AI.Smt.Live.Server;

namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// The live client of the Smt controller. <c>diagnostics</c>, <c>preflight</c>, <c>run</c> and
/// <c>fetch</c> drive the controller's bundled scripts on a real server through the public SDK,
/// as a lab's application does; <c>compare</c>, <c>pack</c>, <c>local</c> and
/// <c>comparative</c> work on this machine only and need no server. What a command line can be
/// refused for (its options, its folder, the environment and the addresses the key would go to)
/// is refused before any connection is made.
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

        // The first Ctrl+C stops the work: a local run between problems, the waiting for a job,
        // which goes on on the server, and a transfer; the command says what it stopped. The
        // second ends the tool at once.
        using var interrupt = new SmtLiveInterrupt();
        ConsoleCancelEventHandler press = (_, key) =>
        {
            key.Cancel = interrupt.Press();
            Console.Error.WriteLine(key.Cancel ? "stopping (Ctrl+C); Ctrl+C again ends the tool at once" : "ended (Ctrl+C a second time)");
        };
        Console.CancelKeyPress += press;

        try
        {
            return await RunAsync(arguments, clock, interrupt.Token);
        }
        catch (OperationCanceledException) when (interrupt.Token.IsCancellationRequested)
        {
            Console.Error.WriteLine($"stopped (Ctrl+C): {arguments.Verb.ToString().ToLowerInvariant()} did not finish");
            return SmtLiveExitCodes.STOPPED;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return SmtLiveExitCodes.ERROR;
        }
        finally
        {
            Console.CancelKeyPress -= press;
        }
    }

    private static async Task<int> RunAsync(SmtLiveArguments arguments, SmtLiveClock clock, CancellationToken ct)
    {
        switch (arguments.Verb)
        {
            case SmtLiveVerb.Compare:
                return new SmtLiveCompare(Console.Out).Run(arguments.Inputs[0], arguments.Inputs[1]);
            case SmtLiveVerb.Pack:
                return new SmtLivePack(Console.Out).Run(arguments.Inputs[0], arguments.Query, arguments.Output!);
            case SmtLiveVerb.Comparative:
                return new SmtLiveComparative(Console.Out).Run(arguments.Inputs[0], arguments.Inputs[1], arguments.Fixed);
        }

        if (arguments.Verb is not (SmtLiveVerb.Diagnostics or SmtLiveVerb.Fetch) && !File.Exists(arguments.Inputs[0]))
        {
            Console.Error.WriteLine($"the problem set {arguments.Inputs[0]} is not there");
            return SmtLiveExitCodes.USAGE;
        }

        if (arguments.Verb == SmtLiveVerb.Local)
            return await new SmtLiveLocal(Console.Out, clock).RunAsync(arguments.Inputs[0], arguments.Options, arguments.Degree, arguments.Output, ct);

        // The folder of a run's documents, and a run's options, are refused before a connection.
        string? folder = null;
        if (arguments.Verb == SmtLiveVerb.Run)
        {
            folder = arguments.Output ?? SmtLiveRun.DefaultFolder(clock);
            if (SmtLiveRun.Refuse(arguments.Options, folder, Console.Out) is { } refused)
                return refused;
        }
        else if (arguments.Job is { } job)
        {
            folder = arguments.Output ?? SmtLiveRun.FetchFolder(job);
            if (!SmtLiveFolder.Prepare(folder, out var unusable))
            {
                Console.Error.WriteLine($"{unusable}; nothing was fetched");
                return SmtLiveExitCodes.USAGE;
            }
        }

        try
        {
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
                SmtLiveVerb.Fetch => await new SmtLiveRun(server, Console.Out, clock).FetchAsync(arguments.Job!.Value, folder, ct),
                _ => await new SmtLiveRun(server, Console.Out, clock).RunAsync(arguments.Inputs[0], arguments.Options, folder, ct)
            };
        }
        finally
        {
            if (folder != null)
                SmtLiveFolder.RemoveIfEmpty(folder);
        }
    }

    #endregion
}
