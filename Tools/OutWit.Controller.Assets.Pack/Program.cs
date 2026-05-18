using CommandLine;
using OutWit.Controller.Assets.Pack.Options;

namespace OutWit.Controller.Assets.Pack
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // Standard CommandLineParser pipeline — produces a real help screen
            // on --help and prints parser errors to stderr instead of silently
            // returning default(T).
            var parser = new Parser(settings =>
            {
                settings.HelpWriter = Console.Out;
                settings.CaseInsensitiveEnumValues = true;
                settings.AutoHelp = true;
                settings.AutoVersion = true;
            });

            return await parser
                .ParseArguments<PackAssetsOptions>(args)
                .MapResult(
                    async opts =>
                    {
                        try
                        {
                            return await PackAssetsRunner.RunAsync(opts);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"outwit-assets-pack: {ex.Message}");
                            return 1;
                        }
                    },
                    errs => Task.FromResult(IsHelpOrVersion(errs) ? 0 : 2));
        }

        private static bool IsHelpOrVersion(IEnumerable<Error> errors)
        {
            // CommandLineParser surfaces --help / --version through error types,
            // which would otherwise look like parse failures. Treat them as
            // clean exits with code 0.
            return errors.Any(e => e is HelpRequestedError or VersionRequestedError or HelpVerbRequestedError);
        }
    }
}
