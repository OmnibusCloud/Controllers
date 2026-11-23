using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Pack;
using OutWit.Controller.AI.Smt.Live.Server;

namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// The live client of the Smt controller. <c>diagnostics</c>, <c>preflight</c> and <c>run</c>
/// drive the controller's bundled scripts on a real server through the public SDK, as a lab's
/// application does; <c>compare</c> and <c>pack</c> work on files only and need no server.
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

        try
        {
            return await RunAsync(arguments, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return SmtLiveExitCodes.FAILED;
        }
    }

    private static async Task<int> RunAsync(SmtLiveArguments arguments, CancellationToken ct)
    {
        switch (arguments.Verb)
        {
            case SmtLiveVerb.Compare:
                return new SmtLiveCompare(Console.Out).Run(arguments.Inputs[0], arguments.Inputs[1]);
            case SmtLiveVerb.Pack when arguments.Output != null:
                return new SmtLivePack(Console.Out).Run(arguments.Inputs[0], arguments.Query, arguments.Output);
        }

        if (arguments.Verb != SmtLiveVerb.Diagnostics && !File.Exists(arguments.Inputs[0]))
        {
            Console.Error.WriteLine($"the problem set {arguments.Inputs[0]} is not there");
            return SmtLiveExitCodes.USAGE;
        }

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
            _ => await new SmtLiveRun(server, Console.Out).RunAsync(arguments.Inputs[0], arguments.Options, arguments.Output, ct)
        };
    }

    #endregion
}
