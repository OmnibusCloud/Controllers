using System.Reflection;
using OutWit.Common.Plugins;
using OutWit.Engine.Interfaces;

// Loads the controllers of a staged folder into one shared load context, as the cloud client
// does, and prints the Verify module's self-check. With a first carrier (an assembly, a type and a
// static method), that assembly is loaded into the same context first and the method is called
// before the controllers load: another controller that loaded the wasm binding and made its wasm
// host first. Its answer is printed as "first: ...". Exit 0 with the report; 2, 3 or 4 when the
// module or the carrier cannot be reached at all.

if (args.Length is not (1 or 4))
{
    Console.Error.WriteLine("usage: ClientHost <staged controllers folder> [<first carrier assembly> <type> <static method>]");
    return 2;
}

if (args.Length == 4)
{
    var first = Assembly.LoadFrom(args[1]).GetType(args[2])?.GetMethod(args[3], BindingFlags.Public | BindingFlags.Static);
    if (first == null)
    {
        Console.Error.WriteLine("the first carrier or its method did not load");
        return 4;
    }

    Console.WriteLine($"first: {first.Invoke(null, null) ?? "(null)"}");
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
