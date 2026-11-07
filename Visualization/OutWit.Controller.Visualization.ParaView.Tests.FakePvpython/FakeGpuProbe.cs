using System.Text.Json;

namespace OutWit.Controller.Visualization.ParaView.Tests.FakePvpython;

/// <summary>
/// The fake's GPU-probe mode: selected when the script is <c>gpu_probe.py</c> (called with
/// <c>--status-file</c>, no task document). Echoes the requested <c>VTK_DEFAULT_OPENGL_WINDOW</c> as
/// the render window; the renderer string is driven by markers in the status-file PATH (tests control
/// the temp-storage root):
///   fake-egl-hw     — a hardware renderer string (the EGL-accepted case);
///   fake-egl-sw     — llvmpipe (EGL silently landing on software; must be rejected);
///   fake-probe-crash — exits 1 without a status (an unusable window class).
/// </summary>
internal static class FakeGpuProbe
{
    #region Constants

    public const string SCRIPT_NAME = "gpu_probe.py";

    public const string MODE_HARDWARE = "fake-egl-hw";

    public const string MODE_SOFTWARE = "fake-egl-sw";

    public const string MODE_CRASH = "fake-probe-crash";

    public const string HARDWARE_RENDERER = "NVIDIA GeForce RTX 9999/PCIe/SSE2";

    public const string SOFTWARE_RENDERER = "llvmpipe (LLVM 15.0.7, 256 bits)";

    #endregion

    #region Functions

    public static int Run(string[] args)
    {
        var statusIndex = Array.IndexOf(args, "--status-file");
        if (statusIndex < 0 || statusIndex + 1 >= args.Length)
        {
            Console.Error.WriteLine("fake-pvpython: gpu_probe expected --status-file <path>");
            return 2;
        }

        var statusPath = args[statusIndex + 1];
        if (statusPath.Contains(MODE_CRASH, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("fake-pvpython: fake probe crash requested");
            return 1;
        }

        var requestedWindow = Environment.GetEnvironmentVariable("VTK_DEFAULT_OPENGL_WINDOW") ?? "FakeOffscreenWindow";
        var renderer = statusPath.Contains(MODE_SOFTWARE, StringComparison.OrdinalIgnoreCase)
            ? SOFTWARE_RENDERER
            : statusPath.Contains(MODE_HARDWARE, StringComparison.OrdinalIgnoreCase)
                ? HARDWARE_RENDERER
                : "FakeGL Hardware";

        var status = new Dictionary<string, object?>
        {
            ["schema"] = 1,
            ["ok"] = true,
            ["stage"] = "done",
            ["error"] = "",
            ["render_window"] = requestedWindow,
            ["renderer"] = renderer
        };
        File.WriteAllText(statusPath, JsonSerializer.Serialize(status));
        return 0;
    }

    #endregion
}
