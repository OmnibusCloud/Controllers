using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OutWit.Controller.AI.Proof.Spike.Sandbox;

// PF-2 spike, isolation option (b): a command in a Windows AppContainer and a Job Object.
//
//   Pf2Sandbox run     --job-dir D [--grant-read TOOLCHAIN]... [--memory-mb N] [--wall-s N]
//                      [--processes N] [--no-container] [--lpac] [--no-children] [--name P] -- cmd args
//   Pf2Sandbox probe   --job-dir D --outside-dir O [--lpac] [--name P]
//   Pf2Sandbox cleanup --name P [--grant-read DIR]... [--job-dir D]
//
// `run` prints the run's JSON. `probe` runs the checks (a file inside the job folder, a file
// outside it, a write outside it, the user's profile, a TCP connection to an address, to a name,
// to a listener of this process on loopback, a child process) each in the container and, as the
// control, without it. `cleanup` removes the container's entries from the folders and deletes its
// profile.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Pf2Sandbox run|probe|cleanup [options]");
    return 2;
}

var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
var options = SandboxOptions.Parse(args.Skip(1).ToArray());
switch (args[0])
{
    case "run":
    {
        var result = AppContainerLauncher.Run(options);
        Console.WriteLine(JsonSerializer.Serialize(result, json));
        return 0;
    }
    case "cleanup":
    {
        var sid = AppContainerLauncher.EnsureProfile(options.Name);
        foreach (var dir in options.ReadDirs.Append(options.JobDir).Where(Directory.Exists))
            AppContainerLauncher.Revoke(dir, sid);
        var hr = AppContainerLauncher.DeleteProfile(options.Name);
        Console.WriteLine(JsonSerializer.Serialize(new { profile = options.Name, sid, delete_hresult = $"0x{hr:X8}" }, json));
        return hr == 0 ? 0 : 1;
    }
    case "probe":
        return Probe(options, json);
    default:
        Console.Error.WriteLine($"unknown verb {args[0]}");
        return 2;
}

static int Probe(SandboxOptions options, JsonSerializerOptions json)
{
    var outside = options.OutsideDir ?? throw new ArgumentException("--outside-dir is required");
    Directory.CreateDirectory(options.JobDir);
    Directory.CreateDirectory(outside);
    File.WriteAllText(Path.Combine(options.JobDir, "inside.txt"), "inside the job folder\n");
    File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside the job folder\n");
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var accepted = 0;
    _ = Task.Run(async () =>
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync();
            Interlocked.Increment(ref accepted);
            var stream = client.GetStream();
            var reply = "HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(reply);
        }
    });
    var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    var curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
    var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var cases = new (string Name, string[] Command, bool NoChildren)[]
    {
        ("read-inside", [cmd, "/c", "type", Path.Combine(options.JobDir, "inside.txt")], false),
        ("read-outside", [cmd, "/c", "type", Path.Combine(outside, "secret.txt")], false),
        ("write-outside", [cmd, "/c", "echo", "x>" + Path.Combine(outside, "written.txt")], false),
        ("list-user-profile", [cmd, "/c", "dir", "/b", profile], false),
        ("connect-address", [curl, "-sS", "-m", "10", "-o", "NUL", "http://1.1.1.1/"], false),
        ("connect-name", [curl, "-sS", "-m", "10", "-o", "NUL", "https://example.com/"], false),
        ("connect-loopback", [curl, "-sS", "-m", "5", "-o", "NUL", $"http://127.0.0.1:{port}/"], false),
        ("child-process", [cmd, "/c", cmd, "/c", "echo child"], true),
    };
    var report = new Dictionary<string, object>();
    foreach (var (name, command, noChildren) in cases)
    {
        foreach (var contained in new[] { true, false })
        {
            var before = accepted;
            var argv = new List<string> { "--name", options.Name, "--job-dir", options.JobDir, "--wall-s", "30", "--processes", "4" };
            if (!contained)
                argv.Add("--no-container");
            if (options.Lpac)
                argv.Add("--lpac");
            if (noChildren)
                argv.Add("--no-children");
            argv.Add("--");
            argv.AddRange(command);
            var written = Path.Combine(outside, "written.txt");
            if (File.Exists(written))
                File.Delete(written);
            var result = AppContainerLauncher.Run(SandboxOptions.Parse(argv));
            Thread.Sleep(200);
            var wroteOutside = File.Exists(written);
            report[$"{name}/{(contained ? "container" : "control")}"] = new
            {
                exit = result.ExitCode,
                ending = result.Ending,
                start_error = result.StartError,
                stdout = result.Stdout.Trim(),
                stderr = result.Stderr.Trim(),
                loopback_connections = accepted - before,
                outside_file_written = wroteOutside,
                wall_s = result.WallSeconds,
            };
        }
    }
    listener.Stop();
    Console.WriteLine(JsonSerializer.Serialize(report, json));
    return 0;
}
