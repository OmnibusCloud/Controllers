using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Runner;

/// <summary>
/// Runs every document of a folder through an engine, each in a child process with a deadline
/// and a working-set ceiling, and records what the child said or how it died. A managed
/// engine's child is this tool (<c>run-one</c>); the wasm route's child is the wasmtime command
/// line running the precompiled extractor with fuel and a linear-memory ceiling.
/// </summary>
public sealed class PdfBatchRunner
{
    #region Constants

    private const string RESULT_PREFIX = "RESULT ";

    #endregion

    #region Fields

    private readonly string m_engine;
    private readonly TimeSpan m_timeout;
    private readonly long m_memoryCapBytes;
    private readonly int m_maxPages;
    private readonly int m_stackBytes;
    private readonly string? m_textFolder;
    private readonly string m_culture;
    private readonly PdfWasmRoute? m_wasm;

    #endregion

    #region Constructors

    /// <summary>Creates a runner.</summary>
    /// <param name="engine">The engine name (pdfpig, pdfpig-budget, pdflibrary or wasm).</param>
    /// <param name="timeout">The deadline per document, process start included.</param>
    /// <param name="memoryCapBytes">The working-set ceiling per document.</param>
    /// <param name="maxPages">Pages read at most (0 = all; managed engines only).</param>
    /// <param name="stackBytes">The extraction thread's stack (0 = the runtime's default; managed engines only).</param>
    /// <param name="textFolder">Where the texts go, or null.</param>
    /// <param name="culture">The culture the child runs under ("" = the machine's; managed engines only).</param>
    /// <param name="wasm">The wasm route, when the engine is wasm.</param>
    public PdfBatchRunner(string engine, TimeSpan timeout, long memoryCapBytes, int maxPages, int stackBytes, string? textFolder, string culture, PdfWasmRoute? wasm = null)
    {
        m_engine = engine;
        m_timeout = timeout;
        m_memoryCapBytes = memoryCapBytes;
        m_maxPages = maxPages;
        m_stackBytes = stackBytes;
        m_textFolder = textFolder;
        m_culture = culture;
        m_wasm = wasm;
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
            var record = m_wasm == null ? RunManaged(file) : RunWasm(file, m_wasm);
            records.Add(record);
            writer.WriteLine(JsonSerializer.Serialize(record));
            writer.Flush();
            Console.Error.WriteLine($"  {m_engine,-14} {record.File,-40} {record.Verdict,-8} {record.WallMs,9:0} ms {record.PeakMb,8:0} MB {record.Exception ?? record.FirstPageError ?? ""}");
        }

        return records;
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

    private PdfRunRecord RunManaged(string file)
    {
        var arguments = new List<string> { "run-one", m_engine, file, "--max-pages", m_maxPages.ToString(), "--stack", m_stackBytes.ToString() };
        if (m_textFolder != null)
            arguments.AddRange(["--text", TextPath(file)]);
        if (m_culture.Length > 0)
            arguments.AddRange(["--culture", m_culture]);

        var outcome = PdfChildProcess.Run(SelfStart(arguments), m_timeout, m_memoryCapBytes);
        var line = outcome.Stdout.Split('\n').FirstOrDefault(candidate => candidate.StartsWith(RESULT_PREFIX, StringComparison.Ordinal));
        var record = line != null && outcome.Killed == null
            ? JsonSerializer.Deserialize<PdfRunRecord>(line[RESULT_PREFIX.Length..])!
            : new PdfRunRecord { Engine = m_engine, File = Path.GetFileName(file), Bytes = new FileInfo(file).Length };

        if (outcome.Killed != null)
        {
            record.Verdict = outcome.Killed;
        }
        else if (line == null)
        {
            record.Verdict = "crash";
            record.Message = Clip(outcome.Stderr);
        }

        return Finish(record, outcome);
    }

    private PdfRunRecord RunWasm(string file, PdfWasmRoute wasm)
    {
        var textPath = m_textFolder != null ? TextPath(file) : Path.Combine(Path.GetTempPath(), $"pdf-spike-{Environment.ProcessId}.txt");
        if (File.Exists(textPath))
            File.Delete(textPath);

        // The guest sees the input's folder as /in and the text's folder as /out, nothing else.
        var start = new ProcessStartInfo(wasm.Wasmtime);
        foreach (var argument in new[]
        {
            "run", "--allow-precompiled",
            "-W", $"fuel={wasm.Fuel}",
            "-W", $"max-memory-size={wasm.MaxMemoryBytes}",
            "-W", "trap-on-grow-failure=y",
            "-W", "nan-canonicalization=y",
            "--dir", $"{Path.GetDirectoryName(Path.GetFullPath(file))}::/in",
            "--dir", $"{Path.GetDirectoryName(Path.GetFullPath(textPath))}::/out",
            wasm.Module,
            "/in/" + Path.GetFileName(file),
            "/out/" + Path.GetFileName(textPath),
        })
        {
            start.ArgumentList.Add(argument);
        }

        var outcome = PdfChildProcess.Run(start, m_timeout, m_memoryCapBytes);
        var record = new PdfRunRecord { Engine = m_engine, File = Path.GetFileName(file), Bytes = new FileInfo(file).Length };

        if (outcome.Killed != null)
        {
            record.Verdict = outcome.Killed;
        }
        else if (outcome.ExitCode == 0 && File.Exists(textPath))
        {
            var text = File.ReadAllText(textPath, Encoding.UTF8);
            record.Verdict = "ok";
            record.Chars = text.Length;
            record.Replacements = text.Count(character => character == '�');
            record.Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(textPath)));
            record.Sha256Lf = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n").Replace('\r', '\n'))));
        }
        else
        {
            var stderr = outcome.Stderr;
            record.Verdict = stderr.Contains("all fuel consumed", StringComparison.Ordinal) ? "fuel"
                : outcome.ExitCode == 2 ? "error"
                : "trap";
            record.Exception = record.Verdict;
            record.Message = Clip(stderr);
        }

        if (m_textFolder == null && File.Exists(textPath))
            File.Delete(textPath);

        return Finish(record, outcome);
    }

    private static PdfRunRecord Finish(PdfRunRecord record, PdfChildOutcome outcome)
    {
        record.ExitCode = outcome.ExitCode;
        record.WallMs = Math.Round(outcome.Wall.TotalMilliseconds, 1);
        record.PeakMb = Math.Round(outcome.PeakBytes / 1048576.0, 1);
        return record;
    }

    private string TextPath(string file)
    {
        return Path.Combine(m_textFolder!, Path.GetFileNameWithoutExtension(file) + ".txt");
    }

    private static string Clip(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = lines.FirstOrDefault(candidate =>
                candidate.Contains("overflow", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Unhandled", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("Fatal", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("fuel", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("panicked", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("error", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault() ?? "";
        return first.Length > 160 ? first[..160] : first;
    }

    #endregion
}
