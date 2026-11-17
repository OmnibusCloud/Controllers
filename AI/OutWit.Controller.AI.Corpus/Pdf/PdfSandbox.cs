using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Controller.AI.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// Runs the extractor module on one PDF at a time per caller, in Corpus's own copy of the wasm
/// sandbox host: <c>pdftext /in/doc.pdf /out</c> with only those two folders visible, no network,
/// the host's pinned clock and seeded randomness, the fuel given after instantiation, a 1 GiB
/// linear memory, and a wall-clock backstop. Every way a PDF can end is a <see cref="PdfVerdict"/>;
/// a host-side fault (a run that reaches the backstop, a module that cannot be instantiated)
/// throws <see cref="PdfSandboxException"/>, so the shard goes back as a fault of this node.
/// <para>
/// A run's time is its fuel's and nothing else's. Each run has a folder of its own (the guest
/// sees <c>/in</c> and <c>/out</c>, the same on every run), written once and never used again,
/// and nobody waits for it to go: it is handed to <see cref="Shards.ShardCleanup"/>. Deleting the
/// input on the run's own path was where a run waited 90 s on a machine that inspects new files
/// (see there).
/// </para>
/// <para>
/// The module is compiled once, on construction; one sandbox serves every shard of a process,
/// concurrently. At most a few PDFs run at once in the process, so that their memory ceilings
/// together stay within the PDFs' share of the memory the process may use (a quarter; the shards
/// in flight have half).
/// </para>
/// </summary>
public sealed class PdfSandbox : IDisposable
{
    #region Constants

    private const string GUEST_NAME = "pdftext";
    private const string GUEST_IN = "/in";
    private const string GUEST_OUT = "/out";
    private const string INPUT_FILE = "doc.pdf";
    private const string TEXT_FILE = "text.txt";
    private const string RESULT_FILE = "result.json";
    private const string RUNS_FOLDER = "pdf";
    private const string IN_FOLDER = "in";
    private const string OUT_FOLDER = "out";
    private const string CAPTURE_FOLDER = "io";

    private const int EXIT_TEXT = 0;
    private const int EXIT_REFUSED = 2;

    private const string CODE_ENCRYPTED = "encrypted";
    private const string CODE_UNREADABLE = "unreadable";
    private const string CODE_NO_PAGES = "no_pages";

    private const int STDOUT_LIMIT_BYTES = 4096;
    private const int STDERR_LIMIT_BYTES = 64 * 1024;

    private static readonly Encoding TEXT_ENCODING = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    #endregion

    #region Fields

    private readonly WasmHost m_host;
    private readonly WasmModule m_module;
    private readonly SemaphoreSlim m_slots;
    private bool m_disposed;

    /// <summary>The runs of this process, for the names of their folders.</summary>
    private static long m_runs;

    #endregion

    #region Constructors

    /// <summary>Creates a sandbox for the module at a path under the stage's own limits.</summary>
    /// <param name="modulePath">The module file.</param>
    /// <exception cref="InvalidDataException">The file's bytes are not the pinned module.</exception>
    /// <exception cref="InvalidOperationException">The loaded wasmtime binding is not the one this controller was built against, or the engine cannot be made.</exception>
    public PdfSandbox(string modulePath)
        : this(modulePath, PdfStageLimits.FUEL, PdfStageLimits.MEMORY_BYTES, PdfStageLimits.MAX_TEXT_BYTES)
    {
    }

    /// <summary>Creates a sandbox for the module at a path with another envelope (calibration and tests).</summary>
    /// <param name="modulePath">The module file.</param>
    /// <param name="fuel">The fuel one PDF may burn from <c>_start</c> on.</param>
    /// <param name="memoryBytes">The cap of the module's linear memory.</param>
    /// <param name="maxTextBytes">The most text one PDF may give.</param>
    /// <param name="wallMs">The wall-clock backstop of one run (tests of a run the machine, not the document, stops).</param>
    /// <exception cref="InvalidDataException">The file's bytes are not the pinned module.</exception>
    /// <exception cref="InvalidOperationException">The loaded wasmtime binding is not the one this controller was built against, or the engine cannot be made.</exception>
    public PdfSandbox(string modulePath, long fuel, long memoryBytes, int maxTextBytes = PdfStageLimits.MAX_TEXT_BYTES, int wallMs = PdfStageLimits.WALL_MS)
    {
        WallMs = Math.Max(1, wallMs);
        var bytes = File.ReadAllBytes(modulePath);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(sha256, PdfStageLimits.MODULE_SHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"the PDF extractor module hashes to {sha256}, not the pinned {PdfStageLimits.MODULE_SHA256}");

        Fuel = Math.Max(1, fuel);
        MemoryBytes = Math.Max(1, memoryBytes);
        MaxTextBytes = Math.Max(0, maxTextBytes);
        m_slots = new SemaphoreSlim(Slots(MemoryBytes, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, Environment.ProcessorCount));

        m_host = new WasmHost();
        try
        {
            m_module = WasmModule.FromBytes(m_host.Engine, GUEST_NAME, bytes);
        }
        catch
        {
            m_host.Dispose();
            throw;
        }
    }

    #endregion

    #region Functions

    /// <summary>
    /// Extracts the text of one PDF. The run's folder is made in the work directory (the task's
    /// temp scope on a node), a new one for every run, and handed over for deletion after it.
    /// </summary>
    /// <param name="pdf">The PDF's bytes.</param>
    /// <param name="workDirectory">A folder of the caller's own; nothing is written outside it.</param>
    /// <param name="cancellationToken">Cancels the wait for a slot (a run that started ends by its budget).</param>
    /// <returns>The verdict, the text and what the run cost.</returns>
    /// <exception cref="PdfSandboxException">The run reached the wall-clock backstop, or the module could not be instantiated: a fault of the host, not of the PDF.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled while the PDF waited for a slot.</exception>
    /// <exception cref="IOException">The run's folder or its input could not be written: this node's disk.</exception>
    public PdfExtraction Extract(ReadOnlySpan<byte> pdf, string workDirectory, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        if (pdf.Length > PdfStageLimits.MAX_INPUT_BYTES)
            return PdfExtraction.Refused(PdfVerdict.Oversized, 0);

        // A folder of this run's own: nothing of an earlier run is read, overwritten or waited for.
        var run = Interlocked.Increment(ref m_runs).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        var runDirectory = Path.Combine(workDirectory, RUNS_FOLDER, run);
        var inDirectory = Path.Combine(runDirectory, IN_FOLDER);
        var outDirectory = Path.Combine(runDirectory, OUT_FOLDER);

        try
        {
            Directory.CreateDirectory(inDirectory);
            Directory.CreateDirectory(outDirectory);
            File.WriteAllBytes(Path.Combine(inDirectory, INPUT_FILE), pdf);

            WasmHostRunOutcome outcome;
            m_slots.Wait(cancellationToken);
            try
            {
                outcome = m_host.Run(m_module, Request(inDirectory, outDirectory, Path.Combine(runDirectory, CAPTURE_FOLDER)));
            }
            finally
            {
                m_slots.Release();
            }

            return Map(outcome, outDirectory);
        }
        finally
        {
            Discard(runDirectory);
        }
    }

    /// <summary>How many PDFs a process runs at once: their memory ceilings within the PDFs' share of its memory (a quarter: <see cref="Shards.ShardDegree"/> holds the node's one memory budget), one at least, never more than its cores.</summary>
    /// <param name="memoryBytes">The memory ceiling of one run.</param>
    /// <param name="availableBytes">The memory the process may use (0 or less = unknown, cores decide).</param>
    /// <param name="cores">Logical processors.</param>
    /// <returns>At least 1.</returns>
    public static int Slots(long memoryBytes, long availableBytes, int cores)
    {
        return Shards.ShardDegree.PdfSlots(memoryBytes, availableBytes, cores);
    }

    private WasmHostRunRequest Request(string inDirectory, string outDirectory, string captureDirectory)
    {
        return new WasmHostRunRequest
        {
            Argv = [GUEST_NAME, $"{GUEST_IN}/{INPUT_FILE}", GUEST_OUT],
            Preopens = [new WasmHostPreopen(inDirectory, GUEST_IN), new WasmHostPreopen(outDirectory, GUEST_OUT, Writable: true)],
            CaptureDirectory = captureDirectory,
            Fuel = Fuel,
            FuelAfterInstantiation = true,
            MemoryBytes = MemoryBytes,
            WallMs = WallMs,
            RandomSeed = PdfStageLimits.RANDOM_SEED,
            StdoutLimitBytes = STDOUT_LIMIT_BYTES,
            StderrLimitBytes = STDERR_LIMIT_BYTES,
            OutOfMemory = stderr => stderr.Contains(PdfStageLimits.OUT_OF_MEMORY_MARKER, StringComparison.Ordinal)
        };
    }

    private PdfExtraction Map(WasmHostRunOutcome outcome, string outDirectory)
    {
        var fuel = outcome.FuelConsumed;
        switch (outcome.Verdict)
        {
            case WasmHostVerdict.OutOfFuel:
                return PdfExtraction.Refused(PdfVerdict.Fuel, fuel);
            case WasmHostVerdict.OutOfMemory:
                return PdfExtraction.Refused(PdfVerdict.Memory, fuel);
            case WasmHostVerdict.Trap:
                return PdfExtraction.Refused(PdfVerdict.Trap, fuel);
            case WasmHostVerdict.EngineError:
                return PdfExtraction.Refused(PdfVerdict.Error, fuel);
            case WasmHostVerdict.Interrupted:
                throw new PdfSandboxException(CorpusResultCodes.FAULT_PDF_BACKSTOP, $"a PDF ran past the wall-clock backstop of {WallMs} ms with fuel left: the machine, not the document, stopped it");
            case WasmHostVerdict.NotInstantiated:
            case WasmHostVerdict.NoStart:
                throw new PdfSandboxException(CorpusResultCodes.FAULT_PDF_SANDBOX, $"the PDF extractor module did not run: {outcome.Fault}");
            case WasmHostVerdict.Exited:
                break;
            default:
                throw new PdfSandboxException(CorpusResultCodes.FAULT_PDF_SANDBOX, $"the sandbox ended a PDF run as {outcome.Verdict}");
        }

        // The module's whole output is bounded after the fact (WASI gives a writable folder no
        // quota): the text, its result and anything else it may have left there.
        if (FolderBytes(outDirectory) > (long)MaxTextBytes + PdfStageLimits.MAX_RESULT_BYTES)
            return PdfExtraction.Refused(PdfVerdict.Oversized, fuel);

        var result = ReadResult(Path.Combine(outDirectory, RESULT_FILE));
        if (result == null)
            return PdfExtraction.Refused(PdfVerdict.Error, fuel);

        var (pages, encrypted, error) = result.Value;
        if (outcome.ExitCode == EXIT_REFUSED)
        {
            var verdict = error switch
            {
                CODE_ENCRYPTED => PdfVerdict.Encrypted,
                CODE_UNREADABLE => PdfVerdict.Unreadable,
                CODE_NO_PAGES => PdfVerdict.NoPages,
                _ => PdfVerdict.Error
            };
            return PdfExtraction.Refused(verdict, fuel, pages, encrypted);
        }

        if (outcome.ExitCode != EXIT_TEXT || error != null)
            return PdfExtraction.Refused(PdfVerdict.Error, fuel, pages, encrypted);

        var textPath = Path.Combine(outDirectory, TEXT_FILE);
        if (!File.Exists(textPath))
            return PdfExtraction.Refused(PdfVerdict.Error, fuel, pages, encrypted);

        if (new FileInfo(textPath).Length > MaxTextBytes)
            return PdfExtraction.Refused(PdfVerdict.Oversized, fuel, pages, encrypted);

        return new PdfExtraction(PdfVerdict.Text, TEXT_ENCODING.GetString(File.ReadAllBytes(textPath)), pages, encrypted, fuel);
    }

    /// <summary>The module's <c>{"pages":N,"encrypted":B,"error":null|"code"}</c>, or null when it is missing, too large or not that shape.</summary>
    private static (int Pages, bool Encrypted, string? Error)? ReadResult(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > PdfStageLimits.MAX_RESULT_BYTES)
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Number || !pages.TryGetInt32(out var pageCount) || pageCount < 0
                || !root.TryGetProperty("encrypted", out var encrypted) || encrypted.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("error", out var error) || error.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                return null;

            return (pageCount, encrypted.GetBoolean(), error.ValueKind == JsonValueKind.Null ? null : error.GetString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long FolderBytes(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
    }

    #endregion

    #region IDisposable

    /// <summary>Releases the compiled module, then the host (its engine and epoch timer).</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_module.Dispose();
        m_host.Dispose();
        m_slots.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Takes a finished run's folder away. Never on the run's own path: the default hands it to
    /// <see cref="Shards.ShardCleanup"/> and returns at once.
    /// </summary>
    internal Action<string> Discard { get; set; } = Shards.ShardCleanup.Schedule;

    /// <summary>The fuel one PDF may burn from <c>_start</c> on.</summary>
    public long Fuel { get; }

    /// <summary>The cap of the module's linear memory, bytes.</summary>
    public long MemoryBytes { get; }

    /// <summary>The most text one PDF may give, bytes of UTF-8.</summary>
    public int MaxTextBytes { get; }

    /// <summary>The wall-clock backstop of one run, milliseconds: a run that reaches it with fuel left is a fault of this node, never the document's verdict.</summary>
    public int WallMs { get; }

    #endregion
}
