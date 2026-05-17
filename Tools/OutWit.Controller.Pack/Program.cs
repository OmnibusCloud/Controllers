using OutWit.Controller.Pack.Options;

namespace OutWit.Controller.Pack
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                var options = PackOptionsParser.Parse(args);
                if (options.ShowHelp)
                {
                    PrintHelp();
                    return 0;
                }

                await PackRunner.RunAsync(options);
                return 0;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"outwit-controller-pack: {ex.Message}");
                Console.Error.WriteLine();
                PrintHelp();
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"outwit-controller-pack: {ex.Message}");
                return 1;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("outwit-controller-pack — package a built controller for WitCloud admin upload.");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  outwit-controller-pack <module-dir> [options]");
            Console.WriteLine("  outwit-controller-pack --module <module-dir> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --module <dir>           Same as positional argument.");
            Console.WriteLine("  --output <zip-path>      Output zip path. Default: <Name>-<Version>.zip");
            Console.WriteLine("                           in the current working directory.");
            Console.WriteLine("  --allow-external-uris    Permit dataAssets entries with non-file:// URIs.");
            Console.WriteLine("  -h, --help               Show this help.");
        }
    }
}
