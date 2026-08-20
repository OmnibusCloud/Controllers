using System.Text.Json;

namespace OutWit.Controller.Visualization.ParaView.Tests.FakePvpython;

/// <summary>
/// The fake's benchmark mode: selected when the script passed before <c>--task-file</c> is
/// <c>benchmark_frames.py</c>. Reads the benchmark task document, writes one frame into
/// <c>output_dir</c>, and reports a deterministic status: <c>frames = max(1, min(max_frames,
/// target_seconds / 0.02))</c> at a simulated 20 ms per frame — one frame per process in the
/// controller's cycle mode.
///
/// Failure modes ride on markers in the <c>output_dir</c> PATH (tests control the temp-storage root;
/// the benchmark has no state file to carry state markers):
///   fake-fail      — fails at the build stage (status ok=false, exit 3);
///   fake-nostatus  — renders, exits 0, writes no status;
///   fake-hang      — hangs like a wedged runner (the cancellation gates must kill the tree);
///   anything else is a normal run.
/// </summary>
internal static class FakeBenchmark
{
    #region Constants

    public const string SCRIPT_NAME = "benchmark_frames.py";

    public const string MODE_FAIL = "fake-fail";

    public const string MODE_NO_STATUS = "fake-nostatus";

    public const string MODE_HANG = "fake-hang";

    public const double SIMULATED_SECONDS_PER_FRAME = 0.02;

    public const string RENDER_WINDOW = "FakeOffscreenWindow";

    public const string PARAVIEW_VERSION = "6.1.1-fake";

    #endregion

    #region Functions

    public static int Run(JsonElement task)
    {
        string Str(string name) => task.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        int Int(string name, int fallback) => task.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : fallback;
        double Dbl(string name, double fallback) => task.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : fallback;

        var statusPath = Str("status_path");
        var outputDir = Str("output_dir");
        var width = Int("width", 512);
        var height = Int("height", 512);
        var target = Dbl("target_seconds", 3.0);
        var maxFrames = Int("max_frames", 120);

        var status = new Dictionary<string, object?>
        {
            ["schema"] = 1,
            ["ok"] = false,
            ["stage"] = "load_task",
            ["error"] = "",
            ["frames"] = 0,
            ["render_seconds"] = 0.0,
            ["width"] = width,
            ["height"] = height,
            ["paraview_version"] = PARAVIEW_VERSION,
            ["render_window"] = RENDER_WINDOW,
            ["points"] = 226981
        };

        void WriteStatus()
        {
            if (!string.IsNullOrEmpty(statusPath))
                File.WriteAllText(statusPath, JsonSerializer.Serialize(status));
        }

        if (Environment.GetEnvironmentVariable("DISPLAY") != null)
        {
            status["stage"] = "load_task";
            status["error"] = "DISPLAY leaked into the runner environment";
            WriteStatus();
            return 3;
        }

        if (outputDir.Contains(MODE_FAIL, StringComparison.OrdinalIgnoreCase))
        {
            status["stage"] = "build";
            status["error"] = "fake benchmark failure requested";
            WriteStatus();
            Console.Error.WriteLine("fake-pvpython: fake benchmark failure requested");
            return 3;
        }

        if (outputDir.Contains(MODE_HANG, StringComparison.OrdinalIgnoreCase))
        {
            status["stage"] = "measure";
            WriteStatus();
            Thread.Sleep(TimeSpan.FromMinutes(5));
            return 0;
        }

        Directory.CreateDirectory(outputDir);
        FakeImageWriter.WritePng(Path.Combine(outputDir, "benchmark_frame.png"), width, height, alpha: false, shade: 128);

        var frames = Math.Max(1, Math.Min(maxFrames, (int)Math.Ceiling(target / SIMULATED_SECONDS_PER_FRAME)));
        status["stage"] = "done";
        status["ok"] = true;
        status["frames"] = frames;
        status["render_seconds"] = frames * SIMULATED_SECONDS_PER_FRAME;
        status["output_bytes"] = new FileInfo(Path.Combine(outputDir, "benchmark_frame.png")).Length;

        if (outputDir.Contains(MODE_NO_STATUS, StringComparison.OrdinalIgnoreCase))
            return 0;

        WriteStatus();
        return 0;
    }

    #endregion
}
