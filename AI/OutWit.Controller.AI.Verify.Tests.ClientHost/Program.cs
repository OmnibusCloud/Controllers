using System.Reflection;
using OutWit.Common.Plugins;
using OutWit.Engine.Interfaces;

// Loads the controllers of a staged folder into one shared load context, as the cloud client
// does, and prints the Verify module's self-check. Exit 0 with the report; 2 or 3 when the
// module cannot be reached at all.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: ClientHost <staged controllers folder>");
    return 2;
}

using var loader = new WitPluginLoader<IWitControllerNode>(args[0], useIsolatedContexts: false);
loader.Load();

var plugin = loader.Plugins.FirstOrDefault(p => p.GetType().Assembly.GetName().Name == "OutWit.Controller.AI.Verify");
var run = plugin?.GetType().Assembly
    .GetType("OutWit.Controller.AI.Verify.Sandbox.VerifySelfCheck")
    ?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
if (run == null)
{
    Console.Error.WriteLine("verify.module or its self-check did not load");
    return 3;
}

Console.WriteLine((string)run.Invoke(null, null)!);
return 0;
