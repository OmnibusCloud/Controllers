// PF-2 spike: `which NAME`: the first NAME(.exe) on PATH, printed, exit 0; nothing found, exit 1.

if (args.Length != 1)
    return 2;
var name = args[0];
foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
{
    foreach (var candidate in new[] { name, name + ".exe" })
    {
        var path = Path.Combine(folder, candidate);
        if (File.Exists(path))
        {
            Console.WriteLine(path);
            return 0;
        }
    }
}
return 1;
