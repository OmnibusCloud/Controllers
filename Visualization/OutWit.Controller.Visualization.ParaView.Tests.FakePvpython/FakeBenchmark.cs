using System.Text.Json;

namespace OutWit.Controller.Visualization.ParaView.Tests.FakePvpython;

/// <summary>
/// The fake's benchmark mode: selected when the script passed before <c>--task-file</c> is
/// <c>benchmark_frames.py</c>. Reads the benchmark task document, writes one frame into
/// <c>output_dir</c>, and reports a deterministic status: <c>frames = min(max_frames, target_seconds / 0.02)</c>
/// rendered at a simulated 20 ms per frame, so the rate the controller derives is known in advance.
///
/// Failure modes ride on <c>warmup_frames</c> (the benchmark has no state file to carry markers):
///   1001 — fails at the build stage (status ok=false, exit 3);
///   1002 — renders, exits 0, writes no status;
///   1003 — hangs like a wedged runner (the cancellation gates must kill the tree);
///   anything else is a normal run.
/// </summary>
internal static class FakeBenchmark
{
    #region Constants

    public const string SCRIPT_NAME = "benchmark_frames.py";

    public const int MODE_FAIL = 1001;

    public const int MODE_NO_STATUS = 1002;

    public const int MODE_HANG = 1003;

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
        var warmup = Int("warmup_frames", 1);
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

        if (warmup == MODE_FAIL)
        {
            status["stage"] = "build";
            status["error"] = "fake benchmark failure requested";
            WriteStatus();
            Console.Error.WriteLine("fake-pvpython: fake benchmark failure requested");
            return 3;
        }

        if (warmup == MODE_HANG)
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

        if (warmup == MODE_NO_STATUS)
            return 0;

        WriteStatus();
        return 0;
    }

    #endregion
}
