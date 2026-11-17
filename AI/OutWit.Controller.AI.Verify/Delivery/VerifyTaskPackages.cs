using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// The host's side of task delivery: the planned batches are grouped into packages, each
/// package is stored as a blob, and what goes on to Grid.ForEach is the list of references.
/// The tasks themselves reach a node only through the blob channel. Every reference carries a
/// ticket of its own (<see cref="VerifyTickets"/>), the version of the controller that made
/// it, and the digest of the tasks it asks for.
/// <para>
/// The host never holds a run's tasks together: batches go to the publisher one at a time
/// (<see cref="VerifyPackagePublisher"/>), from a list that is small by its nature or from the
/// spool a taskset was scanned into (<see cref="VerifyTaskSpool"/>).
/// </para>
/// </summary>
public static class VerifyTaskPackages
{
    #region Functions

    /// <summary>Stores batches that are in memory as package blobs.</summary>
    /// <param name="batches">The planned batches, in order.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="logger">Receives one line with the counts and the time.</param>
    /// <param name="activityName">The activity that publishes, for the log.</param>
    /// <returns>The references, one per package, in order.</returns>
    public static async Task<List<VerifyTaskPackageData>> PublishAsync(IReadOnlyList<VerifyTaskBatchData> batches, IWitBlobService blobs, ILogger logger, string activityName)
    {
        var clock = Stopwatch.StartNew();
        var publisher = new VerifyPackagePublisher(batches.Sum(batch => (long)batch.Tasks.Count), blobs);

        foreach (var batch in batches)
            await publisher.AddAsync(batch);

        return Published(await publisher.CompleteAsync(), publisher, logger, activityName, clock);
    }

    /// <summary>
    /// Stores the tasks of a scanned taskset as package blobs: the tasks are read back from the
    /// spool a runtime at a time, in the order the taskset first named each runtime, cut into
    /// batches and stored a package at a time.
    /// </summary>
    /// <param name="spool">The spool the scan filled.</param>
    /// <param name="options">The run options, or null for the defaults.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="logger">Receives one line with the counts and the time.</param>
    /// <param name="activityName">The activity that publishes, for the log.</param>
    /// <returns>The references, one per package, in order.</returns>
    public static async Task<List<VerifyTaskPackageData>> PublishAsync(VerifyTaskSpool spool, VerifyOptionsData? options, IWitBlobService blobs, ILogger logger, string activityName)
    {
        var clock = Stopwatch.StartNew();
        var publisher = new VerifyPackagePublisher(spool.Tasks, blobs);
        var batchSize = VerifyTasksetPlanner.BatchSize(options, out _);

        foreach (var runtimeId in spool.Runtimes)
        {
            var cutter = new VerifyBatchCutter(runtimeId, options, batchSize);

            foreach (var task in spool.Read(runtimeId))
            {
                // The limits were resolved and clamped before the task was spooled.
                if (cutter.Add(task, VerifyPackagePlanner.EstimateBytes(task), VerifyPackagePlanner.OutputBytes(task.Limits!)) is { } closed)
                    await publisher.AddAsync(closed);
            }

            if (cutter.Finish() is { } last)
                await publisher.AddAsync(last);
        }

        return Published(await publisher.CompleteAsync(), publisher, logger, activityName, clock);
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

    private static List<VerifyTaskPackageData> Published(List<VerifyTaskPackageData> packages, VerifyPackagePublisher publisher, ILogger logger, string activityName, Stopwatch clock)
    {
        logger.LogInformation(
            "{Activity}: {Tasks} task(s) in {Batches} batch(es) stored as {Packages} package blob(s), {Bytes} bytes, in {Elapsed} ms",
            activityName, publisher.Tasks, publisher.Batches, packages.Count, publisher.Bytes, clock.ElapsedMilliseconds);

        return packages;
    }

    #endregion
}
