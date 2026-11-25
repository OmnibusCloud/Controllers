using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The host's intake of a fan-out with its spool in sight, beside <see cref="SmtSettleRig"/>: what
/// was taken and refused, how many bytes of rows the spool then held, and the intake and the
/// settlement under bounds smaller than a host's, so that a test reaches them. As in the other
/// rigs, the controller's types are met inside method bodies only, and the rig waits for the
/// controller's tasks itself.
/// </summary>
internal sealed class SmtIntakeRig : IDisposable
{
    #region Fields

    private readonly string m_folder;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>A rig with a working folder of its own.</summary>
    public SmtIntakeRig()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_intake_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_folder);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Takes a first fan-out in with a spool, as Smt.Disputes and Smt.Settle take it.
    /// </summary>
    /// <param name="issued">The packages it gave out.</param>
    /// <param name="returned">What came back.</param>
    /// <param name="blobs">The blob service.</param>
    /// <param name="rowsBytes">The most bytes of rows the spool holds; null for a host's bound.</param>
    /// <param name="spoolClosed">Whether the spool's file is closed under the intake: a file of the host's own that cannot be written.</param>
    /// <returns>What was taken, and what the spool held then (-1 for a closed one).</returns>
    public SmtIntake Main(IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned, IWitBlobService blobs, long? rowsBytes = null, bool spoolClosed = false)
    {
        using var spool = new SmtSpool(Path.Combine(m_folder, "spool"), rowsBytes ?? SmtSettlementBounds.MAX_ROWS_BYTES);
        if (spoolClosed)
            spool.Dispose();

        var round = SmtSettlement.ReadMainAsync(issued, returned, blobs, spool).GetAwaiter().GetResult();

        return new SmtIntake(round.Accepted.Count, new Dictionary<string, int>(round.Rejected), new Dictionary<string, int>(round.Faults), spoolClosed ? -1 : spool.Length);
    }

    /// <summary>
    /// Settles a run of one fan-out as Smt.Settle does, under given bounds, and gives its results.
    /// </summary>
    /// <param name="setPath">The set's local file.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="issued">The packages of the fan-out.</param>
    /// <param name="returned">What came back.</param>
    /// <param name="blobs">The blob service.</param>
    /// <param name="rowsBytes">The most bytes of rows the spool holds; null for a host's bound.</param>
    /// <param name="modelsBytes">The most bytes of full models the ledger holds; null for a host's bound.</param>
    /// <returns>The results' text.</returns>
    public string Settle(string setPath, SmtOptionsData options, IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned, IWitBlobService blobs,
        long? rowsBytes = null, long? modelsBytes = null)
    {
        var folder = Path.Combine(m_folder, $"settle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        var bounds = new SmtSettlementBounds(rowsBytes ?? SmtSettlementBounds.MAX_ROWS_BYTES, modelsBytes ?? SmtSettlementBounds.MAX_MODELS_BYTES);
        var ledger = SmtRunSettlement.SettleAsync(setPath, options, (issued, returned), null, null, blobs, folder, 0.0, bounds).GetAwaiter().GetResult().LedgerPath;

        return Encoding.UTF8.GetString(SmtLedger.Read(ledger, SmtLedger.RESULTS));
    }

    /// <summary>The working files a settlement left behind in the rig's folder: none when it cleaned up after itself.</summary>
    /// <returns>The files' names.</returns>
    public IReadOnlyList<string> Leftovers()
    {
        return Directory.EnumerateFiles(m_folder, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !name.EndsWith(".wsl", StringComparison.Ordinal))
            .ToList();
    }

    #endregion

    #region IDisposable

    /// <summary>Removes the rig's folder.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Properties

    /// <summary>The name of the bound of the rows a run's settlement keeps, as its failure says it.</summary>
    public static string RowsBound => SmtSettlementBoundException.ROWS;

    /// <summary>The name of the bound of the full models a run's ledger holds, as its failure says it.</summary>
    public static string ModelsBound => SmtSettlementBoundException.MODELS;

    #endregion
}
