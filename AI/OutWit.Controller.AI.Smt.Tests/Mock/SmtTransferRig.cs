using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// How the tests meet a node's transfers with the times of an attempt in their own hands: the
/// time an attempt has, the time one that ran out of it is still waited for, the waits between
/// the attempts. As in <see cref="SmtNodeRig"/>, the controller's types are met inside method
/// bodies only.
/// </summary>
internal static class SmtTransferRig
{
    #region Functions

    /// <summary>Downloads a package's blob as a node does, so that an attempt can be made to run out of its time.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="package">The reference.</param>
    /// <param name="logger">The node's log.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <param name="attemptMs">The time one attempt has, milliseconds.</param>
    /// <param name="lateMs">The time an attempt that ran out of its own is still waited for, milliseconds.</param>
    /// <param name="retryDelaysMs">The waits between the attempts, milliseconds: one attempt more than there are waits.</param>
    /// <returns>The blob's local path, or the fault's code when every attempt failed.</returns>
    public static async Task<(string? Path, string? Fault)> FetchAsync(IWitBlobService blobs, SmtTaskPackageData package, ILogger logger, CancellationToken cancellationToken,
        int attemptMs, int lateMs, params int[] retryDelaysMs)
    {
        try
        {
            var path = await SmtPackageTransfer.FetchAsync(blobs, package, logger, cancellationToken, [.. retryDelaysMs.Select(delay => TimeSpan.FromMilliseconds(delay))],
                TimeSpan.FromMilliseconds(attemptMs), TimeSpan.FromMilliseconds(lateMs));
            return (path, null);
        }
        catch (SmtPackageException exception)
        {
            return (null, exception.Code);
        }
    }

    /// <summary>Uploads a file of rows as a node does, so that an attempt can be made to run out of its time.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="path">The file.</param>
    /// <param name="logger">The node's log.</param>
    /// <param name="attemptMs">The time one attempt has, milliseconds.</param>
    /// <param name="lateMs">The time an attempt that ran out of its own is still waited for, milliseconds.</param>
    /// <param name="retryDelaysMs">The waits between the attempts, milliseconds: one attempt more than there are waits.</param>
    /// <returns>The blob the upload made, or the fault's code when every attempt failed.</returns>
    public static async Task<(Guid BlobId, string? Fault)> UploadAsync(IWitBlobService blobs, string path, ILogger logger, int attemptMs, int lateMs, params int[] retryDelaysMs)
    {
        try
        {
            var result = await SmtPackageTransfer.UploadResultsAsync(blobs, path, SmtNodeRig.TICKET, 1, logger, CancellationToken.None,
                [.. retryDelaysMs.Select(delay => TimeSpan.FromMilliseconds(delay))], TimeSpan.FromMilliseconds(attemptMs), TimeSpan.FromMilliseconds(lateMs));
            return (result.ResultsBlobId, null);
        }
        catch (SmtPackageException exception)
        {
            return (Guid.Empty, exception.Code);
        }
    }

    #endregion

    #region Properties

    /// <summary>The package downloads a node has in flight at once.</summary>
    public static int FetchDegree => SmtPackageTransfer.FETCH_DEGREE;

    #endregion
}
