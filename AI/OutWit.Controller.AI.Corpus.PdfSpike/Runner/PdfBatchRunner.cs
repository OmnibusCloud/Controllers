using System.Diagnostics;
using System.Text.Json;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Runner;

/// <summary>
/// Runs every document of a folder through an engine, each in a child process of this tool
/// with a deadline and a working-set ceiling: the parent samples the child's working set,
/// kills it past either limit, and records what the child said or how it died.
/// </summary>
public sealed class PdfBatchRunner
{
    #region Constants

    private const string RESULT_PREFIX = "RESULT ";
    private const int SAMPLE_MS = 10;

    #endregion

    #region Fields

    private readonly string m_engine;
    private readonly TimeSpan m_timeout;
    private readonly long m_memoryCapBytes;
    private readonly int m_maxPages;
    private readonly int m_stackBytes;
    private readonly string? m_textFolder;
    private readonly string m_culture;

    #endregion

    #region Constructors

    /// <summary>Creates a runner.</summary>
    /// <param name="engine">The engine name.</param>
    /// <param name="timeout">The deadline per document, process start included.</param>
    /// <param name="memoryCapBytes">The working-set ceiling per document.</param>
    /// <param name="maxPages">Pages read at most (0 = all).</param>
    /// <param name="stackBytes">The extraction thread's stack (0 = the runtime's default).</param>
    /// <param name="textFolder">Where the texts go, or null.</param>
    /// <param name="culture">The culture the child runs under ("" = the machine's).</param>
    public PdfBatchRunner(string engine, TimeSpan timeout, long memoryCapBytes, int maxPages, int stackBytes, string? textFolder, string culture)
    {
        m_engine = engine;
        m_timeout = timeout;
        m_memoryCapBytes = memoryCapBytes;
        m_maxPages = maxPages;
        m_stackBytes = stackBytes;
        m_textFolder = textFolder;
        m_culture = culture;
    }

    #endregion

    #region Functions

    /// <summary>Runs every *.pdf of a folder in name order and writes one JSON line per document.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="output">The JSON-lines file.</param>
    /// <returns>The records.</returns>
    public IReadOnlyList<PdfRunRecord> RunFolder(string folder, string output)
    {
        if (m_textFolder != null)
            Directory.CreateDirectory(m_textFolder);

        var files = Directory.GetFiles(folder, "*.pdf").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        var records = new List<PdfRunRecord>();
        using var writer = new StreamWriter(output);

        foreach (var file in files)
        {
            var record = RunOne(file);
            records.Add(record);
            writer.WriteLine(JsonSerializer.Serialize(record));
            writer.Flush();
            Console.Error.WriteLine($"  {m_engine,-14} {record.File,-40} {record.Verdict,-8} {record.WallMs,9:0} ms {record.PeakMb,8:0} MB {record.Exception ?? record.FirstPageError ?? ""}");
        }

        return records;
    }

    private PdfRunRecord RunOne(string file)
    {
        var arguments = new List<string> { "run-one", m_engine, file, "--max-pages", m_maxPages.ToString(), "--stack", m_stackBytes.ToString() };
        if (m_textFolder != null)
            arguments.AddRange(["--text", Path.Combine(m_textFolder, Path.GetFileNameWithoutExtension(file) + ".txt")]);
        if (m_culture.Length > 0)
            arguments.AddRange(["--culture", m_culture]);

        var start = SelfStart(arguments);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;

        var clock = Stopwatch.StartNew();
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();

        long peak = 0;
        string? killed = null;
        while (!child.WaitForExit(SAMPLE_MS))
        {
            try
            {
                child.Refresh();
                peak = Math.Max(peak, child.WorkingSet64);
            }
            catch (InvalidOperationException)
            {
                break;
            }

            if (peak > m_memoryCapBytes)
                killed = "memory";
            else if (clock.Elapsed > m_timeout)
                killed = "timeout";

            if (killed != null)
            {
                try
                {
                    child.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                child.WaitForExit();
                break;
            }
        }

        clock.Stop();
        var text = stdout.GetAwaiter().GetResult();
        var errors = stderr.GetAwaiter().GetResult();

        var line = text.Split('\n').FirstOrDefault(candidate => candidate.StartsWith(RESULT_PREFIX, StringComparison.Ordinal));
        var record = line != null && killed == null
            ? JsonSerializer.Deserialize<PdfRunRecord>(line[RESULT_PREFIX.Length..])!
            : new PdfRunRecord { Engine = m_engine, File = Path.GetFileName(file), Bytes = new FileInfo(file).Length };

        if (killed != null)
        {
            record.Verdict = killed;
        }
        else if (line == null)
        {
            record.Verdict = "crash";
            record.Message = Clip(errors);
        }

        record.ExitCode = child.ExitCode;
        record.WallMs = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
        record.PeakMb = Math.Round(peak / 1048576.0, 1);
        return record;
    }

    /// <summary>A start description for this tool with arguments, whether it runs as an apphost or under dotnet.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The start description.</returns>
    public static ProcessStartInfo SelfStart(IEnumerable<string> arguments)
    {
        var host = Environment.ProcessPath!;
        var start = new ProcessStartInfo(host);
        var name = Path.GetFileNameWithoutExtension(host);
        if (string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(PdfBatchRunner).Assembly.Location);

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        return start;
    }

    private static string Clip(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = lines.FirstOrDefault(candidate => candidate.Contains("overflow", StringComparison.OrdinalIgnoreCase) || candidate.Contains("Unhandled", StringComparison.OrdinalIgnoreCase) || candidate.Contains("Fatal", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault() ?? "";
        return first.Length > 160 ? first[..160] : first;
    }

    #endregion
}
