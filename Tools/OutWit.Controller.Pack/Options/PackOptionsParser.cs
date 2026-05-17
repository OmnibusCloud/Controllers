namespace OutWit.Controller.Pack.Options
{
    public static class PackOptionsParser
    {
        #region Methods

        public static PackOptions Parse(string[] args)
        {
            if (args == null)
                throw new ArgumentNullException(nameof(args));

            if (args.Length == 0 ||
                args.Any(a => a is "-h" or "--help"))
            {
                return new PackOptions { ShowHelp = true };
            }

            string? moduleDir = null;
            string? output = null;
            var allowExternal = false;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--module":
                        moduleDir = RequireValue(args, ref i, arg);
                        break;

                    case "--output":
                        output = RequireValue(args, ref i, arg);
                        break;

                    case "--allow-external-uris":
                        allowExternal = true;
                        break;

                    default:
                        if (moduleDir == null && !arg.StartsWith('-'))
                        {
                            moduleDir = arg;
                            break;
                        }
                        throw new ArgumentException($"Unknown argument: '{arg}'");
                }
            }

            if (string.IsNullOrWhiteSpace(moduleDir))
                throw new ArgumentException(
                    "Required argument missing: --module <dir> (or pass it positionally).");

            return new PackOptions
            {
                ModuleDir = moduleDir!,
                OutputPath = output,
                AllowExternalUris = allowExternal,
                ShowHelp = false,
            };
        }

        private static string RequireValue(string[] args, ref int i, string flag)
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException($"Flag '{flag}' requires a value.");
            return args[++i];
        }

        #endregion
    }
}
