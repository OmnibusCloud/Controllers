using System.Globalization;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Local;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Planning;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Live.Commands;

/// <summary>
/// <c>local</c>: a problem set run on this machine alone by the controller's own code, the local
/// side of the comparative test. The set is read and cut into packages by the host's code (as
/// <c>Smt.Split</c> does); every problem is solved by the node's session code in the controller's
/// sandbox, at the node's degree rule unless a degree is given (<see cref="SmtLiveLocalPool"/>);
/// the run is settled by the host's code, every <c>sat</c> model checked by the host's evaluator
/// (as <c>Smt.Settle</c> does), and the four documents are written by the host's writers (as
/// <c>Smt.Collect*</c>, <c>Smt.Provenance</c> and <c>Smt.Integrity</c> hand them out). Nothing
/// is sent anywhere. One machine makes no copies: the sampling rate is 0, every answer rests on
/// one copy, and there is no repair or dispute round. <c>local.json</c> says what ran and how long
/// it took (<see cref="SmtLiveLocalFacts"/>).
/// </summary>
internal sealed class SmtLiveLocal
{
    #region Constants

    /// <summary>The most sessions at once a degree may give.</summary>
    public const int MAX_DEGREE = 256;

    /// <summary>The degree's reason when it was given on the command line.</summary>
    public const string GIVEN = "given";

    /// <summary>The folder of the run's working files inside its folder, removed at its end.</summary>
    public const string WORK_FOLDER = ".work";

    private const string FOLDER_PREFIX = "smt-local-";
    private const double BYTES_PER_MIB = 1024.0 * 1024.0;
    private const double BYTES_PER_GIB = BYTES_PER_MIB * 1024.0;

    #endregion

    #region Fields

    private readonly TextWriter m_output;
    private readonly SmtLiveClock m_clock;
    private readonly SmtLiveSandbox? m_sandbox;

    #endregion

    #region Constructors

    /// <summary>
    /// Makes the command.
    /// </summary>
    /// <param name="output">Where the lines go.</param>
    /// <param name="clock">The clock the times are read from: the tool's start.</param>
    /// <param name="sandbox">A sandbox to run in (a test's, made once for several runs); null to make one for the run and release it at its end.</param>
    public SmtLiveLocal(TextWriter output, SmtLiveClock clock, SmtLiveSandbox? sandbox = null)
    {
        m_output = output;
        m_clock = clock;
        m_sandbox = sandbox;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs a problem set on this machine.
    /// </summary>
    /// <param name="path">The problem set.</param>
    /// <param name="given">The run's options as given; the sampling rate is set to 0.</param>
    /// <param name="degree">The sessions at once; null for the node's rule.</param>
    /// <param name="folder">Where the documents go; null for <c>smt-local-</c> and the start's time, in the current folder.</param>
    /// <param name="ct">Stops the run: no problem is handed out after it.</param>
    /// <returns>The exit code: 0 when every document holds and nothing is alarming, 3 when something is, 2 when the options are out of range, 1 otherwise.</returns>
    public async Task<int> RunAsync(string path, SmtOptionsData given, int? degree, string? folder, CancellationToken ct)
    {
        var options = given.Clone();
        options.SamplingRate = 0;

        m_output.WriteLine($"local    : this machine alone, by the code of the controller Smt {ControllerBuildInfo.VERSION} (profile {SmtProfile.ID})");
        m_output.WriteLine($"options  : {SmtLiveInput.Describe(options)} (one machine makes no copies)");

        var refused = SmtOptionsRules.Check(options);
        if (refused.Count > 0 || degree is < 1 or > MAX_DEGREE)
        {
            m_output.WriteLine("the options are out of range; nothing was run:");
            foreach (var problem in refused)
                m_output.WriteLine($"  {problem}");

            if (degree is < 1 or > MAX_DEGREE)
                m_output.WriteLine($"  the degree must be from 1 to {MAX_DEGREE}, not {degree}");

            return SmtLiveExitCodes.USAGE;
        }

        var target = Path.GetFullPath(folder ?? FOLDER_PREFIX + m_clock.StartedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var work = Path.Combine(target, WORK_FOLDER);
        Directory.CreateDirectory(target);
        TryDelete(work);
        Directory.CreateDirectory(work);

        var sandbox = m_sandbox;
        try
        {
            sandbox ??= SmtLiveSandbox.Open(Path.Combine(work, "sandbox"), out var unavailable) ?? throw new SmtLiveLocalException($"no sandbox: {unavailable}");
            var startup = m_clock.Elapsed;
            PrintSandbox(sandbox, startup);

            // The set's hash grows with the set: it counts in the reading, not in the start-up.
            var (rows, bytes, sha256) = await SmtLiveInput.DescribeAsync(path, ct);
            m_output.WriteLine($"input    : {Path.GetFileName(path)}, {rows} row(s), {bytes} byte(s), sha256 {sha256}");

            return await RunAsync(path, options, degree, target, work, sandbox, startup, (rows, bytes, sha256), ct);
        }
        catch (SmtLiveLocalException exception)
        {
            m_output.WriteLine($"{SmtLiveRunSummary.LOUD}result   : FAILED: {exception.Message}");
            return SmtLiveExitCodes.FAILED;
        }
        finally
        {
            if (m_sandbox == null)
                sandbox?.Dispose();

            TryDelete(work);
        }
    }

    private async Task<int> RunAsync(string path, SmtOptionsData options, int? given, string target, string work, SmtLiveSandbox sandbox, TimeSpan startup,
        (int Rows, long Bytes, string Sha256) set, CancellationToken ct)
    {
        // The set, read and cut as Smt.Split reads and cuts it.
        var blobs = new SmtLiveBlobStore(Path.Combine(work, "blobs"));
        SmtProblemSetScan scan;
        List<SmtTaskPackageData> packages;
        using (var spool = new SmtProblemSpool(Path.Combine(work, "spool")))
        {
            scan = SmtProblemSetScan.Read(path, options, spool);
            if (scan.Summary.Rows == 0)
                throw new SmtLiveLocalException("the problem set has no row");

            var publisher = new SmtPackagePublisher(spool.Count, SmtOptionsRules.Resolve(options), blobs, Path.Combine(work, "packages"));
            foreach (var problem in spool.ReadAll())
                await publisher.AddAsync(problem);

            packages = await publisher.CompleteAsync();
        }

        var read = m_clock.Elapsed;
        m_output.WriteLine($"set      : {scan.Summary.Rows} row(s), {scan.Summary.Accepted} taken, {scan.Summary.Refused} refused; " +
                           $"{packages.Count} package(s) of at most {SmtPackagePlanner.ProblemsPerPackage(scan.Summary.Accepted)} problem(s), cut as the server cuts them");

        // The degree, as a node decides it for a package (SmtPackageRunner.Prepare), from one fresh reading of the machine.
        var machine = SmtLiveMachine.Current();
        var session = SmtSandboxDegree.SessionBytes(options);
        var rule = SmtSandboxDegree.Compute(machine.Cores, machine.AvailableBytes, session);
        var degree = given is { } sessions ? new SmtDegree(sessions, GIVEN, machine.Cores, machine.AvailableBytes, rule.UsableBytes, session) : rule;
        if (packages.Count > 0)
        {
            try
            {
                var decided = degree;
                degree = SmtPackageRunner.Prepare(packages[0], null, _ => decided);
            }
            catch (SmtPackageException refused)
            {
                throw new SmtLiveLocalException($"{refused.Code}: {refused.Message}");
            }
        }

        PrintMachine(machine, degree, rule);

        // Every problem by the node's session code, one pool over the whole set.
        var solved = 0;
        var progress = new SmtLiveProgress(m_output, "solve");
        var gate = new object();
        var taken = scan.Summary.Accepted;
        var pool = new SmtLiveLocalPool(blobs, Path.Combine(work, "results"));
        var results = await pool.SolveAsync(packages, sandbox.Sandbox, degree.Degree, () =>
        {
            var done = Interlocked.Increment(ref solved);
            lock (gate)
                progress.Report(taken == 0 ? 1.0 : (double)done / taken);
        }, ct);

        var solve = m_clock.Elapsed;
        m_output.WriteLine($"solved   : {solved} problem(s) in {Clock(solve - read)} at {degree.Degree} session(s) at once");

        // Settled and written as Smt.Settle and the documents' activities settle and write a run; no repair or dispute round.
        var (ledger, _) = await SmtRunSettlement.SettleAsync(path, options, (packages, results), null, null, blobs, Path.Combine(work, "settle"), 0.0);
        try
        {
            foreach (var (document, section) in Sections())
                SmtLedger.CopyTo(ledger, section, Path.Combine(target, document.File));
        }
        finally
        {
            SmtSettlement.TryDelete(ledger);
        }

        var wall = m_clock.Elapsed;
        m_output.WriteLine($"documents: {target}");

        var held = Check(target);
        IReadOnlyList<string> alarms;
        try
        {
            alarms = new SmtLiveRunSummary(m_output).Print(Path.Combine(target, SmtLiveDocuments.REPORT.File),
                Path.Combine(target, SmtLiveDocuments.INTEGRITY.File), Path.Combine(target, SmtLiveDocuments.RESULTS.File));
        }
        catch (JsonException exception)
        {
            throw new SmtLiveLocalException($"a document cannot be read: {exception.Message}");
        }

        var facts = new SmtLiveLocalFacts
        {
            StartedUtc = m_clock.StartedUtc,
            Machine = machine,
            Degree = degree.Degree,
            DecidedBy = degree.Reason,
            RuleDegree = rule.Degree,
            RuleDecidedBy = rule.Reason,
            SessionBytes = session,
            Profile = SmtProfile.ID,
            Controller = ControllerBuildInfo.VERSION,
            Module = ModuleName(sandbox.ModulePath),
            ModuleSha256 = sandbox.ModuleSha256,
            ModulePinned = sandbox.ModulePinned,
            Engine = $"wasmtime {WasmHostNatives.VERSION}",
            NativesSha256 = sandbox.NativesSha256,
            NativesPinned = sandbox.NativesPinned,
            ExplicitTraps = sandbox.Sandbox.ExplicitTraps,
            SetFile = Path.GetFileName(path),
            SetSha256 = set.Sha256,
            SetBytes = set.Bytes,
            Rows = scan.Summary.Rows,
            Taken = taken,
            Refused = scan.Summary.Refused,
            Packages = packages.Count,
            Startup = startup,
            Read = read - startup,
            Solve = solve - read,
            Settle = wall - solve,
            Wall = wall
        };
        facts.Write(Path.Combine(target, SmtLiveLocalFacts.FILE));

        m_output.WriteLine($"wall     : {Clock(wall)} from the tool's start to the documents on disk: start-up {Clock(startup)}, read {Clock(read - startup)}, " +
                           $"solve {Clock(solve - read)}, settle {Clock(wall - solve)}");
        m_output.WriteLine($"rate     : {(wall > TimeSpan.Zero ? taken / wall.TotalSeconds : 0).ToString("0.###", CultureInfo.InvariantCulture)} problems a second ({taken} taken problem(s) over the wall time)");
        m_output.WriteLine($"local    : {SmtLiveLocalFacts.FILE} written");

        if (!held)
        {
            m_output.WriteLine("result   : FAILED: a document does not hold to its schema");
            return SmtLiveExitCodes.FAILED;
        }

        if (alarms.Count > 0)
        {
            m_output.WriteLine($"{SmtLiveRunSummary.LOUD}result   : ALARM: {string.Join("; ", alarms)}");
            return SmtLiveExitCodes.ALARM;
        }

        m_output.WriteLine("result   : OK: the four documents hold to their schemas, nothing is alarming");
        return SmtLiveExitCodes.OK;
    }

    /// <summary>Holds each of the four documents to its schema; false when one does not hold.</summary>
    private bool Check(string folder)
    {
        var held = true;
        foreach (var document in SmtLiveDocuments.RUN)
        {
            var schema = SmtLiveDocuments.SchemaOf(document);
            var verdict = SmtLiveDocumentCheck.CheckFile(Path.Combine(folder, document.File), schema, document.Lines);
            var size = document.Lines ? $"{verdict.Items} row(s), {verdict.Bytes} bytes" : $"{verdict.Bytes} bytes";
            m_output.WriteLine(verdict.IsValid
                ? $"  {document.File,-16} {size}: valid ({schema.Name})"
                : $"{SmtLiveRunSummary.LOUD}{document.File,-16} {size}: DOES NOT HOLD TO {schema.Name} ({verdict.Faulty} faulty)");
            foreach (var error in verdict.Errors)
                m_output.WriteLine($"{SmtLiveRunSummary.LOUD}  {error}");

            held &= verdict.IsValid;
        }

        return held;
    }

    private void PrintSandbox(SmtLiveSandbox sandbox, TimeSpan startup)
    {
        m_output.WriteLine($"module   : {ModuleName(sandbox.ModulePath)}, sha256 {sandbox.ModuleSha256} ({(sandbox.ModulePinned ? "the profile's pin" : "NOT THE PIN")})");
        m_output.WriteLine($"engine   : wasmtime {WasmHostNatives.VERSION} {WasmHostNativeLibrary.PortableRid}, sha256 {sandbox.NativesSha256 ?? "unknown"} " +
                           $"({(sandbox.NativesPinned ? "the pin of this platform" : "NOT THE PIN")}); the module compiled in {sandbox.Compile.TotalSeconds:0.0} s, ready {Clock(startup)} after the tool's start");
    }

    private void PrintMachine(SmtLiveMachine machine, SmtDegree degree, SmtDegree rule)
    {
        m_output.WriteLine($"machine  : {machine.Platform}, {machine.Os} {machine.OsVersion}, {machine.Cores} logical core(s), " +
                           $"{machine.MemoryBytes / BYTES_PER_GIB:0.0} GiB, {machine.AvailableBytes / BYTES_PER_GIB:0.0} GiB available");

        var ruleText = $"the node's rule gives {rule.Degree} (decided by {rule.Reason}: min(cores {rule.Cores}, cap {SmtSandboxDegree.MAX_DEGREE}, " +
                       $"{SmtSandboxDegree.RAM_HEADROOM:P0} of the available memory / {rule.SessionBytes / BYTES_PER_MIB:0} MiB a session))";
        m_output.WriteLine(degree.Reason == GIVEN
            ? $"degree   : {degree.Degree} session(s) at once, given; {ruleText}"
            : $"degree   : {degree.Degree} session(s) at once: {ruleText}");
    }

    private static IEnumerable<(SmtLiveDocument Document, int Section)> Sections()
    {
        yield return (SmtLiveDocuments.RESULTS, SmtLedger.RESULTS);
        yield return (SmtLiveDocuments.REPORT, SmtLedger.REPORT);
        yield return (SmtLiveDocuments.INTEGRITY, SmtLedger.INTEGRITY);
        yield return (SmtLiveDocuments.PROVENANCE, SmtLedger.PROVENANCE);
    }

    /// <summary>The module as the description names it: its place beside the tool, or the variable that named it; never a folder of the machine.</summary>
    private static string ModuleName(string path)
    {
        var relative = Path.GetRelativePath(AppContext.BaseDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? $"the file {SmtModuleLocator.OVERRIDE_VARIABLE} names ({Path.GetFileName(path)})"
            : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string Clock(TimeSpan time)
    {
        return time.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture);
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a working folder of the run's own; the next run in the folder removes it
        }
    }

    #endregion
}
