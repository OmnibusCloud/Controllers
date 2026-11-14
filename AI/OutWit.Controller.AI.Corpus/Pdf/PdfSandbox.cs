using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// Runs the extractor module on one PDF at a time per caller, in Corpus's own copy of the wasm
/// sandbox host: <c>pdftext /in/doc.pdf /out</c> with only those two folders visible, no network,
/// the host's pinned clock and seeded randomness, the fuel given after instantiation, a 1 GiB
/// linear memory, and a wall-clock backstop. Every way a PDF can end is a <see cref="PdfVerdict"/>;
/// a host-side fault (a run that reaches the backstop, a module that cannot be instantiated)
/// throws <see cref="PdfSandboxException"/>, so the shard fails and runs elsewhere.
/// <para>
/// The module is compiled once, on construction; one sandbox serves every shard of a process,
/// concurrently. At most a few PDFs run at once in the process, so that their memory ceilings
/// together stay within half the memory the process may use.
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
    private const string IN_FOLDER = "pdf-in";
    private const string OUT_FOLDER = "pdf-out";
    private const string CAPTURE_FOLDER = "pdf-io";

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
    /// <exception cref="InvalidDataException">The file's bytes are not the pinned module.</exception>
    /// <exception cref="InvalidOperationException">The loaded wasmtime binding is not the one this controller was built against, or the engine cannot be made.</exception>
    public PdfSandbox(string modulePath, long fuel, long memoryBytes, int maxTextBytes = PdfStageLimits.MAX_TEXT_BYTES)
    {
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
    /// Extracts the text of one PDF. Its folders are made in the work directory (the task's temp
    /// scope on a node) and emptied after the run.
    /// </summary>
    /// <param name="pdf">The PDF's bytes.</param>
    /// <param name="workDirectory">A folder of the caller's own; nothing is written outside it.</param>
    /// <param name="cancellationToken">Cancels the wait for a slot (a run that started ends by its budget).</param>
    /// <returns>The verdict, the text and what the run cost.</returns>
    /// <exception cref="PdfSandboxException">The run reached the wall-clock backstop, or the module could not be instantiated: a fault of the host, not of the PDF.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled while the PDF waited for a slot.</exception>
    public PdfExtraction Extract(ReadOnlySpan<byte> pdf, string workDirectory, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        if (pdf.Length > PdfStageLimits.MAX_INPUT_BYTES)
            return PdfExtraction.Refused(PdfVerdict.Oversized, 0);

        var inDirectory = Path.Combine(workDirectory, IN_FOLDER);
        var outDirectory = Path.Combine(workDirectory, OUT_FOLDER);
        var captureDirectory = Path.Combine(workDirectory, CAPTURE_FOLDER);
        var inputPath = Path.Combine(inDirectory, INPUT_FILE);

        Directory.CreateDirectory(inDirectory);
        Empty(outDirectory);
        File.WriteAllBytes(inputPath, pdf.ToArray());

        try
        {
            WasmHostRunOutcome outcome;
            m_slots.Wait(cancellationToken);
            try
            {
                outcome = m_host.Run(m_module, Request(inDirectory, outDirectory, captureDirectory));
            }
            finally
            {
                m_slots.Release();
            }

            return Map(outcome, outDirectory);
        }
        finally
        {
            TryDelete(inputPath);
            TryEmpty(outDirectory);
        }
    }

    /// <summary>How many PDFs a process runs at once: their memory ceilings within half its memory, one at least, never more than its cores.</summary>
    /// <param name="memoryBytes">The memory ceiling of one run.</param>
    /// <param name="availableBytes">The memory the process may use (0 or less = unknown, cores decide).</param>
    /// <param name="cores">Logical processors.</param>
    /// <returns>At least 1.</returns>
    public static int Slots(long memoryBytes, long availableBytes, int cores)
    {
        var byCores = Math.Max(1, cores);
        if (availableBytes <= 0)
            return byCores;

        var byMemory = (int)Math.Min(int.MaxValue, availableBytes / 2 / Math.Max(1, memoryBytes));
        return Math.Clamp(byMemory, 1, byCores);
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
            WallMs = PdfStageLimits.WALL_MS,
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
                throw new PdfSandboxException($"a PDF ran past the wall-clock backstop of {PdfStageLimits.WALL_MS} ms with fuel left: the machine, not the document, stopped it");
            case WasmHostVerdict.NotInstantiated:
            case WasmHostVerdict.NoStart:
                throw new PdfSandboxException($"the PDF extractor module did not run: {outcome.Fault}");
            case WasmHostVerdict.Exited:
                break;
            default:
                throw new PdfSandboxException($"the sandbox ended a PDF run as {outcome.Verdict}");
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

    private static void Empty(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        Directory.CreateDirectory(directory);
    }

    private static void TryEmpty(string directory)
    {
        try
        {
            Empty(directory);
        }
        catch (IOException)
        {
            // emptied again before the next run
        }
        catch (UnauthorizedAccessException)
        {
            // emptied again before the next run
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // overwritten by the next run; the scope goes with the task
        }
        catch (UnauthorizedAccessException)
        {
            // overwritten by the next run; the scope goes with the task
        }
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

    /// <summary>The fuel one PDF may burn from <c>_start</c> on.</summary>
    public long Fuel { get; }

    /// <summary>The cap of the module's linear memory, bytes.</summary>
    public long MemoryBytes { get; }

    /// <summary>The most text one PDF may give, bytes of UTF-8.</summary>
    public int MaxTextBytes { get; }

    #endregion
}
