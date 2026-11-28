using OutWit.Controller.AI.Proof.Spike.Probe;

// PF-2 spike: the escape probe, run as the "kernel" inside a sandbox (and once outside it, the control).
//
//   pf2probe [--home DIR] [--home-secret FILE] [--outside DIR] [--outside-secret FILE] [--appdata DIR]
//            [--localappdata DIR] [--runner-files A;B] [--github-env FILE] [--loopback-port N]
//            [--unix-socket A;B] [--pipe A;B] [--host-pid N] [--out FILE]
//
// Prints (or writes) one JSON object: for each check, allowed, blocked or absent, with a count, a size
// or the error, never what was read.

var probe = new EscapeProbe(args);
probe.Run();
var outIndex = Array.IndexOf(args, "--out");
if (outIndex >= 0 && outIndex + 1 < args.Length)
{
    using var file = File.Create(args[outIndex + 1]);
    probe.Write(file);
}
else
{
    using var stdout = Console.OpenStandardOutput();
    probe.Write(stdout);
}
return 0;
