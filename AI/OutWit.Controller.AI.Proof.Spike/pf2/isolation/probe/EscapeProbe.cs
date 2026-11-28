using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Win32;
using static OutWit.Controller.AI.Proof.Spike.Probe.NativeMethods;

namespace OutWit.Controller.AI.Proof.Spike.Probe;

/// <summary>
/// The checks of the escape probe. Each tries one access and records <c>allowed</c>, <c>blocked</c>
/// or <c>absent</c> (the path does not exist where the probe runs) with a count, a size or the error;
/// never the content of what it read.
/// </summary>
public sealed class EscapeProbe
{
    #region Constants

    private static readonly string[] SENSITIVE_NAME_PARTS = ["TOKEN", "SECRET", "PASSWORD", "KEY", "CREDENTIAL", "ACTIONS_RUNTIME", "ACTIONS_ID_TOKEN"];

    #endregion

    #region Fields

    private readonly Dictionary<string, string> m_inputs;

    private readonly List<(string Name, string Result, string Detail)> m_results = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the probe from its inputs (--name value pairs).
    /// </summary>
    /// <param name="args">The command line.</param>
    public EscapeProbe(IReadOnlyList<string> args)
    {
        m_inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Count; i += 2)
            m_inputs[args[i].TrimStart('-')] = args[i + 1];
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs every check that applies to this OS.
    /// </summary>
    public void Run()
    {
        Files();
        Network();
        LocalChannels();
        Processes();
        if (OperatingSystem.IsWindows())
            WindowsOnly();
        Environment();
    }

    /// <summary>
    /// Writes the results as one JSON object.
    /// </summary>
    /// <param name="stream">Where to.</param>
    public void Write(Stream stream)
    {
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("probe", "pf2probe");
        json.WriteString("os", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        json.WriteNumber("pid", System.Environment.ProcessId);
        json.WriteString("user", System.Environment.UserName);
        json.WriteStartArray("checks");
        foreach (var (name, result, detail) in m_results)
        {
            json.WriteStartObject();
            json.WriteString("name", name);
            json.WriteString("result", result);
            json.WriteString("detail", detail.Length > 300 ? detail[..300] : detail);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    #endregion

    #region Checks

    private void Files()
    {
        var home = Input("home");
        if (home is not null)
        {
            Check("home: list", () => $"{Count(home)} entries");
            Check("home: list ~/.ssh", () => $"{Count(Path.Combine(home, ".ssh"))} entries");
            Check("home: list ~/.config", () => $"{Count(Path.Combine(home, ".config"))} entries");
        }
        foreach (var name in new[] { "home-secret", "outside-secret" })
        {
            var path = Input(name);
            if (path is not null)
                Check($"read {name}", () => $"{File.ReadAllBytes(path).Length} bytes readable");
        }
        foreach (var name in new[] { "appdata", "localappdata" })
        {
            var path = Input(name);
            if (path is not null)
                Check($"list %{name.ToUpperInvariant()}%", () => $"{Count(path)} entries");
        }
        foreach (var path in List("runner-files"))
            Check($"read runner file {Path.GetFileName(path)}", () => $"{File.ReadAllBytes(path).Length} bytes readable");
        var githubEnv = Input("github-env");
        if (githubEnv is not null)
        {
            // Opened for appending and closed at once: nothing is written.
            Check("open the job's GITHUB_ENV file for writing", () =>
            {
                using var stream = new FileStream(githubEnv, FileMode.Append, FileAccess.Write);
                return "opened (nothing written)";
            });
        }
        var outside = Input("outside");
        if (outside is not null)
        {
            Check("write a file outside the job folder", () =>
            {
                var path = Path.Combine(outside, $"pf2-probe-{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(path, [1]);
                File.Delete(path);
                return "written and removed";
            });
        }
    }

    private void Network()
    {
        Check("TCP to 1.1.1.1:443", () => Connect(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 443)));
        Check("TCP to example.com:443 (name)", () => Connect(new DnsEndPoint("example.com", 443)));
        Check("UDP: a DNS query to 1.1.1.1:53", () =>
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = 4000;
            byte[] query = [0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e', 3, (byte)'c', (byte)'o', (byte)'m', 0,
                0x00, 0x01, 0x00, 0x01];
            socket.SendTo(query, new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));
            var buffer = new byte[512];
            var read = socket.Receive(buffer);
            return $"answer of {read} bytes";
        });
        Check("DNS: resolve example.com", () => $"{Dns.GetHostAddresses("example.com").Length} addresses");
        var port = Input("loopback-port");
        if (port is not null)
            Check("TCP to a listener on 127.0.0.1", () => Connect(new IPEndPoint(IPAddress.Loopback, int.Parse(port))));
    }

    private void LocalChannels()
    {
        foreach (var path in List("unix-socket"))
        {
            Check($"Unix socket {path}", () =>
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(path));
                return "connected (nothing sent)";
            });
        }
        if (!OperatingSystem.IsWindows())
            return;
        Check("list named pipes", () => $"{Directory.GetFiles(@"\\.\pipe\").Length} pipes visible");
        foreach (var name in List("pipe"))
        {
            Check($"named pipe {name}", () =>
            {
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut);
                pipe.Connect(3000);
                return "connected (nothing sent)";
            });
        }
    }

    private void Processes()
    {
        Check("start a child process", () =>
        {
            var info = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(Path.Combine(System.Environment.SystemDirectory, "cmd.exe"), "/c exit 0")
                : new ProcessStartInfo("/bin/sh", "-c true");
            info.UseShellExecute = false;
            using var child = Process.Start(info)!;
            child.WaitForExit(10000);
            return $"started, exit {child.ExitCode}";
        });
        Check("list processes", () => $"{Process.GetProcesses().Length} visible");
        var host = Input("host-pid");
        if (host is null)
            return;
        var pid = int.Parse(host);
        if (OperatingSystem.IsLinux())
        {
            Check("read /proc/1/cmdline", () => $"{File.ReadAllBytes("/proc/1/cmdline").Length} bytes readable");
            Check("read a host process's /proc/<pid>/cmdline", () => $"{File.ReadAllBytes($"/proc/{pid}/cmdline").Length} bytes readable");
            Check("read a host process's /proc/<pid>/environ", () => $"{File.ReadAllBytes($"/proc/{pid}/environ").Length} bytes readable");
            Check("open a host process's /proc/<pid>/mem", () =>
            {
                using var stream = new FileStream($"/proc/{pid}/mem", FileMode.Open, FileAccess.Read);
                return "opened (nothing read)";
            });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Check("a host process's executable path (proc_pidpath)", () =>
            {
                unsafe
                {
                    var buffer = stackalloc byte[4096];
                    var length = proc_pidpath(pid, buffer, 4096);
                    if (length <= 0)
                        throw new UnauthorizedAccessException($"proc_pidpath returned {length}, errno {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
                    return $"{length} bytes";
                }
            });
        }
        else if (OperatingSystem.IsWindows())
        {
            Check("OpenProcess(query) on a host process", () => Open(pid, PROCESS_QUERY_LIMITED_INFORMATION));
            Check("OpenProcess(read memory) on a host process", () => Open(pid, PROCESS_VM_READ | PROCESS_QUERY_INFORMATION));
        }
    }

    private void WindowsOnly()
    {
        Check("registry: read HKCU\\Software", () =>
        {
            using var key = Registry.CurrentUser.OpenSubKey("Software") ?? throw new UnauthorizedAccessException("not opened");
            return $"{key.SubKeyCount} subkeys";
        });
        Check("registry: read HKLM\\...\\Windows NT\\CurrentVersion", () =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion") ?? throw new UnauthorizedAccessException("not opened");
            return key.GetValue("ProductName") is null ? "opened, no value" : "opened, value read";
        });
        Check("registry: write HKCU\\Software\\Pf2Probe", () =>
        {
            using (Registry.CurrentUser.CreateSubKey(@"Software\Pf2Probe"))
            {
            }
            Registry.CurrentUser.DeleteSubKey(@"Software\Pf2Probe");
            return "created and removed";
        });
        Check("clipboard: open", () =>
        {
            if (!OpenClipboard(IntPtr.Zero))
                throw new UnauthorizedAccessException($"OpenClipboard failed, error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
            try
            {
                var text = IsClipboardFormatAvailable(CF_UNICODETEXT) && GetClipboardData(CF_UNICODETEXT) != IntPtr.Zero;
                return text ? "opened, text present (not read)" : "opened, no text";
            }
            finally
            {
                CloseClipboard();
            }
        });
        Check("desktop: top-level windows", () =>
        {
            var count = 0;
            var window = GetWindow(GetDesktopWindow(), GW_CHILD);
            while (window != IntPtr.Zero && count < 100000)
            {
                count++;
                window = GetWindow(window, GW_HWNDNEXT);
            }
            if (count == 0)
                throw new UnauthorizedAccessException("no window visible");
            return $"{count} windows visible; foreground {(GetForegroundWindow() == IntPtr.Zero ? "none" : "seen")}";
        });
    }

    private void Environment()
    {
        var names = System.Environment.GetEnvironmentVariables().Keys.Cast<string>().OrderBy(n => n, StringComparer.Ordinal).ToList();
        var sensitive = names.Where(n => SENSITIVE_NAME_PARTS.Any(p => n.Contains(p, StringComparison.OrdinalIgnoreCase))).ToList();
        var github = names.Where(n => n.StartsWith("GITHUB_", StringComparison.Ordinal) || n.StartsWith("RUNNER_", StringComparison.Ordinal) || n.StartsWith("ACTIONS_", StringComparison.Ordinal)).ToList();
        m_results.Add(("environment: variables inherited", names.Count == 0 ? "blocked" : "allowed", $"{names.Count} variables"));
        m_results.Add(("environment: GITHUB_/RUNNER_/ACTIONS_ variables", github.Count == 0 ? "blocked" : "allowed", string.Join(",", github)));
        m_results.Add(("environment: names that look like secrets", sensitive.Count == 0 ? "blocked" : "allowed", string.Join(",", sensitive)));
    }

    #endregion

    #region Tools

    private void Check(string name, Func<string> action)
    {
        try
        {
            m_results.Add((name, "allowed", action()));
        }
        catch (Exception error) when (error is DirectoryNotFoundException or FileNotFoundException)
        {
            m_results.Add((name, "absent", error.GetType().Name));
        }
        catch (SocketException error)
        {
            m_results.Add((name, "blocked", $"socket error {error.SocketErrorCode}"));
        }
        catch (AggregateException error) when (error.InnerException is SocketException socket)
        {
            m_results.Add((name, "blocked", $"socket error {socket.SocketErrorCode}"));
        }
        catch (Exception error)
        {
            m_results.Add((name, "blocked", $"{error.GetType().Name}: {error.Message}"));
        }
    }

    private static string Connect(EndPoint endpoint)
    {
        using var socket = endpoint is DnsEndPoint
            ? new Socket(SocketType.Stream, ProtocolType.Tcp)
            : new Socket(((IPEndPoint)endpoint).AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        var task = socket.ConnectAsync(endpoint);
        if (!task.Wait(6000))
            throw new TimeoutException("no connection within 6 s");
        return "connected (nothing sent)";
    }

    private static string Open(int pid, uint access)
    {
        var handle = OpenProcess(access, false, pid);
        if (handle == IntPtr.Zero)
            throw new UnauthorizedAccessException($"OpenProcess failed, error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        CloseHandle(handle);
        return "opened";
    }

    private static int Count(string folder)
    {
        return Directory.EnumerateFileSystemEntries(folder).Take(100000).Count();
    }

    private string? Input(string name)
    {
        return m_inputs.TryGetValue(name, out var value) && value.Length > 0 ? value : null;
    }

    private IEnumerable<string> List(string name)
    {
        return (Input(name) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    #endregion
}
