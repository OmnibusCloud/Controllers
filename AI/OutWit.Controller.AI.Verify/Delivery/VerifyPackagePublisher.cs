using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// Stores the batches of a run as package blobs while they are given one at a time: a package
/// is written out as soon as the next batch does not fit it (<see cref="VerifyPackageMeter"/>),
/// so the host holds the batches of one package, never of a run. Every reference carries a
/// ticket of its own (<see cref="VerifyTickets"/>), the version of the controller that made it,
/// and the digest of the tasks it asks for.
/// </summary>
public sealed class VerifyPackagePublisher
{
    #region Fields

    private readonly IWitBlobService m_blobs;
    private readonly List<VerifyTaskPackageData> m_packages = [];
    private List<VerifyTaskBatchData> m_current = [];
    private VerifyPackageMeter m_meter;

    #endregion

    #region Constructors

    /// <summary>A publisher for the packages of a run.</summary>
    /// <param name="totalTasks">The tasks of the whole run: what decides how many a package aims for.</param>
    /// <param name="blobs">The host's blob service.</param>
    public VerifyPackagePublisher(long totalTasks, IWitBlobService blobs)
    {
        m_blobs = blobs;
        m_meter = new VerifyPackageMeter(totalTasks);
    }

    #endregion

    #region Functions

    /// <summary>Adds a batch; the package before it is stored when the batch does not fit it.</summary>
    /// <param name="batch">The batch, within a package's caps.</param>
    /// <returns>A task that ends when the batch is taken.</returns>
    public async Task AddAsync(VerifyTaskBatchData batch)
    {
        var bytes = VerifyPackagePlanner.EstimateBytes(batch);
        var output = VerifyPackagePlanner.OutputBytes(batch);

        if (!m_meter.Fits(batch.Tasks.Count, bytes, output))
            await StoreAsync();

        m_current.Add(batch);
        m_meter.Add(batch.Tasks.Count, bytes, output);
        Batches++;
    }

    /// <summary>Stores the last package.</summary>
    /// <returns>The references, one per package, in order.</returns>
    public async Task<List<VerifyTaskPackageData>> CompleteAsync()
    {
        if (m_current.Count > 0)
            await StoreAsync();

        return m_packages;
    }

    private async Task StoreAsync()
    {
        var group = m_current;
        m_current = [];
        m_meter.Reset();

        var content = VerifyPackageCodec.WriteTasks(group);
        var blobId = await m_blobs.UploadBytesAsync(content, VerifyPackageCodec.TASKS_FILE_NAME);

        var package = new VerifyTaskPackageData
        {
            TasksBlobId = blobId,
            Sha256 = VerifyPackageCodec.Hash(content),
            SizeBytes = content.Length,
            TaskCount = group.Sum(batch => batch.Tasks.Count),
            BatchCount = group.Count,
            Ticket = VerifyTickets.New(),
            RequiredControllerVersion = ControllerBuildInfo.VERSION,
            IndexDigest = VerifyTaskPackages.IndexDigest(group)
        };

        m_packages.Add(package);
        Bytes += package.SizeBytes;
        Tasks += package.TaskCount;
    }

    #endregion

    #region Properties

    /// <summary>The tasks stored so far.</summary>
    public long Tasks { get; private set; }

    /// <summary>The batches taken so far.</summary>
    public int Batches { get; private set; }

    /// <summary>The bytes of the package blobs stored so far.</summary>
    public long Bytes { get; private set; }

    /// <summary>The packages stored so far.</summary>
    public int Packages => m_packages.Count;

    #endregion
}
