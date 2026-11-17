using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// The host's side of task delivery: the planned batches are grouped into packages, each
/// package is stored as a blob, and what goes on to Grid.ForEach is the list of references.
/// The tasks themselves reach a node only through the blob channel. Every reference carries a
/// ticket of its own (<see cref="VerifyTickets"/>), the version of the controller that made
/// it, and the digest of the tasks it asks for.
/// </summary>
public static class VerifyTaskPackages
{
    #region Functions

    /// <summary>Stores the batches as package blobs.</summary>
    /// <param name="batches">The planned batches, in order.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="logger">Receives one line with the counts and the time.</param>
    /// <param name="activityName">The activity that publishes, for the log.</param>
    /// <returns>The references, one per package, in order.</returns>
    public static async Task<List<VerifyTaskPackageData>> PublishAsync(IReadOnlyList<VerifyTaskBatchData> batches, IWitBlobService blobs, ILogger logger, string activityName)
    {
        var clock = Stopwatch.StartNew();
        var packages = new List<VerifyTaskPackageData>();
        long bytes = 0;
        var tasks = 0;

        foreach (var group in VerifyPackagePlanner.Plan(batches))
        {
            var content = VerifyPackageCodec.WriteTasks(group);
            var blobId = await blobs.UploadBytesAsync(content, VerifyPackageCodec.TASKS_FILE_NAME);

            var package = new VerifyTaskPackageData
            {
                TasksBlobId = blobId,
                Sha256 = VerifyPackageCodec.Hash(content),
                SizeBytes = content.Length,
                TaskCount = group.Sum(batch => batch.Tasks.Count),
                BatchCount = group.Count,
                Ticket = VerifyTickets.New(),
                RequiredControllerVersion = ControllerBuildInfo.VERSION,
                IndexDigest = IndexDigest(group)
            };

            packages.Add(package);
            bytes += package.SizeBytes;
            tasks += package.TaskCount;
        }

        logger.LogInformation(
            "{Activity}: {Tasks} task(s) in {Batches} batch(es) stored as {Packages} package blob(s), {Bytes} bytes, in {Elapsed} ms",
            activityName, tasks, batches.Count, packages.Count, bytes, clock.ElapsedMilliseconds);

        return packages;
    }

    /// <summary>The digest of the tasks a package's batches ask for (<see cref="VerifyTickets.IndexDigest"/>).</summary>
    /// <param name="batches">The package's batches.</param>
    /// <returns>Lower-case hex.</returns>
    public static string IndexDigest(IEnumerable<VerifyTaskBatchData> batches)
    {
        return VerifyTickets.IndexDigest(batches.SelectMany(batch => batch.Tasks).Select(task => task.TaskIndex));
    }

    /// <summary>
    /// Reads the task batches of a package the host itself stored: its own blob, checked against
    /// the reference's hash like any other.
    /// </summary>
    /// <param name="package">The reference.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <returns>The task batches, in order.</returns>
    /// <exception cref="InvalidDataException">The blob is not the package the reference names.</exception>
    public static async Task<List<VerifyTaskBatchData>> LoadAsync(VerifyTaskPackageData package, IWitBlobService blobs)
    {
        var bytes = await File.ReadAllBytesAsync(await blobs.GetLocalPathAsync(package.TasksBlobId));
        VerifyPackageCodec.EnsureHash(bytes, package.Sha256, "task package");

        return VerifyPackageCodec.ReadTasks(bytes);
    }

    #endregion
}
