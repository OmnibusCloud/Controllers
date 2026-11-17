using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Settlement;
using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests.Settlement;

/// <summary>
/// The server's side of a run without an engine: task packages are published to a blob store,
/// "nodes" answer them with result packages a test makes (honest ones, or ones that lie in a
/// stated way), and the run is settled as the bundled scripts settle it. What a test reads back
/// is the four documents a lab reads.
/// </summary>
internal sealed class VerifySettleRig : IDisposable
{
    #region Constants

    /// <summary>The platform an honest node of the rig names.</summary>
    public const string PLATFORM = "linux-x64";

    #endregion

    #region Fields

    private readonly string m_folder;

    #endregion

    #region Constructors

    public VerifySettleRig()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_verify_settle_{Guid.NewGuid():N}");
        Blobs = new VerifyTestBlobService(Path.Combine(m_folder, "blobs"));
    }

    #endregion

    #region Functions

    /// <summary>Publishes batches as task packages, as Verify.Split does.</summary>
    public Task<List<VerifyTaskPackageData>> PublishAsync(params VerifyTaskBatchData[] batches)
    {
        return VerifyTaskPackages.PublishAsync(batches, Blobs, NullLogger.Instance, "test");
    }

    /// <summary>A batch of tasks of the raw WASI runtime, which needs no runtime files: one per index.</summary>
    public static VerifyTaskBatchData Batch(params int[] indices)
    {
        return new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            RuntimeSha256 = "",
            Tasks = indices.Select(index => new VerifyTaskData
            {
                TaskIndex = index,
                RuntimeId = VerifyRuntimeCatalog.WASI,
                EntryPoint = "m.wasm",
                Sources = [new VerifySourceFileData { Name = "m.wasm", Content = "AA==" }]
            }).ToList()
        };
    }

    /// <summary>
    /// A node's answer to a package: one result a task, made by <paramref name="answer"/>, in a
    /// result package as an honest node writes it. <paramref name="tamper"/> changes the batches
    /// before they are written (a lie inside a well-formed package).
    /// </summary>
    public async Task<VerifyResultPackageData> AnswerAsync(VerifyTaskPackageData package, Func<VerifyTaskData, VerifyResultData> answer,
        Action<List<VerifyResultBatchData>>? tamper = null)
    {
        var batches = (await VerifyTaskPackages.LoadAsync(package, Blobs)).Select(batch => new VerifyResultBatchData
        {
            Ticket = package.Ticket,
            Diagnostics = Diagnostics(batch.RuntimeId, batch.Tasks.Count),
            Results = batch.Tasks.Select(answer).ToList()
        }).ToList();

        tamper?.Invoke(batches);
        return await StoreAsync(batches, package.Ticket);
    }

    /// <summary>Writes batches as a result package and stores it, as a node does.</summary>
    public async Task<VerifyResultPackageData> StoreAsync(IReadOnlyList<VerifyResultBatchData> batches, string ticket)
    {
        Directory.CreateDirectory(m_folder);
        var path = Path.Combine(m_folder, $"{Guid.NewGuid():N}.results");
        using (var writer = new VerifyResultPackageWriter(path))
        {
            foreach (var batch in batches)
                writer.Write(batch);

            writer.Complete();
        }

        var reference = await VerifyPackageTransfer.UploadResultsAsync(Blobs, path, ticket, batches.Sum(batch => batch.Results.Count), batches.Count, NullLogger.Instance, CancellationToken.None);
        File.Delete(path);
        return reference;
    }

    /// <summary>Stores arbitrary bytes as a blob and returns a reference that names them truthfully: a package only in name.</summary>
    public async Task<VerifyResultPackageData> StoreRawAsync(byte[] bytes, string ticket)
    {
        return new VerifyResultPackageData
        {
            ResultsBlobId = await Blobs.UploadBytesAsync(bytes, VerifyPackageCodec.RESULTS_FILE_NAME),
            Sha256 = VerifyPackageCodec.Hash(bytes),
            SizeBytes = bytes.Length,
            TaskCount = 1,
            BatchCount = 1,
            Ticket = ticket
        };
    }

    /// <summary>What an honest node of this controller version says of a batch.</summary>
    public static VerifyBatchDiagnosticsData Diagnostics(string runtimeId, int tasks)
    {
        var runtime = VerifyRuntimeCatalog.Find(runtimeId)!;
        return new VerifyBatchDiagnosticsData
        {
            RuntimeTarget = PLATFORM,
            WasmtimeVersion = VerifyEngineVersion.Pinned,
            ControllerVersion = ControllerBuildInfo.VERSION,
            RuntimeId = runtime.Id,
            RuntimeSha256 = runtime.Sha256,
            Degree = 4,
            TaskCount = tasks,
            WallMs = 7
        };
    }

    /// <summary>Settles a run from its first fan-out alone (the other rounds gave out and returned nothing).</summary>
    public Task<VerifySettledRun> SettleAsync(IReadOnlyList<VerifyTaskPackageData> issued, IEnumerable<VerifyResultPackageData> returned)
    {
        return SettleAsync((issued, returned), (Array.Empty<VerifyTaskPackageData>(), Array.Empty<VerifyResultPackageData>()),
            (Array.Empty<VerifyTaskPackageData>(), Array.Empty<VerifyResultPackageData>()));
    }

    /// <summary>Settles a run from its fan-outs, as Verify.Settle does, and reads the documents back.</summary>
    public async Task<VerifySettledRun> SettleAsync(
        (IReadOnlyList<VerifyTaskPackageData> Issued, IEnumerable<VerifyResultPackageData> Returned) fanOut,
        (IReadOnlyList<VerifyTaskPackageData> Issued, IEnumerable<VerifyResultPackageData> Returned)? repair,
        (IReadOnlyList<VerifyTaskPackageData> Issued, IEnumerable<VerifyResultPackageData> Returned)? dispute)
    {
        // Awaited as a plain task, here and below: an awaiter of a controller type would be a
        // value type among this method's fields, and the test assembly's types are all loaded
        // when the tests are discovered. The module-load proof needs the controller unloaded then.
        var settling = VerifySettlement.SettleAsync(fanOut, repair, dispute, Blobs, Path.Combine(m_folder, "work"));
        await (Task)settling;

        var ledgerPath = settling.Result.LedgerPath;
        var summary = settling.Result.Summary;
        try
        {
            var outputs = Path.Combine(m_folder, $"{Guid.NewGuid():N}.jsonl");
            VerifyLedger.CopyTo(ledgerPath, VerifyLedger.OUTPUTS, outputs);

            return new VerifySettledRun(
                summary,
                Encoding.UTF8.GetString(VerifyLedger.Read(ledgerPath, VerifyLedger.REPORT)),
                await File.ReadAllTextAsync(outputs),
                Encoding.UTF8.GetString(VerifyLedger.Read(ledgerPath, VerifyLedger.PROVENANCE)),
                Encoding.UTF8.GetString(VerifyLedger.Read(ledgerPath, VerifyLedger.INTEGRITY)),
                ledgerPath);
        }
        catch
        {
            File.Delete(ledgerPath);
            throw;
        }
    }

    /// <summary>The tasks a first fan-out leaves without a verdict, published for a repair round.</summary>
    public async Task<List<VerifyTaskPackageData>> RepairsAsync(IReadOnlyList<VerifyTaskPackageData> issued, IEnumerable<VerifyResultPackageData> returned)
    {
        var reading = VerifySettlement.ReadMainAsync(issued, returned, Blobs);
        await (Task)reading;

        var batches = await VerifySettlement.RepairBatchesAsync(reading.Result, Blobs);
        return batches.Count == 0 ? [] : await VerifyTaskPackages.PublishAsync(batches, Blobs, NullLogger.Instance, "test");
    }

    /// <summary>The tasks of a first fan-out whose executions disagree, published for a dispute round.</summary>
    public async Task<List<VerifyTaskPackageData>> DisputesAsync(IReadOnlyList<VerifyTaskPackageData> issued, IEnumerable<VerifyResultPackageData> returned)
    {
        var reading = VerifySettlement.ReadMainAsync(issued, returned, Blobs);
        await (Task)reading;

        var batches = await VerifySettlement.DisputeBatchesAsync(reading.Result, Blobs);
        return batches.Count == 0 ? [] : await VerifyTaskPackages.PublishAsync(batches, Blobs, NullLogger.Instance, "test");
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(m_folder))
                Directory.Delete(m_folder, recursive: true);
        }
        catch (IOException)
        {
            // a test's own folder
        }
    }

    #endregion

    #region Properties

    /// <summary>The rig's blob store: the server's and the nodes' at once.</summary>
    public VerifyTestBlobService Blobs { get; }

    #endregion
}
