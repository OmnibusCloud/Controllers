using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Live.Local;

/// <summary>
/// Solves every problem of a local run's packages with one pool of sessions, as one machine with
/// a queue of files would: the problems are handed out in the set's order across the packages, so
/// no session waits for the slowest problem of a package before the next package starts (a node
/// solves one package at a time, and its pool waits for each package's slowest problem). Each
/// package is first checked as a node checks it (<see cref="SmtPackageRunner.EnsureAcceptable"/>,
/// <see cref="SmtPackageRunner.EnsureBlob"/>); every problem is solved by the node's session code
/// (<see cref="SmtSolver.Solve(SmtProblemData, SmtOptionsData, SmtSandbox?, string?, CancellationToken)"/>)
/// under its package's options on the node's worker threads (<see cref="SmtWorkerPool"/>, below
/// normal priority); its row is made by the node's code with its package's ticket and its digest
/// (<see cref="SmtResultRows.Of"/>) and written to its package's result package as its session
/// ends; each result package is stored as a node stores it (<see cref="SmtPackageTransfer.UploadResultsAsync"/>).
/// </summary>
internal sealed class SmtLiveLocalPool
{
    #region Constants

    private const string RESULTS_FILE = "results-{0:D6}.wsp";

    #endregion

    #region Fields

    private readonly IWitBlobService m_blobs;
    private readonly string m_folder;

    #endregion

    #region Constructors

    /// <summary>Makes the pool.</summary>
    /// <param name="blobs">The run's blob store: the packages are in it, the result packages go to it.</param>
    /// <param name="folder">A folder of the run's own for the result package files while they are written.</param>
    public SmtLiveLocalPool(IWitBlobService blobs, string folder)
    {
        m_blobs = blobs;
        m_folder = folder;
        Directory.CreateDirectory(m_folder);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Solves the packages' problems.
    /// </summary>
    /// <param name="packages">The packages, in the set's order.</param>
    /// <param name="sandbox">The sandbox the sessions run in.</param>
    /// <param name="degree">The sessions at once.</param>
    /// <param name="problemDone">Called after each problem's row is written, on the thread that solved it.</param>
    /// <param name="ct">No problem is handed out after it is cancelled; the running sessions end at their next read.</param>
    /// <returns>The result packages, one a package, in the packages' order.</returns>
    /// <exception cref="SmtPackageException">A package is not what a node takes: a fault of the run's own cut.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    public async Task<IReadOnlyList<SmtResultPackageData>> SolveAsync(IReadOnlyList<SmtTaskPackageData> packages, SmtSandbox sandbox, int degree,
        Action problemDone, CancellationToken ct)
    {
        var slots = new List<SmtLiveLocalSlot>(packages.Count);
        for (var index = 0; index < packages.Count; index++)
        {
            var package = packages[index];
            SmtPackageRunner.EnsureAcceptable(package);

            var path = await m_blobs.GetLocalPathAsync(package.ProblemsBlobId);
            SmtPackageRunner.EnsureBlob(package, path);
            slots.Add(new SmtLiveLocalSlot(package, path, Path.Combine(m_folder, string.Format(System.Globalization.CultureInfo.InvariantCulture, RESULTS_FILE, index))));
        }

        try
        {
            await Task.Run(() => Solve(slots, sandbox, degree, problemDone, ct), ct);

            var results = new List<SmtResultPackageData>(slots.Count);
            foreach (var slot in slots)
            {
                results.Add(await SmtPackageTransfer.UploadResultsAsync(m_blobs, slot.ResultsPath, slot.Package.Ticket, slot.Rows, NullLogger.Instance, ct));
                SmtPackageRunner.TryDelete(slot.ResultsPath);
            }

            return results;
        }
        finally
        {
            foreach (var slot in slots)
            {
                slot.Dispose();
                SmtPackageRunner.TryDelete(slot.ResultsPath);
            }
        }
    }

    private static void Solve(IReadOnlyList<SmtLiveLocalSlot> slots, SmtSandbox sandbox, int degree, Action problemDone, CancellationToken ct)
    {
        var total = slots.Sum(slot => slot.Package.ProblemCount);
        var gate = new object();
        var current = 0;
        SmtTaskPackageReader? reader = null;

        try
        {
            SmtWorkerPool.Run(total, degree, _ =>
            {
                SmtProblemData? problem;
                SmtLiveLocalSlot slot;
                lock (gate)
                {
                    while (true)
                    {
                        if (current >= slots.Count)
                            throw new SmtPackageException(SmtNodeFaults.PACKAGE_MISMATCH, "the packages ended before the problems their references name");

                        slot = slots[current];
                        if (reader == null)
                        {
                            reader = new SmtTaskPackageReader(slot.ProblemsPath, slot.Package.ProblemCount);
                            slot.Open();
                        }

                        if (reader.TryRead(out problem))
                            break;

                        reader.Dispose();
                        reader = null;
                        current++;
                    }
                }

                var outcome = SmtSolver.Solve(problem!, slot.Package.Options!, sandbox, null, ct);
                slot.Write(SmtResultRows.Of(problem!, outcome, slot.Package.Ticket));
                problemDone();
            }, ct);
        }
        finally
        {
            reader?.Dispose();
        }
    }

    #endregion
}
